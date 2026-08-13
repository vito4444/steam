using System.Collections.Generic;
using UnityEngine;
using Worker.Core;

namespace Worker.Game
{
    /// <summary>
    /// Renders the factory as low-poly 3D geometry under an orthographic isometric
    /// camera, in the manner of Two Point Hospital or Timberborn.
    ///
    /// Why 3D at all for a game whose simulation is a flat grid: the flat renderer had
    /// no way to express volume, and without volume there is no key light, no shadow and
    /// no silhouette. Every management game that reads well on a store page solves this
    /// the same way, with a fixed high camera, a single strong directional light and
    /// objects of visibly different heights. Height is the load-bearing part; a bench
    /// that stands taller than the floor casts a shadow, and that shadow is what tells
    /// the eye it is an object rather than a painted rectangle.
    ///
    /// All geometry is built from primitives at runtime. No modelling tool is involved
    /// and no mesh asset is committed, which keeps the same "reviewable in a diff"
    /// property the rest of the project has.
    /// </summary>
    [RequireComponent(typeof(SimRunner))]
    public sealed class IsometricFactoryView : MonoBehaviour
    {
        /// <summary>Height of each building kind in world units. One tile is one unit.</summary>
        private static float HeightOf(BuildingKind kind)
        {
            switch (kind)
            {
                case BuildingKind.Conveyor: return 0.18f;
                case BuildingKind.Storage: return 0.85f;
                case BuildingKind.Sawbench: return 0.70f;
                case BuildingKind.Lathe: return 0.75f;
                case BuildingKind.AssemblyBench: return 0.65f;
                case BuildingKind.Intake: return 1.30f;
                case BuildingKind.Shipping: return 1.30f;
                case BuildingKind.BreakRoom: return 1.05f;
                case BuildingKind.Wall: return 1.40f;
                default: return 0.5f;
            }
        }

        private SimRunner _runner;
        private SimWorld _world;
        private Transform _root;

        private readonly Dictionary<BuildingKind, Material> _buildingMaterials = new Dictionary<BuildingKind, Material>();
        private readonly Dictionary<ItemId, Material> _itemMaterials = new Dictionary<ItemId, Material>();
        private readonly Dictionary<int, Material> _shadeMaterials = new Dictionary<int, Material>();
        private readonly Dictionary<int, Transform> _buildings = new Dictionary<int, Transform>();
        private readonly Dictionary<int, WorkerRig> _workers = new Dictionary<int, WorkerRig>();
        private readonly List<Transform> _itemPool = new List<Transform>();
        private int _itemsUsed;

        private Material _floorMaterial;
        private Material _floorLineMaterial;
        private Material _workerMaterial;
        private Material _workerTiredMaterial;
        private Material _accentMaterial;

        private sealed class WorkerRig
        {
            public Transform Root;
            public Renderer Body;
            public Transform Carried;
            public Renderer CarriedRenderer;
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
        }

        private void OnWorldCreated(SimWorld world)
        {
            _world = world;
            Rebuild();
        }

        // ------------------------------------------------------------- materials

        private static Shader _litShader;
        private static bool _shaderWarningLogged;

        /// <summary>
        /// Resolves the lit shader once.
        ///
        /// Runtime-created materials are invisible to the build's shader scanner, so the
        /// shader has to be registered in Always Included Shaders (see
        /// Worker.Editor.RenderPipelineSetup). If that has not been done the lookup
        /// returns null, every material constructor throws, and the world renders black
        /// while the interface keeps working, which is a miserable thing to debug from
        /// a screenshot. Failing loudly here is worth the few lines.
        /// </summary>
        private static Shader ResolveLitShader()
        {
            if (_litShader != null) return _litShader;

            _litShader = Shader.Find("Universal Render Pipeline/Lit")
                         ?? Shader.Find("Universal Render Pipeline/Simple Lit")
                         ?? Shader.Find("Standard")
                         ?? Shader.Find("Sprites/Default");

            if (_litShader == null && !_shaderWarningLogged)
            {
                _shaderWarningLogged = true;
                Debug.LogError("[worker] no usable lit shader was included in this build. "
                               + "Run Worker/Rendering/Create 3D pipeline asset to register it.");
            }

            return _litShader;
        }

        private static Material CreateLit(Color color, float smoothness = 0.15f, float metallic = 0f)
        {
            var shader = ResolveLitShader();
            if (shader == null) return null;

            var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            material.color = color;

            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
            if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", smoothness);
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", metallic);

            return material;
        }

