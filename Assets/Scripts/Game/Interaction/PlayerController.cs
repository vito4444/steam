using System.Collections.Generic;
using UnityEngine;
using Worker.Core;

namespace Worker.Game
{
    public enum InteractionMode
    {
        Select,
        Build,
        Demolish
    }

    /// <summary>
    /// Mouse and keyboard interaction: placing buildings, laying belts, demolishing, and
    /// selecting things to inspect.
    ///
    /// All state changes go through <see cref="SimWorld"/>'s own methods rather than
    /// touching buildings directly, so the player cannot produce a world state the
    /// simulation could not have reached on its own. That is what keeps saves and
    /// replays valid.
    /// </summary>
    [RequireComponent(typeof(SimRunner))]
    public sealed class PlayerController : MonoBehaviour
    {
        /// <summary>Buildings offered on the build bar, in order.</summary>
        public static readonly BuildingKind[] Buildable =
        {
            BuildingKind.Conveyor,
            BuildingKind.Storage,
            BuildingKind.Sawbench,
            BuildingKind.Lathe,
            BuildingKind.AssemblyBench,
            BuildingKind.BreakRoom,
            BuildingKind.Wall
        };

        public InteractionMode Mode { get; private set; } = InteractionMode.Select;
        public BuildingKind SelectedKind { get; private set; } = BuildingKind.Conveyor;
        public Direction PlacementFacing { get; private set; } = Direction.East;

        public GridPos HoveredTile { get; private set; }
        public bool HoveringMap { get; private set; }
        public bool PlacementValid { get; private set; }

        public BuildingInstance SelectedBuilding { get; private set; }
        public WorkerUnit SelectedWorker { get; private set; }

        /// <summary>Set by the HUD while the cursor is over a panel, so clicks do not fall through.</summary>
        public bool PointerOverUi { get; set; }

        private SimRunner _runner;
        private Camera _camera;

        private void Awake()
        {
            _runner = GetComponent<SimRunner>();
        }

        private void Update()
        {
            var world = _runner.World;
            if (world == null) return;

            if (_camera == null) _camera = Camera.main;
            if (_camera == null) return;

            UpdateHover(world);
            HandleHotkeys(world);

            if (PointerOverUi) return;

            if (Input.GetMouseButton(0)) HandlePrimary(world);
            if (Input.GetMouseButtonDown(1)) HandleSecondary(world);
        }

        // ----------------------------------------------------------------- hover

        private void UpdateHover(SimWorld world)
        {
            var mouse = Input.mousePosition;
            var point = _camera.ScreenToWorldPoint(new Vector3(mouse.x, mouse.y, -_camera.transform.position.z));

            var tile = new GridPos(Mathf.FloorToInt(point.x), Mathf.FloorToInt(point.y));
            HoveredTile = tile;
            HoveringMap = world.Map.InBounds(tile);

            PlacementValid = Mode == InteractionMode.Build
                             && HoveringMap
                             && world.Map.CanPlace(SelectedKind, PlacementOrigin(), PlacementFacing)
                             && world.Ledger.CanAfford(BuildingData.Get(SelectedKind).Cost);
        }

        /// <summary>
        /// Multi-tile buildings are placed centred on the cursor rather than anchored at
        /// their corner, which is what players expect when dragging a 2x2 bench around.
        /// </summary>
        public GridPos PlacementOrigin()
        {
            var def = BuildingData.Get(SelectedKind);
            int width = (PlacementFacing == Direction.East || PlacementFacing == Direction.West) ? def.Height : def.Width;
            int height = (PlacementFacing == Direction.East || PlacementFacing == Direction.West) ? def.Width : def.Height;

            return new GridPos(HoveredTile.X - (width - 1) / 2, HoveredTile.Y - (height - 1) / 2);
        }

        // --------------------------------------------------------------- hotkeys

        private void HandleHotkeys(SimWorld world)
        {
            for (int i = 0; i < Buildable.Length && i < 9; i++)
            {
                if (!Input.GetKeyDown(KeyCode.Alpha1 + i)) continue;
                SelectedKind = Buildable[i];
                Mode = InteractionMode.Build;
            }

            if (Input.GetKeyDown(KeyCode.R))
            {
                PlacementFacing = PlacementFacing.RotateCW();
            }

            if (Input.GetKeyDown(KeyCode.X))
            {
                Mode = Mode == InteractionMode.Demolish ? InteractionMode.Select : InteractionMode.Demolish;
            }

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                Mode = InteractionMode.Select;
                ClearSelection();
            }
        }

        // ----------------------------------------------------------------- click

        private void HandlePrimary(SimWorld world)
        {
            if (!HoveringMap) return;

            switch (Mode)
            {
                case InteractionMode.Build:
                    TryPlace(world);
                    break;

                case InteractionMode.Demolish:
                    TryDemolish(world);
                    break;

                default:
                    // Selection should not repeat while the button is held.
                    if (Input.GetMouseButtonDown(0)) SelectAt(world, HoveredTile);
                    break;
            }
        }

