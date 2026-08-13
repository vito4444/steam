using UnityEngine;

namespace Decoder.Signal
{
    /// <summary>传真图的题材。每种对应一类侦察产物。</summary>
    public enum FacsimileSubject
    {
        /// <summary>校准图：灰阶楔与同心圆。台站正式发图前先发这个，让接收端对好增益。</summary>
        Calibration,

        /// <summary>设施平面图：围墙、主楼、附属建筑、一个标注点。</summary>
        Facility,

        /// <summary>海岸线：不规则岸线加经纬网格。</summary>
        Coastline,
    }

    /// <summary>
    /// 一幅慢扫描传真图。
    ///
    /// 分辨率刻意压得很低（96×64）。真实的慢扫描传真受限于话音带宽，
    /// 一幅图要发好几分钟，分辨率本来就只有这个量级；而且它最终要显示在
    /// 一块巴掌大的示波管上，再高也看不出来。低分辨率还带来一个玩法上的好处：
    /// 图像必须"认"而不是"看"，玩家得从粗糙的轮廓里判断那是什么。
    ///
    /// 图是算出来的，不是美术画的——项目没有美术，而且程序生成能保证
    /// 同一个种子每次得到同一幅图，玩家反复接收同一次传输时不会看到两张不同的图。
    /// </summary>
    public sealed class FacsimileImage
    {
        public const int DefaultWidth = 96;
        public const int DefaultHeight = 64;

        private readonly float[] _pixels;

        public FacsimileImage(int width, int height)
        {
            Width = Mathf.Max(1, width);
            Height = Mathf.Max(1, height);
            _pixels = new float[Width * Height];
        }

        public int Width { get; }
        public int Height { get; }

        /// <summary>取像素亮度，0 是黑 1 是白。行号从顶部数起，越界会被钳到边界。</summary>
        public float Sample(int x, int y)
        {
            x = Mathf.Clamp(x, 0, Width - 1);
            y = Mathf.Clamp(y, 0, Height - 1);
            return _pixels[y * Width + x];
        }

        public void Set(int x, int y, float value)
        {
            if (x < 0 || x >= Width || y < 0 || y >= Height)
            {
                return;
            }

            _pixels[y * Width + x] = Mathf.Clamp01(value);
        }

        public static FacsimileImage Render(FacsimileSubject subject, int seed)
        {
            return Render(subject, seed, DefaultWidth, DefaultHeight);
        }

        public static FacsimileImage Render(FacsimileSubject subject, int seed, int width, int height)
        {
            var image = new FacsimileImage(width, height);
            switch (subject)
            {
                case FacsimileSubject.Calibration:
                    RenderCalibration(image);
                    break;
                case FacsimileSubject.Facility:
                    RenderFacility(image, seed);
                    break;
                default:
                    RenderCoastline(image, seed);
                    break;
            }

            return image;
        }

        /// <summary>左半边是灰阶楔，右半边是同心圆。一眼能看出增益和线性对不对。</summary>
        private static void RenderCalibration(FacsimileImage image)
        {
            var steps = 8;
            for (var y = 0; y < image.Height; y++)
            {
                for (var x = 0; x < image.Width; x++)
                {
                    float value;
                    if (x < image.Width / 2)
                    {
                        var step = x * steps / (image.Width / 2);
                        value = step / (float)(steps - 1);
                    }
                    else
                    {
                        var cx = image.Width * 0.75f;
                        var cy = image.Height * 0.5f;
                        var dx = (x - cx) / (image.Width * 0.25f);
                        var dy = (y - cy) / (image.Height * 0.5f);
                        var radius = Mathf.Sqrt(dx * dx + dy * dy);
                        value = radius > 1f ? 0.08f : (Mathf.Repeat(radius * 4f, 1f) < 0.5f ? 0.92f : 0.10f);
                    }

                    image.Set(x, y, value);
                }
            }
        }

