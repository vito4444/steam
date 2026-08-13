using System.Collections.Generic;
using UnityEngine;
using Abyssal.Visual;

namespace Overclock
{
    /// <summary>
    /// HUD 用的程序化纹理。
    ///
    /// 和场景一样不引入任何位图资产：面板底、进度条、元件图标、快捷键角标
    /// 全部现画。好处是任何一处数值或标注改动都只是改一行代码，
    /// 而且在没有显卡的机器上一秒钟就能重生成全部 UI 看效果。
    /// </summary>
    public static class HudTextures
    {
        static readonly Dictionary<string, Texture2D> Cache = new Dictionary<string, Texture2D>();

        static Texture2D Cached(string key, System.Func<Texture2D> build)
        {
            if (Cache.TryGetValue(key, out var tex) && tex != null) return tex;
            tex = build();
            Cache[key] = tex;
            return tex;
        }

        public static void ClearCache()
        {
            foreach (var t in Cache.Values) if (t != null) Object.DestroyImmediate(t);
            Cache.Clear();
        }

        static readonly Color32 PanelFill = new Color32(0x0D, 0x1A, 0x24, 0xEE);
        static readonly Color32 PanelEdge = new Color32(0x35, 0x6B, 0x82, 0xFF);
        static readonly Color32 Ink = new Color32(0xC8, 0xE4, 0xEC, 0xFF);
        static readonly Color32 InkDim = new Color32(0x5E, 0x7C, 0x8A, 0xFF);

        /// <summary>HUD 面板底：半透明深色加一圈冷调描边。</summary>
        public static Texture2D Panel(int w = 256, int h = 96)
        {
            return Cached($"panel:{w}:{h}", () =>
            {
                var p = new Painter(w, h, PanelFill);
                p.RectOutline(0, 0, w, h, 2, PanelEdge);
                // 左上角一小段亮边，让面板有个明确的「起点」。
                p.Rect(2, h - 5, w / 4, 3, new Color32(0x56, 0xB6, 0xD4, 0xFF));
                return p.ToTexture($"HudPanel_{w}x{h}", false);
            });
        }

        /// <summary>纯色块，配合 UI 的颜色相乘用于进度条填充。</summary>
        public static Texture2D Solid()
        {
            return Cached("solid", () =>
            {
                var p = new Painter(4, 4, new Color32(255, 255, 255, 255));
                return p.ToTexture("HudSolid", false, FilterMode.Point);
            });
        }

        /// <summary>
        /// 元件图标。用俯视的简笔轮廓来表达形体差异，
        /// 和场景里的三维形体一一对应，玩家不用二次学习。
        /// </summary>
        public static Texture2D ComponentIcon(Core.ComponentKind kind, int size = 96)
        {
            return Cached($"icon:{kind}:{size}", () =>
            {
                var p = new Painter(size, size, new Color32(0, 0, 0, 0));
                float c = size * 0.5f;
                var tint = IconColor(kind);

                switch (kind)
                {
                    case Core.ComponentKind.Trace:
                        // 一条细横线，代表最薄最省的走线。
                        p.Rect((int)(size * 0.12f), (int)(c - size * 0.05f),
                               (int)(size * 0.76f), (int)(size * 0.10f), tint);
                        break;

                    case Core.ComponentKind.Buffer:
                        p.Rect((int)(size * 0.24f), (int)(size * 0.24f),
                               (int)(size * 0.52f), (int)(size * 0.52f), tint);
                        p.RectOutline((int)(size * 0.24f), (int)(size * 0.24f),
                               (int)(size * 0.52f), (int)(size * 0.52f), 2, Ink);
                        break;

                    case Core.ComponentKind.Bus:
                        // 三条并排的粗线，暗示它是「多路合一」的宽通道。
                        for (int i = 0; i < 3; i++)
                        {
                            p.Rect((int)(size * 0.12f), (int)(size * (0.28f + i * 0.18f)),
                                   (int)(size * 0.76f), (int)(size * 0.09f), tint);
                        }
                        break;

                    case Core.ComponentKind.Splitter:
                        p.Line(size * 0.14f, c, c, c, size * 0.10f, tint);
                        p.Line(c, c, size * 0.86f, size * 0.78f, size * 0.09f, tint);
                        p.Line(c, c, size * 0.86f, size * 0.22f, size * 0.09f, tint);
                        break;

                    case Core.ComponentKind.Compressor:
                        // 两个相向的三角，表达「压缩」。
                        p.Line(size * 0.16f, size * 0.20f, c, c, size * 0.10f, tint);
                        p.Line(size * 0.16f, size * 0.80f, c, c, size * 0.10f, tint);
                        p.Line(size * 0.84f, size * 0.20f, c, c, size * 0.10f, tint);
                        p.Line(size * 0.84f, size * 0.80f, c, c, size * 0.10f, tint);
                        break;

                    case Core.ComponentKind.HeatSink:
                        // 散热鳍片。
                        for (int i = 0; i < 4; i++)
                        {
                            p.Rect((int)(size * (0.18f + i * 0.18f)), (int)(size * 0.18f),
                                   (int)(size * 0.08f), (int)(size * 0.64f), tint);
                        }
                        break;

                    default:
                        p.Ring(c, c, size * 0.30f, size * 0.08f, tint);
                        break;
                }

                return p.ToTexture($"Icon_{kind}", false);
            });
        }

