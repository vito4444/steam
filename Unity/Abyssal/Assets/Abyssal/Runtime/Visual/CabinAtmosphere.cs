using System.Collections.Generic;
using UnityEngine;

namespace Abyssal.Visual
{
    /// <summary>
    /// 舱室在不同事故状态下的视觉表现。
    ///
    /// 这是「单场景」方案能不能站住的关键。一个永远长得一样的控制舱看十分钟就腻了；
    /// 但如果井涌时整个舱室被旋转警报灯染成红色、井喷时蒸汽从管道接缝里喷出来、
    /// 断电时只剩仪表自己的微光和窗外的探照灯，那么同一个空间就能撑起整局游戏。
    ///
    /// 所有效果都是灯光、粒子和材质参数的组合，不需要额外的模型或贴图。
    /// </summary>
    public sealed class CabinAtmosphere
    {
        /// <summary>舱室的整体状态。每一档对应一组完全不同的光照和粒子配置。</summary>
        public enum State
        {
            /// <summary>正常钻进。暖色工作灯，一切安静。</summary>
            Normal,
            /// <summary>井控警报。红色旋转灯启动，主照明被压暗让红光读得出来。</summary>
            Alarm,
            /// <summary>井喷。蒸汽、火花、剧烈震动，红光压过一切。</summary>
            Blowout,
            /// <summary>断电。主照明全灭，只剩应急灯、仪表自发光和窗外的探照灯。</summary>
            BlackOut,
        }

        public State Current { get; private set; } = State.Normal;

        readonly List<Light> _mainLights = new List<Light>();
        readonly List<float> _mainIntensities = new List<float>();

        Light _beaconPort;
        Light _beaconStbd;
        Transform _beaconPortHousing;
        Transform _beaconStbdHousing;
        Light _emergencyLight;
        ParticleSystem _steam;
        ParticleSystem _sparks;

        float _shakeAmplitude;
        float _beaconAngle;

        /// <summary>相机每帧应该叠加的抖动偏移。井喷时平台整个在震。</summary>
        public Vector3 CameraShake { get; private set; }

        /// <summary>
        /// 在已经建好的舱室上装事故表现。
        /// <paramref name="existingLights"/> 是正常照明，切状态时要按比例压暗或熄灭。
        /// </summary>
        public void Build(Transform parent, IEnumerable<Light> existingLights)
        {
            foreach (var light in existingLights)
            {
                if (light == null) continue;
                _mainLights.Add(light);
                _mainIntensities.Add(light.intensity);
            }

            var root = new GameObject("Atmosphere").transform;
            root.SetParent(parent, false);

            BuildBeacons(root);
            BuildEmergencyLighting(root);
            BuildSteam(root);
            BuildSparks(root);

            Apply(State.Normal, 0f);
        }

        /// <summary>
        /// 旋转警报灯。真实平台上这种灯装在天花板角落，
        /// 亮起来时会把整个空间周期性地扫成红色——这是「出事了」最直观的信号。
        /// </summary>
        void BuildBeacons(Transform root)
        {
            (string name, float x, bool port)[] spots =
            {
                ("BeaconPort", -1.68f, true),
                ("BeaconStbd", 1.68f, false),
            };

            foreach (var (name, x, port) in spots)
            {
                var go = new GameObject(name);
                go.transform.SetParent(root, false);
                go.transform.localPosition = new Vector3(x, 2.42f, 1.55f);

                // 灯罩。不亮的时候它是个暗红色的塑料壳，本身就是个环境细节。
                var dome = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                dome.name = "Dome";
                Object.DestroyImmediate(dome.GetComponent<Collider>());
                dome.transform.SetParent(go.transform, false);
                dome.transform.localScale = new Vector3(0.16f, 0.10f, 0.16f);
                dome.GetComponent<MeshRenderer>().sharedMaterial =
                    MaterialLibrary.Emissive($"beacondome_{name}", null,
                        new Color(1.00f, 0.12f, 0.06f), 0.12f);

                var mount = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                mount.name = "Mount";
                Object.DestroyImmediate(mount.GetComponent<Collider>());
                mount.transform.SetParent(go.transform, false);
                mount.transform.localPosition = new Vector3(0f, 0.055f, 0f);
                mount.transform.localScale = new Vector3(0.19f, 0.02f, 0.19f);
                mount.GetComponent<MeshRenderer>().sharedMaterial = MaterialLibrary.PipeSteel;

                var light = go.AddComponent<Light>();
                light.type = LightType.Spot;
                light.color = new Color(1.00f, 0.16f, 0.09f);
                light.intensity = 0f;
                light.range = 6.5f;
                light.spotAngle = 78f;
                light.innerSpotAngle = 20f;
                light.shadows = LightShadows.None;
                go.transform.localEulerAngles = new Vector3(58f, port ? 40f : -40f, 0f);

                if (port) { _beaconPort = light; _beaconPortHousing = go.transform; }
                else { _beaconStbd = light; _beaconStbdHousing = go.transform; }
            }
        }

