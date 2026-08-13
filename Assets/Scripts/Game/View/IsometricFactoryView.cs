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

        /// <summary>
        /// Diagnostic switch for --no-point-lights. Two independent video reviews
        /// reported the scene as having no shadows while stills appeared to show them,
        /// and the suspicion is that the machine lamps are filling in every shadowed
        /// face. Being able to remove them without a code change makes that testable in
        /// one build instead of several.
        /// </summary>
        private bool _pointLightsDisabled;

        private readonly Dictionary<BuildingKind, Material> _buildingMaterials = new Dictionary<BuildingKind, Material>();
        private readonly Dictionary<ItemId, Material> _itemMaterials = new Dictionary<ItemId, Material>();
        private readonly Dictionary<int, Material> _shadeMaterials = new Dictionary<int, Material>();
        private readonly Dictionary<int, Transform> _buildings = new Dictionary<int, Transform>();
        private readonly Dictionary<int, BuildingRig> _rigs = new Dictionary<int, BuildingRig>();
        private readonly Dictionary<int, WorkerRig> _workers = new Dictionary<int, WorkerRig>();
        private readonly List<Transform> _itemPool = new List<Transform>();
        private int _itemsUsed;

        private Material _floorMaterial;
        private Material _floorLineMaterial;
        private Material _workerMaterial;
        private Material _workerTiredMaterial;
        private Material _accentMaterial;
        private Material _metalMaterial;
        private Material _darkMetalMaterial;
        private Material _glassMaterial;
        private Material _hazardMaterial;
        private Material _helmetMaterial;
        private Material _shadowMaterial;
        private IsometricBuildingBuilder _builder;

        private sealed class BuildingRig
        {
            public Transform Root;
            public MachineAnimator Spinner;
            public Light Lamp;
            public Renderer Indicator;
            public Material IndicatorMaterial;
            public ParticleSystem Emitter;
        }

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
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--no-point-lights") _pointLightsDisabled = true;
            }

            _runner = GetComponent<SimRunner>();
            _runner.WorldCreated += OnWorldCreated;
            if (_runner.World != null) OnWorldCreated(_runner.World);
        }

        private void OnDestroy()
        {
            if (_runner != null) _runner.WorldCreated -= OnWorldCreated;
            ProceduralMesh.ClearCache();
            ProceduralTextures.ClearCache();
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

        private static Material CreateLit(Color color, float smoothness = 0.15f, float metallic = 0f,
            Texture2D surface = null, Vector2 tiling = default)
        {
            var shader = ResolveLitShader();
            if (shader == null) return null;

            var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            material.color = color;

            // The texture multiplies the base colour, so one greyscale surface map can
            // serve every hue instead of needing a variant per material.
            if (surface != null)
            {
                if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", surface);
                else if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", surface);

                // Meshes are unit sized with 0..1 UVs, so a texture is stretched across
                // whatever the object is scaled to. On the floor slab that meant one
                // 192 pixel texture spread over twenty four tiles, which is why doubling
                // its contrast changed nothing measurable. Tiling has to be set per
                // surface, in tiles.
                if (tiling != default)
                {
                    if (material.HasProperty("_BaseMap")) material.SetTextureScale("_BaseMap", tiling);
                    else if (material.HasProperty("_MainTex")) material.SetTextureScale("_MainTex", tiling);
                }
            }

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

        /// <summary>
        /// A darker shade of a building's own hue, cached per kind and depth.
        ///
        /// Roof and trim pieces have to be darker than the body without being black.
        /// The first pass used the near-black edge colour for these, which turned every
        /// canopy into a hole in the image under a light that was working correctly.
        /// </summary>
        private Material ShadeFor(BuildingKind kind, int darkenPercent)
        {
            int key = (int)kind * 1000 + darkenPercent;
            if (_shadeMaterials.TryGetValue(key, out var existing)) return existing;

            var baseColor = Palette.ForBuilding(kind);
            var shade = CreateLit(
                darkenPercent <= 0 ? baseColor.ToUnity() : baseColor.Darken(darkenPercent).ToUnity(),
                surface: ProceduralTextures.PaintedPanel(), tiling: new Vector2(2f, 2f));

            _shadeMaterials[key] = shade;
            return shade;
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
            _rigs.Clear();
            _workers.Clear();
            _itemPool.Clear();
            _buildingMaterials.Clear();
            _itemMaterials.Clear();
            _shadeMaterials.Clear();

            // Lifted well above the flat renderer's floor tone. In the 2D view the floor
            // only had to sit below the buildings in value; here it also has to receive
            // the key light and show the shadows cast onto it, and a near-black floor
            // shows neither.
            // Warm concrete rather than the palette's blue-grey. The flat renderer needed
            // a cold dark floor to sit under flat sprites; lit geometry standing on it
            // needs a surface that reflects the warm key, or the whole interior reads
            // colder than the grass outside it.
            _floorMaterial = CreateLit(new Color(0.50f, 0.49f, 0.46f), surface: ProceduralTextures.Concrete(),
                tiling: new Vector2(Scenarios.FloorWidth * 0.5f, Scenarios.FloorHeight * 0.5f));
            _floorLineMaterial = CreateLit(new Color(0.50f, 0.48f, 0.45f));
            _workerMaterial = CreateLit(Palette.WorkerBody.ToUnity(), smoothness: 0.2f);
            _workerTiredMaterial = CreateLit(Palette.WorkerTired.ToUnity(), smoothness: 0.2f);
            _accentMaterial = CreateLit(Palette.BuildingEdge.ToUnity(), smoothness: 0.05f);

            // A small shared set of surface treatments. Machines are mostly their own
            // hue, but bare metal, dark castings, lit glass and hazard paint appear on
            // several of them and reading as the same material each time is what makes
            // the factory look like one designed object rather than a kit of parts.
            _metalMaterial = CreateLit(new Color(0.60f, 0.63f, 0.69f), smoothness: 0.45f, metallic: 0.45f, surface: ProceduralTextures.BrushedMetal(), tiling: new Vector2(2f, 2f));
            _darkMetalMaterial = CreateLit(new Color(0.26f, 0.28f, 0.33f), smoothness: 0.42f, metallic: 0.55f, surface: ProceduralTextures.BrushedMetal(), tiling: new Vector2(2f, 2f));
            _glassMaterial = CreateLit(new Color(1f, 0.90f, 0.66f), smoothness: 0.85f);
            if (_glassMaterial != null && _glassMaterial.HasProperty("_EmissionColor"))
            {
                _glassMaterial.EnableKeyword("_EMISSION");
                _glassMaterial.SetColor("_EmissionColor", new Color(1f, 0.74f, 0.38f) * 8f);
                _glassMaterial.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            _hazardMaterial = CreateLit(new Color(0.92f, 0.72f, 0.18f), smoothness: 0.3f);
            _helmetMaterial = CreateLit(new Color(1f, 0.68f, 0.12f), smoothness: 0.5f);
            SetEmission(_helmetMaterial, new Color(1f, 0.55f, 0.08f), 0.55f);

            _shadowMaterial = BlobShadows.CreateMaterial();

            _builder = new IsometricBuildingBuilder(ShadeFor, _metalMaterial, _darkMetalMaterial,
                _glassMaterial, _hazardMaterial);
            _builder.SetCrateMaterialLookup(MaterialFor);

            var holder = new GameObject("IsometricView");
            holder.transform.SetParent(transform, false);
            _root = holder.transform;

            BuildGround();
            BuildLighting();
            BuildDressing();
            PostProcessingRig.Install(Camera.main, _root);
        }

        private void BuildGround()
        {
            var map = _world.Map;

            // A single slab for the yard, with the factory floor sitting slightly proud
            // of it. The lip catches the key light and reads as a raised concrete pad.
            var yard = CreateBox("Yard", _root, CreateLit(new Color(0.30f, 0.29f, 0.28f),
                surface: ProceduralTextures.Concrete(), tiling: new Vector2(18f, 14f)));
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

        private void BuildDressing()
        {
            var dressing = new IsometricSceneDressing(
                _root,
                concrete: CreateLit(new Color(0.34f, 0.35f, 0.38f), surface: ProceduralTextures.Concrete(),
                    tiling: new Vector2(20f, 16f)),
                paint: CreateLit(new Color(0.80f, 0.82f, 0.84f), smoothness: 0.05f),
                hazard: _hazardMaterial,
                timber: CreateLit(new Color(0.62f, 0.45f, 0.28f), smoothness: 0.1f, surface: ProceduralTextures.Wood(), tiling: new Vector2(2f, 2f)),
                drum: CreateLit(new Color(0.32f, 0.46f, 0.40f), smoothness: 0.35f, metallic: 0.3f, surface: ProceduralTextures.BrushedMetal()),
                grass: CreateLit(new Color(0.19f, 0.25f, 0.17f), surface: ProceduralTextures.Ground(),
                    tiling: new Vector2(22f, 18f)),
                foliage: CreateLit(new Color(0.17f, 0.29f, 0.19f)),
                trunk: CreateLit(new Color(0.28f, 0.22f, 0.17f)),
                makeMaterial: color => CreateLit(color, smoothness: 0.25f, surface: ProceduralTextures.PaintedPanel()));

            dressing.Build(_world);
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

            // Flat ambient, and the skybox explicitly cleared.
            //
            // Trilight looked correct in code and did nothing in the build: the scene
            // still had Unity's default skybox, which keeps contributing environment
            // light, and RenderSettings.ambientIntensity only applies in Skybox mode.
            // The result was a dusk rig that rendered at noon. Flat mode takes a single
            // colour and cannot be quietly overridden by an asset nobody set.
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.115f, 0.135f, 0.19f);
            RenderSettings.defaultReflectionMode = UnityEngine.Rendering.DefaultReflectionMode.Custom;
            RenderSettings.customReflectionTexture = null;
            // Dusk sky: cool and dim, so the warm key and the point lights own the image.
            // ambientSkyColor aliases ambientLight in Flat mode, so it is set last and
            // is the value that actually takes effect.
            RenderSettings.ambientSkyColor = new Color(0.115f, 0.135f, 0.19f);

            Debug.Log("[worker] lighting: ambientMode=" + RenderSettings.ambientMode
                      + " ambientLight=" + RenderSettings.ambientLight
                      + " skybox=" + (RenderSettings.skybox == null ? "none" : RenderSettings.skybox.name));
        }

        /// <summary>
        /// Late afternoon, low and warm.
        ///
        /// The scene was previously lit like noon, which is the worst possible choice
        /// for it: a high white key flattens everything it touches and drowns out the
        /// machine lamps and window glow entirely. Dropping the sun to a low angle gives
        /// every object a long shadow and a bright rim, and darkening the ambient lets
        /// the small warm lights actually read. It is the single biggest change to the
        /// mood of the image, and it costs one rotation and three colours.
        /// </summary>
        private static void ConfigureKeyLight(Light light)
        {
            light.type = LightType.Directional;
            light.color = new Color(1f, 0.70f, 0.44f);
            // Raised, and the point lights cut hard, after a video review found the
            // scene reading as unlit. Eight point lights at intensity 13-18 were filling
            // in every shadowed face the key light produced: the shadows were being
            // rendered and then immediately lit back up.
            light.intensity = 2.1f;
            light.transform.rotation = Quaternion.Euler(24f, -42f, 0f);

            light.shadows = LightShadows.Soft;
            light.shadowStrength = 1f;

            // Generous bias. Under an orthographic camera with a single cascade the
            // shadow map is coarse relative to these small objects, and the default bias
            // let flat roofs shadow themselves: every building rendered with a black top
            // face while its sides lit correctly.
            light.shadowBias = 0.15f;
            light.shadowNormalBias = 0.9f;
            light.shadowNearPlane = 0.2f;

            Debug.Log("[worker] shadows=" + light.shadows + " strength=" + light.shadowStrength
                      + " qualityShadowDistance=" + QualitySettings.shadowDistance
                      + " qualityShadows=" + QualitySettings.shadows);
            Debug.Log("[worker] key light: intensity=" + light.intensity
                      + " colour=" + light.color
                      + " rotation=" + light.transform.rotation.eulerAngles);
        }

        // ------------------------------------------------------------------ tick

        private void LateUpdate()
        {
            if (_world == null) return;

            SyncBuildings();
            UpdateLifeSigns();
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
                building.Origin.X + building.Width * 0.5f, 0.2f,
                building.Origin.Y + building.Height * 0.5f);

            float height = HeightOf(building.Kind);
            BlobShadows.Attach(holder.transform, _shadowMaterial,
                building.Width * 0.92f, building.Height * 0.92f, height);

            if (building.IsConveyor)
            {
                BuildConveyor(holder.transform, building, height);
                return holder.transform;
            }

            // Belts rotate; everything else is authored facing the camera so the machine
            // details stay legible from this fixed angle.
            _builder.Build(holder.transform, building);
            AttachLifeSigns(holder.transform, building);
            return holder.transform;
        }

        private void BuildConveyor(Transform parent, BuildingInstance building, float height)
        {
            bool horizontal = building.Facing == Direction.East || building.Facing == Direction.West;

            // Legs first: a belt that stands on something reads as machinery, and the
            // gap underneath is where the ambient occlusion does its work.
            for (int side = -1; side <= 1; side += 2)
            {
                var leg = CreateBox("Leg", parent, _darkMetalMaterial);
                leg.localScale = new Vector3(0.1f, height, 0.1f);
                leg.localPosition = horizontal
                    ? new Vector3(side * 0.34f, height * 0.5f, 0f)
                    : new Vector3(0f, height * 0.5f, side * 0.34f);
            }

            var bed = CreateBox("Bed", parent, CreateLit(Palette.ConveyorBed.Darken(12).ToUnity(),
                smoothness: 0.18f, metallic: 0.1f, surface: ProceduralTextures.BeltSurface(),
                tiling: new Vector2(1f, 1f)));

            // Scrolling the belt surface is what stops cargo looking like it floats.
            var scroller = bed.gameObject.AddComponent<BeltScroller>();
            scroller.Speed = 0.55f;
            scroller.Direction = horizontal ? Vector2.right : Vector2.up;
            bed.localScale = horizontal
                ? new Vector3(1.0f, 0.08f, 0.66f)
                : new Vector3(0.66f, 0.08f, 1.0f);
            bed.localPosition = new Vector3(0f, height + 0.04f, 0f);

            var railMaterial = CreateLit(Palette.ConveyorRail.Darken(18).ToUnity(), smoothness: 0.3f, metallic: 0.25f);
            for (int side = -1; side <= 1; side += 2)
            {
                var rail = CreateBox("Rail", parent, railMaterial);
                rail.localScale = horizontal
                    ? new Vector3(1.0f, 0.09f, 0.07f)
                    : new Vector3(0.07f, 0.09f, 1.0f);
                rail.localPosition = horizontal
                    ? new Vector3(0f, height + 0.1f, side * 0.31f)
                    : new Vector3(side * 0.31f, height + 0.1f, 0f);
            }

            // End rollers, which also visually join one tile of belt to the next.
            for (int end = -1; end <= 1; end += 2)
            {
                var roller = CreateCylinder("Roller", parent, _metalMaterial);
                roller.localScale = new Vector3(0.16f, 0.32f, 0.16f);
                roller.localPosition = horizontal
                    ? new Vector3(end * 0.47f, height + 0.05f, 0f)
                    : new Vector3(0f, height + 0.05f, end * 0.47f);
                roller.localRotation = horizontal
                    ? Quaternion.Euler(90f, 0f, 0f)
                    : Quaternion.Euler(0f, 0f, 90f);
            }
        }

        /// <summary>
        /// Adds the parts that make a machine look alive: a warm working light, a status
        /// lamp, and whatever spins.
        ///
        /// These read from a distance in a way that geometry does not. A player scanning
        /// the floor sees which benches are lit and which have gone dark long before
        /// they could read a progress bar, and bloom turns the emissive parts into the
        /// brightest thing in frame, which is what gives the image a focal point.
        /// </summary>
        private void AttachLifeSigns(Transform holder, BuildingInstance building)
        {
            var rig = new BuildingRig { Root = holder };

            if (_pointLightsDisabled)
            {
                _rigs[building.Id] = rig;
                return;
            }

            if (building.Def.IsStation)
            {
                // Status lamp on a short post, its own material so it can change colour.
                rig.IndicatorMaterial = CreateLit(new Color(0.2f, 0.9f, 0.35f), smoothness: 0.7f);
                SetEmission(rig.IndicatorMaterial, new Color(0.25f, 1f, 0.4f), 9f);

                var lamp = CreateBox("StatusLamp", holder, rig.IndicatorMaterial);
                lamp.localScale = new Vector3(0.13f, 0.13f, 0.13f);
                lamp.localPosition = new Vector3(building.Width * 0.5f - 0.22f, 1.24f, -building.Height * 0.5f + 0.22f);
                rig.Indicator = lamp.GetComponent<Renderer>();

                var post = CreateCylinder("LampPost", holder, _darkMetalMaterial);
                post.localScale = new Vector3(0.05f, 0.28f, 0.05f);
                post.localPosition = lamp.localPosition + new Vector3(0f, -0.28f, 0f);

                var light = new GameObject("WorkLight");
                light.transform.SetParent(holder, false);
                light.transform.localPosition = new Vector3(0f, 1.5f, 0f);

                rig.Lamp = light.AddComponent<Light>();
                rig.Lamp.type = LightType.Point;
                rig.Lamp.color = new Color(1f, 0.80f, 0.50f);
                rig.Lamp.range = 4.5f;
                rig.Lamp.intensity = 4.5f;
                rig.Lamp.shadows = LightShadows.None;

                rig.Spinner = FindSpinner(holder, building);
                rig.Emitter = AttachEmitter(holder, building);
            }
            else if (building.Kind == BuildingKind.BreakRoom)
            {
                var light = new GameObject("RoomLight");
                light.transform.SetParent(holder, false);
                light.transform.localPosition = new Vector3(0f, 0.9f, 0f);

                rig.Lamp = light.AddComponent<Light>();
                rig.Lamp.type = LightType.Point;
                rig.Lamp.color = new Color(1f, 0.72f, 0.40f);
                rig.Lamp.range = 5f;
                rig.Lamp.intensity = 6f;
                rig.Lamp.shadows = LightShadows.None;
            }
            else if (building.Kind == BuildingKind.Intake || building.Kind == BuildingKind.Shipping)
            {
                var floodMaterial = CreateLit(new Color(0.95f, 0.95f, 0.9f), smoothness: 0.6f);
                SetEmission(floodMaterial, new Color(1f, 0.95f, 0.82f), 10f);

                var flood = CreateBox("Floodlight", holder, floodMaterial);
                flood.localScale = new Vector3(0.4f, 0.1f, 0.16f);
                flood.localPosition = new Vector3(0f, 1.42f, -0.5f);

                var light = new GameObject("DockLight");
                light.transform.SetParent(holder, false);
                light.transform.localPosition = new Vector3(0f, 1.3f, 0.4f);

                rig.Lamp = light.AddComponent<Light>();
                rig.Lamp.type = LightType.Point;
                rig.Lamp.color = new Color(1f, 0.92f, 0.78f);
                rig.Lamp.range = 4.5f;
                rig.Lamp.intensity = 5f;
                rig.Lamp.shadows = LightShadows.None;
            }

            _rigs[building.Id] = rig;
        }

        /// <summary>
        /// Sawdust for cutting machines, steam for the rest. Emission is driven by the
        /// simulation, so a machine that has run out of material goes quiet.
        /// </summary>
        private ParticleSystem AttachEmitter(Transform holder, BuildingInstance building)
        {
            switch (building.Kind)
            {
                case BuildingKind.Sawbench:
                    return MachineParticles.AttachSawdust(holder, new Vector3(0f, 1.05f, 0.1f),
                        CreateLit(new Color(0.80f, 0.63f, 0.38f), smoothness: 0.05f));

                case BuildingKind.Lathe:
                    return MachineParticles.AttachSawdust(holder, new Vector3(0.05f, 0.95f, 0.2f),
                        CreateLit(new Color(0.74f, 0.58f, 0.34f), smoothness: 0.05f));

                case BuildingKind.AssemblyBench:
                    return MachineParticles.AttachSteam(holder, new Vector3(0f, 1.2f, -0.5f),
                        CreateLit(new Color(0.82f, 0.84f, 0.88f), smoothness: 0.1f));

                default:
                    return null;
            }
        }

        /// <summary>The part that should turn: the sawbench blade or the lathe spindle.</summary>
        private static MachineAnimator FindSpinner(Transform holder, BuildingInstance building)
        {
            string wanted = building.Kind == BuildingKind.Sawbench ? "Blade"
                : building.Kind == BuildingKind.Lathe ? "Spindle"
                : null;

            if (wanted == null) return null;

            var part = holder.Find(wanted);
            if (part == null) return null;

            var animator = part.gameObject.AddComponent<MachineAnimator>();

            // Both parts are cylinders laid on their side, so their local Y is the
            // rotation axis in both cases.
            animator.AxisLocal = Vector3.up;
            animator.DegreesPerSecond = building.Kind == BuildingKind.Sawbench ? 900f : 420f;
            return animator;
        }

        private static void SetEmission(Material material, Color color, float intensity)
        {
            if (material == null || !material.HasProperty("_EmissionColor")) return;

            material.EnableKeyword("_EMISSION");
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            material.SetColor("_EmissionColor", color * intensity);
        }

        /// <summary>Drives the lights and moving parts from simulation state each frame.</summary>
        private void UpdateLifeSigns()
        {
            for (int i = 0; i < _world.Buildings.Count; i++)
            {
                var building = _world.Buildings[i];
                if (!_rigs.TryGetValue(building.Id, out var rig)) continue;

                bool working = building.Def.IsStation && building.WorkProgress > 0;

                if (rig.Spinner != null) rig.Spinner.Active = working;
                MachineParticles.SetEmitting(rig.Emitter, working);

                if (rig.Lamp != null && building.Def.IsStation)
                {
                    // Idle benches dim rather than switch off, so a dark machine reads as
                    // stalled instead of as missing.
                    rig.Lamp.intensity = working ? 5.5f : 1.8f;
                }

                if (rig.IndicatorMaterial == null) continue;

                bool starved = building.Def.IsStation && !working && !building.CanStartWork();
                var color = starved ? new Color(1f, 0.45f, 0.2f) : new Color(0.25f, 1f, 0.4f);
                rig.IndicatorMaterial.color = color;
                SetEmission(rig.IndicatorMaterial, color, starved ? 8f : 9f);
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

            // Proportions matter more than polygon count at this size. A narrow body, a
            // clearly separated head and a hard hat give a silhouette that stays human
            // at roughly twenty pixels tall, which is all a worker ever occupies here.
            var body = CreateCapsule("Body", holder.transform, _workerMaterial);
            body.localScale = new Vector3(0.36f, 0.26f, 0.36f);
            body.localPosition = new Vector3(0f, 0.3f, 0f);

            for (int side = -1; side <= 1; side += 2)
            {
                var arm = CreateCapsule("Arm", holder.transform, _workerMaterial);
                arm.localScale = new Vector3(0.11f, 0.13f, 0.11f);
                arm.localPosition = new Vector3(side * 0.2f, 0.34f, 0.02f);
            }

            var head = CreateSphere("Head", holder.transform, _workerMaterial);
            head.localScale = new Vector3(0.28f, 0.28f, 0.28f);
            head.localPosition = new Vector3(0f, 0.66f, 0f);

            // The hat is the brightest thing on the figure and reads before anything
            // else, which is what makes people findable in a busy factory.
            var helmet = CreateSphere("Helmet", holder.transform, _helmetMaterial);
            helmet.localScale = new Vector3(0.33f, 0.2f, 0.33f);
            helmet.localPosition = new Vector3(0f, 0.74f, 0f);

            var brim = CreateCylinder("Brim", holder.transform, _helmetMaterial);
            brim.localScale = new Vector3(0.36f, 0.02f, 0.36f);
            brim.localPosition = new Vector3(0f, 0.63f, 0.02f);

            BlobShadows.Attach(holder.transform, _shadowMaterial, 0.5f, 0.5f, 0.7f);

            var carried = CreateBox("Carried", holder.transform, MaterialFor(ItemId.Log));
            carried.localScale = new Vector3(0.24f, 0.24f, 0.24f);
            carried.localPosition = new Vector3(0f, 0.58f, 0.28f);
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
            => MeshObjects.Box(name, parent, material);

        private static Transform CreateCylinder(string name, Transform parent, Material material)
            => MeshObjects.Cylinder(name, parent, material);

        private static Transform CreateCapsule(string name, Transform parent, Material material)
            => MeshObjects.Capsule(name, parent, material);

        private static Transform CreateSphere(string name, Transform parent, Material material)
            => MeshObjects.Sphere(name, parent, material);
    }
}