        private Material MaterialFor(BuildingKind kind)
        {
            if (_buildingMaterials.TryGetValue(kind, out var existing)) return existing;

            var material = CreateLit(Palette.ForBuilding(kind).ToUnity());
            _buildingMaterials[kind] = material;
            return material;
        }

        private Material MaterialFor(ItemId item)
        {
            if (_itemMaterials.TryGetValue(item, out var existing)) return existing;

            var material = CreateLit(Palette.ForItem(item).ToUnity(), smoothness: 0.25f);
            _itemMaterials[item] = material;
            return material;
        }

        // --------------------------------------------------------------- assembly

        private void Rebuild()
        {
            if (_root != null) Destroy(_root.gameObject);

            _buildings.Clear();
            _workers.Clear();
            _itemPool.Clear();
            _buildingMaterials.Clear();
            _itemMaterials.Clear();
            _shadeMaterials.Clear();

            // Lifted well above the flat renderer's floor tone. In the 2D view the floor
            // only had to sit below the buildings in value; here it also has to receive
            // the key light and show the shadows cast onto it, and a near-black floor
            // shows neither.
            _floorMaterial = CreateLit(Palette.FloorA.Lighten(38).ToUnity());
            _floorLineMaterial = CreateLit(Palette.FloorLine.Lighten(30).ToUnity());
            _workerMaterial = CreateLit(Palette.WorkerBody.ToUnity(), smoothness: 0.2f);
            _workerTiredMaterial = CreateLit(Palette.WorkerTired.ToUnity(), smoothness: 0.2f);
            _accentMaterial = CreateLit(Palette.BuildingEdge.ToUnity(), smoothness: 0.05f);

            var holder = new GameObject("IsometricView");
            holder.transform.SetParent(transform, false);
            _root = holder.transform;

            BuildGround();
            BuildLighting();
        }

        private void BuildGround()
        {
            var map = _world.Map;

            // A single slab for the yard, with the factory floor sitting slightly proud
            // of it. The lip catches the key light and reads as a raised concrete pad.
            var yard = CreateBox("Yard", _root, CreateLit(Palette.Yard.Lighten(14).ToUnity()));
            yard.localScale = new Vector3(map.Width + 8f, 0.4f, map.Height + 8f);
            yard.localPosition = new Vector3(map.Width * 0.5f, -0.2f, map.Height * 0.5f);

            var floor = CreateBox("Floor", _root, _floorMaterial);
            floor.localScale = new Vector3(Scenarios.FloorWidth, 0.2f, Scenarios.FloorHeight);
            floor.localPosition = new Vector3(
                Scenarios.FloorOrigin.X + Scenarios.FloorWidth * 0.5f,
                0.1f,
                Scenarios.FloorOrigin.Y + Scenarios.FloorHeight * 0.5f);

            // Painted floor lines every four tiles. Cheap, and it gives the eye a sense
            // of scale that a flat colour cannot.
            for (int x = 4; x < Scenarios.FloorWidth; x += 4)
            {
                var line = CreateBox("GridX", _root, _floorLineMaterial);
                line.localScale = new Vector3(0.06f, 0.02f, Scenarios.FloorHeight);
                line.localPosition = new Vector3(
                    Scenarios.FloorOrigin.X + x, 0.21f,
                    Scenarios.FloorOrigin.Y + Scenarios.FloorHeight * 0.5f);
            }

            for (int z = 4; z < Scenarios.FloorHeight; z += 4)
            {
                var line = CreateBox("GridZ", _root, _floorLineMaterial);
                line.localScale = new Vector3(Scenarios.FloorWidth, 0.02f, 0.06f);
                line.localPosition = new Vector3(
                    Scenarios.FloorOrigin.X + Scenarios.FloorWidth * 0.5f, 0.21f,
                    Scenarios.FloorOrigin.Y + z);
            }
        }

