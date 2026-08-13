using System.Collections.Generic;
using UnityEngine;
using Worker.Core;

namespace Worker.Game
{
    /// <summary>
    /// Draws a <see cref="SimWorld"/> with sprite renderers.
    ///
    /// The composition deliberately mirrors the headless preview renderer in
    /// Tools/Preview: same palette, same layering (floor, belts, buildings, workers),
    /// same readouts on the buildings themselves. Keeping the two in step is what lets
    /// a preview screenshot stand in for a real one during art review.
    ///
    /// Views are created once per building and reused; only transforms, colours and
    /// item chips change per frame.
    /// </summary>
    [RequireComponent(typeof(SimRunner))]
    public sealed class FactoryView : MonoBehaviour
    {
        private const int SortingFloor = 0;
        private const int SortingBelt = 10;
        private const int SortingBeltItem = 20;
        private const int SortingBuilding = 30;
        private const int SortingBuildingOverlay = 40;
        private const int SortingWorker = 50;
        private const int SortingCarried = 60;

        private SimRunner _runner;
        private SimWorld _world;
        private Transform _root;

        private SpriteRenderer _floor;
        private readonly Dictionary<int, BuildingView> _buildings = new Dictionary<int, BuildingView>();
        private readonly Dictionary<int, WorkerView> _workers = new Dictionary<int, WorkerView>();
        private readonly List<SpriteRenderer> _beltItemPool = new List<SpriteRenderer>();
        private int _beltItemsUsed;

        private sealed class BuildingView
        {
            public SpriteRenderer Body;
            public SpriteRenderer Progress;
            public Transform Root;
        }

        private sealed class WorkerView
        {
            public Transform Root;
            public SpriteRenderer Body;
            public SpriteRenderer Carried;
            public bool LastTired;
        }

        private void Awake()
        {
            _runner = GetComponent<SimRunner>();
            _runner.WorldCreated += OnWorldCreated;
            if (_runner.World != null) OnWorldCreated(_runner.World);
        }

        private void OnDestroy()
        {
            if (_runner != null) _runner.WorldCreated -= OnWorldCreated;
            ProceduralSprites.ClearCache();
        }

        private void OnWorldCreated(SimWorld world)
        {
            _world = world;
            Rebuild();
        }

        private void Rebuild()
        {
            if (_root != null) Destroy(_root.gameObject);

            _buildings.Clear();
            _workers.Clear();
            _beltItemPool.Clear();

            var rootObject = new GameObject("FactoryView");
            rootObject.transform.SetParent(transform, false);
            _root = rootObject.transform;

            _floor = CreateRenderer("Floor", _root, ProceduralSprites.Floor(_world.Map), SortingFloor);
            _floor.transform.position = new Vector3(_world.Map.Width * 0.5f, _world.Map.Height * 0.5f, 0f);
        }

        private void LateUpdate()
        {
            if (_world == null) return;

            SyncBuildings();
            SyncWorkers();
            SyncBeltItems();
        }

        // -------------------------------------------------------------- buildings

        private void SyncBuildings()
        {
            for (int i = 0; i < _world.Buildings.Count; i++)
            {
                var building = _world.Buildings[i];
                if (!_buildings.TryGetValue(building.Id, out var view))
                {
                    view = CreateBuildingView(building);
                    _buildings[building.Id] = view;
                }

                UpdateProgressBar(building, view);
            }

            // Drop views for buildings that were demolished.
            if (_buildings.Count == _world.Buildings.Count) return;

            var live = new HashSet<int>();
            for (int i = 0; i < _world.Buildings.Count; i++) live.Add(_world.Buildings[i].Id);

            var stale = new List<int>();
            foreach (var pair in _buildings)
            {
                if (!live.Contains(pair.Key)) stale.Add(pair.Key);
            }

            for (int i = 0; i < stale.Count; i++)
            {
                var view = _buildings[stale[i]];
                if (view.Root != null) Destroy(view.Root.gameObject);
                _buildings.Remove(stale[i]);
            }
        }

        private BuildingView CreateBuildingView(BuildingInstance building)
        {
            var holder = new GameObject(building.Kind + "#" + building.Id);
            holder.transform.SetParent(_root, false);
            holder.transform.position = new Vector3(
                building.Origin.X + building.Width * 0.5f,
                building.Origin.Y + building.Height * 0.5f,
                0f);

            var view = new BuildingView { Root = holder.transform };

            if (building.IsConveyor)
            {
                view.Body = CreateRenderer("Body", holder.transform,
                    ProceduralSprites.Conveyor(), SortingBelt);
                // Sprites are authored pointing east; rotate into the belt's facing.
                holder.transform.rotation = Quaternion.Euler(0f, 0f, FacingToZRotation(building.Facing));
                return view;
            }

            view.Body = CreateRenderer("Body", holder.transform,
                ProceduralSprites.Building(building.Kind, building.Width, building.Height), SortingBuilding);

            if (building.Def.IsStation)
            {
                view.Progress = CreateRenderer("Progress", holder.transform,
                    ProceduralSprites.White(), SortingBuildingOverlay);
                view.Progress.color = Palette.ProgressFill.ToUnity();
                view.Progress.enabled = false;
            }

            return view;
        }

        private void UpdateProgressBar(BuildingInstance building, BuildingView view)
        {
            if (view.Progress == null) return;

            var recipe = building.CurrentRecipe();
            if (recipe == null || building.WorkProgress <= 0)
            {
                view.Progress.enabled = false;
                return;
            }

            int required = recipe.WorkTicks * 100;
            float fraction = Mathf.Clamp01((float)building.WorkProgress / required);

            float fullWidth = building.Width * 0.8f;
            float height = 0.12f;

            view.Progress.enabled = true;
            view.Progress.transform.localScale = new Vector3(fullWidth * fraction, height, 1f);
            // Grow from the left edge rather than from the centre.
            view.Progress.transform.localPosition = new Vector3(
                -fullWidth * 0.5f + fullWidth * fraction * 0.5f,
                -building.Height * 0.5f + 0.28f,
                0f);
        }

