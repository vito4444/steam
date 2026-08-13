namespace Worker.Core
{
    /// <summary>
    /// Engine-free 8-bit colour. Lives in Core so that the Unity renderer and the
    /// headless preview renderer draw from one identical palette; a colour tweak must
    /// never be able to make the self-test screenshots disagree with the real game.
    /// </summary>
    public readonly struct RgbColor
    {
        public readonly byte R;
        public readonly byte G;
        public readonly byte B;
        public readonly byte A;

        public RgbColor(byte r, byte g, byte b, byte a = 255)
        {
            R = r;
            G = g;
            B = b;
            A = a;
        }

        public static readonly RgbColor Clear = new RgbColor(0, 0, 0, 0);
        public static readonly RgbColor Black = new RgbColor(0, 0, 0);
        public static readonly RgbColor White = new RgbColor(255, 255, 255);

        public RgbColor WithAlpha(byte alpha) => new RgbColor(R, G, B, alpha);

        public RgbColor Lerp(RgbColor other, int numerator, int denominator)
        {
            if (denominator <= 0) return this;
            if (numerator <= 0) return this;
            if (numerator >= denominator) return other;

            return new RgbColor(
                (byte)(R + (other.R - R) * numerator / denominator),
                (byte)(G + (other.G - G) * numerator / denominator),
                (byte)(B + (other.B - B) * numerator / denominator),
                (byte)(A + (other.A - A) * numerator / denominator));
        }

        /// <summary>Mixes towards white. Amount is a percentage.</summary>
        public RgbColor Lighten(int percent) => Lerp(White, percent, 100);

        /// <summary>Mixes towards black. Amount is a percentage.</summary>
        public RgbColor Darken(int percent) => Lerp(Black, percent, 100);

        /// <summary>Perceptual luminance 0..255, used to verify value separation in tests.</summary>
        public int Luminance => (R * 299 + G * 587 + B * 114) / 1000;

        public override string ToString() => "#" + R.ToString("x2") + G.ToString("x2") + B.ToString("x2");
    }
}
