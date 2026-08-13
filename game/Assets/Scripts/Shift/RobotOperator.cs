using System;
using Maner.Controls;
using Maner.Sim;

namespace Maner.Shift
{
    /// <summary>
    /// 机器人操作员。它通过与人类玩家完全相同的入口操作控制台——只写控件位置，
    /// 不直接改仿真状态——因此它能跑通一个班次，就证明这个班次对人类而言也是可完成的。
    ///
    /// 它的存在有两个用途：一是作为验收标准第 3 条的自动化验证手段，
    /// 二是可以脱离渲染以任意倍速反复跑，用来做平衡性回归。
    /// 它不代表「好的玩法」，只代表「按规程操作能走通」。
    /// </summary>
    public sealed class RobotOperator
    {
        const double NominalVoltage = 380.0;

        public bool VerboseLog;
        readonly System.Collections.Generic.List<string> log = new System.Collections.Generic.List<string>();
        public System.Collections.Generic.IReadOnlyList<string> Log => log;

        double lastFaultCheck;

        public void Reset()
        {
            log.Clear();
            lastFaultCheck = 0.0;
        }

        /// <summary>每个仿真步调用一次，根据当前指令调整控制台。</summary>
        public void Tick(double shiftTime, ShiftDirector director, ShaftSimulation sim, ConsoleState console)
        {
            // 任何时候先处理跳闸：断路器被打掉了，别的都无从谈起。
            HandleTrippedBreakers(shiftTime, console);

            var order = director.Current;
            if (order == null)
            {
                return;
            }

            // 机组与通风是所有指令的前置条件，始终维持。
            MaintainPowerPlant(console, sim);

            switch (order.Kind)
            {
                case OrderKind.EnergizeBus:
                    TuneExcitation(console, sim, order.Target);
                    break;

                case OrderKind.EstablishAirflow:
                    MaintainVentilation(console, sim, order.Target);
                    break;

                case OrderKind.LowerCage:
                case OrderKind.RaiseCage:
                    MaintainVentilation(console, sim, 45.0);
                    DriveCageTo(console, sim, order.Target, order.Tolerance);
                    break;

                case OrderKind.SuppressGas:
                    ParkCage(console);
                    PurgeGas(console, sim);
                    break;

                case OrderKind.ClearFault:
                    MaintainVentilation(console, sim, 45.0);
                    break;
            }
        }

        void HandleTrippedBreakers(double shiftTime, ConsoleState console)
        {
            foreach (var id in new[]
            {
                ControlId.BreakerHoist, ControlId.BreakerVentilation,
                ControlId.BreakerLighting, ControlId.BreakerAuxiliary,
            })
            {
                if (console.IsTripped(id))
                {
                    console.ClearTrip(id);
                    console.Set(id, 1.0);
                    if (VerboseLog)
                    {
                        log.Add($"[{shiftTime:0.0}s] 复位并合上 {id}");
                    }
                }
            }

            lastFaultCheck = shiftTime;
        }

        void MaintainPowerPlant(ConsoleState console, ShaftSimulation sim)
        {
            console.Set(ControlId.FuelValve, 0.9);
            console.Set(ControlId.GeneratorMaster, 1.0);
            console.Set(ControlId.CoolantPump, 1.0);
            console.Set(ControlId.BreakerLighting, 1.0);
            console.Set(ControlId.BreakerAuxiliary, 1.0);
            console.Set(ControlId.BreakerVentilation, 1.0);
            console.Set(ControlId.BreakerHoist, 1.0);

            if (sim.Power.MainBreakerTripped)
            {
                console.Press(ControlId.ResetOverload);
            }

            if (sim.Ventilation.GasAlarmActive && !sim.Ventilation.GasAlarmSilenced)
            {
                console.Press(ControlId.SilenceGasAlarm);
            }
        }

