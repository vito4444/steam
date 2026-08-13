using System;
using System.Collections.Generic;

namespace Maner.Sim
{
    /// <summary>
    /// 卷扬机与罐笼。深度以米计，0 为地面，正值向下。
    /// 罐笼里通常有人，所以这套系统的每一个失误都会写进后果账本。
    /// </summary>
    public sealed class HoistSystem
    {
        public const double CageMassKg = 4200.0;
        public const double MaxPayloadKg = 3000.0;
        public const double MaxDepthMeters = 2000.0;
        public const double MaxSpeed = 12.0;              // m/s
        public const double OverspeedLimit = 13.2;
        public const double MotorMaxForceN = 320000.0;
        public const double BrakeDeceleration = 2.6;      // m/s²
        public const double RopeBreakingLoadN = 1_150_000.0;
        public const double RopeSafeLoadN = 620_000.0;
        public const double ImpactSpeedThreshold = 0.5;
        public const double MotorEfficiency = 0.88;
        const double Gravity = 9.80665;

        // —— 状态 ——
        public double Depth;
        public double Velocity;           // 正 = 下放
        public double PayloadKg;
        public int PersonnelAboard;
        public double MotorKw;
        public double RopeTensionN;
        public double RopeFatigue;
        public bool OverspeedTripped;
        public bool Energized;
        public bool CageLocked;
        public double TargetDepth = -1.0; // 负值表示没有停靠目标
        public bool BottomSignalPending;

        // —— 磨损 ——
        public double BrakePadWear;
        public double MotorWear;

        public IncidentSeverity LastIncident;
        public double MaxOverspeedSeen;

        public void Reset()
        {
            Depth = 0.0;
            Velocity = 0.0;
            PayloadKg = 0.0;
            PersonnelAboard = 0;
            MotorKw = 0.0;
            RopeTensionN = (CageMassKg) * Gravity;
            OverspeedTripped = false;
            Energized = false;
            CageLocked = true;
            TargetDepth = -1.0;
            BottomSignalPending = false;
            LastIncident = IncidentSeverity.None;
            MaxOverspeedSeen = 0.0;
        }

        public void HandlePulse(SimPulse pulse, List<SimEvent> events, double tick)
        {
            switch (pulse)
            {
                case SimPulse.ResetOverspeed:
                    if (OverspeedTripped && Math.Abs(Velocity) < 0.05)
                    {
                        OverspeedTripped = false;
                    }
                    break;
                case SimPulse.SignalBell:
                    events.Add(new SimEvent(SimEventKind.SignalBellRung, tick));
                    break;
                case SimPulse.ConfirmBottomSignal:
                    if (BottomSignalPending)
                    {
                        BottomSignalPending = false;
                        events.Add(new SimEvent(SimEventKind.BottomSignalConfirmed, tick));
                    }
                    break;
            }
        }

        /// <summary>
        /// 计算本步电机申请的功率。必须在 PowerSystem.Step 之前调用。
        /// 这里只算需求，实际能否达成取决于母线电压。
        /// </summary>
        public double ComputeDemandKw(in SimInputs input, double busVoltage)
        {
            Energized = input.HoistPower && input.BreakerHoist && busVoltage > PowerSystem.UndervoltageThreshold;
            if (!Energized || OverspeedTripped || input.CageLock || input.Direction == 0)
            {
                return 0.0;
            }

            double totalMass = CageMassKg + PayloadKg;
            double throttle = Clamp01(input.Throttle);
            // 下放时重力做功，电机主要起制动作用，功率需求低于提升。
            double gravityTerm = input.Direction > 0 ? -0.35 : 1.0;
            double demand = totalMass * Gravity * MaxSpeed * throttle * gravityTerm / MotorEfficiency / 1000.0;
            return Math.Max(0.0, demand);
        }

