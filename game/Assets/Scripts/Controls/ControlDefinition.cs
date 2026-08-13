using Maner.Sim;

namespace Maner.Controls
{
    public enum PanelId
    {
        Power = 0,
        Ventilation = 1,
        Hoist = 2,
    }

    /// <summary>
    /// 控件原型。每一种都有独立的物理操作方式——本作刻意不提供「按 E 交互」的统一快捷方式，
    /// 因为操作本身就是玩法：阀轮要转好几圈，闸刀要压到底，旋钮要画圆。
    /// </summary>
    public enum ControlKind
    {
        /// <summary>闸刀。必须向下拖拽超过行程阈值才会合闸，松手不到位会弹回。</summary>
        KnifeSwitch,
        /// <summary>拨杆。在若干个固定档位之间拖拽切换，有明确的段落感。</summary>
        ToggleLever,
        /// <summary>旋钮。按住画圆，连续调节。</summary>
        RotaryKnob,
        /// <summary>阀轮。需要连续转多圈才能从全关到全开，是最耗时的操作。</summary>
        ValveWheel,
        /// <summary>按钮。按下弹起，产生一次性脉冲。</summary>
        PushButton,
        /// <summary>断路器。二态，但可以被系统单方面跳闸，跳闸后必须手动复位。</summary>
        Breaker,
        /// <summary>调速手柄。沿弧形轨道拖拽的连续量，是唯一需要精细控制的控件。</summary>
        ThrottleHandle,
        /// <summary>表盘。只读，不可交互。</summary>
        Gauge,
    }

    public enum ControlId
    {
        // —— 供电盘 ——
        GeneratorMaster,
        FuelValve,
        CoolantPump,
        Excitation,
        BreakerHoist,
        BreakerVentilation,
        BreakerLighting,
        BreakerAuxiliary,
        BatteryTie,
        ResetOverload,

        // —— 通风盘 ——
        MainFanSwitch,
        FanSpeedWheel,
        Damper1,
        Damper2,
        Damper3,
        GasDrainagePump,
        ReverseAirflow,
        SilenceGasAlarm,

        // —— 卷扬盘 ——
        HoistPower,
        Throttle,
        Brake,
        CageLock,
        Direction,
        CageLight,
        RopeSpeedTrim,
        SignalBell,
        ConfirmBottomSignal,
        ResetOverspeed,

        // —— 表盘（只读） ——
        GaugeBusVoltage,
        GaugeLoad,
        GaugeFrequency,
        GaugeCoolantTemp,
        GaugeFuel,
        GaugeGas,
        GaugeAirflow,
        GaugeFanSpeed,
        GaugeDepth,
        GaugeCageSpeed,
        GaugePayload,
        GaugeRopeTension,
    }

    /// <summary>
    /// 程序化生成控件所需的全部参数。控制台的物理布局完全由这张表驱动，
    /// 没有任何手工摆放的场景对象——这是本方案「零手工美术资产」的基础。
    /// </summary>
    public readonly struct ControlDefinition
    {
        public readonly ControlId Id;
        public readonly ControlKind Kind;
        public readonly PanelId Panel;
        /// <summary>面板丝印文字。运行时由代码绘制到面板贴图上。</summary>
        public readonly string Label;
        /// <summary>面板局部坐标，原点在面板左下角，单位米。</summary>
        public readonly float X;
        public readonly float Y;
        /// <summary>控件外形尺寸，单位米。</summary>
        public readonly float Size;
        public readonly double DefaultValue;
        /// <summary>拨杆档位数。2 表示开关，3 表示上/停/下这类三态。</summary>
        public readonly int Detents;
        /// <summary>阀轮从全关转到全开所需的圈数。</summary>
        public readonly float TurnsToFull;
        /// <summary>闸刀合闸所需的最小行程比例。</summary>
        public readonly float ThrowThreshold;
        /// <summary>按钮对应的仿真脉冲。非按钮为 null。</summary>
        public readonly SimPulse? Pulse;
        /// <summary>该控件三段音效的音色基频，单位赫兹。</summary>
        public readonly float AudioBaseHz;

        public ControlDefinition(
            ControlId id, ControlKind kind, PanelId panel, string label,
            float x, float y, float size,
            double defaultValue = 0.0, int detents = 2, float turnsToFull = 3f,
            float throwThreshold = 0.6f, SimPulse? pulse = null, float audioBaseHz = 220f)
        {
            Id = id;
            Kind = kind;
            Panel = panel;
            Label = label;
            X = x;
            Y = y;
            Size = size;
            DefaultValue = defaultValue;
            Detents = detents;
            TurnsToFull = turnsToFull;
            ThrowThreshold = throwThreshold;
            Pulse = pulse;
            AudioBaseHz = audioBaseHz;
        }

        public bool IsInteractive => Kind != ControlKind.Gauge;

        public bool IsContinuous =>
            Kind == ControlKind.RotaryKnob ||
            Kind == ControlKind.ValveWheel ||
            Kind == ControlKind.ThrottleHandle;
    }
}
