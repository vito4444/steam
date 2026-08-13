using Decoder.Signal;
using UnityEngine;

namespace Decoder.UI
{
    /// <summary>
    /// 工位上的 CRT 示波器。它同时承担两件事：
    ///
    /// 一是画面焦点。整个房间最亮的东西就是这块屏幕，它决定了玩家第一眼看到什么。
    /// 二是无障碍。包络的宽窄就是点和划的区别，听障玩家完全靠看它读电码，
    /// 所以波形必须和耳朵听到的严格同源——它读的是合成器的解调包络，
    /// 不是一段"看起来差不多"的装饰动画。
    ///
    /// 绘制方式模仿真实示波器：光点从左往右扫，扫到头回卷；
    /// 磷光有余辉，旧迹线逐渐暗下去而不是瞬间消失；刻度网格是背景层，不参与衰减。
    /// </summary>
    public sealed class OscilloscopeDisplay : MonoBehaviour
    {
        [Header("接线")]
        public RadioReceiver receiver;

        [Tooltip("显示波形的渲染器。留空则用自身的 Renderer")]
        public Renderer targetRenderer;

        [Header("屏幕")]
        [Tooltip("纹理宽度。这是 CPU 逐像素绘制，分辨率直接换算成每帧开销")]
        public int textureWidth = 384;

        public int textureHeight = 192;

        [Tooltip("整屏扫过一遍代表多少秒。2 到 3 秒能同时看到几个点划，便于分辨长短")]
        public float sweepSeconds = 2.4f;

        [Header("磷光")]
        // 半衰期要跟扫描周期挂钩。比周期短太多，光点扫到右半屏时左半屏已经黑了，
        // 屏幕上永远只有一小段波形在游动；玩家读不出一个字符的完整节奏。
        // 取周期的一半左右，整圈迹线都留得住，同时新旧之间还有肉眼可辨的亮度梯度，
        // 一眼能看出光点现在扫到哪。这也正是长余辉示波管的观感。
        [Tooltip("余辉半衰期。太短只剩一小段在游动，太长糊成一片")]
        public float persistenceHalfLife = 1.1f;

        // 屏幕配色的唯一定义。建场景时要用同一套颜色烘一张待机贴图给 CRT 材质，
        // 两边各写一份的话，没通电的屏幕和通了电的屏幕会是两种绿。
        public static readonly Color DefaultTraceColor = new(0.30f, 0.76f, 0.38f, 1f);
        public static readonly Color DefaultGridColor = new(0.055f, 0.19f, 0.085f, 1f);
        public static readonly Color DefaultAxisColor = new(0.11f, 0.36f, 0.17f, 1f);
        public static readonly Color DefaultBackgroundColor = new(0.010f, 0.045f, 0.020f, 1f);

        public Color traceColor = DefaultTraceColor;

        [Tooltip("自发光倍率。纹理值本身很低，靠这个把屏幕点亮")]
        [Range(1f, 12f)] public float emissionBoost = 1.25f;

        [Tooltip("迹线宽度，单位是纹理列。屏幕在画面里很小，单列的线会被采样丢掉")]
        [Range(1, 15)] public int traceWidth = 3;
        // 真实示波器的屏幕底色几乎全黑，迹线是很细的一条。照搬到游戏里的结果是
        // 这块屏幕在画面上只有两个像素宽的亮线，截图十有八九抓在余辉衰减的暗区，
        // 而听障玩家要靠它读点划。底光和刻度都往上提，让屏幕先是"亮着的"。
        public Color gridColor = DefaultGridColor;
        public Color axisColor = DefaultAxisColor;
        public Color backgroundColor = DefaultBackgroundColor;

        [Header("噪声")]
        [Tooltip("无信号时基线的抖动幅度，占屏高比例")]
        [Range(0f, 0.2f)] public float baselineJitter = 0.035f;

        private Texture2D _texture;
        private Color32[] _pixels;
        private Color32[] _background;
        private NoiseSource _jitter;

        private float _sweepPosition;
        private int _lastColumn = -1;
        private float _lastAmplitude = -1f;

        // 电子束驻留亮度查表，索引是到中心的归一化距离。见 BuildDwellTable。
        private const int DwellResolution = 256;
        private static readonly float[] Dwell = BuildDwellTable();

        private void Awake()
        {
            if (targetRenderer == null)
            {
                targetRenderer = GetComponent<Renderer>();
            }

            _jitter = new NoiseSource(0x5CA1E);
            BuildTexture();
        }

