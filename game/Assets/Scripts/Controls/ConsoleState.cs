using System;
using System.Collections.Generic;
using Maner.Sim;

namespace Maner.Controls
{
    /// <summary>
    /// 控制台上每个控件当前所处的物理位置，以及这些位置如何翻译成仿真输入。
    ///
    /// 这一层刻意与表现层分离：玩家用鼠标拖出的弧线最终只会落成一个 0..1 的数，
    /// 而仿真只认这个数。因此机器人操作员脚本可以绕过鼠标直接写值，
    /// 走的仍然是与人类玩家完全相同的这条路径。
    /// </summary>
    public sealed class ConsoleState
    {
        readonly double[] values;
        readonly bool[] trippedBreakers;
        readonly Queue<SimPulse> pulses = new Queue<SimPulse>();
        readonly List<ControlId> changedThisFrame = new List<ControlId>();

        public ConsoleState()
        {
            int count = Enum.GetValues(typeof(ControlId)).Length;
            values = new double[count];
            trippedBreakers = new bool[count];
            ResetToDefaults();
        }

        public IReadOnlyList<ControlId> ChangedThisFrame => changedThisFrame;

        public void ResetToDefaults()
        {
            Array.Clear(trippedBreakers, 0, trippedBreakers.Length);
            foreach (var def in ConsoleLayout.All)
            {
                values[(int)def.Id] = def.DefaultValue;
            }
            pulses.Clear();
            changedThisFrame.Clear();
        }

        public double Get(ControlId id) => values[(int)id];

        public bool GetBool(ControlId id) => values[(int)id] >= 0.5;

        /// <summary>三档拨杆的档位序号，0 起算。</summary>
        public int GetDetent(ControlId id)
        {
            var def = ConsoleLayout.Get(id);
            int detents = Math.Max(2, def.Detents);
            return (int)Math.Round(values[(int)id] * (detents - 1));
        }

        /// <summary>
        /// 设置控件位置。被跳闸的断路器在复位之前拒绝合闸——
        /// 这是断路器与普通开关的唯一区别，也是它值得作为独立控件原型的原因。
        /// </summary>
        public void Set(ControlId id, double value)
        {
            var def = ConsoleLayout.Get(id);
            if (!def.IsInteractive)
            {
                return;
            }

            if (def.Kind == ControlKind.Breaker && trippedBreakers[(int)id] && value >= 0.5)
            {
                return;
            }

            double clamped = value < 0.0 ? 0.0 : value > 1.0 ? 1.0 : value;

            // 有档位的控件吸附到最近的档位，连续控件保留原值。
            if (!def.IsContinuous && def.Kind != ControlKind.PushButton)
            {
                int detents = Math.Max(2, def.Detents);
                clamped = Math.Round(clamped * (detents - 1)) / (detents - 1);
            }

            if (Math.Abs(values[(int)id] - clamped) > 1e-9)
            {
                values[(int)id] = clamped;
                changedThisFrame.Add(id);
            }
        }

        /// <summary>按下按钮，把对应脉冲排入队列。非按钮控件调用无效果。</summary>
        public bool Press(ControlId id)
        {
            var def = ConsoleLayout.Get(id);
            if (def.Kind != ControlKind.PushButton || !def.Pulse.HasValue)
            {
                return false;
            }
            pulses.Enqueue(def.Pulse.Value);
            changedThisFrame.Add(id);
            return true;
        }

        /// <summary>由故障系统调用，单方面把某个断路器打到断开位并锁住。</summary>
        public void TripBreaker(ControlId id)
        {
            var def = ConsoleLayout.Get(id);
            if (def.Kind != ControlKind.Breaker)
            {
                return;
            }
            trippedBreakers[(int)id] = true;
            values[(int)id] = 0.0;
            changedThisFrame.Add(id);
        }

        public bool IsTripped(ControlId id) => trippedBreakers[(int)id];

        /// <summary>清除跳闸锁定，之后玩家才能重新合闸。</summary>
        public void ClearTrip(ControlId id) => trippedBreakers[(int)id] = false;

        public void ClearChangeLog() => changedThisFrame.Clear();

        /// <summary>把排队的脉冲交给仿真，队列随之清空。</summary>
        public void DrainPulses(ShaftSimulation sim)
        {
            while (pulses.Count > 0)
            {
                sim.QueuePulse(pulses.Dequeue());
            }
        }

        public int PendingPulseCount => pulses.Count;