        private void HandleSecondary(SimWorld world)
        {
            if (Mode != InteractionMode.Select)
            {
                Mode = InteractionMode.Select;
                return;
            }

            ClearSelection();
        }

        private void TryPlace(SimWorld world)
        {
            var origin = PlacementOrigin();
            if (!world.Map.CanPlace(SelectedKind, origin, PlacementFacing)) return;

            var placed = world.TryBuild(SelectedKind, origin, PlacementFacing);
            if (placed == null) return;

            // Holding the button along a row should lay a run of belt, so stay in build
            // mode; anything else is usually placed one at a time but the cost check and
            // occupancy test already prevent accidental double placement.
            if (SelectedKind != BuildingKind.Conveyor) SelectedBuilding = placed;
        }

        private void TryDemolish(SimWorld world)
        {
            var building = world.BuildingAt(HoveredTile);
            if (building == null) return;

            // Intake and shipping are the factory's connection to the outside world;
            // letting the player delete them turns a bad click into an unwinnable game.
            if (building.Kind == BuildingKind.Intake || building.Kind == BuildingKind.Shipping) return;

            if (SelectedBuilding == building) SelectedBuilding = null;
            world.RemoveBuilding(building.Id);
        }

        private void SelectAt(SimWorld world, GridPos tile)
        {
            ClearSelection();

            for (int i = 0; i < world.Workers.Count; i++)
            {
                if (world.Workers[i].Pos != tile) continue;
                SelectedWorker = world.Workers[i];
                return;
            }

            SelectedBuilding = world.BuildingAt(tile);
        }

        public void ClearSelection()
        {
            SelectedBuilding = null;
            SelectedWorker = null;
        }

        public void SetMode(InteractionMode mode) => Mode = mode;

        public void SetSelectedKind(BuildingKind kind)
        {
            SelectedKind = kind;
            Mode = InteractionMode.Build;
        }

        public void RotatePlacement() => PlacementFacing = PlacementFacing.RotateCW();

        // ------------------------------------------------------------- commands

        /// <summary>Cycles a station to its next available recipe.</summary>
        public void CycleRecipe(BuildingInstance station)
        {
            if (station == null || !station.Def.IsStation) return;

            var options = GameData.RecipesFor(station.Kind);
            if (options.Count == 0) return;

            int current = options.FindIndex(recipe => recipe.Id == station.ActiveRecipe);
            var next = options[(current + 1) % options.Count];

            station.ActiveRecipe = next.Id;

            // Work in progress belongs to the old recipe; keeping it would let a player
            // start an expensive job and finish it as a cheap one.
            station.WorkProgress = 0;
        }

        public void HireWorker()
        {
            var world = _runner.World;
            if (world == null) return;

            const int hiringFee = 12000;
            if (!world.Ledger.CanAfford(hiringFee)) return;

            var spawn = FindSpawnTile(world);
            if (!spawn.IsValid) return;

            var names = Scenarios.WorkerNamePool;
            string name = names[world.Workers.Count % names.Count];

            world.HireWorker(name, spawn);
            world.Ledger.Record(world.Tick, LedgerEntryKind.Wages, -hiringFee);
        }

        public void LayOffSelectedWorker()
        {
            var world = _runner.World;
            if (world == null || SelectedWorker == null) return;

            // Severance is a week of wages, and everyone left takes a morale hit. This is
            // the cost the whole design is built around: replacing people is never free.
            int severance = SelectedWorker.DailyWage * 7;
            world.LayOffWorker(SelectedWorker.Id, severance, moraleHitPerWorker: 1200);
            SelectedWorker = null;
        }

        private static GridPos FindSpawnTile(SimWorld world)
        {
            var center = new GridPos(world.Map.Width / 2, world.Map.Height / 2);

            for (int radius = 0; radius < 12; radius++)
            {
                for (int dy = -radius; dy <= radius; dy++)
                {
                    for (int dx = -radius; dx <= radius; dx++)
                    {
                        var tile = new GridPos(center.X + dx, center.Y + dy);
                        if (!world.Map.InBounds(tile)) continue;
                        if (world.Map.GetTerrain(tile) != TerrainKind.Floor) continue;
                        if (!world.Map.IsWalkable(tile)) continue;
                        return tile;
                    }
                }
            }

            return GridPos.Invalid;
        }

        /// <summary>Tiles the placement preview should cover, for the view layer.</summary>
        public IEnumerable<GridPos> PreviewFootprint()
        {
            if (Mode != InteractionMode.Build || !HoveringMap) yield break;

            var probe = new BuildingInstance(-1, SelectedKind, PlacementOrigin(), PlacementFacing);
            foreach (var tile in probe.Footprint()) yield return tile;
        }
    }
}
