using System.Collections.Generic;
using UnityEngine;

namespace Abyssal.Visual
{
    /// <summary>
    /// 控制舱用到的全部材质，全部在运行时构造，不依赖任何材质资产。
    ///
    /// 只用两类着色器：URP Lit 负责有质感的金属和塑料，URP Unlit 负责
    /// 自发光的指示灯和屏幕。自发光走 Unlit 是刻意的——舱内环境光被压得极暗，
    /// 让 Lit 材质去表现「亮」需要很高的 Emission 强度，反而会在软件渲染下失真。
    /// </summary>
    public static class MaterialLibrary
    {
        static readonly Dictionary<string, Material> Cache = new Dictionary<string, Material>();

        static Shader _lit;
        static Shader _unlit;

        static Shader Lit => _lit != null ? _lit : (_lit = FindShader(
            "Universal Render Pipeline/Lit", "Standard"));

        static Shader Unlit => _unlit != null ? _unlit : (_unlit = FindShader(
            "Universal Render Pipeline/Unlit", "Unlit/Texture"));

        static Shader FindShader(string preferred, string fallback)
        {
            var s = Shader.Find(preferred);
            if (s == null)
            {
                Debug.LogWarning($"ABYSSAL: shader '{preferred}' not found, falling back to '{fallback}'");
                s = Shader.Find(fallback);
            }
            return s;
        }

        public static void ClearCache()
        {
            foreach (var m in Cache.Values)
                if (m != null) Object.DestroyImmediate(m);
            Cache.Clear();
        }

        static Material Cached(string key, System.Func<Material> build)
        {
            if (Cache.TryGetValue(key, out var m) && m != null) return m;
            m = build();
            m.name = key;
            Cache[key] = m;
            return m;
        }

        /// <summary>带贴图的金属或塑料表面。</summary>
        public static Material Surface(string key, Texture2D map, Color tint,
                                       float metallic, float smoothness)
        {
            return Cached($"surf:{key}", () =>
            {
                var m = new Material(Lit);
                if (map != null) m.SetTexture("_BaseMap", map);
                m.SetColor("_BaseColor", tint);
                m.SetFloat("_Metallic", metallic);
                m.SetFloat("_Smoothness", smoothness);
                m.SetFloat("_Surface", 0f);
                return m;
            });
        }

        /// <summary>纯色金属或塑料，无贴图。</summary>
        public static Material Solid(string key, Color color, float metallic, float smoothness)
            => Surface(key, null, color, metallic, smoothness);

        /// <summary>
        /// 自发光表面。<paramref name="intensity"/> 大于 1 会溢出到 Bloom，
        /// 这是舱内那些「刺眼的小红灯」的来源。
        /// </summary>
        public static Material Emissive(string key, Texture2D map, Color color, float intensity)
        {
            return Cached($"emis:{key}:{intensity:F2}", () =>
            {
                var m = new Material(Unlit);
                if (map != null) m.SetTexture("_BaseMap", map);
                m.SetColor("_BaseColor", color * intensity);
                return m;
            });
        }

        /// <summary>
        /// 受光但自身也发一点光的表面，用于仪表盘面。
        ///
        /// 纯 Unlit 的表盘在暗舱里会显得像贴纸，因为它完全不参与光照，
        /// 工作灯扫过时没有任何变化。纯 Lit 又会因为环境光近乎为零而读不清刻度。
        /// 两者叠加：底光保证可读性，受光部分保证它是这个空间里的实体。
        /// </summary>
        public static Material LitEmissive(string key, Texture2D map, Color tint,
                                           Color emission, float smoothness = 0.55f)
        {
            return Cached($"litemis:{key}", () =>
            {
                var m = new Material(Lit);
                if (map != null)
                {
                    m.SetTexture("_BaseMap", map);
                    m.SetTexture("_EmissionMap", map);
                }
                m.SetColor("_BaseColor", tint);
                m.SetFloat("_Metallic", 0.15f);
                m.SetFloat("_Smoothness", smoothness);
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", emission);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                return m;
            });
        }

        /// <summary>
        /// 加色混合的粒子材质，用于深海雪和蒸汽。
        ///
        /// 加色而不是透明混合：悬浮颗粒是被探照灯照亮的，它们只会让画面变亮，
        /// 不会遮住后面的东西。用透明混合的话颗粒会在暗背景上留下一圈脏边。
        /// </summary>
        public static Material Particle(string key, Texture2D map, Color tint, float intensity)
        {
            return Cached($"particle:{key}:{intensity:F2}", () =>
            {
                var m = new Material(Unlit);
                if (map != null) m.SetTexture("_BaseMap", map);
                m.SetColor("_BaseColor", tint * intensity);
                m.SetFloat("_Surface", 1f);
                m.SetFloat("_Blend", 1f);
                m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
                m.SetFloat("_ZWrite", 0f);
                m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                return m;
            });
        }

        /// <summary>
        /// 耐压玻璃。几乎全透，只留一点点带色的反光。
        /// 完全透明的话玩家不会意识到中间隔着东西，反光太强又会挡住窗外的景象。
        /// </summary>
        public static Material Glass(string key, Color tint)
        {
            return Cached($"glass:{key}", () =>
            {
                var m = new Material(Lit);
                m.SetColor("_BaseColor", tint);
                m.SetFloat("_Metallic", 0.05f);
                m.SetFloat("_Smoothness", 0.94f);
                m.SetFloat("_Surface", 1f);
                m.SetFloat("_Blend", 0f);
                m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_ZWrite", 0f);
                m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                return m;
            });
        }

        /// <summary>带透明通道的贴花，用于指针和标签。</summary>
        public static Material Decal(string key, Texture2D map, Color tint)
        {
            return Cached($"decal:{key}", () =>
            {
                var m = new Material(Unlit);
                if (map != null) m.SetTexture("_BaseMap", map);
                m.SetColor("_BaseColor", tint);
                m.SetFloat("_Surface", 1f);          // Transparent
                m.SetFloat("_Blend", 0f);            // Alpha
                m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_ZWrite", 0f);
                m.SetFloat("_AlphaClip", 0f);
                m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.DisableKeyword("_ALPHATEST_ON");
                return m;
            });
        }

        // ------------------------------------------------------------------ 预设

        public static Material BulkheadSteel => Surface("bulkhead",
            ProceduralTextures.Panel("", 512, 512, 11),
            new Color(0.44f, 0.38f, 0.30f), 0.72f, 0.26f);

        public static Material DeckPlate => Surface("deck",
            ProceduralTextures.Panel("", 512, 512, 23),
            new Color(0.28f, 0.24f, 0.19f), 0.62f, 0.16f);

        public static Material ConsoleShell => Solid("console",
            new Color(0.30f, 0.26f, 0.20f), 0.68f, 0.34f);

        public static Material DarkPlastic => Solid("plastic",
            new Color(0.12f, 0.11f, 0.09f), 0.05f, 0.44f);

        public static Material BrassKnob => Solid("brass",
            new Color(0.78f, 0.54f, 0.22f), 0.88f, 0.68f);

        public static Material PipeSteel => Solid("pipe",
            new Color(0.38f, 0.33f, 0.27f), 0.84f, 0.42f);

        public static Material RustAccent => Solid("rust",
            new Color(0.72f, 0.33f, 0.11f), 0.52f, 0.26f);

        public static Material Hazard => Surface("hazard",
            ProceduralTextures.HazardStripes(), Color.white, 0.30f, 0.30f);
    }
}
