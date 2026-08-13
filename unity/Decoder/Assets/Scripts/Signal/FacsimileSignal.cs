using UnityEngine;

namespace Decoder.Signal
{
    /// <summary>
    /// 慢扫描传真的调制格式。
    ///
    /// 一行的结构是：行同步脉冲 → 消隐 → 逐像素的亮度。亮度用频率表示，
    /// 黑对应 1500 赫兹、白对应 2300 赫兹，同步脉冲固定 1200 赫兹落在黑电平以下——
    /// 这样接收端不需要额外的时钟，只要盯住"频率什么时候低于黑电平"就知道换行了。
    /// 这是真实业余无线电慢扫描电视用的办法。
    ///
    /// 这里只定义时间到频率、时间到像素这两个映射。发端按它生成音频，
    /// 收端按它把时间还原成扫描位置，两边共用同一套换算，所以听到的和看到的
    /// 不可能对不上——这对本作是硬要求，屏幕是听障玩家唯一的信息来源。
    /// </summary>
    public static class FacsimileSignal
    {
        public const float SyncHertz = 1200f;
        public const float BlackHertz = 1500f;
        public const float WhiteHertz = 2300f;

        /// <summary>行同步脉冲时长。</summary>
        public const float SyncSeconds = 0.006f;

        /// <summary>同步之后、像素之前的消隐段，给接收端的鉴频器一点稳定时间。</summary>
        public const float PorchSeconds = 0.002f;

        /// <summary>每个像素占的时间。96 像素一行时，一行约 0.123 秒。</summary>
        public const float DefaultPixelSeconds = 0.0012f;

        /// <summary>整幅图开始前的引导音，让接收端知道有图要来了。</summary>
        public const float LeaderSeconds = 0.30f;

        public const float LeaderHertz = 1900f;

        public static float LineSeconds(int width, float pixelSeconds)
        {
            return SyncSeconds + PorchSeconds + width * Mathf.Max(1e-6f, pixelSeconds);
        }

        public static float TotalSeconds(FacsimileImage image, float pixelSeconds)
        {
            return LeaderSeconds + image.Height * LineSeconds(image.Width, pixelSeconds);
        }

        /// <summary>
        /// 此刻应该发出的音频频率。传输结束后停在黑电平，
        /// 这样接收端不会把"发完了"误读成一行全白。
        /// </summary>
        public static float FrequencyAt(FacsimileImage image, float seconds, float pixelSeconds)
        {
            if (seconds < 0f)
            {
                return BlackHertz;
            }

            if (seconds < LeaderSeconds)
            {
                return LeaderHertz;
            }

            if (!Locate(image, seconds, pixelSeconds, out var x, out var y, out var phase))
            {
                return phase == LinePhase.Sync ? SyncHertz : BlackHertz;
            }

            return LuminanceToHertz(image.Sample(x, y));
        }

        /// <summary>
        /// 此刻扫描点落在哪个像素上。同步、消隐、引导音和传输结束时返回 false，
        /// 这些时段电子束不写像素。
        /// </summary>
        public static bool PixelAt(FacsimileImage image, float seconds, float pixelSeconds,
            out int x, out int y)
        {
            return Locate(image, seconds, pixelSeconds, out x, out y, out _);
        }

        private enum LinePhase
        {
            Sync,
            Porch,
            Pixels,
            Outside,
        }

        private static bool Locate(FacsimileImage image, float seconds, float pixelSeconds,
            out int x, out int y, out LinePhase phase)
        {
            x = 0;
            y = 0;
            phase = LinePhase.Outside;

            if (image == null || seconds < LeaderSeconds)
            {
                return false;
            }

            var step = Mathf.Max(1e-6f, pixelSeconds);
            var line = LineSeconds(image.Width, step);
            var intoImage = seconds - LeaderSeconds;
            var row = Mathf.FloorToInt(intoImage / line);
            if (row < 0 || row >= image.Height)
            {
                return false;
            }

            var intoLine = intoImage - row * line;
            if (intoLine < SyncSeconds)
            {
                phase = LinePhase.Sync;
                return false;
            }

            if (intoLine < SyncSeconds + PorchSeconds)
            {
                phase = LinePhase.Porch;
                return false;
            }

            var column = Mathf.FloorToInt((intoLine - SyncSeconds - PorchSeconds) / step);
            if (column < 0 || column >= image.Width)
            {
                phase = LinePhase.Porch;
                return false;
            }

            phase = LinePhase.Pixels;
            x = column;
            y = row;
            return true;
        }

        public static float LuminanceToHertz(float luminance)
        {
            return Mathf.Lerp(BlackHertz, WhiteHertz, Mathf.Clamp01(luminance));
        }

        /// <summary>
        /// 频率还原成亮度。低于黑电平的一律当作黑，因为那是同步脉冲，
        /// 不是"比黑还黑"的像素。
        /// </summary>
        public static float HertzToLuminance(float hertz)
        {
            return Mathf.Clamp01((hertz - BlackHertz) / (WhiteHertz - BlackHertz));
        }

        /// <summary>
        /// 判断此刻是不是行同步脉冲。接收端靠它对齐行首，
        /// 失谐或者中途调走再调回来的时候，这是唯一能重新找回行边界的信号。
        /// </summary>
        public static bool IsSync(FacsimileImage image, float seconds, float pixelSeconds)
        {
            Locate(image, seconds, pixelSeconds, out _, out _, out var phase);
            return phase == LinePhase.Sync;
        }
    }
}