        static Color32 IconColor(Core.ComponentKind kind)
        {
            var c = kind == Core.ComponentKind.HeatSink
                ? OverclockPalette.Coolant
                : OverclockPalette.Data;
            return new Color32((byte)(c.r * 255), (byte)(c.g * 255), (byte)(c.b * 255), 255);
        }

        /// <summary>
        /// 元件槽位底框。<paramref name="selected"/> 时描边变亮变粗，
        /// 这是玩家判断「我现在手上拿的是什么」的唯一线索，必须一眼可辨。
        /// </summary>
        public static Texture2D Slot(bool selected, bool affordable, int size = 128)
        {
            return Cached($"slot:{selected}:{affordable}:{size}", () =>
            {
                var fill = affordable
                    ? new Color32(0x0C, 0x14, 0x1C, 0xE8)
                    : new Color32(0x14, 0x0C, 0x0C, 0xE8);
                var p = new Painter(size, size, fill);

                if (selected)
                {
                    p.RectOutline(0, 0, size, size, 4, new Color32(0xE8, 0xF4, 0xF8, 0xFF));
                    p.RectOutline(4, 4, size - 8, size - 8, 1, new Color32(0x4E, 0xC8, 0xD4, 0xFF));
                }
                else
                {
                    p.RectOutline(0, 0, size, size, 2,
                        affordable ? PanelEdge : new Color32(0x4A, 0x22, 0x1E, 0xFF));
                }

                return p.ToTexture($"Slot_{selected}_{affordable}", false);
            });
        }

        /// <summary>
        /// 文本贴图。项目里没有字体资产，所有 UI 文字都走 5×7 点阵现画。
        /// 在这种像素风的科技界面里，点阵字反而比矢量字体更贴。
        /// </summary>
        public static Texture2D Text(string text, int scale = 2, bool dim = false)
        {
            return Cached($"text:{text}:{scale}:{dim}", () =>
            {
                int w = Mathf.Max(1, BitmapFont.MeasureWidth(text, scale));
                int h = BitmapFont.GlyphHeight * scale;
                var p = new Painter(w, h, new Color32(0, 0, 0, 0));
                p.Text(text, 0, 0, scale, dim ? InkDim : Ink);
                return p.ToTexture($"Text_{text}", false, FilterMode.Point);
            });
        }

        /// <summary>
        /// 升级卡片底框。悬停时描边变亮变粗，同时底色微微提亮。
        /// 三张卡并排时，玩家的视线需要一个明确的「我正指着这张」的反馈。
        /// </summary>
        public static Texture2D Card(bool hovered, int w = 400, int h = 300)
        {
            return Cached($"card:{hovered}:{w}:{h}", () =>
            {
                var fill = hovered
                    ? new Color32(0x12, 0x24, 0x30, 0xF6)
                    : new Color32(0x0A, 0x14, 0x1C, 0xEE);
                var p = new Painter(w, h, fill);

                if (hovered)
                {
                    p.RectOutline(0, 0, w, h, 4, new Color32(0x7A, 0xE4, 0xF4, 0xFF));
                    p.RectOutline(6, 6, w - 12, h - 12, 1, new Color32(0x2E, 0x6C, 0x82, 0xFF));
                }
                else
                {
                    p.RectOutline(0, 0, w, h, 2, new Color32(0x2A, 0x50, 0x62, 0xFF));
                }

                // 顶部一条色带，让卡片在视觉上有个「表头」。
                p.Rect(0, h - 10, w, 4,
                       hovered ? new Color32(0x7A, 0xE4, 0xF4, 0xFF) : new Color32(0x2E, 0x6C, 0x82, 0xFF));
                return p.ToTexture($"Card_{hovered}", false);
            });
        }

        /// <summary>
        /// 横向进度条。带刻度和一条目标线——
        /// 玩家需要知道的不只是「涨到哪了」，更是「还差多少」。
        /// </summary>
        public static Texture2D Gauge(int w = 512, int h = 40, float targetMark = 1f)
        {
            return Cached($"gauge:{w}:{h}:{targetMark:F2}", () =>
            {
                var p = new Painter(w, h, new Color32(0x0B, 0x16, 0x1F, 0xF4));
                p.RectOutline(0, 0, w, h, 2, PanelEdge);

                for (int i = 1; i < 10; i++)
                    p.Rect(w * i / 10, 3, 1, h - 6, new Color32(0x28, 0x4C, 0x5E, 0xFF));

                if (targetMark < 1f)
                {
                    int x = Mathf.Clamp((int)(w * targetMark), 2, w - 4);
                    p.Rect(x, 1, 2, h - 2, new Color32(0xF2, 0xC1, 0x4E, 0xFF));
                }

                return p.ToTexture($"Gauge_{w}", false);
            });
        }
    }
}
