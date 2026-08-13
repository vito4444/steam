using System.Collections.Generic;
using UnityEngine;

namespace Maner.Cabin
{
    /// <summary>
    /// 5×7 点阵字库。控制舱里所有丝印文字与表盘数字都用它绘制到贴图上。
    ///
    /// 选择点阵而不是矢量字体有两个原因：一是它不需要任何字体资产文件，
    /// 二是点阵本身就是工业仪表丝网印刷的真实质感，越糙越对。
    /// 界面上的中文由 UI 层的系统字体负责，不烘焙进贴图。
    /// </summary>
    public static class BitmapFont
    {
        public const int GlyphWidth = 5;
        public const int GlyphHeight = 7;

        static readonly Dictionary<char, byte[]> Glyphs = new Dictionary<char, byte[]>
        {
            [' '] = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 },
            ['0'] = new byte[] { 0x0E, 0x11, 0x13, 0x15, 0x19, 0x11, 0x0E },
            ['1'] = new byte[] { 0x04, 0x0C, 0x04, 0x04, 0x04, 0x04, 0x0E },
            ['2'] = new byte[] { 0x0E, 0x11, 0x01, 0x02, 0x04, 0x08, 0x1F },
            ['3'] = new byte[] { 0x1F, 0x02, 0x04, 0x02, 0x01, 0x11, 0x0E },
            ['4'] = new byte[] { 0x02, 0x06, 0x0A, 0x12, 0x1F, 0x02, 0x02 },
            ['5'] = new byte[] { 0x1F, 0x10, 0x1E, 0x01, 0x01, 0x11, 0x0E },
            ['6'] = new byte[] { 0x06, 0x08, 0x10, 0x1E, 0x11, 0x11, 0x0E },
            ['7'] = new byte[] { 0x1F, 0x01, 0x02, 0x04, 0x08, 0x08, 0x08 },
            ['8'] = new byte[] { 0x0E, 0x11, 0x11, 0x0E, 0x11, 0x11, 0x0E },
            ['9'] = new byte[] { 0x0E, 0x11, 0x11, 0x0F, 0x01, 0x02, 0x0C },
            ['A'] = new byte[] { 0x0E, 0x11, 0x11, 0x1F, 0x11, 0x11, 0x11 },
            ['B'] = new byte[] { 0x1E, 0x11, 0x11, 0x1E, 0x11, 0x11, 0x1E },
            ['C'] = new byte[] { 0x0E, 0x11, 0x10, 0x10, 0x10, 0x11, 0x0E },
            ['D'] = new byte[] { 0x1C, 0x12, 0x11, 0x11, 0x11, 0x12, 0x1C },
            ['E'] = new byte[] { 0x1F, 0x10, 0x10, 0x1E, 0x10, 0x10, 0x1F },
            ['F'] = new byte[] { 0x1F, 0x10, 0x10, 0x1E, 0x10, 0x10, 0x10 },
            ['G'] = new byte[] { 0x0E, 0x11, 0x10, 0x17, 0x11, 0x11, 0x0F },
            ['H'] = new byte[] { 0x11, 0x11, 0x11, 0x1F, 0x11, 0x11, 0x11 },
            ['I'] = new byte[] { 0x0E, 0x04, 0x04, 0x04, 0x04, 0x04, 0x0E },
            ['J'] = new byte[] { 0x07, 0x02, 0x02, 0x02, 0x02, 0x12, 0x0C },
            ['K'] = new byte[] { 0x11, 0x12, 0x14, 0x18, 0x14, 0x12, 0x11 },
            ['L'] = new byte[] { 0x10, 0x10, 0x10, 0x10, 0x10, 0x10, 0x1F },
            ['M'] = new byte[] { 0x11, 0x1B, 0x15, 0x15, 0x11, 0x11, 0x11 },
            ['N'] = new byte[] { 0x11, 0x11, 0x19, 0x15, 0x13, 0x11, 0x11 },
            ['O'] = new byte[] { 0x0E, 0x11, 0x11, 0x11, 0x11, 0x11, 0x0E },
            ['P'] = new byte[] { 0x1E, 0x11, 0x11, 0x1E, 0x10, 0x10, 0x10 },
            ['Q'] = new byte[] { 0x0E, 0x11, 0x11, 0x11, 0x15, 0x12, 0x0D },
            ['R'] = new byte[] { 0x1E, 0x11, 0x11, 0x1E, 0x14, 0x12, 0x11 },
            ['S'] = new byte[] { 0x0F, 0x10, 0x10, 0x0E, 0x01, 0x01, 0x1E },
            ['T'] = new byte[] { 0x1F, 0x04, 0x04, 0x04, 0x04, 0x04, 0x04 },
            ['U'] = new byte[] { 0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x0E },
            ['V'] = new byte[] { 0x11, 0x11, 0x11, 0x11, 0x11, 0x0A, 0x04 },
            ['W'] = new byte[] { 0x11, 0x11, 0x11, 0x15, 0x15, 0x15, 0x0A },
            ['X'] = new byte[] { 0x11, 0x11, 0x0A, 0x04, 0x0A, 0x11, 0x11 },
            ['Y'] = new byte[] { 0x11, 0x11, 0x0A, 0x04, 0x04, 0x04, 0x04 },
            ['Z'] = new byte[] { 0x1F, 0x01, 0x02, 0x04, 0x08, 0x10, 0x1F },
            ['.'] = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x0C, 0x0C },
            [','] = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x0C, 0x04, 0x08 },
            [':'] = new byte[] { 0x00, 0x0C, 0x0C, 0x00, 0x0C, 0x0C, 0x00 },
            ['-'] = new byte[] { 0x00, 0x00, 0x00, 0x1F, 0x00, 0x00, 0x00 },
            ['+'] = new byte[] { 0x00, 0x04, 0x04, 0x1F, 0x04, 0x04, 0x00 },
            ['/'] = new byte[] { 0x01, 0x02, 0x02, 0x04, 0x08, 0x08, 0x10 },
            ['%'] = new byte[] { 0x19, 0x1A, 0x02, 0x04, 0x08, 0x0B, 0x13 },
            ['°'] = new byte[] { 0x0C, 0x12, 0x12, 0x0C, 0x00, 0x00, 0x00 },
            ['('] = new byte[] { 0x02, 0x04, 0x08, 0x08, 0x08, 0x04, 0x02 },
            [')'] = new byte[] { 0x08, 0x04, 0x02, 0x02, 0x02, 0x04, 0x08 },
            ['!'] = new byte[] { 0x04, 0x04, 0x04, 0x04, 0x04, 0x00, 0x04 },
            ['*'] = new byte[] { 0x00, 0x0A, 0x04, 0x1F, 0x04, 0x0A, 0x00 },
            ['_'] = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x1F },
        };

        public static int MeasureWidth(string text, int scale, int spacing = 1) =>
            text.Length <= 0 ? 0 : text.Length * (GlyphWidth + spacing) * scale - spacing * scale;

        /// <summary>
        /// 在像素缓冲上绘制一行文字。originX/originY 是左上角，Y 轴向下增长。
        /// 缓冲按行主序排列，索引为 y * width + x。
        /// </summary>
        public static void Draw(Color32[] buffer, int width, int height, string text,
            int originX, int originY, int scale, Color32 color, int spacing = 1, bool mirror = false)
        {
            int advance = (GlyphWidth + spacing) * scale;
            int totalWidth = MeasureWidth(text, scale, spacing);

            for (int index = 0; index < text.Length; index++)
            {
                char raw = text[index];
                // 镜像模式下从右往左排字符，配合字形自身的水平翻转，
                // 这样贴图被 UV 再镜像一次之后，玩家看到的就是正向文字。
                int cursor = mirror
                    ? originX + totalWidth - (index + 1) * advance + spacing * scale
                    : originX + index * advance;

                char c = char.ToUpperInvariant(raw);
                if (!Glyphs.TryGetValue(c, out var rows))
                {
                    continue;
                }

                for (int gy = 0; gy < GlyphHeight; gy++)
                {
                    byte row = rows[gy];
                    for (int gx = 0; gx < GlyphWidth; gx++)
                    {
                        int sampleColumn = mirror ? GlyphWidth - 1 - gx : gx;
                        if ((row & (1 << (GlyphWidth - 1 - sampleColumn))) == 0)
                        {
                            continue;
                        }

                        for (int sy = 0; sy < scale; sy++)
                        {
                            int py = originY + gy * scale + sy;
                            if (py < 0 || py >= height)
                            {
                                continue;
                            }
                            int rowBase = py * width;
                            for (int sx = 0; sx < scale; sx++)
                            {
                                int px = cursor + gx * scale + sx;
                                if (px < 0 || px >= width)
                                {
                                    continue;
                                }
                                buffer[rowBase + px] = color;
                            }
                        }
                    }
                }
            }
        }

        /// <summary>以给定点为水平中心绘制。</summary>
        public static void DrawCentered(Color32[] buffer, int width, int height, string text,
            int centerX, int topY, int scale, Color32 color, int spacing = 1, bool mirror = false)
        {
            int w = MeasureWidth(text, scale, spacing);
            Draw(buffer, width, height, text, centerX - w / 2, topY, scale, color, spacing, mirror);
        }
    }
}