        /// <summary>断电后唯一还亮着的东西。绿色是安全出口标识的通用色。</summary>
        void BuildEmergencyLighting(Transform root)
        {
            var go = new GameObject("EmergencyLight");
            go.transform.SetParent(root, false);
            go.transform.localPosition = new Vector3(0f, 2.34f, -0.55f);

            var fixture = GameObject.CreatePrimitive(PrimitiveType.Cube);
            fixture.name = "Fixture";
            Object.DestroyImmediate(fixture.GetComponent<Collider>());
            fixture.transform.SetParent(go.transform, false);
            fixture.transform.localScale = new Vector3(0.30f, 0.09f, 0.05f);
            fixture.GetComponent<MeshRenderer>().sharedMaterial =
                MaterialLibrary.Emissive("emergencylens", null, new Color(0.24f, 1.00f, 0.42f), 1.4f);

            _emergencyLight = go.AddComponent<Light>();
            _emergencyLight.type = LightType.Point;
            _emergencyLight.color = new Color(0.36f, 1.00f, 0.52f);
            _emergencyLight.intensity = 0f;
            _emergencyLight.range = 5.5f;
            _emergencyLight.shadows = LightShadows.None;
        }

        /// <summary>
        /// 从管道接缝喷出的蒸汽。井喷时井底的高温流体窜上来，
        /// 地面管汇的密封先扛不住——这是玩家能在舱内直接看到的第一个物理后果。
        /// </summary>
        void BuildSteam(Transform root)
        {
            var go = new GameObject("SteamLeak");
            go.transform.SetParent(root, false);
            go.transform.localPosition = new Vector3(-0.78f, 0.90f, 0.86f);
            go.transform.localEulerAngles = new Vector3(-78f, 12f, 0f);

            _steam = go.AddComponent<ParticleSystem>();
            var main = _steam.main;
            main.duration = 4f;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 2.1f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2.4f, 5.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.10f, 0.34f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.86f, 0.88f, 0.90f, 0.42f),
                new Color(1.00f, 0.98f, 0.94f, 0.68f));
            main.gravityModifier = -0.18f;
            main.maxParticles = 1600;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.playOnAwake = false;

            var emission = _steam.emission;
            emission.rateOverTime = 260f;

            var shape = _steam.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 11f;
            shape.radius = 0.03f;