        /// <summary>
        /// 一列上某个高度的亮度权重，入参是到中心的距离除以本列幅度。
        ///
        /// 示波器上的正弦波不是均匀亮的一整条：光点走的是 y = A·sin(ωt)，
        /// 垂直速度 ∝ √(A²−y²)，在波峰波谷处速度趋近零、磷光被激发得最久，
        /// 过零点处最快、最暗。所以满幅键控在屏幕上是"上下两条亮边夹一层暗填充"，
        /// 而不是一堵实心绿墙——后者是没有这层加权时的样子，一眼假。
        ///
        /// 严格的 1/√(1−u²) 在边缘发散、亮带只有一两像素宽，
        /// 而真实电子束有束斑展宽会把这个尖峰抹开。用幂函数近似这个结果：
        /// 形状对，过渡柔和，且能整条预计算成查表。
        /// </summary>
        public static float DwellWeight(float normalizedDistance)
        {
            const float interior = 0.13f;
            const float falloff = 3.6f;
            var u = Mathf.Clamp01(normalizedDistance);
            return interior + (1f - interior) * Mathf.Pow(u, falloff);
        }

        private static float[] BuildDwellTable()
        {
            var table = new float[DwellResolution];
            for (var i = 0; i < DwellResolution; i++)
            {
                table[i] = DwellWeight(i / (float)(DwellResolution - 1));
            }

            return table;
        }

        private void BuildTexture()
        {
            _texture = new Texture2D(textureWidth, textureHeight, TextureFormat.RGBA32, false)
            {
                name = "OscilloscopeTrace",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };

            _pixels = new Color32[textureWidth * textureHeight];
            _background = new Color32[textureWidth * textureHeight];
            PaintGrid();

            System.Array.Copy(_background, _pixels, _pixels.Length);
            _texture.SetPixels32(_pixels);
            _texture.Apply(false);

            if (targetRenderer != null)
            {
                // 直接实例化材质，而不是走 MaterialPropertyBlock。
                // 属性块设置的贴图在静态批处理下会被丢掉，屏幕就是一片黑，
                // 而且不报任何错。这里只有一块屏幕，多一份材质实例不值得省。
                var material = targetRenderer.material;
                material.mainTexture = _texture;
                material.EnableKeyword("_EMISSION");
                // 屏幕自身发光，所以同一张图也喂给自发光通道，
                // 否则波形在暗房间里会是死的。
                material.SetTexture("_EmissionMap", _texture);
                // 自发光倍率不能是 1。纹理里的底色只有 0.11、迹线也就到 1.0，
                // 乘以白色之后在 HDR 加色调映射下几乎全被压没——
                // 屏幕看起来就是一块没通电的暗玻璃。这个倍率决定屏幕
                // 在暗房间里是不是"亮着的"。
                material.SetColor("_EmissionColor", Color.white * emissionBoost);
                material.SetColor("_Color", Color.white);
            }
        }

        private void PaintGrid()
        {
            PaintGrid(_background, textureWidth, textureHeight, backgroundColor, gridColor, axisColor);
        }

        /// <summary>刻度网格。真实示波器是 10 格宽 8 格高，中心两轴更亮。</summary>
        public static void PaintGrid(Color32[] target, int width, int height,
            Color background, Color grid, Color axis)
        {
            var bg = (Color32)background;
            for (var i = 0; i < target.Length; i++)
            {
                target[i] = bg;
            }

            var gridColor32 = (Color32)grid;
            var axisColor32 = (Color32)axis;

            for (var division = 1; division < 10; division++)
            {
                var x = Mathf.RoundToInt(width * division / 10f);
                if (x <= 0 || x >= width)
                {
                    continue;
                }

                var color = division == 5 ? axisColor32 : gridColor32;
                for (var y = 0; y < height; y++)
                {
                    target[y * width + x] = color;
                }
            }

            for (var division = 1; division < 8; division++)
            {
                var y = Mathf.RoundToInt(height * division / 8f);
                if (y <= 0 || y >= height)
                {
                    continue;
                }

                var color = division == 4 ? axisColor32 : gridColor32;
                var row = y * width;
                for (var x = 0; x < width; x++)
                {
                    target[row + x] = color;
                }
            }
        }

