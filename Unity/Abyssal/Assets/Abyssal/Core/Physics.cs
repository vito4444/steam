using System;

namespace Abyssal.Core
{
    /// <summary>
    /// 钻井物理的基础常量与换算。
    ///
    /// 全部采用公制：深度 m、压力 kPa、密度 kg/m³、钻压 kN、
    /// 泵排量 L/s、机械钻速 m/h、扭矩 kN·m、温度 °C。
    /// </summary>
    public static class Physics
    {
        /// <summary>重力加速度，m/s²。</summary>
        public const double G = 9.81;

        /// <summary>地表平均温度，°C。</summary>
        public const double SurfaceTemperature = 4.0;

        /// <summary>地温梯度，°C/m。深海钻井典型值。</summary>
        public const double GeothermalGradient = 0.030;

        /// <summary>钻头半径，m。用于扭矩换算。</summary>
        public const double BitRadius = 0.152;

        /// <summary>
        /// 静液柱压力，kPa。
        /// 这是泥浆自重压在井底的压力，是压制地层流体的主要手段。
        /// </summary>
        public static double HydrostaticPressure(double mudDensity, double depth)
            => mudDensity * G * depth / 1000.0;

        /// <summary>
        /// 环空摩阻，kPa。泥浆在井筒和钻杆之间的环形空间里向上流动产生的额外压力。
        /// 与排量的平方成正比，与井深成正比。这是「循环时井底压力高于停泵时」的原因。
        /// </summary>
        public static double AnnularFrictionLoss(double pumpRate, double depth)
            => 0.00042 * pumpRate * pumpRate * depth;

        /// <summary>
        /// 当量循环密度对应的井底压力，kPa。等于静液柱加环空摩阻。
        /// 判断井涌和漏失都用这个值，而不是单纯的静液柱压力。
        /// </summary>
        public static double BottomholePressure(double mudDensity, double depth, double pumpRate)
            => HydrostaticPressure(mudDensity, depth) + AnnularFrictionLoss(pumpRate, depth);

        /// <summary>把井底压力换算回等效泥浆密度，kg/m³。仪表上给玩家看的就是这个数。</summary>
        public static double EquivalentCirculatingDensity(double bottomholePressure, double depth)
            => depth < 1.0 ? 0.0 : bottomholePressure * 1000.0 / (G * depth);

        /// <summary>未受扰动的地层静态温度，°C。</summary>
        public static double StaticFormationTemperature(double depth)
            => SurfaceTemperature + GeothermalGradient * depth;

        public static double Clamp(double v, double lo, double hi)
            => v < lo ? lo : (v > hi ? hi : v);

        public static double Clamp01(double v) => Clamp(v, 0.0, 1.0);

        /// <summary>把 v 从 [a,b] 线性映射到 [0,1] 并截断。</summary>
        public static double InverseLerp(double a, double b, double v)
            => Math.Abs(b - a) < 1e-9 ? 0.0 : Clamp01((v - a) / (b - a));

        public static double Lerp(double a, double b, double t) => a + (b - a) * Clamp01(t);
    }
}