        /// <summary>
        /// One warm key light casting shadows, plus cool ambient fill. The warm/cool
        /// split is what stops low-poly geometry looking like untextured grey boxes.
        /// </summary>
        private void BuildLighting()
        {
            var existing = FindFirstObjectByType<Light>();
            if (existing != null && existing.type == LightType.Directional)
            {
                ConfigureKeyLight(existing);
            }
            else
            {
                var holder = new GameObject("KeyLight");
                holder.transform.SetParent(_root, false);
                ConfigureKeyLight(holder.AddComponent<Light>());
            }

            // Cool sky fill against the warm key. Raised well above the default: with a
            // single directional light, ambient is the only thing keeping shadowed faces
            // from going to solid black, and solid black reads as a hole in the image.
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.55f, 0.61f, 0.74f);
            RenderSettings.ambientEquatorColor = new Color(0.40f, 0.44f, 0.53f);
            RenderSettings.ambientGroundColor = new Color(0.24f, 0.25f, 0.30f);
            RenderSettings.ambientIntensity = 1f;
        }

        private static void ConfigureKeyLight(Light light)
        {
            light.type = LightType.Directional;
            light.color = new Color(1f, 0.95f, 0.86f);
            light.intensity = 2.3f;
            light.transform.rotation = Quaternion.Euler(52f, -38f, 0f);

            light.shadows = LightShadows.Soft;
            light.shadowStrength = 0.78f;

            // Generous bias. Under an orthographic camera with a single cascade the
            // shadow map is coarse relative to these small objects, and the default bias
            // let flat roofs shadow themselves: every building rendered with a black top
            // face while its sides lit correctly.
            light.shadowBias = 0.15f;
            light.shadowNormalBias = 0.9f;
            light.shadowNearPlane = 0.2f;
        }

        // ------------------------------------------------------------------ tick

        private void LateUpdate()
        {
            if (_world == null) return;

            SyncBuildings();
            SyncWorkers();
            SyncBeltItems();
        }