        /// <summary>一处设施的俯视图：外围墙、主楼、两栋附属、一个圈出来的目标点。</summary>
        private static void RenderFacility(FacsimileImage image, int seed)
        {
            var noise = new NoiseSource(seed);

            // 场区外是空地，比场区内暗。整幅图不能只有中间调，
            // 传真在示波管上本来对比就有限，底子再抬高就全糊成一片绿。
            for (var y = 0; y < image.Height; y++)
            {
                for (var x = 0; x < image.Width; x++)
                {
                    image.Set(x, y, 0.10f + noise.NextWhite() * 0.03f);
                }
            }

            var left = image.Width / 8;
            var right = image.Width - image.Width / 8;
            var top = image.Height / 8;
            var bottom = image.Height - image.Height / 8;

            // 场区地面
            Rectangle(image, left, top, right, bottom, 0.24f, filled: true);
            for (var y = top; y <= bottom; y++)
            {
                for (var x = left; x <= right; x++)
                {
                    image.Set(x, y, image.Sample(x, y) + noise.NextWhite() * 0.04f);
                }
            }

            // 围墙：一圈亮线
            Rectangle(image, left, top, right, bottom, 0.82f, filled: false);

            // 主楼：偏左的大矩形，中间一道屋脊
            const int mainTop = 10;
            var mainLeft = left + 5;
            var mainRight = left + 28;
            var mainBottom = bottom - 8;
            Rectangle(image, mainLeft, top + mainTop, mainRight, mainBottom, 0.55f, filled: true);
            Rectangle(image, mainLeft, top + mainTop, mainRight, mainBottom, 0.95f, filled: false);
            Rectangle(image, mainLeft + 11, top + mainTop + 2, mainLeft + 12, mainBottom - 2,
                0.72f, filled: true);

            // 通道：从大门进来绕过主楼东侧，不穿楼。
            // 原先这条带子横着切过整个场区，把主楼劈成两半，一眼看出是画错了。
            var laneX = mainRight + 4;
            Rectangle(image, laneX, top, laneX + 2, bottom, 0.42f, filled: true);
            Rectangle(image, laneX, image.Height / 2 - 1, right, image.Height / 2 + 1,
                0.42f, filled: true);

            // 两栋附属建筑
            Rectangle(image, right - 22, top + 5, right - 8, top + 15, 0.50f, filled: true);
            Rectangle(image, right - 22, top + 5, right - 8, top + 15, 0.86f, filled: false);
            Rectangle(image, right - 20, bottom - 17, right - 8, bottom - 7, 0.50f, filled: true);
            Rectangle(image, right - 20, bottom - 17, right - 8, bottom - 7, 0.86f, filled: false);

            // 目标标记：手画上去的一个圈，压在附属建筑上。
            // 它是这幅图的全部意义所在——发图的人要收图的人看的就是这里。
            Circle(image, right - 15, top + 10, 8, 1.0f);
        }

        /// <summary>一段海岸线加网格。岸线用低频噪声起伏，陆地比海面亮。</summary>
        private static void RenderCoastline(FacsimileImage image, int seed)
        {
            var noise = new NoiseSource(seed);

            // 先抽一串控制点，再线性插值成岸线，保证岸线连续而不是逐列跳变。
            const int controls = 9;
            var shore = new float[controls];
            for (var i = 0; i < controls; i++)
            {
                shore[i] = 0.32f + Mathf.Abs(noise.NextWhite()) * 0.38f;
            }

            for (var x = 0; x < image.Width; x++)
            {
                var t = x / (float)(image.Width - 1) * (controls - 1);
                var i0 = Mathf.Clamp(Mathf.FloorToInt(t), 0, controls - 1);
                var i1 = Mathf.Min(i0 + 1, controls - 1);
                var line = Mathf.Lerp(shore[i0], shore[i1], t - i0) * image.Height;

                for (var y = 0; y < image.Height; y++)
                {
                    var land = y > line;
                    var value = land ? 0.62f : 0.14f;

                    // 岸线本身是一条亮边，传真图上海陆交界总是最清楚的
                    if (Mathf.Abs(y - line) < 1.2f)
                    {
                        value = 0.95f;
                    }

                    // 经纬网格：每 16 列 12 行一条淡线
                    if (x % 16 == 0 || y % 12 == 0)
                    {
                        value = Mathf.Max(value, land ? 0.78f : 0.34f);
                    }

                    image.Set(x, y, value);
                }
            }
        }

        private static void Rectangle(FacsimileImage image, int x0, int y0, int x1, int y1,
            float value, bool filled)
        {
            for (var y = y0; y <= y1; y++)
            {
                for (var x = x0; x <= x1; x++)
                {
                    if (filled || x == x0 || x == x1 || y == y0 || y == y1)
                    {
                        image.Set(x, y, value);
                    }
                }
            }
        }

        private static void Circle(FacsimileImage image, int cx, int cy, int radius, float value)
        {
            for (var y = cy - radius; y <= cy + radius; y++)
            {
                for (var x = cx - radius; x <= cx + radius; x++)
                {
                    var dx = x - cx;
                    var dy = y - cy;
                    var distance = Mathf.Sqrt(dx * dx + dy * dy);
                    if (Mathf.Abs(distance - radius) < 0.9f)
                    {
                        image.Set(x, y, value);
                    }
                }
            }
        }
    }
}
