using System.Collections.Generic;
using UnityEngine;

namespace Abyssal.Visual
{
    /// <summary>
    /// 5×7 点阵字体，用于在程序化生成的贴图上刻标签。
    ///
    /// 控制台上那些「WOB kN」「MUD DENSITY」「ANNULUS PRESS」的丝印字，
    /// 是这类模拟游戏真实感的关键细节之一。用点阵字体现画可以完全避免
    /// 引入字体资产和 TextMeshPro 的运行时开销，而且在低分辨率下反而更像
    /// 真实设备上的丝网印刷。
    ///
    /// 每个字符用 5 个 byte 表示 5 列，每个 byte 的低 7 位是这一列从上到下的像素。
    /// </summary>
    public static class BitmapFont
    {
        public const int GlyphWidth = 5;
        public const int GlyphHeight = 7;

        static readonly Dictionary<char, byte[]> Glyphs = new Dictionary<char, byte[]>
        {
            [' '] = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00 },
            ['0'] = new byte[] { 0x3E, 0x51, 0x49, 0x45, 0x3E },
            ['1'] = new byte[] { 0x00, 0x42, 0x7F, 0x40, 0x00 },
            ['2'] = new byte[] { 0x42, 0x61, 0x51, 0x49, 0x46 },
            ['3'] = new byte[] { 0x21, 0x41, 0x45, 0x4B, 0x31 },
            ['4'] = new byte[] { 0x18, 0x14, 0x12, 0x7F, 0x10 },
            ['5'] = new byte[] { 0x27, 0x45, 0x45, 0x45, 0x39 },
            ['6'] = new byte[] { 0x3C, 0x4A, 0x49, 0x49, 0x30 },
            ['7'] = new byte[] { 0x01, 0x71, 0x09, 0x05, 0x03 },
            ['8'] = new byte[] { 0x36, 0x49, 0x49, 0x49, 0x36 },
            ['9'] = new byte[] { 0x06, 0x49, 0x49, 0x29, 0x1E },
            ['A'] = new byte[] { 0x7E, 0x11, 0x11, 0x11, 0x7E },
            ['B'] = new byte[] { 0x7F, 0x49, 0x49, 0x49, 0x36 },
            ['C'] = new byte[] { 0x3E, 0x41, 0x41, 0x41, 0x22 },
            ['D'] = new byte[] { 0x7F, 0x41, 0x41, 0x22, 0x1C },
            ['E'] = new byte[] { 0x7F, 0x49, 0x49, 0x49, 0x41 },
            ['F'] = new byte[] { 0x7F, 0x09, 0x09, 0x09, 0x01 },
            ['G'] = new byte[] { 0x3E, 0x41, 0x49, 0x49, 0x7A },
            ['H'] = new byte[] { 0x7F, 0x08, 0x08, 0x08, 0x7F },
            ['I'] = new byte[] { 0x00, 0x41, 0x7F, 0x41, 0x00 },
            ['J'] = new byte[] { 0x20, 0x40, 0x41, 0x3F, 0x01 },
            ['K'] = new byte[] { 0x7F, 0x08, 0x14, 0x22, 0x41 },
            ['L'] = new byte[] { 0x7F, 0x40, 0x40, 0x40, 0x40 },
            ['M'] = new byte[] { 0x7F, 0x02, 0x0C, 0x02, 0x7F },
            ['N'] = new byte[] { 0x7F, 0x04, 0x08, 0x10, 0x7F },
            ['O'] = new byte[] { 0x3E, 0x41, 0x41, 0x41, 0x3E },
            ['P'] = new byte[] { 0x7F, 0x09, 0x09, 0x09, 0x06 },
            ['Q'] = new byte[] { 0x3E, 0x41, 0x51, 0x21, 0x5E },
            ['R'] = new byte[] { 0x7F, 0x09, 0x19, 0x29, 0x46 },
            ['S'] = new byte[] { 0x46, 0x49, 0x49, 0x49, 0x31 },
            ['T'] = new byte[] { 0x01, 0x01, 0x7F, 0x01, 0x01 },
            ['U'] = new byte[] { 0x3F, 0x40, 0x40, 0x40, 0x3F },
            ['V'] = new byte[] { 0x1F, 0x20, 0x40, 0x20, 0x1F },
            ['W'] = new byte[] { 0x3F, 0x40, 0x38, 0x40, 0x3F },
            ['X'] = new byte[] { 0x63, 0x14, 0x08, 0x14, 0x63 },
            ['Y'] = new byte[] { 0x07, 0x08, 0x70, 0x08, 0x07 },
            ['Z'] = new byte[] { 0x61, 0x51, 0x49, 0x45, 0x43 },
            ['.'] = new byte[] { 0x00, 0x60, 0x60, 0x00, 0x00 },
            [','] = new byte[] { 0x00, 0x50, 0x30, 0x00, 0x00 },
            ['-'] = new byte[] { 0x08, 0x08, 0x08, 0x08, 0x08 },
            ['/'] = new byte[] { 0x20, 0x10, 0x08, 0x04, 0x02 },
            ['%'] = new byte[] { 0x23, 0x13, 0x08, 0x64, 0x62 },
            [':'] = new byte[] { 0x00, 0x36, 0x36, 0x00, 0x00 },
            ['°'] = new byte[] { 0x00, 0x07, 0x05, 0x07, 0x00 },
            ['³'] = new byte[] { 0x00, 0x09, 0x0B, 0x0D, 0x00 },
            ['('] = new byte[] { 0x00, 0x1C, 0x22, 0x41, 0x00 },
            [')'] = new byte[] { 0x00, 0x41, 0x22, 0x1C, 0x00 },
        };

