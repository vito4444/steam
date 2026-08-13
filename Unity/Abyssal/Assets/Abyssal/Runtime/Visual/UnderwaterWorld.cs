using System.Collections.Generic;
using UnityEngine;

namespace Abyssal.Visual
{
    /// <summary>
    /// 舷窗外的深海。
    ///
    /// 之前这里只是一块纯黑的板子，结果整个舱室看起来像个封死的盒子。
    /// 一块什么都没有的黑玻璃传达不出「外面是三千米深的海」，
    /// 它只传达「这里没做完」。
    ///
    /// 真实深海里能看见的东西其实很少，但恰恰是这「很少」需要被做出来：
    /// 探照灯照亮的一小片水体、被光柱切出来的悬浮颗粒、
    /// 光边缘之外若隐若现的钢结构，以及偶尔从视野边缘掠过的东西。
    /// 玩家看不清外面，但必须能感觉到外面有东西。
    /// </summary>
    public sealed class UnderwaterWorld
    {
        public Transform Root { get; private set; }

        readonly List<Transform> _drifters = new List<Transform>();
        ParticleSystem _marineSnow;
        Light _floodlight;
        Transform _beam;
        Transform _creature;

        /// <summary>
        /// 在 <paramref name="parent"/> 下搭建舷窗外的世界。
        /// <paramref name="origin"/> 是窗外空间的中心，通常在舷窗正前方几米处。
        /// </summary>
        public void Build(Transform parent, Vector3 origin)
        {
            Root = new GameObject("UnderwaterWorld").transform;
            Root.SetParent(parent, false);
            Root.localPosition = origin;

            BuildWaterVolume();
            BuildFloodlight();
            BuildStructure();
            BuildMarineSnow();
            BuildCreature();
        }

        /// <summary>
        /// 深海本体。用一个巨大的内表面盒子把窗外整个包住，
        /// 让玩家无论从什么角度看出去都不会看到「场景边缘」。
        /// </summary>
        void BuildWaterVolume()
        {
            // 一个朝内的大盒子。用极暗的蓝黑色，比舱内的黑更冷。
            var water = GameObject.CreatePrimitive(PrimitiveType.Cube);
            water.name = "WaterVolume";
            Object.DestroyImmediate(water.GetComponent<Collider>());
            water.transform.SetParent(Root, false);
            water.transform.localPosition = new Vector3(0f, 0f, 14f);
            water.transform.localScale = new Vector3(-46f, -30f, -46f);
            water.GetComponent<MeshRenderer>().sharedMaterial =
                MaterialLibrary.Emissive("deepwater", null, new Color(0.006f, 0.013f, 0.019f), 1f);
        }

        /// <summary>
        /// 平台探照灯。这是窗外唯一的光源，也是整个画面里最重要的一笔：
        /// 它把「一片黑」变成「一片被照亮的水，和它照不到的更深的黑」。
        /// </summary>
        void BuildFloodlight()
        {
            var go = new GameObject("Floodlight");
            go.transform.SetParent(Root, false);
            // 必须放在舱壁外侧。放在里面的话光会被舱壁整个挡掉，
            // 窗外还是一片黑，而且不会有任何报错提示。
            go.transform.localPosition = new Vector3(-2.6f, 2.9f, 1.8f);
            go.transform.localEulerAngles = new Vector3(22f, 26f, 0f);

            _floodlight = go.AddComponent<Light>();
            _floodlight.type = LightType.Spot;
            // 深海探照灯偏冷偏绿，水体会把红光吃掉。
            _floodlight.color = new Color(0.62f, 0.86f, 0.92f);
            // 强度要远高于舱内灯具：光在水里衰减极快，
            // 而且这盏灯要照亮的是十几米外的钢结构。
            _floodlight.intensity = 240f;
            _floodlight.range = 46f;
            _floodlight.spotAngle = 62f;
            _floodlight.innerSpotAngle = 18f;
            _floodlight.shadows = LightShadows.None;

            // 灯具本体，让光有个看得见的来源。
            var housing = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            housing.name = "FloodlightHousing";
            Object.DestroyImmediate(housing.GetComponent<Collider>());
            housing.transform.SetParent(go.transform, false);
            housing.transform.localEulerAngles = new Vector3(90f, 0f, 0f);
            housing.transform.localScale = new Vector3(0.42f, 0.34f, 0.42f);
            housing.GetComponent<MeshRenderer>().sharedMaterial = MaterialLibrary.PipeSteel;

            var lens = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            lens.name = "Lens";
            Object.DestroyImmediate(lens.GetComponent<Collider>());
            lens.transform.SetParent(go.transform, false);
            lens.transform.localPosition = new Vector3(0f, 0f, 0.36f);
            lens.transform.localEulerAngles = new Vector3(90f, 0f, 0f);
            lens.transform.localScale = new Vector3(0.34f, 0.04f, 0.34f);
            lens.GetComponent<MeshRenderer>().sharedMaterial =
                MaterialLibrary.Emissive("floodlens", null, new Color(0.75f, 0.95f, 1.00f), 3.4f);

            // 可见的光柱。URP 没有体积光，用一个贴渐变的空心锥面来假装，
            // 代价几乎为零，而深海探照灯那道切开黑水的光束正是这个场景的标志性画面。
            _beam = MeshShapes.Create("Beam", go.transform, MeshShapes.Cone(28),
                MaterialLibrary.Particle("floodbeam", ProceduralTextures.BeamGradient(),
                    new Color(0.42f, 0.68f, 0.78f), 0.55f),
                new Vector3(0f, 0f, 0.30f), new Vector3(90f, 0f, 0f),
                new Vector3(38f, 32f, 38f)).transform;
        }

