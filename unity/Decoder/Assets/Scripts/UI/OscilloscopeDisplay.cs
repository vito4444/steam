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
        [Tooltip("余辉半衰期。太短像素点，太长糊成一片")]
        public float persistenceHalfLife = 0.28f;

        public Color traceColor = new Color(0.45f, 1f, 0.55f, 1f);
        public Color gridColor = new Color(0.06f, 0.22f, 0.10f, 1f);
        public Color axisColor = new Color(0.10f, 0.34f, 0.16f, 1f);
        public Color backgroundColor = new Color(0.008f, 0.035f, 0.015f, 1f);

        [Header("噪声")]
        [Tooltip("无信号时基线的抖动幅度，占屏高比例")]
        [Range(0f, 0.2f)] public float baselineJitter = 0.035f;

        private Texture2D _texture;
        private Color32[] _pixels;
        private Color32[] _background;
        private MaterialPropertyBlock _block;
        private NoiseSource _jitter;

        private float _sweepPosition;
        private int _lastColumn = -1;
        private int _lastTopY = -1;
        private int _lastBottomY = -1;

        private void Awake()
        {
            if (targetRenderer == null)
            {
                targetRenderer = GetComponent<Renderer>();
            }

            _jitter = new NoiseSource(0x5CA1E);
            BuildTexture();
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
                _block = new MaterialPropertyBlock();
                targetRenderer.GetPropertyBlock(_block);
                _block.SetTexture("_MainTex", _texture);
                // 屏幕自身发光，所以同一张图也喂给自发光通道，
                // 否则波形在暗房间里会是死的。
                _block.SetTexture("_EmissionMap", _texture);
                _block.SetColor("_EmissionColor", Color.white);
                targetRenderer.SetPropertyBlock(_block);
            }
        }

        /// <summary>刻度网格。真实示波器是 10 格宽 8 格高，中心两轴更亮。</summary>
        private void PaintGrid()
        {
            var bg = (Color32)backgroundColor;
            for (var i = 0; i < _background.Length; i++)
            {
                _background[i] = bg;
            }

            var grid = (Color32)gridColor;
            var axis = (Color32)axisColor;

            for (var division = 1; division < 10; division++)
            {
                var x = Mathf.RoundToInt(textureWidth * division / 10f);
                if (x <= 0 || x >= textureWidth)
                {
                    continue;
                }

                var color = division == 5 ? axis : grid;
                for (var y = 0; y < textureHeight; y++)
                {
                    _background[y * textureWidth + x] = color;
                }
            }

            for (var division = 1; division < 8; division++)
            {
                var y = Mathf.RoundToInt(textureHeight * division / 8f);
                if (y <= 0 || y >= textureHeight)
                {
                    continue;
                }

                var color = division == 4 ? axis : grid;
                var row = y * textureWidth;
                for (var x = 0; x < textureWidth; x++)
                {
                    _background[row + x] = color;
                }
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
            var center = textureHeight * 0.5f;
            // 包络是单极性的，示波器上显示成对称的双极波形，和真实 CW 监听一致。
            var amplitude = envelope * textureHeight * 0.42f;
            if (envelope <= 0.001f)
            {
                amplitude = baselineJitter * textureHeight * Mathf.Abs(_jitter.NextWhite());
            }

            var topY = Mathf.Clamp(Mathf.RoundToInt(center + amplitude), 0, textureHeight - 1);
            var bottomY = Mathf.Clamp(Mathf.RoundToInt(center - amplitude), 0, textureHeight - 1);

            // 与上一列连线，让陡峭的上升沿画成竖直边而不是两个孤立的点。
            if (_lastColumn == column - 1 && _lastTopY >= 0)
            {
                topY = FillSpan(column, topY, _lastTopY);
                bottomY = FillSpan(column, bottomY, _lastBottomY);
            }

            FillSpan(column, bottomY, topY);

            _lastColumn = column;
            _lastTopY = topY;
            _lastBottomY = bottomY;
        }

        private int FillSpan(int column, int fromY, int toY)
        {
            var lo = Mathf.Min(fromY, toY);
            var hi = Mathf.Max(fromY, toY);
            var trace = (Color32)traceColor;

            for (var y = lo; y <= hi; y++)
            {
                var index = y * textureWidth + column;
                var pixel = _pixels[index];
                // 加法混合：迹线重叠处更亮，模拟电子束停留更久的地方磷光更强。
                pixel.r = (byte)Mathf.Min(255, pixel.r + trace.r);
                pixel.g = (byte)Mathf.Min(255, pixel.g + trace.g);
                pixel.b = (byte)Mathf.Min(255, pixel.b + trace.b);
                _pixels[index] = pixel;
            }

            return fromY;
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