        /// <summary>把控制台的物理位置翻译成一份仿真输入。</summary>
        public SimInputs BuildInputs()
        {
            var input = new SimInputs
            {
                GeneratorMaster = GetBool(ControlId.GeneratorMaster),
                FuelValve = Get(ControlId.FuelValve),
                CoolantPump = GetBool(ControlId.CoolantPump),
                Excitation = Get(ControlId.Excitation),
                BreakerHoist = GetBool(ControlId.BreakerHoist),
                BreakerVentilation = GetBool(ControlId.BreakerVentilation),
                BreakerLighting = GetBool(ControlId.BreakerLighting),
                BreakerAuxiliary = GetBool(ControlId.BreakerAuxiliary),
                BatteryTie = GetBool(ControlId.BatteryTie),

                MainFanSwitch = GetBool(ControlId.MainFanSwitch),
                FanSpeedWheel = Get(ControlId.FanSpeedWheel),
                Damper1 = Get(ControlId.Damper1),
                Damper2 = Get(ControlId.Damper2),
                Damper3 = Get(ControlId.Damper3),
                GasDrainagePump = GetBool(ControlId.GasDrainagePump),
                ReverseAirflow = GetBool(ControlId.ReverseAirflow),

                HoistPower = GetBool(ControlId.HoistPower),
                Throttle = Get(ControlId.Throttle),
                Brake = Get(ControlId.Brake),
                CageLock = GetBool(ControlId.CageLock),
                CageLight = GetBool(ControlId.CageLight),
                RopeSpeedTrim = Get(ControlId.RopeSpeedTrim),
                // 三档拨杆：0 档提升、1 档停、2 档下放。
                Direction = GetDetent(ControlId.Direction) - 1,
            };
            return input;
        }

        /// <summary>把仿真读数写回表盘控件，供表现层统一读取。</summary>
        public void PushGaugeReadings(ShaftSimulation sim)
        {
            values[(int)ControlId.GaugeBusVoltage] = sim.Power.BusVoltage;
            values[(int)ControlId.GaugeLoad] = sim.Power.LoadKw;
            values[(int)ControlId.GaugeFrequency] = sim.Power.Frequency;
            values[(int)ControlId.GaugeCoolantTemp] = sim.Power.CoolantTemp;
            values[(int)ControlId.GaugeFuel] = sim.Power.FuelLevel;
            values[(int)ControlId.GaugeGas] = sim.Ventilation.GasPercent;
            values[(int)ControlId.GaugeAirflow] = sim.Ventilation.Airflow;
            values[(int)ControlId.GaugeFanSpeed] = sim.Ventilation.FanSpeed;
            values[(int)ControlId.GaugeDepth] = sim.Hoist.Depth;
            values[(int)ControlId.GaugeCageSpeed] = sim.Hoist.Velocity;
            values[(int)ControlId.GaugePayload] = sim.Hoist.PayloadKg;
            values[(int)ControlId.GaugeRopeTension] = sim.Hoist.RopeTensionN / 1000.0;
        }

        /// <summary>表盘的显示量程，用于把读数映射成指针角度。</summary>
        public static (double min, double max) GaugeRange(ControlId id) => id switch
        {
            ControlId.GaugeBusVoltage => (0.0, 450.0),
            ControlId.GaugeLoad => (0.0, 1400.0),
            ControlId.GaugeFrequency => (40.0, 55.0),
            ControlId.GaugeCoolantTemp => (0.0, 130.0),
            ControlId.GaugeFuel => (0.0, 1.0),
            ControlId.GaugeGas => (0.0, 2.5),
            ControlId.GaugeAirflow => (0.0, 100.0),
            ControlId.GaugeFanSpeed => (0.0, 1.0),
            ControlId.GaugeDepth => (0.0, HoistSystem.MaxDepthMeters),
            ControlId.GaugeCageSpeed => (-14.0, 14.0),
            ControlId.GaugePayload => (0.0, 3500.0),
            ControlId.GaugeRopeTension => (0.0, 1200.0),
            _ => (0.0, 1.0),
        };

        /// <summary>表盘的红区起点，超过即为危险。返回 null 表示该表没有红区。</summary>
        public static double? GaugeRedline(ControlId id) => id switch
        {
            ControlId.GaugeLoad => PowerSystem.RatedKw * PowerSystem.OverloadRatio,
            ControlId.GaugeCoolantTemp => PowerSystem.CoolantTripCelsius,
            ControlId.GaugeGas => VentilationSystem.GasAlarmPercent,
            ControlId.GaugeCageSpeed => HoistSystem.MaxSpeed,
            ControlId.GaugeRopeTension => HoistSystem.RopeSafeLoadN / 1000.0,
            ControlId.GaugePayload => HoistSystem.MaxPayloadKg,
            _ => null,
        };
    }
}
