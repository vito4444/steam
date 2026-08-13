using UnityEngine;

namespace Overclock
{
    /// <summary>
    /// OVERCLOCK 的全部配色。
    ///
    /// 整套视觉只有两种材质：纯黑的哑光基底，和自发光的线条。
    /// 没有贴图、没有 UV 展开、没有法线贴图、没有实时光照。
    /// 这不只是省成本——在纯黑背景上，颜色本身就成了唯一的信息载体，
    /// 玩家扫一眼就能读出整个系统的状态：哪里在流、哪里在烧、哪里已经死了。
    ///
    /// 这也是它在没有独立显卡的机器上渲染结果和真机几乎一致的原因：
    /// Unlit 加自发光的路径不涉及任何光照计算。
    /// </summary>
    public static class OverclockPalette
    {
        /// <summary>基底。几乎是黑的，只带一点冷调。</summary>
        public static readonly Color Substrate = new Color(0.062f, 0.086f, 0.118f);

        /// <summary>格子的分隔网格线。</summary>
        public static readonly Color Grid = new Color(0.130f, 0.180f, 0.235f);

        /// <summary>坏块。暗褐色，和冷调的基底区分开。</summary>
        public static readonly Color DeadCell = new Color(0.135f, 0.098f, 0.078f);

        /// <summary>烧毁的元件。焦黑带一点余烬的红。</summary>
        public static readonly Color Burned = new Color(0.145f, 0.055f, 0.043f);

        /// <summary>数据流。整个画面的主色。</summary>
        public static readonly Color Data = new Color(0.133f, 0.827f, 0.933f);

        /// <summary>高优先级数据。</summary>
        public static readonly Color Priority = new Color(0.659f, 0.333f, 0.969f);

        /// <summary>热量。</summary>
        public static readonly Color Heat = new Color(0.870f, 0.430f, 0.130f);

        /// <summary>过热警告。</summary>
        public static readonly Color Critical = new Color(0.780f, 0.140f, 0.090f);

        /// <summary>数据源。</summary>
        public static readonly Color Source = new Color(0.545f, 0.361f, 0.965f);

        /// <summary>输出端口。</summary>
        public static readonly Color Sink = new Color(0.290f, 0.871f, 0.502f);

        /// <summary>散热片。冷色，和发热的元件形成对比。</summary>
        public static readonly Color Coolant = new Color(0.376f, 0.647f, 0.980f);

        /// <summary>玩家当前选中的格子。</summary>
        public static readonly Color Selection = Color.white;

        /// <summary>
        /// 按热应力取颜色：从数据青经热橙过渡到过热红。
        /// 分两段而不是直接插值，是为了让「开始发烫」和「马上要炸」
        /// 在余光里就能区分开。
        /// </summary>
        public static Color ByThermalStress(float stress)
        {
            stress = Mathf.Clamp01(stress);
            var hue = stress < 0.55f
                ? Color.Lerp(Data, Heat, stress / 0.55f)
                : Color.Lerp(Heat, Critical, (stress - 0.55f) / 0.45f);

            // 温度只负责色相，亮度留给流量。
            // 两者都往上推的话，一过热整片就烧成白光，把数据包和拓扑全糊掉——
            // 而那恰恰是玩家在最紧张的时刻最需要看清的东西。
            // 危险感应该来自「颜色不对了」，不是来自「更刺眼了」。
            return hue * Mathf.Lerp(1.0f, 0.62f, stress);
        }

        /// <summary>基底格子的颜色。热量会把衬底本身也烤出颜色来。</summary>
        public static Color SubstrateByHeat(float stress)
        {
            stress = Mathf.Clamp01(stress);
            // 用平方让低温区保持干净的黑，只有真正热起来的地方才染色。
            return Color.Lerp(Substrate, Heat * 0.42f, stress * stress);
        }
    }
}