        private void SyncBuildings()
        {
            for (int i = 0; i < _world.Buildings.Count; i++)
            {
                var building = _world.Buildings[i];
                if (_buildings.ContainsKey(building.Id)) continue;
                _buildings[building.Id] = CreateBuilding(building);
            }

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
                if (_buildings[stale[i]] != null) Destroy(_buildings[stale[i]].gameObject);
                _buildings.Remove(stale[i]);
            }
        }

        private Transform CreateBuilding(BuildingInstance building)
        {
            var holder = new GameObject(building.Kind + "#" + building.Id);
            holder.transform.SetParent(_root, false);
            holder.transform.position = new Vector3(
                building.Origin.X + building.Width * 0.5f, 0f,
                building.Origin.Y + building.Height * 0.5f);

            float height = HeightOf(building.Kind);
            var material = MaterialFor(building.Kind);

            if (building.IsConveyor)
            {
                BuildConveyor(holder.transform, building, height);
                return holder.transform;
            }

            // Dark plinth under every machine. It reads as a mounting plate and, more
            // usefully, separates the object from the floor colour beneath it.
            var plinth = CreateBox("Plinth", holder.transform, _accentMaterial);
            plinth.localScale = new Vector3(building.Width - 0.06f, 0.12f, building.Height - 0.06f);
            plinth.localPosition = new Vector3(0f, 0.26f, 0f);

            var body = CreateBox("Body", holder.transform, material);
            body.localScale = new Vector3(building.Width - 0.24f, height, building.Height - 0.24f);
            body.localPosition = new Vector3(0f, 0.2f + height * 0.5f + 0.12f, 0f);

            // A smaller block on top breaks the silhouette so two neighbouring machines
            // of the same footprint do not read as one long slab.
            AddRoofDetail(holder.transform, building, height, material);

            return holder.transform;
        }

        private Material ShadeFor(BuildingKind kind, int darkenPercent)
        {
            int key = (int)kind * 1000 + darkenPercent;
            if (_shadeMaterials.TryGetValue(key, out var existing)) return existing;

            var shade = CreateLit(Palette.ForBuilding(kind).Darken(darkenPercent).ToUnity());
            _shadeMaterials[key] = shade;
            return shade;
        }

        private void AddRoofDetail(Transform parent, BuildingInstance building, float height, Material material)
        {
            float top = 0.32f + height;

            // Roof pieces are a darker shade of the building's own hue, never the near
            // black edge colour: a canopy that covers the whole top face in edge colour
            // turns the roof into a hole, which is exactly how the first pass looked.
            var roofShade = ShadeFor(building.Kind, 28);
            var trimShade = ShadeFor(building.Kind, 48);

            switch (building.Kind)
            {
                case BuildingKind.Sawbench:
                case BuildingKind.Lathe:
                {
                    var drum = CreateCylinder("Drum", parent, material);
                    drum.localScale = new Vector3(0.55f, 0.16f, 0.55f);
                    drum.localPosition = new Vector3(0f, top + 0.14f, 0f);

                    var stack = CreateBox("Stack", parent, trimShade);
                    stack.localScale = new Vector3(0.18f, 0.5f, 0.18f);
                    stack.localPosition = new Vector3(building.Width * 0.28f, top + 0.25f, -building.Height * 0.28f);
                    break;
                }

                case BuildingKind.AssemblyBench:
                {
                    for (int i = 0; i < 4; i++)
                    {
                        float dx = (i % 2 == 0 ? -1f : 1f) * building.Width * 0.22f;
                        float dz = (i < 2 ? -1f : 1f) * building.Height * 0.22f;
                        var post = CreateBox("Post", parent, trimShade);
                        post.localScale = new Vector3(0.16f, 0.36f, 0.16f);
                        post.localPosition = new Vector3(dx, top + 0.18f, dz);
                    }
                    break;
                }

                case BuildingKind.Intake:
                case BuildingKind.Shipping:
                {
                    var canopy = CreateBox("Canopy", parent, roofShade);
                    canopy.localScale = new Vector3(building.Width - 0.1f, 0.1f, building.Height - 0.1f);
                    canopy.localPosition = new Vector3(0f, top + 0.06f, 0f);
                    break;
                }

                case BuildingKind.Storage:
                {
                    // Two shelf boards, so a rack reads as shelving rather than a block.
                    for (int i = 0; i < 2; i++)
                    {
                        var board = CreateBox("Shelf", parent, trimShade);
                        board.localScale = new Vector3(0.86f, 0.05f, 0.86f);
                        board.localPosition = new Vector3(0f, 0.45f + i * 0.3f, 0f);
                    }
                    break;
                }

                case BuildingKind.BreakRoom:
                {
                    var roof = CreateBox("Roof", parent, roofShade);
                    roof.localScale = new Vector3(building.Width + 0.05f, 0.12f, building.Height + 0.05f);
                    roof.localPosition = new Vector3(0f, top + 0.06f, 0f);
                    break;
                }
            }
        }

        private void BuildConveyor(Transform parent, BuildingInstance building, float height)
        {
            var bed = CreateBox("Bed", parent, CreateLit(Palette.ConveyorBed.ToUnity(), smoothness: 0.3f, metallic: 0.35f));
            bed.localScale = new Vector3(0.94f, height, 0.94f);
            bed.localPosition = new Vector3(0f, 0.2f + height * 0.5f, 0f);

            var railMaterial = CreateLit(Palette.ConveyorRail.ToUnity(), smoothness: 0.45f, metallic: 0.5f);
            bool horizontal = building.Facing == Direction.East || building.Facing == Direction.West;

            for (int side = -1; side <= 1; side += 2)
            {
                var rail = CreateBox("Rail", parent, railMaterial);
                rail.localScale = horizontal
                    ? new Vector3(0.98f, 0.1f, 0.1f)
                    : new Vector3(0.1f, 0.1f, 0.98f);
                rail.localPosition = horizontal
                    ? new Vector3(0f, 0.2f + height + 0.03f, side * 0.44f)
                    : new Vector3(side * 0.44f, 0.2f + height + 0.03f, 0f);
            }
        }

        // ---------------------------------------------------------------- workers

        private void SyncWorkers()
        {
            for (int i = 0; i < _world.Workers.Count; i++)
            {
                var worker = _world.Workers[i];
                if (!_workers.TryGetValue(worker.Id, out var rig))
                {
                    rig = CreateWorkerRig(worker);
                    _workers[worker.Id] = rig;
                }

                var position = InterpolatedPosition(worker);
                rig.Root.position = new Vector3(position.x, 0.2f, position.y);

                bool tired = worker.Stamina <= SimConfig.StaminaSeekRestThreshold;
                if (tired != rig.LastTired)
                {
                    rig.Body.sharedMaterial = tired ? _workerTiredMaterial : _workerMaterial;
                    rig.LastTired = tired;
                }

                if (worker.Carried.IsEmpty)
                {
                    rig.Carried.gameObject.SetActive(false);
                }
                else
                {
                    rig.Carried.gameObject.SetActive(true);
                    rig.CarriedRenderer.sharedMaterial = MaterialFor(worker.Carried.Item);
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
                if (_workers[stale[i]].Root != null) Destroy(_workers[stale[i]].Root.gameObject);
                _workers.Remove(stale[i]);
            }
        }

        private WorkerRig CreateWorkerRig(WorkerUnit worker)
        {
            var holder = new GameObject("Worker " + worker.Name);
            holder.transform.SetParent(_root, false);

            var body = CreateCapsule("Body", holder.transform, _workerMaterial);
            body.localScale = new Vector3(0.34f, 0.26f, 0.34f);
            body.localPosition = new Vector3(0f, 0.3f, 0f);

            var head = CreateSphere("Head", holder.transform, _workerMaterial);
            head.localScale = new Vector3(0.28f, 0.28f, 0.28f);
            head.localPosition = new Vector3(0f, 0.66f, 0f);

            var carried = CreateBox("Carried", holder.transform, MaterialFor(ItemId.Log));
            carried.localScale = new Vector3(0.26f, 0.26f, 0.26f);
            carried.localPosition = new Vector3(0.26f, 0.62f, 0f);
            carried.gameObject.SetActive(false);

            return new WorkerRig
            {
                Root = holder.transform,
                Body = body.GetComponent<Renderer>(),
                Carried = carried,
                CarriedRenderer = carried.GetComponent<Renderer>()
            };
        }

        private static Vector2 InterpolatedPosition(WorkerUnit worker)
        {
            var from = new Vector2(worker.Pos.X + 0.5f, worker.Pos.Y + 0.5f);
            if (worker.PathCursor >= worker.Path.Count) return from;

            var next = worker.NextStep;
            var to = new Vector2(next.X + 0.5f, next.Y + 0.5f);
            return Vector2.Lerp(from, to, worker.StepProgressPermille / 1000f);
        }

        // ------------------------------------------------------------ belt cargo

        private void SyncBeltItems()
        {
            _itemsUsed = 0;

            for (int i = 0; i < _world.Buildings.Count; i++)
            {
                var belt = _world.Buildings[i];
                if (!belt.IsConveyor) continue;

                for (int slot = 0; slot < ConveyorState.Capacity; slot++)
                {
                    var item = belt.Conveyor.ItemAt(slot);
                    if (item == ItemId.None) continue;

                    var cube = RentItem();
                    cube.GetComponent<Renderer>().sharedMaterial = MaterialFor(item);

                    float travel = belt.Conveyor.PositionPermille(slot) / 1000f;
                    var offset = belt.Facing.Offset();
                    cube.position = new Vector3(
                        belt.Origin.X + 0.5f + offset.X * (travel - 0.5f),
                        0.2f + HeightOf(BuildingKind.Conveyor) + 0.14f,
                        belt.Origin.Y + 0.5f + offset.Y * (travel - 0.5f));
                }
            }

            for (int i = _itemsUsed; i < _itemPool.Count; i++)
            {
                _itemPool[i].gameObject.SetActive(false);
            }
        }

        private Transform RentItem()
        {
            if (_itemsUsed < _itemPool.Count)
            {
                var existing = _itemPool[_itemsUsed++];
                existing.gameObject.SetActive(true);
                return existing;
            }

            var cube = CreateBox("BeltItem", _root, MaterialFor(ItemId.Log));
            cube.localScale = new Vector3(0.26f, 0.22f, 0.26f);
            _itemPool.Add(cube);
            _itemsUsed++;
            return cube;
        }

        // ------------------------------------------------------------- primitives

        private static Transform CreateBox(string name, Transform parent, Material material)
            => CreatePrimitive(PrimitiveType.Cube, name, parent, material);

        private static Transform CreateCylinder(string name, Transform parent, Material material)
            => CreatePrimitive(PrimitiveType.Cylinder, name, parent, material);

        private static Transform CreateCapsule(string name, Transform parent, Material material)
            => CreatePrimitive(PrimitiveType.Capsule, name, parent, material);

        private static Transform CreateSphere(string name, Transform parent, Material material)
            => CreatePrimitive(PrimitiveType.Sphere, name, parent, material);

        private static Transform CreatePrimitive(PrimitiveType type, string name, Transform parent, Material material)
        {
            var holder = GameObject.CreatePrimitive(type);
            holder.name = name;
            holder.transform.SetParent(parent, false);

            // Colliders cost memory and CPU and nothing here is ever raycast: selection
            // works off the simulation grid, not physics.
            var collider = holder.GetComponent<Collider>();
            if (collider != null) Destroy(collider);

            holder.GetComponent<Renderer>().sharedMaterial = material;
            return holder.transform;
        }
    }
}