        // ---------------------------------------------------------------- workers

        private void SyncWorkers()
        {
            for (int i = 0; i < _world.Workers.Count; i++)
            {
                var worker = _world.Workers[i];
                if (!_workers.TryGetValue(worker.Id, out var view))
                {
                    view = CreateWorkerView(worker);
                    _workers[worker.Id] = view;
                }

                view.Root.position = InterpolatedPosition(worker);

                bool tired = worker.Stamina <= SimConfig.StaminaSeekRestThreshold;
                if (tired != view.LastTired)
                {
                    view.Body.sprite = ProceduralSprites.Worker(tired);
                    view.LastTired = tired;
                }

                if (worker.Carried.IsEmpty)
                {
                    view.Carried.enabled = false;
                }
                else
                {
                    view.Carried.enabled = true;
                    view.Carried.sprite = ProceduralSprites.Item(worker.Carried.Item);
                }
            }

            if (_workers.Count == _world.Workers.Count) return;

            var live = new HashSet<int>();
            for (int i = 0; i < _world.Workers.Count; i++) live.Add(_world.Workers[i].Id);

            var stale = new List<int>();
            foreach (var pair in _workers)
            {
                if (!live.Contains(pair.Key)) stale.Add(pair.Key);
            }

            for (int i = 0; i < stale.Count; i++)
            {
                var view = _workers[stale[i]];
                if (view.Root != null) Destroy(view.Root.gameObject);
                _workers.Remove(stale[i]);
            }
        }

        private WorkerView CreateWorkerView(WorkerUnit worker)
        {
            var holder = new GameObject("Worker " + worker.Name);
            holder.transform.SetParent(_root, false);

            var view = new WorkerView { Root = holder.transform };
            view.Body = CreateRenderer("Body", holder.transform, ProceduralSprites.Worker(false), SortingWorker);
            view.Carried = CreateRenderer("Carried", holder.transform, ProceduralSprites.Item(ItemId.Log), SortingCarried);
            view.Carried.transform.localPosition = new Vector3(0.3f, 0.42f, 0f);
            view.Carried.enabled = false;

            return view;
        }

        /// <summary>
        /// Blends between the worker's current tile and the one they are stepping onto.
        /// The simulation moves in whole tiles; without this, figures would teleport.
        /// </summary>
        private Vector3 InterpolatedPosition(WorkerUnit worker)
        {
            var from = TileCenter(worker.Pos);
            if (worker.PathCursor >= worker.Path.Count) return from;

            var to = TileCenter(worker.NextStep);
            float t = worker.StepProgressPermille / 1000f;
            return Vector3.Lerp(from, to, t);
        }

        private static Vector3 TileCenter(GridPos pos) => new Vector3(pos.X + 0.5f, pos.Y + 0.5f, 0f);

        // ------------------------------------------------------------ belt items

        /// <summary>
        /// Items riding belts are drawn from a pool: their count changes constantly, and
        /// creating a GameObject per item per frame would be the single largest source of
        /// garbage in the view.
        /// </summary>
        private void SyncBeltItems()
        {
            _beltItemsUsed = 0;

            for (int i = 0; i < _world.Buildings.Count; i++)
            {
                var belt = _world.Buildings[i];
                if (!belt.IsConveyor) continue;

                for (int slot = 0; slot < ConveyorState.Capacity; slot++)
                {
                    var item = belt.Conveyor.ItemAt(slot);
                    if (item == ItemId.None) continue;

                    var renderer = RentBeltItem();
                    renderer.sprite = ProceduralSprites.Item(item);
                    renderer.transform.position = BeltItemPosition(belt, slot);
                }
            }

            for (int i = _beltItemsUsed; i < _beltItemPool.Count; i++)
            {
                _beltItemPool[i].enabled = false;
            }
        }

        private SpriteRenderer RentBeltItem()
        {
            if (_beltItemsUsed < _beltItemPool.Count)
            {
                var existing = _beltItemPool[_beltItemsUsed++];
                existing.enabled = true;
                return existing;
            }

            var renderer = CreateRenderer("BeltItem", _root, ProceduralSprites.Item(ItemId.Log), SortingBeltItem);
            _beltItemPool.Add(renderer);
            _beltItemsUsed++;
            return renderer;
        }

        private static Vector3 BeltItemPosition(BuildingInstance belt, int slot)
        {
            float travel = belt.Conveyor.PositionPermille(slot) / 1000f;
            var offset = belt.Facing.Offset();

            // Start half a tile back along the facing and travel one full tile forward.
            float cx = belt.Origin.X + 0.5f + offset.X * (travel - 0.5f);
            float cy = belt.Origin.Y + 0.5f + offset.Y * (travel - 0.5f);
            return new Vector3(cx, cy, 0f);
        }

        // ----------------------------------------------------------------- helpers

        private static SpriteRenderer CreateRenderer(string name, Transform parent, Sprite sprite, int sortingOrder)
        {
            var holder = new GameObject(name);
            holder.transform.SetParent(parent, false);

            var renderer = holder.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sortingOrder = sortingOrder;
            return renderer;
        }

        private static float FacingToZRotation(Direction facing)
        {
            switch (facing)
            {
                case Direction.East: return 0f;
                case Direction.North: return 90f;
                case Direction.West: return 180f;
                default: return 270f;
            }
        }
    }
}
