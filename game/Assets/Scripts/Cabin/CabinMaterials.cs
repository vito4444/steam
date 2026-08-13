using UnityEngine;

namespace Maner.Cabin
{
    /// <summary>
    /// 控制舱使用的全部材质，运行时用代码创建，不依赖任何美术资产文件。
    /// 色彩定位：1970 年代东欧工业美学——灰绿搪瓷漆、黄铜、胶木、磨损的钢。
    /// </summary>
    public sealed class CabinMaterials
    {
        public readonly Material PanelSteel;
        public readonly Material FrameSteel;
        public readonly Material BrightSteel;
        public readonly Material Brass;
        public readonly Material Bakelite;
        public readonly Material RedBakelite;
        public readonly Material GaugeFace;
        public readonly Material Needle;
        public readonly Material LampOff;
        public readonly Material LampAmber;
        public readonly Material LampRed;
        public readonly Material LampGreen;
        public readonly Material Glass;
        public readonly Material Concrete;
        public readonly Material RustedIron;
        public readonly Material CrtScreen;

        public CabinMaterials()
        {
            // Shader.Find 只能命中已被打包进构建的 shader。控制舱的材质全部在运行时创建，
            // 没有任何场景资产引用这些 shader，因此必须由 ManerBootstrap 把它们写进
            // Graphics Settings 的 Always Included Shaders，否则在 Player 里会静默拿到 null，
            // 全场退化成白色默认材质——这个坑在编辑器里完全看不出来。
            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            Shader unlit = Shader.Find("Universal Render Pipeline/Unlit");

            if (lit == null)
            {
                Debug.LogError("[Cabin] 找不到 Universal Render Pipeline/Lit，材质将全部失效。" +
                               "检查 Graphics Settings 的 Always Included Shaders。");
                lit = Shader.Find("Standard") ?? Shader.Find("Sprites/Default");
            }
            if (unlit == null)
            {
                unlit = lit;
            }

            // 金属度刻意压得很低。控制台上的钢板全是搪瓷漆面，物理上属于电介质而非裸金属；
            // 更要紧的是这间屋子里没有天空也没有反射探针，高金属度的材质会因为无处可反射
            // 而直接变成一块黑板——只剩边缘一道高光，正是第一版实机截图的样子。
            PanelSteel = Make(lit, "M_PanelSteel", new Color(0.212f, 0.248f, 0.228f), 0.26f, 0.0f);
            FrameSteel = Make(lit, "M_FrameSteel", new Color(0.155f, 0.165f, 0.160f), 0.22f, 0.08f);
            BrightSteel = Make(lit, "M_BrightSteel", new Color(0.44f, 0.45f, 0.46f), 0.38f, 0.30f);
            Brass = Make(lit, "M_Brass", new Color(0.52f, 0.38f, 0.17f), 0.40f, 0.28f);
            Bakelite = Make(lit, "M_Bakelite", new Color(0.105f, 0.096f, 0.090f), 0.34f, 0.0f);
            RedBakelite = Make(lit, "M_RedBakelite", new Color(0.46f, 0.095f, 0.068f), 0.38f, 0.0f);
            Needle = Make(lit, "M_Needle", new Color(0.82f, 0.18f, 0.11f), 0.24f, 0.0f);
            Concrete = Make(lit, "M_Concrete", new Color(0.145f, 0.142f, 0.134f), 0.05f, 0.0f);
            RustedIron = Make(lit, "M_RustedIron", new Color(0.215f, 0.160f, 0.120f), 0.16f, 0.12f);

            // 表盘与指示灯走 Unlit。它们在设定上都是背光件，本来就不该受舱内照明影响；
            // 同时这也绕开了 emission 着色器变体在构建时被剥离的风险——那种失效是静默的，
            // 在编辑器里完全看不出来。
            GaugeFace = MakeUnlit(unlit, "M_GaugeFace", Color.white);
            LampOff = Make(lit, "M_LampOff", new Color(0.115f, 0.100f, 0.092f), 0.55f, 0.0f);
            LampAmber = MakeUnlit(unlit, "M_LampAmber", new Color(1.00f, 0.66f, 0.22f));
            LampRed = MakeUnlit(unlit, "M_LampRed", new Color(1.00f, 0.20f, 0.14f));
            LampGreen = MakeUnlit(unlit, "M_LampGreen", new Color(0.34f, 1.00f, 0.46f));
            CrtScreen = MakeUnlit(unlit, "M_CrtScreen", new Color(0.36f, 1.0f, 0.48f));

            Glass = Make(lit, "M_Glass", new Color(0.72f, 0.78f, 0.80f, 0.16f), 0.94f, 0.0f);
            SetTransparent(Glass);
        }

        static Material Make(Shader shader, string name, Color baseColor, float smoothness, float metallic)
        {
            var m = new Material(shader) { name = name };
            m.SetColor("_BaseColor", baseColor);
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_Metallic", metallic);
            EnsureUnitTiling(m);
            return m;
        }

        /// <summary>
        /// 显式把主贴图的 tiling 设为 1、offset 设为 0。
        /// 用 new Material(shader) 在运行时创建的材质，其 _BaseMap_ST 不保证被初始化成
        /// (1,1,0,0)；一旦缩放是 0，整张网格会采样贴图上的同一个纹素，表现为一块纯色。
        /// 这种失效非常隐蔽：贴图内容、绑定与网格 UV 全部检查通过，就是画不出来。
        /// </summary>
        public static void EnsureUnitTiling(Material m)
        {
            if (m.HasProperty("_BaseMap"))
            {
                m.SetTextureScale("_BaseMap", Vector2.one);
                m.SetTextureOffset("_BaseMap", Vector2.zero);
            }
            if (m.HasProperty("_MainTex"))
            {
                m.SetTextureScale("_MainTex", Vector2.one);
                m.SetTextureOffset("_MainTex", Vector2.zero);
            }
        }

        static Material MakeUnlit(Shader shader, string name, Color baseColor)
        {
            var m = new Material(shader) { name = name };
            m.SetColor("_BaseColor", baseColor);
            // Unlit shader 同时暴露 _Color，两个都写上以兼容不同 URP 小版本。
            if (m.HasProperty("_Color"))
            {
                m.SetColor("_Color", baseColor);
            }
            EnsureUnitTiling(m);
            return m;
        }

        static void SetTransparent(Material m)
        {
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.DisableKeyword("_ALPHATEST_ON");
        }

        /// <summary>按状态取指示灯材质。</summary>
        public Material Lamp(LampState state) => state switch
        {
            LampState.Amber => LampAmber,
            LampState.Red => LampRed,
            LampState.Green => LampGreen,
            _ => LampOff,
        };
    }

    public enum LampState
    {
        Off,
        Amber,
        Red,
        Green,
    }
}