        /// <summary>
        /// 窗外的钢结构：钻井导管架的一角、几根立管、一段桁架。
        ///
        /// 这些东西的作用是给「深」提供参照。没有任何参照物的黑水
        /// 在视觉上等价于纯色背景；有了半截被光照到的钢梁，
        /// 玩家才能感受到自己是被一个巨大的东西包着的。
        /// </summary>
        void BuildStructure()
        {
            var rig = new GameObject("Structure").transform;
            rig.SetParent(Root, false);

            // 四根主立柱，向下延伸出视野。
            (float x, float z, float scale)[] legs =
            {
                (-4.6f, 7.0f, 1.00f),
                (4.9f, 8.2f, 1.05f),
                (-7.8f, 13.5f, 0.85f),
                (8.6f, 15.0f, 0.80f),
            };

            foreach (var (x, z, scale) in legs)
            {
                Cylinder(rig, "Leg", new Vector3(x, -6f, z), Vector3.zero,
                    new Vector3(0.62f * scale, 12f, 0.62f * scale), MaterialLibrary.PipeSteel);

                // 立柱上的加强环，是判断距离的重要线索。
                for (int i = 0; i < 5; i++)
                {
                    Cylinder(rig, "Collar", new Vector3(x, -10f + i * 2.6f, z), Vector3.zero,
                        new Vector3(0.78f * scale, 0.10f, 0.78f * scale), MaterialLibrary.RustAccent);
                }
            }

            // 斜撑桁架。
            (Vector3 pos, Vector3 rot, float len)[] braces =
            {
                (new Vector3(0.2f, 1.4f, 7.6f), new Vector3(0f, 0f, 58f), 6.4f),
                (new Vector3(0.4f, -2.6f, 7.6f), new Vector3(0f, 0f, -62f), 6.0f),
                (new Vector3(-6.2f, 0.6f, 10.4f), new Vector3(0f, 24f, 48f), 5.2f),
            };

            foreach (var (pos, rot, len) in braces)
            {
                Cylinder(rig, "Brace", pos, rot, new Vector3(0.30f, len, 0.30f),
                    MaterialLibrary.PipeSteel);
            }

            // 一束向下的立管。它们是画面里唯一的垂直重复元素，
            // 会在探照灯下形成一组明暗相间的竖线，很有辨识度。
            for (int i = 0; i < 6; i++)
            {
                Cylinder(rig, $"Riser{i}", new Vector3(-1.6f + i * 0.44f, -4f, 9.2f), Vector3.zero,
                    new Vector3(0.16f, 10f, 0.16f), MaterialLibrary.PipeSteel);
            }

            // 远处更暗的一段结构，只在雾里露出个轮廓。
            Cylinder(rig, "FarMass", new Vector3(-12f, -2f, 22f), new Vector3(0f, 0f, 12f),
                new Vector3(2.4f, 9f, 2.4f),
                MaterialLibrary.Solid("farsteel", new Color(0.05f, 0.07f, 0.08f), 0.4f, 0.15f));
        }

        /// <summary>
        /// 深海雪：不断从上方飘落的有机碎屑。
        ///
        /// 这是让窗外「活起来」最便宜的一招。静止的水和飘着颗粒的水，
        /// 在观感上是死物和活物的区别。它还兼任光柱的显影剂——
        /// 探照灯的光锥只有打在颗粒上才看得见。
        /// </summary>
        void BuildMarineSnow()
        {
            var go = new GameObject("MarineSnow");
            go.transform.SetParent(Root, false);
            go.transform.localPosition = new Vector3(0f, 6f, 8f);

            _marineSnow = go.AddComponent<ParticleSystem>();
            var main = _marineSnow.main;
            main.duration = 12f;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(14f, 26f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.06f, 0.22f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.055f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.72f, 0.80f, 0.78f, 0.55f),
                new Color(0.88f, 0.92f, 0.86f, 0.85f));
            main.gravityModifier = 0.008f;
            main.maxParticles = 3200;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.playOnAwake = true;

            var emission = _marineSnow.emission;
            emission.rateOverTime = 190f;

            var shape = _marineSnow.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(26f, 1f, 22f);

