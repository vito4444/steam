using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Abyssal.Core;
using Abyssal.Visual;

namespace Abyssal
{
    /// <summary>
    /// 游戏启动入口。整个控制舱在这里于运行时搭建，场景文件里只有这一个组件。
    ///
    /// 之所以不把舱室存成场景资产：舱内所有贴图和材质都是程序化生成的运行时对象，
    /// 一旦被序列化进场景，场景文件会膨胀到五十兆以上，而且重新打开时这些引用会失效。
    /// 运行时构建反而更小、更快，也让编辑器截图和实际游戏走的是完全同一条代码路径。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CabinBootstrap : MonoBehaviour
    {
        [Header("视角")]
        [Tooltip("站姿视场角。模拟类不需要广角，窄一点更有幽闭感。")]
        [Range(40f, 80f)] public float StandingFieldOfView = 55f;

        [Tooltip("初始俯角。让控制台占据画面下三分之二。")]
        [Range(0f, 30f)] public float InitialPitch = 15f;

        [Header("仿真")]
        [Tooltip("启动时的井深，米。")]
        public double StartDepth = 1985.0;

        [Tooltip("启动时的泥浆密度，kg/m³。")]
        public double StartMudDensity = 1265.0;

        [Tooltip("启动时的钻头磨损度 0–1。上一班留下来的。")]
        [Range(0f, 1f)] public float StartBitWear = 0.31f;

        public CabinBuilder Cabin { get; private set; }
        public DrillSimulation Simulation { get; private set; }
        public Camera PlayerCamera { get; private set; }

        DrillControls _controls = DrillControls.NominalDrilling;

        // 窗外那个东西的出现节奏。间隔很长是故意的：
        // 它一旦变得可预期，恐怖就退化成了背景装饰。
        const float PassbyInterval = 190f;
        const float PassbyDuration = 26f;
        float _passbyTimer;

        void Awake()
        {
            Cabin = new CabinBuilder();
            Cabin.Build();

            _controls.TargetMudDensity = StartMudDensity;
            Simulation = new DrillSimulation(
                WellProfiles.ShiftOne(),
                new DrillState
                {
                    Depth = StartDepth,
                    MudDensity = StartMudDensity,
                    BitWear = StartBitWear,
                });

            PlayerCamera = EnsureCamera();
            EnsurePostProcessing();
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            // 仿真步长上限 0.25 秒。更大的步长会让井涌的指数增长失真，
            // 掉帧时宁可让仿真慢下来也不能让它跳过临界点。
            Simulation.Step(_controls, Mathf.Min(dt, 0.25f));
            DriveInstruments(dt);
            DriveUnderwater(dt);
        }

        /// <summary>
        /// 窗外的深海。那个东西每隔几分钟才会经过一次，
        /// 而且只在玩家没有直视舷窗的时候开始移动——
        /// 让它成为余光里的事件，而不是一场表演。
        /// </summary>
        void DriveUnderwater(float dt)
        {
            if (Cabin.Underwater == null) return;

            _passbyTimer += dt;
            bool visible = false;
            float progress = 0f;

            if (_passbyTimer > PassbyInterval)
            {
                float elapsed = _passbyTimer - PassbyInterval;
                progress = elapsed / PassbyDuration;
                visible = progress <= 1f;
                if (!visible) _passbyTimer = 0f;
            }

            Cabin.Underwater.Tick(dt, progress, visible);
        }

        /// <summary>把仿真状态推到面板上的每一个仪表、旋钮和指示灯。</summary>
        void DriveInstruments(float dt)
        {
            var s = Simulation.State;

            Track("wob", (float)(_controls.WeightOnBit / Limits.MaxWeightOnBit), dt);
            Track("rpm", (float)(_controls.RotarySpeed / Limits.MaxRotarySpeed), dt);
            Track("torque", (float)(s.Torque / 50.0), dt, 0.22f);
            Track("ecd", (float)((s.EquivalentCirculatingDensity - 1000.0) / 1000.0), dt, 0.60f);
            Track("pump", (float)(_controls.PumpRate / Limits.MaxPumpRate), dt);
            Track("rop", (float)(s.RateOfPenetration / 40.0), dt, 0.55f);
            Track("pit", (float)((s.PitVolume - 50.0) / 25.0), dt, 1.20f);
            Track("temp", (float)(s.BottomholeTemperature / 200.0), dt, 1.60f);
            Track("wear", (float)s.BitWear, dt, 2.00f);
            Track("depth", (float)((s.Depth - 1800.0) / 1600.0), dt, 2.00f);

            // 告警灯。红灯用 2 Hz 闪烁，橙灯常亮——
            // 闪烁留给「现在就要处理」的情况，常亮表示「记着这件事」。
            float blink = Mathf.Repeat(Time.time * 2f, 1f) < 0.5f ? 1f : 0.08f;

            Lamp("kick", s.KickVolume > 0.25 ? blink : 0f);
            Lamp("loss", s.LossVolume > 0.4 ? 0.85f : 0f);
            Lamp("gas", s.KickIsGas ? blink : 0f);
            Lamp("torq", s.Torque > Limits.TorqueLimit ? blink : 0f);
            Lamp("stuck", (float)Mathf.Clamp01((float)s.StuckSeverity * 2f) * 0.9f);
            Lamp("temp", s.BottomholeTemperature > Limits.BitTemperatureWarning ? 0.9f : 0f);
            Lamp("wear", (float)s.BitWear > 0.7 ? blink : (float)s.BitWear * 0.5f);
            Lamp("pump", s.IsCirculating ? 0.8f : 0f);
            Lamp("pwr", 1f);
            Lamp("comm", 0.85f);
            Lamp("run", _controls.BitOnBottom && s.RateOfPenetration > 0.1 ? 1f : 0f);
            Lamp("hold", _controls.BitOnBottom ? 0f : 0.8f);

            Knob("wob", (float)(_controls.WeightOnBit / Limits.MaxWeightOnBit));
            Knob("rpm", (float)(_controls.RotarySpeed / Limits.MaxRotarySpeed));
            Knob("mud", (float)((_controls.TargetMudDensity - Limits.MinMudDensity)
                                / (Limits.MaxMudDensity - Limits.MinMudDensity)));
            Knob("pump", (float)(_controls.PumpRate / Limits.MaxPumpRate));
            Knob("choke", (float)_controls.ChokeOpening);

            Lever("bit", _controls.BitOnBottom ? 1f : -1f);
            Lever("bop", _controls.BlowoutPreventerClosed ? 1f : -1f);
        }

        void Track(string key, float value, float dt, float response = 0.35f)
        {
            if (Cabin.Gauges.TryGetValue(key, out var g)) g.TrackNormalized(value, dt, response);
        }

        void Lamp(string key, float intensity)
        {
            if (Cabin.Lamps.TryGetValue(key, out var l)) l.SetIntensity(intensity);
        }

        void Knob(string key, float value)
        {
            if (Cabin.Knobs.TryGetValue(key, out var k)) k.SetNormalized(value);
        }

        void Lever(string key, float position)
        {
            if (Cabin.Levers.TryGetValue(key, out var l)) l.SetPosition(position);
        }

        Camera EnsureCamera()
        {
            var cam = Camera.main;
            if (cam == null)
            {
                var go = new GameObject("MainCamera") { tag = "MainCamera" };
                cam = go.AddComponent<Camera>();
            }

            cam.transform.SetPositionAndRotation(
                CabinBuilder.EyePosition, Quaternion.Euler(InitialPitch, 0f, 0f));
            cam.fieldOfView = StandingFieldOfView;
            cam.nearClipPlane = 0.03f;
            cam.farClipPlane = 60f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.004f, 0.008f, 0.010f);
            cam.allowHDR = true;
            cam.allowMSAA = true;

            var data = cam.GetComponent<UniversalAdditionalCameraData>()
                       ?? cam.gameObject.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            data.antialiasingQuality = AntialiasingQuality.High;
            data.renderShadows = true;

            return cam;
        }

