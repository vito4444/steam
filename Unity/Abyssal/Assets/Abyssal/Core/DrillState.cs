using System;

namespace Abyssal.Core
{
    /// <summary>井下和地面设备的完整状态。仪表读的就是这里的字段。</summary>
    [Serializable]
    public sealed class DrillState
    {
        // ---------------------------------------------------------- 进尺
        /// <summary>当前井深，m。</summary>
        public double Depth = 1850.0;

        /// <summary>机械钻速，m/h。</summary>
        public double RateOfPenetration;

        /// <summary>本班次已进尺，m。绩效评估的主要指标。</summary>
        public double FootageThisShift;

        // ---------------------------------------------------------- 钻头
        /// <summary>钻头磨损度 0–1。到 1 就必须起钻换钻头，那意味着这一班白干。</summary>
        public double BitWear;

        /// <summary>井底温度，°C。</summary>
        public double BottomholeTemperature = 60.0;

        // ---------------------------------------------------------- 力学
        /// <summary>转盘扭矩，kN·m。</summary>
        public double Torque;

        /// <summary>钻具疲劳损伤 0–1。到 1 就断钻具，这一口井基本就废了。</summary>
        public double DrillstringFatigue;

        /// <summary>卡钻程度 0–1。到 1 钻具完全卡死。</summary>
        public double StuckSeverity;

        // ---------------------------------------------------------- 水力
        /// <summary>实际泥浆密度，kg/m³。跟随目标值但有滞后。</summary>
        public double MudDensity = 1200.0;

        /// <summary>井底压力，kPa。</summary>
        public double BottomholePressure;

        /// <summary>当量循环密度，kg/m³。玩家实际读的是这个而不是裸压力值。</summary>
        public double EquivalentCirculatingDensity;

        /// <summary>环空岩屑浓度 0–1。过高会卡钻，也会拖慢钻速。</summary>
        public double CuttingsLoad;

        /// <summary>泥浆池体积，m³。**这是井涌和漏失最早的可观测信号。**</summary>
        public double PitVolume = Limits.NominalPitVolume;

        /// <summary>已侵入井筒的地层流体，m³。</summary>
        public double KickVolume;

        /// <summary>已漏入地层的泥浆，m³。</summary>
        public double LossVolume;

        /// <summary>井涌流体是否含气。含气井涌上返时会膨胀，处理窗口短得多。</summary>
        public bool KickIsGas;

        /// <summary>关井后的立管压力，kPa。压井作业时玩家盯的就是它。</summary>
        public double ShutInPressure;

        // ---------------------------------------------------------- 派生
        /// <summary>当前所在地层。仿真每步更新。</summary>
        public Formation CurrentFormation;

        /// <summary>泵是否在有效循环。</summary>
        public bool IsCirculating;

        /// <summary>本次仿真已运行的时间，秒。</summary>
        public double ElapsedSeconds;

        public DrillState Clone() => (DrillState)MemberwiseClone();
    }

    /// <summary>仿真每一步产生的离散事件，供音效、告警灯和日志系统消费。</summary>
    public enum DrillEvent
    {
        None = 0,
        FormationChanged,
        KickStarted,
        KickWorsening,
        LossStarted,
        TorqueOverLimit,
        BitOverheating,
        StuckPipeWarning,
        StuckPipeSevere,
        BitWornOut,
        DrillstringFailed,
        Blowout,
        PumpStalled,
    }
}
