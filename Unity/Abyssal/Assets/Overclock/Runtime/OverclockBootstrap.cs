using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Overclock.Core;

namespace Overclock
{
    /// <summary>
    /// OVERCLOCK 的运行时入口。场景文件里只有这一个组件，
    /// 硅层、视图、HUD、输入和后处理全部在这里于运行时搭建。
    ///
    /// 和 ABYSSAL 一样走运行时构建：所有材质和贴图都是程序化生成的运行时对象，
    /// 序列化进场景会让文件膨胀且重新打开时引用失效。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OverclockBootstrap : MonoBehaviour
    {
        [Header("棋盘")]
        [Tooltip("硅层的宽度，格。")]
        [Range(10, 32)] public int GridWidth = 18;

        [Tooltip("硅层的高度，格。")]
        [Range(8, 20)] public int GridHeight = 11;

        [Header("局")]
        [Tooltip("留空表示每次启动都随机。")]
        public int Seed;

        [Tooltip("打穿多少层算通关。")]
        [Range(3, 16)] public int TotalLayers = 8;

        public RunState Run { get; private set; }
        public SiliconLayer Layer { get; private set; }

        LayerView _view;
        GameHud _hud;
        UpgradeScreen _screen;
        PlayerController _controller;
        Camera _camera;

        float _elapsed;
        bool _awaitingChoice;

        void Awake()
        {
            if (Seed == 0) Seed = Random.Range(1, int.MaxValue);

            _camera = CreateCamera();
            CreatePostProcessing();

            Run = new RunState(Seed, TotalLayers);
            StartLayer();

            _hud = new GameHud();
            _hud.Build(null, _camera);

            _screen = new UpgradeScreen();
            _screen.Build(_hud.Canvas.transform);

            Application.targetFrameRate = 120;
        }

        void StartLayer()
        {
            _view?.Dispose();

            Layer = Run.CreateLayer(GridWidth, GridHeight);
            _view = new LayerView();
            _view.Build(Layer, null);

            _controller = new PlayerController(Layer, _view, _camera);
            _controller.ResetForNewLayer();
            _awaitingChoice = false;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            _elapsed += dt;

            var keyboard = Keyboard.current;
            var mouse = Mouse.current;

            if (_screen.Current != UpgradeScreen.Mode.Hidden)
            {
                TickScreen(keyboard, mouse);
                _view.TickPackets(_elapsed);
                return;
            }

            _controller.Tick(dt);

            // 改了布局就立刻推进一小步，让流量在同一帧内反映出来。
            // 拖着鼠标铺线时如果要等到下一次定时重算，手感会明显发黏。
            Layer.Step(_controller.LayoutChanged ? 0.001 : Mathf.Min(dt, 0.1f));

            _view.Refresh();
            _view.TickPackets(_elapsed);
            _hud.Refresh(Layer, Run.LayerIndex, _controller.SelectedSlot);

            CheckLayerOutcome();
        }

        void CheckLayerOutcome()
        {
            if (_awaitingChoice) return;

            if (Layer.Cleared)
            {
                _awaitingChoice = true;

                if (Run.LayerIndex >= Run.TotalLayers)
                {
                    _screen.ShowOutcome(true, Run.LayerIndex,
                        $"PEAK {Layer.HistoricalPeak:F0}C");
                    return;
                }

                var offers = Run.DrawUpgrades();
                if (offers.Count == 0)
                {
                    Run.AdvanceLayer();
                    StartLayer();
                    return;
                }

                _screen.ShowUpgrades(offers, Run.LayerIndex);
            }
            else if (Layer.Failed)
            {
                _awaitingChoice = true;
                _screen.ShowOutcome(false, Run.LayerIndex - 1,
                    $"BURNED {Layer.BurnedCount}");
            }
        }

        void TickScreen(Keyboard keyboard, Mouse mouse)
        {
            _screen.Tick(keyboard, mouse, _camera);

            if (_screen.Current == UpgradeScreen.Mode.Upgrade)
            {
                int pick = _screen.PickedIndex;
                if (pick < 0) return;

                var offers = Run.DrawUpgrades();
                if (pick < offers.Count) Run.TakeUpgrade(offers[pick]);

                _screen.ConsumePick();
                _screen.Hide();
                Run.AdvanceLayer();
                StartLayer();
                return;
            }

            if (!_screen.Acknowledged) return;

            // 结算之后开新的一局。
            _screen.Hide();
            Seed = Random.Range(1, int.MaxValue);
            Run = new RunState(Seed, TotalLayers);
            StartLayer();
        }

        Camera CreateCamera()
        {
            var cam = Camera.main;
            if (cam == null)
            {
                var go = new GameObject("MainCamera") { tag = "MainCamera" };
                cam = go.AddComponent<Camera>();
            }

            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 140f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            // 纯黑背景。这套视觉的前提是「除了发光的东西，什么都看不见」。
            cam.backgroundColor = Color.black;
            cam.allowHDR = true;
            cam.allowMSAA = true;

            var data = cam.GetComponent<UniversalAdditionalCameraData>()
                       ?? cam.gameObject.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            data.antialiasingQuality = AntialiasingQuality.High;
            data.renderShadows = false;

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = Color.black;
            RenderSettings.fog = false;

            return cam;
        }

        void CreatePostProcessing()
        {
            if (FindFirstObjectByType<Volume>() != null) return;

            var go = new GameObject("PostProcessing");
            var volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 1f;

            var profile = ScriptableObject.CreateInstance<VolumeProfile>();

            // 场景里没有任何光源，所有的体积感都来自发光线条的 Bloom 溢出。
            var bloom = profile.Add<Bloom>(true);
            bloom.threshold.Override(0.92f);
            bloom.intensity.Override(1.35f);
            bloom.scatter.Override(0.78f);
            bloom.tint.Override(new Color(0.86f, 0.96f, 1.00f));
            bloom.highQualityFiltering.Override(true);

            var vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(0.34f);
            vignette.smoothness.Override(0.62f);
            vignette.color.Override(Color.black);

            var grading = profile.Add<ColorAdjustments>(true);
            grading.postExposure.Override(0.18f);
            grading.contrast.Override(16f);
            grading.saturation.Override(22f);

            var aberration = profile.Add<ChromaticAberration>(true);
            aberration.intensity.Override(0.20f);

            var tonemap = profile.Add<Tonemapping>(true);
            tonemap.mode.Override(TonemappingMode.ACES);

            volume.sharedProfile = profile;
        }
    }
}
