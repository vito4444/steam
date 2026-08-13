using System;

namespace Abyssal.Core
{
    /// <summary>
    /// 玩家通过控制舱里的物理旋钮、拨杆和阀门能改变的量。
    ///
    /// 这个结构体是「玩家意图」的完整表述。仿真只读它，不写它，
    /// 这样整套模型可以脱离 Unity 用脚本驱动，跑批量参数扫描。
    /// </summary>
    [Serializable]
    public struct DrillControls
    {
        /// <summary>钻压，kN。松开钻头 = 0，压得太狠钻头会憋死。</summary>
        public double WeightOnBit;

        /// <summary>转盘转速，rev/min。</summary>
        public double RotarySpeed;

        /// <summary>泥浆密度，kg/m³。改动它需要时间（见 <see cref="MudSystem"/>）。</summary>
        public double TargetMudDensity;

        /// <summary>泥浆泵排量，L/s。同时负责清洗井底和冷却钻头。</summary>
        public double PumpRate;

        /// <summary>钻头是否下到井底。抬离井底后不再进尺，但仍可循环。</summary>
        public bool BitOnBottom;

        /// <summary>防喷器是否关闭。关井能挡住井涌，但井筒压力会迅速上升。</summary>
        public bool BlowoutPreventerClosed;

        /// <summary>节流阀开度 0–1。关井后靠它控制井筒回压，是压井作业的核心操作。</summary>
        public double ChokeOpening;

        public static DrillControls Idle => new DrillControls
        {
            WeightOnBit = 0.0,
            RotarySpeed = 0.0,
            TargetMudDensity = 1200.0,
            PumpRate = 0.0,
            BitOnBottom = false,
            BlowoutPreventerClosed = false,
            ChokeOpening = 1.0,
        };

        /// <summary>典型的稳定钻进参数，用于测试和「培训模式」的推荐值。</summary>
        public static DrillControls NominalDrilling => new DrillControls
        {
            WeightOnBit = 160.0,
            RotarySpeed = 120.0,
            TargetMudDensity = 1250.0,
            PumpRate = 34.0,
            BitOnBottom = true,
            BlowoutPreventerClosed = false,
            ChokeOpening = 1.0,
        };

        public DrillControls Clamped()
        {
            var c = this;
            c.WeightOnBit = Physics.Clamp(c.WeightOnBit, 0.0, Limits.MaxWeightOnBit);
            c.RotarySpeed = Physics.Clamp(c.RotarySpeed, 0.0, Limits.MaxRotarySpeed);
            c.TargetMudDensity = Physics.Clamp(c.TargetMudDensity, Limits.MinMudDensity, Limits.MaxMudDensity);
            c.PumpRate = Physics.Clamp(c.PumpRate, 0.0, Limits.MaxPumpRate);
            c.ChokeOpening = Physics.Clamp01(c.ChokeOpening);
            return c;
        }
    }

    /// <summary>设备的物理量程。仪表刻度和旋钮行程都从这里取，保证两者一致。</summary>
    public static class Limits
    {
        public const double MaxWeightOnBit = 300.0;   // kN
        public const double MaxRotarySpeed = 200.0;   // rev/min
        public const double MinMudDensity = 1000.0;   // kg/m³
        public const double MaxMudDensity = 2200.0;   // kg/m³
        public const double MaxPumpRate = 60.0;       // L/s

        /// <summary>钻具扭矩极限，kN·m。超过就开始累积疲劳损伤。</summary>
        public const double TorqueLimit = 38.0;

        /// <summary>钻头温度报警线，°C。超过它磨损速率显著上升。</summary>
        public const double BitTemperatureWarning = 110.0;

        /// <summary>泥浆池标称容积，m³。液位变化是井涌和漏失的第一指示器。</summary>
        public const double NominalPitVolume = 62.0;

        /// <summary>井筒截面积，m²。比钻头略大，用于计算钻进时的泥浆消耗。</summary>
        public const double HoleCrossSection = 0.080;
    }
}
