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
        private PropRenderer _props;
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

            _props = BuildPropRenderer(_town, _renderer);
            _buildings = BuildBuildingRenderer(_town, _renderer, _renderer.ActiveDepth);
            _agents = BuildAgentRenderer(_town, _renderer, _renderer.ActiveDepth);

            _camera = BuildCamera(_town, _renderer);
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
            _props.Rebuild(_renderer.ActiveDepth);
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

            // 2:1 diamonds, the projection the concept art is drawn to.
            grid.cellLayout = GridLayout.CellLayout.Isometric;
            grid.cellSize = Iso.CellSize;
            grid.cellSwizzle = GridLayout.CellSwizzle.XYZ;

            var primary = CreateTilemapLayer(gridObject.transform, "Tilemap_Primary", sortingOrder: -20);
            var overlay = CreateTilemapLayer(gridObject.transform, "Tilemap_Overlay", sortingOrder: -10);

            var renderer = gridObject.AddComponent<WorldRenderer>();
            renderer.Configure(grid, primary, overlay);
            return renderer;
        }

        private static Tilemap CreateTilemapLayer(Transform parent, string name, int sortingOrder)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, worldPositionStays: false);
            var tilemap = go.AddComponent<Tilemap>();

            tilemap.orientation = Tilemap.Orientation.XY;
            tilemap.tileAnchor = new Vector3(0f, 0f, 0f);

            // Ground diamonds are exactly one cell and never overlap, so the tilemap needs no
            // internal ordering and can be batched. Everything that does overlap - scenery,
            // buildings, people - is a sprite ordered by WorldRenderer.SortingOrderFor.
            var tilemapRenderer = go.AddComponent<TilemapRenderer>();
            tilemapRenderer.mode = TilemapRenderer.Mode.Chunk;
            tilemapRenderer.sortingOrder = sortingOrder;
            return tilemap;
        }

        private static PropRenderer BuildPropRenderer(TownState town, WorldRenderer world)
        {
            var go = new GameObject("Props");
            var renderer = go.AddComponent<PropRenderer>();
            renderer.Bind(town, world);
            return renderer;
        }

        private static BuildingRenderer BuildBuildingRenderer(TownState town, WorldRenderer world, int activeDepth)
        {
            var go = new GameObject("Buildings");
            var renderer = go.AddComponent<BuildingRenderer>();
            renderer.Bind(town, world);
            renderer.ApplyLayerVisibility(activeDepth);
            return renderer;
        }

        private static AgentRenderer BuildAgentRenderer(TownState town, WorldRenderer world, int activeDepth)
        {
            var go = new GameObject("Agents");
            var renderer = go.AddComponent<AgentRenderer>();
            renderer.Bind(town, world);
            renderer.SetActiveDepth(activeDepth);
            return renderer;
        }

        private PlayerController BuildController(TownState town, WorldRenderer world, BuildingRenderer buildings, Camera camera)
        {
            var ghostObject = new GameObject("PlacementGhost");
            var ghost = ghostObject.AddComponent<PlacementGhost>();

            var controller = gameObject.AddComponent<PlayerController>();
            controller.Bind(town, world, buildings, camera, ghost);
            controller.LayerToggleRequested += SwitchLayer;
            ghost.Bind(world);

            // Scenery has to follow the world as well as the tilemaps do: fencing is derived
            // from where plots meet lanes and from what is built on them, so paving a cell or
            // putting a shed on one changes which edges are fenced.
            controller.WorldChanged += () =>
            {
                world.Redraw();
                _props.Rebuild(world.ActiveDepth);
            };

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

        private static Camera BuildCamera(TownState town, WorldRenderer world)
        {
            var go = new GameObject("MainCamera");
            go.tag = "MainCamera";
            var cam = go.AddComponent<Camera>();
            cam.orthographic = true;

            // Frame a readable slice of town rather than the whole map, and sit the centre
            // above the HUD bar so the bottom rows of tiles are not hidden behind it.
            cam.backgroundColor = new Color32(0x14, 0x12, 0x10, 0xFF);
            cam.clearFlags = CameraClearFlags.SolidColor;

            // Frame the settlement rather than the middle of the map. Terrain is generated, so
            // the town lands somewhere different every seed, and the bottom of the view is
            // covered by the HUD bar - both have to be solved by measurement.
            //
            // The bounds have to be taken in world space, not in cells: under an isometric
            // projection a compact block of cells becomes a wide, shallow diamond, and sizing
            // the camera off cell extents frames it badly in both directions.
            FrameTown(cam, town, world);
            return cam;
        }

        private static void FrameTown(Camera cam, TownState town, WorldRenderer world)
        {
            const float hudFraction = 232f / 1080f;

            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);
            bool any = false;

            foreach (var building in town.Buildings)
            {
                var def = building.Def;
                if (def == null || def.Underground) continue;

                // Every corner of the footprint, since a diamond's extremes are its corners.
                for (int dy = 0; dy <= def.Height; dy += Mathf.Max(1, def.Height))
                for (int dx = 0; dx <= def.Width; dx += Mathf.Max(1, def.Width))
                {
                    var p = world.CellCentre(building.Origin.Offset(dx, dy));
                    min = Vector2.Min(min, p);
                    max = Vector2.Max(max, p);
                    any = true;
                }
            }

            if (!any)
            {
                var centre = world.CellCentre(new Coord(town.Map.Width / 2, town.Map.Height / 2));
                min = max = centre;
            }

            // One pixel of art across two pixels of screen.
            //
            // The camera used to be sized to whatever the settlement's extent asked for, which
            // put it at about 1.65 art pixels per screen pixel. Point sampling at a fractional
            // scale keeps edges hard but makes them uneven: some rows of a texture land on two
            // screen pixels and their neighbours on one, so a straight eave comes out with a
            // wobble in it and the ground dither crawls when the camera moves.
            //
            // Which whole step to open on was settled by putting the reference alongside at
            // the same width. Counted in cells 1:1 is the nearer match - the reference covers
            // about twenty-five and 1:1 covers thirty, against fifteen at 2:1 - but counting
            // cells measures the wrong thing. At 1:1 the settlement sits as a small island in
            // a field of empty pasture and its doorways, yard goods and people all fall below
            // the size at which they read; at 2:1 the town fills the frame and runs off the
            // edges, which is what the reference does. The wheel steps back out for the
            // overview.
            cam.orthographicSize = Iso.CameraSize(ScreenHeight(), 2);

            // The HUD covers the bottom band of the viewport, so the visible area's centre sits
            // above the camera's. Putting the town in the middle of what can actually be seen
            // means moving the camera down by half the hidden band, not up: raising it pushes
            // the southern edge of the settlement underneath the HUD, which is where the town
            // hall was disappearing to.
            float hiddenWorldHeight = cam.orthographicSize * 2f * hudFraction;
            cam.transform.position = new Vector3(
                (min.x + max.x) / 2f,
                (min.y + max.y) / 2f - hiddenWorldHeight / 2f,
                -10f);
        }

        /// <summary>Viewport height in pixels, with a 1080-line fallback for headless runs.</summary>
        private static float ScreenHeight() => Screen.height > 16 ? Screen.height : 1080f;
    }
}
