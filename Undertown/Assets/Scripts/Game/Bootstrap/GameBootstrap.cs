using UnityEngine;
using UnityEngine.Tilemaps;
using Undertown.Core.Buildings;
using Undertown.Core.Economy;
using Undertown.Core.Sim;
using Undertown.Core.World;
using Undertown.Game.InputHandling;
using Undertown.Game.Presentation;
using Undertown.Game.UI;

namespace Undertown.Game.Bootstrap
{
    /// <summary>
    /// Builds the whole runtime scene in code. The project is developed on a headless
    /// machine, so nothing may depend on objects dragged into a scene by hand: if it cannot
    /// be constructed from a script it cannot be built, tested or screenshotted here.
    /// </summary>
    public sealed class GameBootstrap : MonoBehaviour
    {
        /// <summary>Simulated minutes per real second at normal speed.</summary>
        private const float MinutesPerSecond = 12f;

        [SerializeField] private uint _seed = 20260813;

        private TownState _town;
        private WorldRenderer _renderer;
        private BuildingRenderer _buildings;
        private AgentRenderer _agents;
        private PlayerController _controller;
        private HudController _hud;
        private Camera _camera;
        private float _tickAccumulator;

        public TownState Town => _town;
        public GridMap Map => _town?.Map;
        public WorldRenderer Renderer => _renderer;

        private void Awake()
        {
            var settings = MapSettings.Default;
            var map = MapGenerator.Generate(settings, _seed);

            _town = new TownState(map, _seed);
            SeedStartingHoldings(_town);
            TownFounder.Found(_town);
            AgentSystem.Populate(_town);

            _renderer = BuildRenderer();
            _renderer.Bind(map, _town.Digs);

            _buildings = BuildBuildingRenderer(_town, _renderer.ActiveDepth);
            _agents = BuildAgentRenderer(_town, _renderer.ActiveDepth);

            _camera = BuildCamera(settings, _town);
            _controller = BuildController(_town, _renderer, _buildings, _camera);
            _hud = BuildHud(_town, _controller.Tools);
            UpdateLayerBadge();

            Debug.Log($"[SMOKE] boot ok seed={_seed} map={settings.Width}x{settings.Height}x{settings.DepthCount} " +
                      $"fingerprint={map.Fingerprint():X8}");
        }

        private void Update()
        {
            if (_town == null) return;

            _tickAccumulator += Time.deltaTime * MinutesPerSecond;
            int wholeMinutes = Mathf.FloorToInt(_tickAccumulator);
            if (wholeMinutes > 0)
            {
                _tickAccumulator -= wholeMinutes;
                _town.Clock.Advance(wholeMinutes);
                ProductionSystem.Tick(_town, wholeMinutes);
                NeedsSystem.Tick(_town);
                AgentSystem.Tick(_town, wholeMinutes);
                InspectionSystem.Tick(_town, wholeMinutes);
            }
        }

        /// <summary>Exposed so the screenshot harness can document both layers from one run.</summary>
        public void SwitchLayer()
        {
            _renderer.ToggleLayer();
            _buildings.ApplyLayerVisibility(_renderer.ActiveDepth);
            _agents.SetActiveDepth(_renderer.ActiveDepth);
            UpdateLayerBadge();
        }

        /// <summary>Exposed for the screenshot harness, which arms tools without a mouse.</summary>
        public PlayerTools Tools => _controller != null ? _controller.Tools : null;

        /// <summary>
        /// Starts every idle illicit workshop, the same act as clicking each one. Returns how
        /// many were started.
        /// </summary>
        public int StartIllicitWorks()
        {
            int started = 0;
            foreach (var building in _town.Buildings)
            {
                var def = building.Def;
                if (def == null || !def.Illicit || def.WorkerSlots <= 0 || building.Working) continue;
                if (building.ToggleWork()) started++;
            }
            return started;
        }

        /// <summary>
        /// Fast-forwards the simulation. Used by the screenshot harness to document states
        /// that would otherwise take minutes of real time to reach, such as an inspection.
        /// </summary>
        public void FastForward(int minutes)
        {
            const int chunk = 15;
            for (int elapsed = 0; elapsed < minutes; elapsed += chunk)
            {
                int step = Mathf.Min(chunk, minutes - elapsed);
                _town.Clock.Advance(step);
                ProductionSystem.Tick(_town, step);
                NeedsSystem.Tick(_town);
                AgentSystem.Tick(_town, step);
                InspectionSystem.Tick(_town, step);
            }

            var dryRun = _town.DryRunAudit();
            Debug.Log($"[SMOKE] fast-forward landed on season {_town.Clock.Season + 1} day {_town.Clock.DayOfSeason} " +
                      $"{_town.Clock.TimeOfDayLabel}, suspicion {_town.Suspicion}, " +
                      $"grain {_town.Stock.Get(MaterialId.Grain)}, ale {_town.Stock.Get(MaterialId.Ale)}, " +
                      $"moonshine {_town.Stock.Get(MaterialId.Moonshine)}, " +
                      $"grain gap {_town.LedgerGap(MaterialId.Grain)}, " +
                      $"dry-run audit {dryRun.TotalSuspicion} from {dryRun.Issues.Count} issue(s)");
        }

        private void UpdateLayerBadge()
        {
            if (_hud == null) return;
            _hud.SetLayerLabel(_renderer.ViewingSurface
                ? "SURFACE  ·  hatching marks hollow ground"
                : $"UNDERGROUND  depth {_renderer.ActiveDepth}m  ·  town shown above");
        }

