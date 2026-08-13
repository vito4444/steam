using System;
using System.Collections.Generic;
using Maner.Controls;
using Maner.Sim;

namespace Maner.Shift
{
    public enum FaultKind
    {
        /// <summary>卷扬分路断路器无征兆跳闸，罐笼在半途失去动力。</summary>
        HoistBreakerTrip,
        /// <summary>井下瓦斯突然涌出，浓度短时间内翻倍。</summary>
        GasSurge,
        /// <summary>冷却泵卡死，水温开始爬升。</summary>
        CoolantPumpSeized,
        /// <summary>一号风门卡在当前开度，玩家只能靠另外两道风门补偿。</summary>
        DamperJammed,
    }

    public readonly struct ScheduledFault
    {
        public readonly FaultKind Kind;
        public readonly double AtSeconds;

        public ScheduledFault(FaultKind kind, double atSeconds)
        {
            Kind = kind;
            AtSeconds = atSeconds;
        }
    }

    /// <summary>
    /// 故障注入。故障不是随机惩罚，而是把玩家已经建立起来的平衡打破，
    /// 逼他在有限的时间里重新分配电力与风量——这是本作紧张感的主要来源。
    /// </summary>
    public sealed class FaultSystem
    {
        readonly List<ScheduledFault> schedule = new List<ScheduledFault>();
        readonly HashSet<FaultKind> active = new HashSet<FaultKind>();
        readonly List<string> log = new List<string>();
        int nextIndex;

        public IReadOnlyCollection<FaultKind> Active => active;
        public IReadOnlyList<string> Log => log;
        public int TriggeredCount { get; private set; }
        public int ResolvedCount { get; private set; }

        public void Schedule(FaultKind kind, double atSeconds)
        {
            schedule.Add(new ScheduledFault(kind, atSeconds));
            schedule.Sort((a, b) => a.AtSeconds.CompareTo(b.AtSeconds));
        }

        public void Reset()
        {
            active.Clear();
            log.Clear();
            nextIndex = 0;
            TriggeredCount = 0;
            ResolvedCount = 0;
        }

        public void Tick(double shiftTime, ShaftSimulation sim, ConsoleState console)
        {
            while (nextIndex < schedule.Count && schedule[nextIndex].AtSeconds <= shiftTime)
            {
                Trigger(schedule[nextIndex].Kind, shiftTime, sim, console);
                nextIndex++;
            }

            CheckResolution(shiftTime, sim, console);
        }

        void Trigger(FaultKind kind, double shiftTime, ShaftSimulation sim, ConsoleState console)
        {
            if (!active.Add(kind))
            {
                return;
            }

            TriggeredCount++;
            log.Add($"[{shiftTime:0.0}s] 故障发生：{Describe(kind)}");

            switch (kind)
            {
                case FaultKind.HoistBreakerTrip:
                    console.TripBreaker(ControlId.BreakerHoist);
                    break;
                case FaultKind.GasSurge:
                    sim.Ventilation.GasPercent = Math.Max(sim.Ventilation.GasPercent * 2.4, 0.85);
                    break;
                case FaultKind.CoolantPumpSeized:
                    sim.Power.CoolantTemp += 22.0;
                    break;
                case FaultKind.DamperJammed:
                    // 风门卡住由表现层锁定控件，这里只登记状态。
                    break;
            }
        }

        void CheckResolution(double shiftTime, ShaftSimulation sim, ConsoleState console)
        {
            if (active.Contains(FaultKind.HoistBreakerTrip) &&
                !console.IsTripped(ControlId.BreakerHoist) &&
                console.GetBool(ControlId.BreakerHoist))
            {
                Resolve(FaultKind.HoistBreakerTrip, shiftTime);
            }

            if (active.Contains(FaultKind.GasSurge) &&
                sim.Ventilation.GasPercent <= VentilationSystem.GasAlarmPercent)
            {
                Resolve(FaultKind.GasSurge, shiftTime);
            }

            if (active.Contains(FaultKind.CoolantPumpSeized) &&
                sim.Power.CoolantTemp < 80.0)
            {
                Resolve(FaultKind.CoolantPumpSeized, shiftTime);
            }
        }

        void Resolve(FaultKind kind, double shiftTime)
        {
            if (!active.Remove(kind))
            {
                return;
            }
            ResolvedCount++;
            log.Add($"[{shiftTime:0.0}s] 故障已处置：{Describe(kind)}");
        }

        public static string Describe(FaultKind kind) => kind switch
        {
            FaultKind.HoistBreakerTrip => "卷扬分路断路器跳闸",
            FaultKind.GasSurge => "井下瓦斯突涌",
            FaultKind.CoolantPumpSeized => "冷却泵卡死，水温上升",
            FaultKind.DamperJammed => "一号风门卡死",
            _ => kind.ToString(),
        };
    }
}