            // 极缓慢的横向漂移，模拟深层洋流。完全垂直下落看起来很假。
            var velocity = _marineSnow.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.x = new ParticleSystem.MinMaxCurve(-0.10f, 0.06f);
            velocity.y = new ParticleSystem.MinMaxCurve(-0.16f, -0.04f);
            velocity.z = new ParticleSystem.MinMaxCurve(-0.05f, 0.05f);

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = MaterialLibrary.Particle("marinesnow",
                ProceduralTextures.SoftDot(), new Color(0.80f, 0.88f, 0.86f), 1.6f);
            renderer.sortingOrder = 1;

            // 让粒子系统在编辑器批处理下也能有内容可渲染，
            // 否则截图时会是一片空的（粒子还没来得及生成）。
            _marineSnow.Simulate(18f, true, true);
            _marineSnow.Play();
        }

        /// <summary>
        /// 偶尔从视野外缘经过的东西。
        ///
        /// 它永远不会被完全照亮，永远不停留，也永远不解释是什么。
        /// 这是方案 A 里恐怖层的唯一载体：不追击、不伤害、不发出巨响，
        /// 只是让玩家不确定自己刚才看到了什么。
        /// </summary>
        void BuildCreature()
        {
            var go = new GameObject("Passerby");
            go.transform.SetParent(Root, false);
            go.transform.localPosition = new Vector3(-16f, -1.2f, 13f);

            // 造型刻意保持模糊：一段拉长的躯体加几片鳍，
            // 在雾和暗光下只能读出一个轮廓。
            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            Object.DestroyImmediate(body.GetComponent<Collider>());
            body.transform.SetParent(go.transform, false);
            body.transform.localEulerAngles = new Vector3(0f, 0f, 90f);
            body.transform.localScale = new Vector3(1.5f, 5.2f, 1.5f);

            var silhouette = MaterialLibrary.Solid("creature",
                new Color(0.020f, 0.026f, 0.028f), 0.05f, 0.30f);
            body.GetComponent<MeshRenderer>().sharedMaterial = silhouette;

            (Vector3 pos, Vector3 rot, Vector3 scale)[] fins =
            {
                (new Vector3(-2.0f, 0.9f, 0f), new Vector3(0f, 0f, 24f), new Vector3(2.6f, 0.12f, 1.4f)),
                (new Vector3(-2.2f, -0.8f, 0f), new Vector3(0f, 0f, -30f), new Vector3(2.2f, 0.12f, 1.2f)),
                (new Vector3(4.4f, 0.2f, 0f), new Vector3(0f, 0f, 8f), new Vector3(1.8f, 0.10f, 2.2f)),
            };

            foreach (var (pos, rot, scale) in fins)
            {
                var fin = GameObject.CreatePrimitive(PrimitiveType.Cube);
                fin.name = "Fin";
                Object.DestroyImmediate(fin.GetComponent<Collider>());
                fin.transform.SetParent(go.transform, false);
                fin.transform.localPosition = pos;
                fin.transform.localEulerAngles = rot;
                fin.transform.localScale = scale;
                fin.GetComponent<MeshRenderer>().sharedMaterial = silhouette;
            }

            _creature = go.transform;
            _drifters.Add(_creature);
        }

        /// <summary>
        /// 让窗外的东西动起来。<paramref name="creatureProgress"/> 为 0–1，
        /// 控制那个东西横穿视野的进度，超过 1 之后它就消失了。
        /// </summary>
        public void Tick(float deltaTime, float creatureProgress, bool creatureVisible)
        {
            if (_creature != null)
            {
                _creature.gameObject.SetActive(creatureVisible);
                if (creatureVisible)
                {
                    float x = Mathf.Lerp(-18f, 18f, Mathf.Clamp01(creatureProgress));
                    float y = -1.2f + Mathf.Sin(creatureProgress * Mathf.PI * 2f) * 0.9f;
                    _creature.localPosition = new Vector3(x, y, 13f);
                    _creature.localEulerAngles = new Vector3(
                        0f, 0f, Mathf.Sin(creatureProgress * Mathf.PI * 3f) * 6f);
                }
            }

            // 探照灯有极轻微的摆动，因为整个平台在洋流里是会晃的。
            if (_floodlight != null)
            {
                float t = Time.time;
                _floodlight.transform.localEulerAngles = new Vector3(
                    28f + Mathf.Sin(t * 0.23f) * 1.4f,
                    22f + Mathf.Sin(t * 0.17f) * 2.1f,
                    0f);
            }
        }

        /// <summary>探照灯故障时窗外会彻底陷入黑暗，这是断电事件最有冲击力的部分。</summary>
        public void SetFloodlight(float intensity)
        {
            intensity = Mathf.Clamp01(intensity);
            if (_floodlight != null) _floodlight.intensity = 240f * intensity;
            if (_beam != null) _beam.gameObject.SetActive(intensity > 0.02f);
        }

        static GameObject Cylinder(Transform parent, string name, Vector3 pos, Vector3 rot,
                                   Vector3 scale, Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localEulerAngles = rot;
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            return go;
        }
    }
}
