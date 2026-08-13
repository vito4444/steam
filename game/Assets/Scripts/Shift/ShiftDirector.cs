using System;
using System.Collections.Generic;
using Maner.Controls;
using Maner.Sim;

namespace Maner.Shift
{
    public enum ShiftPhase
    {
        Briefing,
        Running,
        Settled,
    }

    /// <summary>一条指令的执行结果，用于结算与后果账本。</summary>
    public sealed class OrderResult
    {
        public int OrderId;
        public OrderKind Kind;
        public string Text;
        public bool Completed;
        public double CompletedAtSeconds;
        public double ElapsedSeconds;
        public bool TimedOut;
    }

    /// <summary>
    /// 班次导演。它按顺序发布指令、在预定时点注入故障、记录每条指令的完成情况，
    /// 并在班次结束时结算。整个类不依赖 Unity，可以脱离渲染以任意倍速跑完，
    /// 这是平衡性测试与自动化验收的基础。
    /// </summary>
    public sealed class ShiftDirector
    {
        readonly List<Order> orders = new List<Order>();
        readonly List<OrderResult> results = new List<OrderResult>();
        readonly List<string> log = new List<string>();

        public FaultSystem Faults { get; } = new FaultSystem();
        public ShiftPhase Phase { get; private set; } = ShiftPhase.Briefing;
        public double ShiftTime { get; private set; }
        public int CurrentIndex { get; private set; }
        public IReadOnlyList<Order> Orders => orders;
        public IReadOnlyList<OrderResult> Results => results;
        public IReadOnlyList<string> Log => log;
        public ShiftSettlement Settlement { get; private set; }

        double holdTimer;
        double orderStartTime;

        public Order Current => CurrentIndex >= 0 && CurrentIndex < orders.Count ? orders[CurrentIndex] : null;

        public void Load(IEnumerable<Order> newOrders)
        {
            orders.Clear();
            orders.AddRange(newOrders);
            Reset();
        }

        public void Reset()
        {
            results.Clear();
            log.Clear();
            Faults.Reset();
            Phase = ShiftPhase.Briefing;
            ShiftTime = 0.0;
            CurrentIndex = 0;
            holdTimer = 0.0;
            orderStartTime = 0.0;
            Settlement = null;
        }

        public void Begin()
        {
            Phase = ShiftPhase.Running;
            orderStartTime = ShiftTime;
            if (Current != null)
            {
                log.Add($"[{ShiftTime:0.0}s] 指令下达：{Current.Text}");
            }
        }

        /// <summary>按仿真步长推进班次。返回本步是否完成了一条指令。</summary>
        public bool Tick(double deltaTime, ShaftSimulation sim, ConsoleState console)
        {
            if (Phase != ShiftPhase.Running)
            {
                return false;
            }

            ShiftTime += deltaTime;
            Faults.Tick(ShiftTime, sim, console);

            var order = Current;
            if (order == null)
            {
                Settle(sim);
                return false;
            }

            double elapsed = ShiftTime - orderStartTime;

            if (order.IsSatisfied(sim))
            {
                holdTimer += deltaTime;
                if (holdTimer >= order.HoldSeconds)
                {
                    CompleteCurrent(elapsed, false);
                    return true;
                }
            }
            else
            {
                holdTimer = 0.0;
            }

            if (order.TimeLimitSeconds > 0.0 && elapsed > order.TimeLimitSeconds)
            {
                CompleteCurrent(elapsed, true);
                return true;
            }

            return false;
        }

        void CompleteCurrent(double elapsed, bool timedOut)
        {
            var order = Current;
            results.Add(new OrderResult
            {
                OrderId = order.Id,
                Kind = order.Kind,
                Text = order.Text,
                Completed = !timedOut,
                CompletedAtSeconds = ShiftTime,
                ElapsedSeconds = elapsed,
                TimedOut = timedOut,
            });

            log.Add(timedOut
                ? $"[{ShiftTime:0.0}s] 指令超时：{order.Text}"
                : $"[{ShiftTime:0.0}s] 指令完成：{order.Text}（用时 {elapsed:0.0}s）");

            CurrentIndex++;
            holdTimer = 0.0;
            orderStartTime = ShiftTime;

            if (Current != null)
            {
                log.Add($"[{ShiftTime:0.0}s] 指令下达：{Current.Text}");
            }
        }

