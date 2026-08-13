using System;
using Maner.Sim;

namespace Maner.Shift
{
    public enum OrderKind
    {
        /// <summary>把机组带起来并把母线电压稳定在额定区间。</summary>
        EnergizeBus,
        /// <summary>启动主扇，把井下风量带到指定值以上。</summary>
        EstablishAirflow,
        /// <summary>把罐笼下放到指定深度并停稳。</summary>
        LowerCage,
        /// <summary>把罐笼提升回地面并停稳。</summary>
        RaiseCage,
        /// <summary>在时限内把瓦斯浓度压回阈值以下。</summary>
        SuppressGas,
        /// <summary>处置已发生的故障，恢复受影响的分路。</summary>
        ClearFault,
    }

    /// <summary>
    /// 一条来自上级的火力指令……这里是调度指令。每条指令都有可机读的完成判据，
    /// 因此机器人操作员脚本与人类玩家走的是完全相同的验收标准。
    /// </summary>
    public sealed class Order
    {
        public int Id { get; }
        public OrderKind Kind { get; }
        /// <summary>面向玩家的指令原文，会打在电报纸带与任务卡上。</summary>
        public string Text { get; }
        public double Target { get; }
        public double Tolerance { get; }
        public double TimeLimitSeconds { get; }

        /// <summary>完成本条指令要求状态连续保持的秒数，防止「擦边一瞬间」被判通过。</summary>
        public double HoldSeconds { get; }

        public Order(int id, OrderKind kind, string text, double target,
            double tolerance = 0.0, double timeLimitSeconds = 0.0, double holdSeconds = 2.0)
        {
            Id = id;
            Kind = kind;
            Text = text;
            Target = target;
            Tolerance = tolerance;
            TimeLimitSeconds = timeLimitSeconds;
            HoldSeconds = holdSeconds;
        }

        public bool IsSatisfied(ShaftSimulation sim) => Kind switch
        {
            OrderKind.EnergizeBus =>
                sim.Power.GeneratorRunning &&
                Math.Abs(sim.Power.BusVoltage - Target) <= Tolerance &&
                !sim.Power.MainBreakerTripped,

            OrderKind.EstablishAirflow =>
                Math.Abs(sim.Ventilation.Airflow) >= Target,

            OrderKind.LowerCage =>
                Math.Abs(sim.Hoist.Depth - Target) <= Tolerance &&
                Math.Abs(sim.Hoist.Velocity) < 0.08,

            OrderKind.RaiseCage =>
                Math.Abs(sim.Hoist.Depth - Target) <= Tolerance &&
                Math.Abs(sim.Hoist.Velocity) < 0.08,

            OrderKind.SuppressGas =>
                sim.Ventilation.GasPercent <= Target,

            OrderKind.ClearFault =>
                !sim.Power.MainBreakerTripped && sim.Power.BusLive,

            _ => false,
        };

        public override string ToString() => $"#{Id} {Kind} {Text}";
    }
}
