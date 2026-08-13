using UnityEngine;
using UnityEngine.EventSystems;
using Undertown.Core.Buildings;
using Undertown.Core.Sim;
using Undertown.Core.World;
using Undertown.Game.Presentation;

namespace Undertown.Game.InputHandling
{
    /// <summary>
    /// Mouse and keyboard. Placement, excavation and demolition all resolve through the core
    /// simulation rather than editing the world directly, so anything the player can do is
    /// also something a test can do.
    /// </summary>
    public sealed class PlayerController : MonoBehaviour
    {
        private const float PanSpeed = 22f;

        private TownState _town;
        private WorldRenderer _world;
        private BuildingRenderer _buildings;
        private Camera _camera;
        private PlacementGhost _ghost;

        public PlayerTools Tools { get; } = new PlayerTools();

        /// <summary>Raised after anything that changes what is on the map, so renderers can refresh.</summary>
        public System.Action WorldChanged;

        /// <summary>Raised when the player asks to look at the other layer.</summary>
        public System.Action LayerToggleRequested;

        private bool _dragging;
        private Coord _dragStart;
        private int _zoomStep = 2;

        public void Bind(TownState town, WorldRenderer world, BuildingRenderer buildings, Camera camera, PlacementGhost ghost)
        {
            _town = town;
            _world = world;
            _buildings = buildings;
            _camera = camera;
            _ghost = ghost;
        }

        private void Update()
        {
            if (_town == null) return;

            HandleCamera();
            HandleHotkeys();
            HandlePointer();
        }

        private void HandleCamera()
        {
            float x = 0f, y = 0f;
            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) x -= 1f;
            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) x += 1f;
            if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) y -= 1f;
            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) y += 1f;

            if (x != 0f || y != 0f)
            {
                // Pan faster when zoomed out, so crossing the map always feels the same.
                float scale = _camera.orthographicSize / 13f;
                _camera.transform.position += new Vector3(x, y, 0f) * (PanSpeed * scale * Time.deltaTime);
            }

            // Whole steps only. A continuous wheel zoom lands the camera between whole-pixel
            // scales, where point sampling stretches some rows of a texture and not others and
            // every straight edge in the scene picks up a wobble.
            float wheel = Input.mouseScrollDelta.y;
            if (Mathf.Abs(wheel) > 0.01f)
            {
                _zoomStep = Mathf.Clamp(_zoomStep + (wheel > 0f ? 1 : -1),
                    Iso.MinZoomStep, Iso.MaxZoomStep);
                _camera.orthographicSize = Iso.CameraSize(Screen.height, _zoomStep);
            }
        }

        private void HandleHotkeys()
        {
            if (Input.GetKeyDown(KeyCode.Tab)) LayerToggleRequested?.Invoke();
            if (Input.GetKeyDown(KeyCode.Escape)) Tools.Cancel();
            if (Input.GetKeyDown(KeyCode.E)) Tools.SelectExcavate();
            if (Input.GetKeyDown(KeyCode.X)) Tools.SelectDemolish();
        }

        private void HandlePointer()
        {
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            {
                _ghost.Hide();
                return;
            }

            var cell = CellUnderPointer();
            UpdateGhost(cell);

            if (Input.GetMouseButtonDown(1)) { Tools.Cancel(); _dragging = false; return; }

            switch (Tools.Mode)
            {
                case ToolMode.Place: HandlePlace(cell); break;
                case ToolMode.Excavate: HandleExcavate(cell); break;
                case ToolMode.Demolish: HandleDemolish(cell); break;
                case ToolMode.Inspect: HandleInspect(cell); break;
            }
        }

        /// <summary>
        /// Clicking a workshop starts or stops it. Running the still is the decision the whole
        /// game turns on, so it has to be one click away and reversible the moment an
        /// inspection is announced.
        /// </summary>
        private void HandleInspect(Coord cell)
        {
            if (!Input.GetMouseButtonDown(0)) return;

            var building = BuildingPlacement.BuildingAt(_town, cell);
            if (building == null) return;

            var def = building.Def;
            if (def == null || def.WorkerSlots <= 0) return;

            bool working = building.ToggleWork();
            _town.Record(working
                ? $"{def.Name} at {building.Origin} is working"
                : $"{def.Name} at {building.Origin} has stood down");
        }

        private void HandlePlace(Coord cell)
        {
            if (!Input.GetMouseButtonDown(0)) return;

            var placed = BuildingPlacement.Place(_town, Tools.Selected, cell);
            if (placed == null)
            {
                _town.Record($"cannot build there: {BuildingPlacement.Check(_town, Tools.Selected, cell)}");
                return;
            }

            _buildings.Track(placed, _world.ActiveDepth);
            WorldChanged?.Invoke();
        }

        /// <summary>Dragging marks a rectangle, which is how a chamber gets ordered rather than a single cell.</summary>
        private void HandleExcavate(Coord cell)
        {
            if (Input.GetMouseButtonDown(0)) { _dragging = true; _dragStart = cell; }
            if (!_dragging || !Input.GetMouseButtonUp(0)) return;

            _dragging = false;
            int ordered = 0;
            int x0 = Mathf.Min(_dragStart.X, cell.X), x1 = Mathf.Max(_dragStart.X, cell.X);
            int y0 = Mathf.Min(_dragStart.Y, cell.Y), y1 = Mathf.Max(_dragStart.Y, cell.Y);

            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
                if (_town.Digs.Order(_town.Map, new Coord(x, y, cell.Depth))) ordered++;

            if (ordered > 0) _town.Record($"{ordered} cells marked for digging");
            WorldChanged?.Invoke();
        }

        private void HandleDemolish(Coord cell)
        {
            if (!Input.GetMouseButtonDown(0)) return;

            var building = BuildingPlacement.BuildingAt(_town, cell);
            if (building == null)
            {
                if (_town.Digs.Cancel(cell)) WorldChanged?.Invoke();
                return;
            }

            BuildingPlacement.Demolish(_town, building);
            _buildings.Rebuild();
            _buildings.ApplyLayerVisibility(_world.ActiveDepth);
            WorldChanged?.Invoke();
        }

        private void UpdateGhost(Coord cell)
        {
            if (Tools.Mode == ToolMode.Place)
            {
                var result = BuildingPlacement.Check(_town, Tools.Selected, cell);
                _ghost.ShowBuilding(Tools.Selected, cell, result == PlacementResult.Ok);
                return;
            }

            if (Tools.Mode == ToolMode.Excavate)
            {
                bool diggable = !cell.IsSurface && Tiles.IsDiggable(_town.Map.Get(cell));
                _ghost.ShowCell(cell, diggable);
                return;
            }

            if (Tools.Mode == ToolMode.Demolish)
            {
                _ghost.ShowCell(cell, BuildingPlacement.BuildingAt(_town, cell) != null || _town.Digs.IsOrdered(cell));
                return;
            }

            _ghost.Hide();
        }

        /// <summary>
        /// Under an isometric projection the cell under the cursor is not a floor of the
        /// world position; the grid owns that inverse transform, so it does it.
        /// </summary>
        private Coord CellUnderPointer()
        {
            var world = _camera.ScreenToWorldPoint(Input.mousePosition);
            return _world.CellAt(world);
        }
    }
}