        /// <summary>
        /// What the town owns on the first morning. These figures are also written into the
        /// books as the opening line, so the very first audit has something to balance against.
        /// </summary>
        private static void SeedStartingHoldings(TownState town)
        {
            town.Coin = 480;
            town.Stock.Add(MaterialId.Timber, 120);
            town.Stock.Add(MaterialId.Clay, 40);
            town.Stock.Add(MaterialId.Grain, 200);
            town.Books.BeginPeriod(town.Stock.Get);
        }

        private static WorldRenderer BuildRenderer()
        {
            var gridObject = new GameObject("Grid");
            var grid = gridObject.AddComponent<Grid>();
            grid.cellSize = new Vector3(1f, 1f, 0f);

            var primary = CreateTilemapLayer(gridObject.transform, "Tilemap_Primary", sortingOrder: 0);
            var overlay = CreateTilemapLayer(gridObject.transform, "Tilemap_Overlay", sortingOrder: 10);

            var renderer = gridObject.AddComponent<WorldRenderer>();
            renderer.Configure(primary, overlay);
            return renderer;
        }

        private static Tilemap CreateTilemapLayer(Transform parent, string name, int sortingOrder)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, worldPositionStays: false);
            var tilemap = go.AddComponent<Tilemap>();
            var tilemapRenderer = go.AddComponent<TilemapRenderer>();
            tilemapRenderer.sortingOrder = sortingOrder;
            return tilemap;
        }

        private static BuildingRenderer BuildBuildingRenderer(TownState town, int activeDepth)
        {
            var go = new GameObject("Buildings");
            var renderer = go.AddComponent<BuildingRenderer>();
            renderer.Bind(town);
            renderer.ApplyLayerVisibility(activeDepth);
            return renderer;
        }

        private static AgentRenderer BuildAgentRenderer(TownState town, int activeDepth)
        {
            var go = new GameObject("Agents");
            var renderer = go.AddComponent<AgentRenderer>();
            renderer.Bind(town);
            renderer.SetActiveDepth(activeDepth);
            return renderer;
        }

        private PlayerController BuildController(TownState town, WorldRenderer world, BuildingRenderer buildings, Camera camera)
        {
            var ghostObject = new GameObject("PlacementGhost");
            var ghost = ghostObject.AddComponent<PlacementGhost>();

            var controller = gameObject.AddComponent<PlayerController>();
            controller.Bind(town, world, buildings, camera, ghost);
            controller.WorldChanged += () => world.Redraw();
            controller.LayerToggleRequested += SwitchLayer;

            // uGUI buttons need an event system, and nothing else in a script-built scene
            // creates one.
            if (UnityEngine.EventSystems.EventSystem.current == null)
            {
                var events = new GameObject("EventSystem");
                events.AddComponent<UnityEngine.EventSystems.EventSystem>();
                events.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
            }

            return controller;
        }

        private static HudController BuildHud(TownState town, PlayerTools tools)
        {
            var go = new GameObject("Hud");
            var hud = go.AddComponent<HudController>();
            hud.Bind(town, tools);
            return hud;
        }

        private static Camera BuildCamera(MapSettings settings, TownState town)
        {
            var go = new GameObject("MainCamera");
            go.tag = "MainCamera";
            var cam = go.AddComponent<Camera>();
            cam.orthographic = true;

            // Frame a readable slice of town rather than the whole map, and sit the centre
            // above the HUD bar so the bottom rows of tiles are not hidden behind it.
            cam.backgroundColor = new Color32(0x14, 0x12, 0x10, 0xFF);
            cam.clearFlags = CameraClearFlags.SolidColor;

            // Frame the settlement rather than the middle of the map. Terrain is generated,
            // so the town lands somewhere different every seed, and the bottom of the view is
            // covered by the HUD bar - both have to be solved by measurement, not by a
            // hand-tuned offset that only looks right on one map.
            FrameTown(cam, settings, town);
            return cam;
        }

        private static void FrameTown(Camera cam, MapSettings settings, TownState town)
        {
            const float hudFraction = 232f / 1080f;
            const float margin = 3f;

            float minX = settings.Width / 2f, maxX = minX;
            float minY = settings.Height / 2f, maxY = minY;
            bool any = false;

            foreach (var building in town.Buildings)
            {
                var def = building.Def;
                if (def == null || def.Underground) continue;

                float x0 = building.Origin.X, y0 = building.Origin.Y;
                if (!any) { minX = x0; maxX = x0; minY = y0; maxY = y0; any = true; }

                minX = Mathf.Min(minX, x0);
                minY = Mathf.Min(minY, y0);
                maxX = Mathf.Max(maxX, x0 + def.Width);
                maxY = Mathf.Max(maxY, y0 + def.Height);
            }

            float spanX = maxX - minX + margin * 2f;
            float spanY = maxY - minY + margin * 2f;

            // The HUD hides the lower band of the viewport, so the usable height is smaller
            // than the viewport height and the size has to be inflated to compensate.
            float aspect = cam.aspect > 0.1f ? cam.aspect : 16f / 9f;
            float sizeForHeight = spanY / (2f * (1f - hudFraction));
            float sizeForWidth = spanX / (2f * aspect);
            cam.orthographicSize = Mathf.Max(9f, Mathf.Max(sizeForHeight, sizeForWidth));

            // Push the centre up by half the hidden band so the town sits in the visible part.
            float hiddenWorldHeight = cam.orthographicSize * 2f * hudFraction;
            cam.transform.position = new Vector3(
                (minX + maxX) / 2f,
                (minY + maxY) / 2f + hiddenWorldHeight / 2f,
                -10f);
        }
    }
}