            var size = _steam.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f,
                AnimationCurve.EaseInOut(0f, 0.35f, 1f, 2.6f));

            var color = _steam.colorOverLifetime;
            color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[]
                {
                    new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.85f, 0.18f),
                    new GradientAlphaKey(0f, 1f),
                });
            color.color = gradient;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = MaterialLibrary.Particle("steam",
                ProceduralTextures.SoftDot(128), new Color(0.90f, 0.93f, 0.95f), 0.85f);
        }

        /// <summary>配电盘打火。断电和井喷时都会出现，是画面里唯一的高频闪烁元素。</summary>
        void BuildSparks(Transform root)
        {
            var go = new GameObject("Sparks");
            go.transform.SetParent(root, false);
            go.transform.localPosition = new Vector3(1.32f, 1.86f, 1.58f);
            go.transform.localEulerAngles = new Vector3(28f, -142f, 0f);

            _sparks = go.AddComponent<ParticleSystem>();
            var main = _sparks.main;
            main.duration = 2.4f;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.22f, 0.75f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.8f, 5.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.008f, 0.028f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(1.00f, 0.82f, 0.36f, 1f),
                new Color(1.00f, 0.52f, 0.14f, 1f));
            main.gravityModifier = 1.4f;
            main.maxParticles = 260;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.playOnAwake = false;

            var emission = _sparks.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[]
            {
                new ParticleSystem.Burst(0.0f, 14, 26, 1, 0.01f),
                new ParticleSystem.Burst(0.9f, 8, 18, 1, 0.01f),
                new ParticleSystem.Burst(1.7f, 20, 34, 1, 0.01f),
            });

            var shape = _sparks.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 34f;
            shape.radius = 0.05f;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.velocityScale = 0.06f;
            renderer.lengthScale = 2.4f;
            renderer.sharedMaterial = MaterialLibrary.Particle("sparks",
                ProceduralTextures.SoftDot(32), new Color(1.00f, 0.72f, 0.30f), 3.2f);
        }

        /// <summary>
        /// 切换状态。<paramref name="severity"/> 为 0–1，用于在同一状态内表现严重程度，
        /// 比如井涌刚开始和快压不住时的区别。
        /// </summary>
        public void Apply(State state, float severity)
        {
            Current = state;
            severity = Mathf.Clamp01(severity);

            float mainScale;
            float beaconPeak;
            float emergency;
            bool steam, sparks;

            switch (state)
            {
                case State.Alarm:
                    // 主照明压到六成。红灯要能读出来，靠的是周围变暗而不是红灯变亮。
                    mainScale = 0.60f;
                    beaconPeak = 14f + severity * 10f;
                    emergency = 0f;
                    steam = false;
                    sparks = false;
                    _shakeAmplitude = 0.0016f * severity;
                    break;

                case State.Blowout:
                    mainScale = 0.34f;
                    beaconPeak = 30f;
                    emergency = 1.6f;
                    steam = true;
                    sparks = true;
                    _shakeAmplitude = 0.010f + 0.013f * severity;
                    break;

                case State.BlackOut:
                    // 全灭。仪表的自发光和窗外的探照灯成为唯一光源，
                    // 这是整个游戏里画面反差最大的一刻。
                    mainScale = 0f;
                    beaconPeak = 0f;
                    emergency = 2.4f;
                    steam = false;
                    sparks = true;
                    _shakeAmplitude = 0.0022f;
                    break;

                default:
                    mainScale = 1f;
                    beaconPeak = 0f;
                    emergency = 0f;
                    steam = false;
                    sparks = false;
                    _shakeAmplitude = 0f;
                    break;
            }

            for (int i = 0; i < _mainLights.Count; i++)
            {
                if (_mainLights[i] != null)
                    _mainLights[i].intensity = _mainIntensities[i] * mainScale;
            }

            _beaconPeak = beaconPeak;
            if (_emergencyLight != null) _emergencyLight.intensity = emergency;

            _steamActive = steam;
            _sparksActive = sparks;
            SetParticles(_steam, steam);
            SetParticles(_sparks, sparks);
        }

        float _beaconPeak;

        static void SetParticles(ParticleSystem ps, bool on)
        {
            if (ps == null) return;
            if (on)
            {
                if (!ps.isPlaying) ps.Play();
            }
            else if (ps.isPlaying)
            {
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }

        /// <summary>每帧更新旋转警报灯和相机抖动。</summary>
        public void Tick(float deltaTime)
        {
            _beaconAngle = Mathf.Repeat(_beaconAngle + deltaTime * 190f, 360f);

            if (_beaconPortHousing != null)
                _beaconPortHousing.localEulerAngles = new Vector3(58f, _beaconAngle, 0f);
            if (_beaconStbdHousing != null)
                _beaconStbdHousing.localEulerAngles = new Vector3(58f, -_beaconAngle + 180f, 0f);

            // 旋转灯的亮度跟着转角走：光束扫过舱内时亮，背对时暗。
            // 这比单纯的闪烁更像真的，也更让人紧张。
            float sweep = Mathf.Pow(Mathf.Abs(Mathf.Cos(_beaconAngle * Mathf.Deg2Rad)), 2.2f);
            if (_beaconPort != null) _beaconPort.intensity = _beaconPeak * sweep;
            if (_beaconStbd != null) _beaconStbd.intensity = _beaconPeak * (1f - sweep * 0.75f);

            if (_shakeAmplitude <= 0f)
            {
                CameraShake = Vector3.zero;
                return;
            }

            // 多个不同频率的正弦叠加，避免抖动读起来有周期性。
            float t = Time.time;
            CameraShake = new Vector3(
                (Mathf.Sin(t * 27.3f) + Mathf.Sin(t * 41.7f) * 0.6f) * _shakeAmplitude,
                (Mathf.Sin(t * 33.1f) + Mathf.Sin(t * 19.4f) * 0.7f) * _shakeAmplitude,
                Mathf.Sin(t * 23.9f) * _shakeAmplitude * 0.5f);
        }

        /// <summary>让粒子系统预演一段时间，供批处理截图使用。</summary>
        public void PrewarmForCapture(float seconds = 3f)
        {
            Prewarm(_steam, _steamActive, seconds);
            Prewarm(_sparks, _sparksActive, seconds);
        }

        static void Prewarm(ParticleSystem ps, bool active, float seconds)
        {
            if (ps == null) return;
            if (!active)
            {
                ps.Clear(true);
                return;
            }
            // 不能用 isPlaying 判断：编辑器批处理下没有播放循环，它恒为 false。
            ps.Clear(true);
            ps.Simulate(seconds, true, true);
        }

        bool _steamActive;
        bool _sparksActive;
    }
}
