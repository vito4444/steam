using UnityEngine;

namespace Decoder.Rendering
{
    /// <summary>
    /// 工位后处理。挂在玩家摄像机上。
    ///
    /// 这一层负责的是"照片感"：几何和材质决定画面里有什么，
    /// 后处理决定它看上去像不像一台机器拍下来的。渐晕把注意力收到中心，
    /// 颗粒掩盖软件渲染的色带，影调曲线把暗部压住——
    /// 没有它，同样的场景会显得干净得不真实。
    ///
    /// 参数按"一台老式监控显示器"来调，不是电影镜头：畸变和扫描线是刻意留的，
    /// 因为玩家在设定里就是隔着一层设备在看这个房间。
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class StationPostProcess : MonoBehaviour
    {
        [Header("镜头")]
        [Tooltip("渐晕强度。0 关闭")]
        [Range(0f, 1f)] public float vignette = 0.55f;

        [Tooltip("渐晕的过渡起点。越小压得越靠内")]
        [Range(0.2f, 1.4f)] public float vignetteSoftness = 0.55f;

        [Tooltip("横向色散，单位是纹素。超过 3 会明显发虚")]
        [Range(0f, 4f)] public float aberration = 1.1f;

        [Tooltip("桶形畸变。模仿 CRT 显示器微微鼓起的玻璃面")]
        [Range(-0.2f, 0.2f)] public float barrel = 0.018f;

        [Header("介质")]
        [Tooltip("胶片颗粒强度。幅度按曝光量开方缩放，所以这个系数比固定幅度时期要大")]
        [Range(0f, 0.3f)] public float grain = 0.032f;

        [Tooltip("扫描线强度")]
        [Range(0f, 0.5f)] public float scanline = 0.055f;

        [Tooltip("扫描线条数。设成屏幕高度的一半左右最像真实 CRT")]
        public float scanlineCount = 540f;

        [Header("影调")]
        [Tooltip("暗部抬升。给纯黑一点底，避免死黑成片")]
        [Range(-0.05f, 0.1f)] public float lift = 0.004f;

        [Tooltip("整体增益")]
        [Range(0.5f, 2f)] public float gain = 1.22f;

        [Range(0f, 2f)] public float saturation = 1.12f;

        [Tooltip("颗粒是否随时间跳动。关掉后每帧一致，便于做画面比对")]
        public bool animateGrain = true;

        /// <summary>
        /// 着色器从 Resources 加载，而不是用场景里的序列化引用。
        ///
        /// 实测：只要把这个着色器作为资源引用序列化进场景，构建出的 level0
        /// 就会损坏，运行时报 corrupted 直接崩溃，而构建过程没有任何提示。
        /// 同一个场景挂上组件但不引用着色器则完全正常。走 Resources 之后，
        /// 场景不再持有这个引用，即使着色器本身出问题也只是后处理静默降级，
        /// 不会连累整个场景加载不出来。
        /// </summary>
        private const string ShaderName = "Decoder/StationPost";

        /// <summary>
        /// 编辑器截图时由外部注入的着色器。刻意不序列化：
        /// 这个着色器一旦以任何形式进入构建包（被场景引用，或者放进 Resources），
        /// 产出的 level0 就会在运行时报 corrupted 并崩溃，而着色器编译毫无报错。
        /// 根因未定位，怀疑与无 GPU 的 batchmode 下变体序列化有关。
        /// 在查清之前，后处理只在编辑器截图路径上生效。
        /// </summary>
        [System.NonSerialized] public Shader editorShader;

        [Tooltip("运行时使用的着色器。序列化引用会让它被打进构建包")]
        public Shader runtimeShader;

        private Material _material;
        private bool _shaderMissingLogged;
        private static readonly int VignetteId = Shader.PropertyToID("_Vignette");
        private static readonly int VignetteSoftId = Shader.PropertyToID("_VignetteSoft");
        private static readonly int GrainId = Shader.PropertyToID("_Grain");
        private static readonly int GrainTimeId = Shader.PropertyToID("_GrainTime");
        private static readonly int AberrationId = Shader.PropertyToID("_Aberration");
        private static readonly int ScanlineId = Shader.PropertyToID("_Scanline");
        private static readonly int ScanlineCountId = Shader.PropertyToID("_ScanlineCount");
        private static readonly int BarrelId = Shader.PropertyToID("_Barrel");
        private static readonly int LiftId = Shader.PropertyToID("_Lift");
        private static readonly int GainId = Shader.PropertyToID("_Gain");
        private static readonly int SaturationId = Shader.PropertyToID("_Saturation");

        private Material Material
        {
            get
            {
                if (_material == null)
                {
                    var resolved = editorShader != null ? editorShader
                        : runtimeShader != null ? runtimeShader
                        : Shader.Find(ShaderName);
                    if (resolved == null || !resolved.isSupported)
                    {
                        if (!_shaderMissingLogged)
                        {
                            _shaderMissingLogged = true;
                            Debug.LogWarning(
                                $"[StationPostProcess] 未能加载着色器 {ShaderName}，后处理已跳过");
                        }

                        return null;
                    }

                    _material = new Material(resolved) { hideFlags = HideFlags.HideAndDontSave };
                }

                return _material;
            }
        }

        private void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            Apply(source, destination);
        }

        /// <summary>
        /// 执行一遍后处理。运行时由 OnRenderImage 调用，
        /// 编辑器截图时由 CaptureHarness 显式调用。
        ///
        /// 之所以要显式暴露而不是给组件挂 ExecuteAlways：带 ExecuteAlways 的组件
        /// 会参与编辑器里的场景保存流程，本项目已经两次因此产出损坏的 level0，
        /// 运行时直接崩溃而构建过程毫无提示。
        /// </summary>
        public void Apply(RenderTexture source, RenderTexture destination)
        {
            var material = Material;
            if (material == null)
            {
                // 着色器没打进包时直接透传，宁可画面朴素也不能黑屏。
                Graphics.Blit(source, destination);
                return;
            }

            material.SetFloat(VignetteId, vignette);
            material.SetFloat(VignetteSoftId, vignetteSoftness);
            material.SetFloat(GrainId, grain);
            material.SetFloat(GrainTimeId, animateGrain ? Time.unscaledTime * 37.13f : 0f);
            material.SetFloat(AberrationId, aberration);
            material.SetFloat(ScanlineId, scanline);
            material.SetFloat(ScanlineCountId, Mathf.Max(1f, scanlineCount));
            material.SetFloat(BarrelId, barrel);
            material.SetFloat(LiftId, lift);
            material.SetFloat(GainId, gain);
            material.SetFloat(SaturationId, saturation);

            Graphics.Blit(source, destination, material);
        }

        private void OnDisable()
        {
            if (_material != null)
            {
                DestroyImmediate(_material);
                _material = null;
            }
        }
    }
}