        void EnsurePostProcessing()
        {
            if (FindFirstObjectByType<Volume>() != null) return;

            var go = new GameObject("PostProcessing");
            var volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 1f;

            var profile = Resources.Load<VolumeProfile>("AbyssalProfile");
            if (profile != null)
            {
                volume.sharedProfile = profile;
                return;
            }

            // 找不到打包好的配置就现场建一份，保证构建出来的版本不会因为
            // 缺一个资产就退化成没有任何调色的画面。参数与编辑器侧保持一致。
            profile = ScriptableObject.CreateInstance<VolumeProfile>();

            var bloom = profile.Add<Bloom>(true);
            bloom.threshold.Override(1.05f);
            bloom.intensity.Override(0.95f);
            bloom.scatter.Override(0.74f);
            bloom.tint.Override(new Color(1.00f, 0.86f, 0.68f));

            var vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(0.44f);
            vignette.smoothness.Override(0.52f);
            vignette.color.Override(new Color(0.02f, 0.014f, 0.008f));

            var grading = profile.Add<ColorAdjustments>(true);
            grading.postExposure.Override(0.10f);
            grading.contrast.Override(26f);
            grading.saturation.Override(9f);
            grading.colorFilter.Override(new Color(1.03f, 0.94f, 0.84f));

            var grain = profile.Add<FilmGrain>(true);
            grain.type.Override(FilmGrainLookup.Medium2);
            grain.intensity.Override(0.28f);
            grain.response.Override(0.75f);

            var tonemap = profile.Add<Tonemapping>(true);
            tonemap.mode.Override(TonemappingMode.ACES);

            volume.sharedProfile = profile;
        }
    }
}