        /// <summary>
        /// 烘一张屏幕画面给 CRT 材质。
        ///
        /// 没有这张贴图，材质的自发光槽是纯色，屏幕在编辑器和画面自检里就是
        /// 一整块过曝的亮绿方块，既看不出那是台示波器，也会把自检的亮部、
        /// 饱和度和绿色占比全带偏。运行时组件一启动就会用实时波形覆盖它。
        ///
        /// 画的是一段真实的键控波形而不是待机基线：这张图是自检看到的屏幕，
        /// 而玩家看到的屏幕上永远有信号在跑。画成一条平线的话，自检读到的
        /// 亮部和对比度会远低于实际画面，照着调光只会越调越偏。
        /// </summary>
        public static Texture2D CreateStandbyTexture(int width = 384, int height = 192)
        {
            var pixels = new Color32[width * height];
            PaintGrid(pixels, width, height, DefaultBackgroundColor, DefaultGridColor, DefaultAxisColor);

            // 18 字/分的 CQ 展开约 1.8 秒，正好填满一屏而不至于挤成一片。
            var timeline = MorseCode.BuildTimeline(MorseCode.Encode("CQ"), 18f);
            var total = 0f;
            foreach (var element in timeline)
            {
                total += element.Seconds;
            }

            if (total > 0f)
            {
                var center = height * 0.5f;
                var index = 0;
                var elapsed = timeline[0].Seconds;
                for (var x = 0; x < width; x++)
                {
                    var t = (x + 0.5f) / width * total;
                    while (t > elapsed && index < timeline.Count - 1)
                    {
                        index++;
                        elapsed += timeline[index].Seconds;
                    }

                    var amplitude = timeline[index].KeyDown ? height * 0.42f : height * 0.015f;
                    PaintStandbyColumn(pixels, width, height, x, center, amplitude);
                }
            }

            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = "OscilloscopeStandby",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            texture.SetPixels32(pixels);
            texture.Apply(false);
            return texture;
        }

        private static void PaintStandbyColumn(
            Color32[] pixels, int width, int height, int column, float center, float amplitude)
        {
            var top = Mathf.Clamp(Mathf.RoundToInt(center + amplitude), 0, height - 1);
            var bottom = Mathf.Clamp(Mathf.RoundToInt(center - amplitude), 0, height - 1);
            var scale = Mathf.Max(1f, amplitude);
            var flat = amplitude < 1f;

            for (var y = bottom; y <= top; y++)
            {
                var weight = flat ? 1f : DwellWeight(Mathf.Abs(y - center) / scale);
                var index = y * width + column;
                var pixel = pixels[index];
                pixel.r = System.Math.Max(pixel.r, (byte)(DefaultTraceColor.r * weight * 255f));
                pixel.g = System.Math.Max(pixel.g, (byte)(DefaultTraceColor.g * weight * 255f));
                pixel.b = System.Math.Max(pixel.b, (byte)(DefaultTraceColor.b * weight * 255f));
                pixels[index] = pixel;
            }
        }

        private void Update()
        {
            if (_texture == null || receiver == null)
            {
                return;
            }

            var dt = Time.unscaledDeltaTime;
            Decay(dt);
            AdvanceSweep(dt);

            _texture.SetPixels32(_pixels);
            _texture.Apply(false);
        }

        /// <summary>
        /// 磷光衰减。每个像素朝背景色靠拢，而不是朝黑色——
        /// 朝黑色衰减会把刻度网格一起吃掉，屏幕上就只剩一条孤零零的迹线。
        /// </summary>
        private void Decay(float deltaTime)
        {
            if (persistenceHalfLife <= 0f)
            {
                System.Array.Copy(_background, _pixels, _pixels.Length);
                return;
            }

            var keep = Mathf.Pow(0.5f, deltaTime / persistenceHalfLife);
            var fade = (byte)Mathf.Clamp(Mathf.RoundToInt(keep * 255f), 0, 255);
            var inverse = 255 - fade;

            for (var i = 0; i < _pixels.Length; i++)
            {
                var current = _pixels[i];
                var target = _background[i];
                current.r = (byte)((current.r * fade + target.r * inverse) / 255);
                current.g = (byte)((current.g * fade + target.g * inverse) / 255);
                current.b = (byte)((current.b * fade + target.b * inverse) / 255);
                _pixels[i] = current;
            }
        }