        /// <summary>提前收工，例如玩家主动结束班次或发生灾难性事故。</summary>
        public void ForceSettle(ShaftSimulation sim) => Settle(sim);

        void Settle(ShaftSimulation sim)
        {
            if (Phase == ShiftPhase.Settled)
            {
                return;
            }

            int completed = 0;
            int timedOut = 0;
            foreach (var r in results)
            {
                if (r.Completed)
                {
                    completed++;
                }
                else
                {
                    timedOut++;
                }
            }

            Settlement = new ShiftSettlement
            {
                TotalOrders = orders.Count,
                CompletedOrders = completed,
                TimedOutOrders = timedOut,
                FaultsTriggered = Faults.TriggeredCount,
                FaultsResolved = Faults.ResolvedCount,
                ShiftSeconds = ShiftTime,
                WorstIncident = sim.Hoist.LastIncident,
                PeakGasPercent = sim.Ventilation.PeakGasPercent,
                MaxCageSpeed = sim.Hoist.MaxOverspeedSeen,
                GeneratorWear = sim.Power.GeneratorWear,
                BrakePadWear = sim.Hoist.BrakePadWear,
                MotorWear = sim.Hoist.MotorWear,
                FanBearingWear = sim.Ventilation.FanBearingWear,
                FuelRemaining = sim.Power.FuelLevel,
            };

            Phase = ShiftPhase.Settled;
            log.Add($"[{ShiftTime:0.0}s] 班次结束：完成 {completed}/{orders.Count}，" +
                    $"故障 {Faults.TriggeredCount} 起已处置 {Faults.ResolvedCount} 起");
        }

        /// <summary>本作的首个班次。六条指令，第四条执行途中强制注入一次故障。</summary>
        public static List<Order> BuildFirstShift()
        {
            return new List<Order>
            {
                new Order(1, OrderKind.EnergizeBus,
                    "起机组，母线电压稳定在 380 伏，允许偏差 20 伏。", 380.0, 20.0, 180.0),
                new Order(2, OrderKind.EstablishAirflow,
                    "开主扇，井下风量不得低于 45 立方米每秒。", 45.0, 0.0, 240.0),
                new Order(3, OrderKind.LowerCage,
                    "三班组下井，罐笼下放至 620 米平台停稳。", 620.0, 6.0, 300.0),
                new Order(4, OrderKind.LowerCage,
                    "继续下放至 1420 米作业面，注意通风。", 1420.0, 6.0, 360.0),
                new Order(5, OrderKind.SuppressGas,
                    "瓦斯读数偏高，压回 0.5% 以下再继续。", VentilationSystem.GasAlarmPercent, 0.0, 300.0),
                new Order(6, OrderKind.RaiseCage,
                    "收工，把人提回地面。", 0.0, 4.0, 420.0),
            };
        }

        /// <summary>首个班次的故障编排：第四条指令执行途中卷扬分路跳闸。</summary>
        public void ScheduleFirstShiftFaults() => Faults.Schedule(FaultKind.HoistBreakerTrip, 200.0);
    }

    public sealed class ShiftSettlement
    {
        public int TotalOrders;
        public int CompletedOrders;
        public int TimedOutOrders;
        public int FaultsTriggered;
        public int FaultsResolved;
        public double ShiftSeconds;
        public IncidentSeverity WorstIncident;
        public double PeakGasPercent;
        public double MaxCageSpeed;
        public double GeneratorWear;
        public double BrakePadWear;
        public double MotorWear;
        public double FanBearingWear;
        public double FuelRemaining;

        public double CompletionRate => TotalOrders == 0 ? 0.0 : (double)CompletedOrders / TotalOrders;

        /// <summary>班次评级。事故一票否决，这是本作的态度：完成度救不了出事故。</summary>
        public char Grade
        {
            get
            {
                if (WorstIncident >= IncidentSeverity.Catastrophic)
                {
                    return 'F';
                }
                if (WorstIncident >= IncidentSeverity.Serious)
                {
                    return 'D';
                }
                double rate = CompletionRate;
                if (rate >= 1.0 && WorstIncident == IncidentSeverity.None)
                {
                    return 'A';
                }
                if (rate >= 0.83)
                {
                    return 'B';
                }
                return rate >= 0.5 ? 'C' : 'D';
            }
        }
    }
}
