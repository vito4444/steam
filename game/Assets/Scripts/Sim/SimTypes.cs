namespace Maner.Sim
{
    /// <summary>
    /// 玩家通过控制台施加给仿真的持续状态。所有字段都是「控件当前所处的物理位置」，
    /// 而不是「玩家想做什么」——这是本作的核心设计：你操作的是机器，不是菜单。
    /// </summary>
    public struct SimInputs
    {
        // —— 供电盘 ——
        public bool GeneratorMaster;      // 主机组闸刀
        public double FuelValve;          // 燃油阀轮 0..1
        public bool CoolantPump;          // 冷却泵开关
        public double Excitation;         // 励磁旋钮 0..1，调节母线电压设定点
        public bool BreakerHoist;         // 卷扬分路断路器
        public bool BreakerVentilation;   // 通风分路断路器
        public bool BreakerLighting;      // 照明分路断路器
        public bool BreakerAuxiliary;     // 辅助分路断路器
        public bool BatteryTie;           // 应急电池并网闸刀

        // —— 通风盘 ——
        public bool MainFanSwitch;        // 主扇启停闸刀
        public double FanSpeedWheel;      // 主扇调速轮 0..1
        public double Damper1;            // 一号风门阀轮 0..1
        public double Damper2;            // 二号风门阀轮 0..1
        public double Damper3;            // 三号风门阀轮 0..1
        public bool GasDrainagePump;      // 瓦斯抽放泵开关
        public bool ReverseAirflow;       // 反风闸刀

        // —— 卷扬盘 ——
        public bool HoistPower;           // 卷扬机主电源闸刀
        public double Throttle;           // 调速手柄 0..1
        public double Brake;              // 制动手柄 0..1
        public bool CageLock;             // 罐笼锁定拨杆
        public int Direction;             // 方向拨杆：-1 提升 / 0 停 / +1 下放
        public bool CageLight;            // 罐笼照明开关
        public double RopeSpeedTrim;      // 绳速微调旋钮 0..1，中位 0.5

        public static SimInputs Neutral => new SimInputs
        {
            FuelValve = 0.0,
            Excitation = 0.9,
            Damper1 = 0.0,
            Damper2 = 0.0,
            Damper3 = 0.0,
            RopeSpeedTrim = 0.5,
            Direction = 0,
        };
    }

    /// <summary>瞬时按钮事件。与持续状态分开，因为按钮是一次性动作而非位置。</summary>
    public enum SimPulse
    {
        ResetOverload,        // 过载复位按钮
        SilenceGasAlarm,      // 瓦斯报警消音按钮
        SignalBell,           // 信号铃按钮
        ConfirmBottomSignal,  // 井底信号确认按钮
        ResetOverspeed,       // 超速保护复位按钮
    }

    /// <summary>仿真在一步之内产生的事件，供表现层与班次导演消费。</summary>
    public enum SimEventKind
    {
        GeneratorStarted,
        GeneratorStopped,
        MainBreakerTripped,
        BranchBreakerTripped,
        BusUndervoltage,
        FanStarted,
        FanStopped,
        GasAlarmRaised,
        GasAlarmCleared,
        GasCritical,
        HoistOverspeed,
        HoistImpact,
        RopeOverstress,
        CageArrived,
        CoolantOverheat,
        FuelExhausted,
        SignalBellRung,
        BottomSignalConfirmed,
    }

    public readonly struct SimEvent
    {
        public readonly SimEventKind Kind;
        public readonly double Tick;
        public readonly double Value;

        public SimEvent(SimEventKind kind, double tick, double value = 0.0)
        {
            Kind = kind;
            Tick = tick;
            Value = value;
        }

        public override string ToString() => $"{Kind}@{Tick:0.00}s({Value:0.###})";
    }

    /// <summary>事故等级。用于班次结算与后果账本。</summary>
    public enum IncidentSeverity
    {
        None = 0,
        Minor = 1,
        Serious = 2,
        Catastrophic = 3,
    }
}