        private void AdvanceSweep(float deltaTime)
        {
            var synth = receiver.Synthesizer;
            var sweep = Mathf.Max(0.05f, sweepSeconds);
            var previousPosition = _sweepPosition;
            _sweepPosition += deltaTime / sweep;

            // 一帧内扫过的列数。低帧率下这个值会大于 1，必须逐列补齐，
            // 否则波形会出现断点，玩家会把断点误读成电码的间隔。
            var fromColumn = Mathf.FloorToInt(previousPosition * textureWidth);
            var toColumn = Mathf.FloorToInt(_sweepPosition * textureWidth);
            var columnSeconds = sweep / textureWidth;

            for (var column = fromColumn + 1; column <= toColumn; column++)
            {
                var wrapped = ((column % textureWidth) + textureWidth) % textureWidth;
                if (wrapped < column % textureWidth || wrapped == 0)
                {
                    // 回卷到左边缘，断开与上一列的连线，避免横穿整屏的假迹线。
                    _lastColumn = -1;
                }

                // 每一列对应的时刻要往回推，因为扫描是"已经发生过的"波形。
                var columnsBehind = toColumn - column;
                var sampleTime = synth.ElapsedSeconds - columnsBehind * columnSeconds;
                DrawColumn(wrapped, synth.EnvelopeAt(sampleTime));
            }

            if (_sweepPosition >= 1f)
            {
                _sweepPosition -= Mathf.Floor(_sweepPosition);
            }
        }

        private void DrawColumn(int column, float envelope)
        {
            // 包络是单极性的，示波器上显示成对称的双极波形，和真实 CW 监听一致。
            var amplitude = envelope * textureHeight * 0.42f;
            if (envelope <= 0.001f)
            {
                amplitude = baselineJitter * textureHeight * Mathf.Abs(_jitter.NextWhite());
            }

            // 键控的上升下降沿只有几毫秒，往往落在两列之间。只画本列幅度的话，
            // 一个划的两端会缺掉竖边，看上去像断开的两段。取与上一列的较大者补齐，
            // 陡沿就画成一条完整的竖线——真实示波器上包络突变时也是这样。
            var span = amplitude;
            if (_lastColumn == column - 1 && _lastAmplitude >= 0f)
            {
                span = Mathf.Max(amplitude, _lastAmplitude);
            }

            PaintColumn(column, span);

            _lastColumn = column;
            _lastAmplitude = amplitude;
        }

        /// <summary>
        /// 画一列迹线，亮度按电子束驻留时间分布，见 <see cref="BuildDwellTable"/>。
        /// </summary>
        private void PaintColumn(int column, float amplitude)
        {
            var center = textureHeight * 0.5f;
            var top = Mathf.Clamp(Mathf.RoundToInt(center + amplitude), 0, textureHeight - 1);
            var bottom = Mathf.Clamp(Mathf.RoundToInt(center - amplitude), 0, textureHeight - 1);

            // 迹线画满 traceWidth 列而不是一列。屏幕在画面里只占两百来像素宽，
            // 384 列的纹理缩下去，单列的线会被采样直接丢掉——
            // 听障玩家全靠看这条线读点划，它不能只在放大截图里才存在。
            var from = Mathf.Max(0, column - traceWidth / 2);
            var to = Mathf.Min(textureWidth - 1, column + traceWidth / 2);

            // 幅度小于一像素时（无信号的基线），整条按满亮度画，
            // 否则归一化会把仅有的一两个像素也压到 interior 那一档，基线就没了。
            var scale = Mathf.Max(1f, amplitude);
            var flat = amplitude < 1f;

            for (var y = bottom; y <= top; y++)
            {
                var weight = 1f;
                if (!flat)
                {
                    var u = Mathf.Abs(y - center) / scale;
                    var slot = Mathf.Clamp(Mathf.RoundToInt(u * (DwellResolution - 1)), 0, DwellResolution - 1);
                    weight = Dwell[slot];
                }

                var r = (byte)(traceColor.r * weight * 255f);
                var g = (byte)(traceColor.g * weight * 255f);
                var b = (byte)(traceColor.b * weight * 255f);

                var row = y * textureWidth;
                for (var x = from; x <= to; x++)
                {
                    var index = row + x;
                    var pixel = _pixels[index];
                    // 取最大值而不是相加。磷光是单色的：电子束在同一处停留再久，
                    // 也只是那一种绿更亮，不会变白。加法混合下点和划的粗条
                    // 几帧就累加到饱和，整条迹线变成白色，看着像别的东西。
                    pixel.r = System.Math.Max(pixel.r, r);
                    pixel.g = System.Math.Max(pixel.g, g);
                    pixel.b = System.Math.Max(pixel.b, b);
                    _pixels[index] = pixel;
                }
            }
        }

        private void OnDestroy()
        {
            if (_texture != null)
            {
                Destroy(_texture);
            }
        }
    }
}