        /// <summary>用比例控制把母线电压推到目标值。励磁旋钮是唯一的调压手段。</summary>
        void TuneExcitation(ConsoleState console, ShaftSimulation sim, double targetVolts)
        {
            double current = console.Get(ControlId.Excitation);
            double error = targetVolts - sim.Power.BusVoltage;
            double step = Math.Clamp(error * 0.0006, -0.02, 0.02);
            console.Set(ControlId.Excitation, Math.Clamp(current + step, 0.55, 1.0));
        }

        void MaintainVentilation(ConsoleState console, ShaftSimulation sim, double targetAirflow)
        {
            console.Set(ControlId.MainFanSwitch, 1.0);
            console.Set(ControlId.Damper1, 1.0);
            console.Set(ControlId.Damper2, 1.0);
            console.Set(ControlId.Damper3, 1.0);

            // 风量不足就加转速，够了就收一点，给卷扬机让出电力余量。
            double current = console.Get(ControlId.FanSpeedWheel);
            double airflow = Math.Abs(sim.Ventilation.Airflow);
            double desired = airflow < targetAirflow * 1.05 ? current + 0.01 : current - 0.004;
            console.Set(ControlId.FanSpeedWheel, Math.Clamp(desired, 0.35, 1.0));
        }

        void PurgeGas(ConsoleState console, ShaftSimulation sim)
        {
            console.Set(ControlId.MainFanSwitch, 1.0);
            console.Set(ControlId.FanSpeedWheel, 1.0);
            console.Set(ControlId.Damper1, 1.0);
            console.Set(ControlId.Damper2, 1.0);
            console.Set(ControlId.Damper3, 1.0);
            console.Set(ControlId.GasDrainagePump, 1.0);
        }

        void ParkCage(ConsoleState console)
        {
            console.Set(ControlId.Throttle, 0.0);
            console.Set(ControlId.Brake, 1.0);
            console.Set(ControlId.Direction, 0.5);
            console.Set(ControlId.CageLock, 1.0);
        }

        /// <summary>
        /// 把罐笼开到目标深度。核心是减速曲线：按剩余距离反算允许的最大速度，
        /// 留出 30% 余量，否则会以全速撞进端点——那是灾难性事故。
        /// </summary>
        void DriveCageTo(ConsoleState console, ShaftSimulation sim, double targetDepth, double tolerance)
        {
            console.Set(ControlId.HoistPower, 1.0);

            double delta = targetDepth - sim.Hoist.Depth;
            double distance = Math.Abs(delta);
            double speed = Math.Abs(sim.Hoist.Velocity);

            if (distance <= tolerance * 0.5)
            {
                console.Set(ControlId.Throttle, 0.0);
                console.Set(ControlId.Brake, 1.0);
                console.Set(ControlId.Direction, 0.5);
                if (speed < 0.05)
                {
                    console.Set(ControlId.CageLock, 1.0);
                }
                return;
            }

            console.Set(ControlId.CageLock, 0.0);
            console.Set(ControlId.Direction, delta > 0 ? 1.0 : 0.0);

            // 按剩余距离反算允许的最大速度，留出三成裕度。
            // 电机的规程加速度只有 1.15 m/s²，从满速停下来要走六十多米，
            // 不提前收油门就一定会冲过站台。
            double safeSpeed = Math.Sqrt(2.0 * HoistSystem.BrakeDeceleration * distance) * 0.7;
            double targetSpeed = Math.Min(HoistSystem.MaxSpeed, safeSpeed);
            double throttle = Math.Clamp(targetSpeed / HoistSystem.MaxSpeed, 0.0, 1.0);

            if (speed > targetSpeed * 1.12)
            {
                console.Set(ControlId.Throttle, 0.0);
                console.Set(ControlId.Brake, Math.Clamp((speed - targetSpeed) * 0.8, 0.2, 1.0));
            }
            else
            {
                console.Set(ControlId.Brake, 0.0);
                console.Set(ControlId.Throttle, throttle);
            }
        }
    }
}