        public void Step(double dt, in SimInputs input, double busVoltage, List<SimEvent> events, double tick)
        {
            CageLocked = input.CageLock;
            double totalMass = CageMassKg + PayloadKg;

            double voltageScale = Energized ? Clamp01(busVoltage / PowerSystem.NominalVoltage) : 0.0;
            double trim = 0.9 + Clamp01(input.RopeSpeedTrim) * 0.2; // 0.9..1.1
            double targetVelocity;

            if (!Energized || OverspeedTripped || CageLocked)
            {
                targetVelocity = 0.0;
            }
            else
            {
                targetVelocity = input.Direction * MaxSpeed * Clamp01(input.Throttle) * voltageScale * trim;
            }

            // 电机能提供的最大加速度受额定推力限制；电压不足会同比削弱。
            double maxAccel = MotorMaxForceN * Math.Max(voltageScale, OverspeedTripped ? 0.0 : voltageScale) / totalMass;
            maxAccel = Math.Max(0.35, maxAccel);

            double desiredAccel = (targetVelocity - Velocity) / Math.Max(dt, 1e-6);
            double accel = Math.Max(-maxAccel, Math.Min(maxAccel, desiredAccel));

            // 制动手柄叠加，方向永远与运动相反。超速跳闸后强制全制动。
            double brakeInput = OverspeedTripped ? 1.0 : Clamp01(input.Brake);
            if (brakeInput > 0.0 && Math.Abs(Velocity) > 1e-4)
            {
                double brakeAccel = BrakeDeceleration * brakeInput * Math.Sign(Velocity);
                accel -= brakeAccel;
                BrakePadWear = Math.Min(1.0, BrakePadWear + dt * brakeInput * Math.Abs(Velocity) * 0.00022);
            }
            else if (brakeInput > 0.0 && Math.Abs(Velocity) <= 1e-4)
            {
                accel = 0.0;
            }

            double previousVelocity = Velocity;
            Velocity += accel * dt;

            if (brakeInput > 0.95 && Math.Sign(previousVelocity) != Math.Sign(Velocity))
            {
                Velocity = 0.0;
            }

            double previousDepth = Depth;
            Depth += Velocity * dt;

            // 端点撞击。以超过阈值的速度撞到井口或井底是重大事故。
            if (Depth < 0.0)
            {
                RegisterImpact(0.0, previousDepth, events, tick);
            }
            else if (Depth > MaxDepthMeters)
            {
                RegisterImpact(MaxDepthMeters, previousDepth, events, tick);
            }

            double speed = Math.Abs(Velocity);
            if (speed > MaxOverspeedSeen)
            {
                MaxOverspeedSeen = speed;
            }

            if (speed > OverspeedLimit && !OverspeedTripped)
            {
                OverspeedTripped = true;
                LastIncident = Max(LastIncident, IncidentSeverity.Serious);
                events.Add(new SimEvent(SimEventKind.HoistOverspeed, tick, speed));
            }

            // 钢丝绳张力：静载加上加速度项。急动会显著放大张力。
            double actualAccel = (Velocity - previousVelocity) / Math.Max(dt, 1e-6);
            RopeTensionN = totalMass * (Gravity - actualAccel);
            if (RopeTensionN > RopeSafeLoadN)
            {
                double excess = (RopeTensionN - RopeSafeLoadN) / (RopeBreakingLoadN - RopeSafeLoadN);
                RopeFatigue = Math.Min(1.0, RopeFatigue + dt * excess * 0.045);
                events.Add(new SimEvent(SimEventKind.RopeOverstress, tick, RopeTensionN));
                if (RopeTensionN > RopeBreakingLoadN)
                {
                    LastIncident = IncidentSeverity.Catastrophic;
                }
            }

            if (Energized && speed > 0.05)
            {
                MotorKw = Math.Abs(RopeTensionN * Velocity) / 1000.0 / MotorEfficiency;
                MotorWear = Math.Min(1.0, MotorWear + dt * 0.000008 * (1.0 + speed / MaxSpeed));
            }
            else
            {
                MotorKw = 0.0;
            }

            if (TargetDepth >= 0.0 && speed < 0.05)
            {
                if (Math.Abs(Depth - TargetDepth) < 1.5)
                {
                    events.Add(new SimEvent(SimEventKind.CageArrived, tick, Depth));
                    TargetDepth = -1.0;
                }
            }
        }

        void RegisterImpact(double clampDepth, double previousDepth, List<SimEvent> events, double tick)
        {
            double impactSpeed = Math.Abs(Velocity);
            Depth = clampDepth;
            Velocity = 0.0;

            if (impactSpeed > ImpactSpeedThreshold)
            {
                var severity = impactSpeed > 4.0 ? IncidentSeverity.Catastrophic
                    : impactSpeed > 1.8 ? IncidentSeverity.Serious
                    : IncidentSeverity.Minor;
                LastIncident = Max(LastIncident, severity);
                events.Add(new SimEvent(SimEventKind.HoistImpact, tick, impactSpeed));
            }
            else if (Math.Abs(previousDepth - clampDepth) > 1e-6)
            {
                events.Add(new SimEvent(SimEventKind.CageArrived, tick, clampDepth));
            }
        }

        static IncidentSeverity Max(IncidentSeverity a, IncidentSeverity b) => a > b ? a : b;

        static double Clamp01(double v) => v < 0.0 ? 0.0 : v > 1.0 ? 1.0 : v;
    }
}