        /// <summary>字符串按给定缩放绘制后占用的像素宽度（字符之间留一列空隙）。</summary>
        public static int MeasureWidth(string text, int scale)
            => string.IsNullOrEmpty(text) ? 0 : (text.Length * (GlyphWidth + 1) - 1) * scale;

        /// <summary>
        /// 把文本画进像素缓冲。<paramref name="x"/>、<paramref name="y"/> 是左下角。
        /// 缓冲按行主序存放，原点在左下角，和 Unity 的 Texture2D 一致。
        /// </summary>
        public static void Draw(Color32[] buffer, int bufferWidth, int bufferHeight,
                                string text, int x, int y, int scale, Color32 color)
        {
            if (string.IsNullOrEmpty(text)) return;
            int penX = x;

            foreach (char raw in text)
            {
                char ch = char.ToUpperInvariant(raw);
                if (!Glyphs.TryGetValue(ch, out var glyph)) glyph = Glyphs[' '];

                for (int col = 0; col < GlyphWidth; col++)
                {
                    byte bits = glyph[col];
                    for (int row = 0; row < GlyphHeight; row++)
                    {
                        if ((bits & (1 << row)) == 0) continue;

                        // 点阵表里 row 0 是顶部，纹理里 y 向上，所以要翻过来。
                        int baseX = penX + col * scale;
                        int baseY = y + (GlyphHeight - 1 - row) * scale;

                        for (int sy = 0; sy < scale; sy++)
                        {
                            int py = baseY + sy;
                            if (py < 0 || py >= bufferHeight) continue;
                            int rowOffset = py * bufferWidth;
                            for (int sx = 0; sx < scale; sx++)
                            {
                                int px = baseX + sx;
                                if (px < 0 || px >= bufferWidth) continue;
                                buffer[rowOffset + px] = color;
                            }
                        }
                    }
                }

                penX += (GlyphWidth + 1) * scale;
            }
        }

        /// <summary>以 <paramref name="centerX"/> 为水平中心绘制。</summary>
        public static void DrawCentered(Color32[] buffer, int bufferWidth, int bufferHeight,
                                        string text, int centerX, int y, int scale, Color32 color)
            => Draw(buffer, bufferWidth, bufferHeight, text,
                    centerX - MeasureWidth(text, scale) / 2, y, scale, color);
    }
}
