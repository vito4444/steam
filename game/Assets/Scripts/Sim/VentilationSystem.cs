using System;
using System.Collections.Generic;

namespace Maner.Sim
{
    /// <summary>
    /// 主扇、风门与瓦斯浓度。风机功率随转速三次方增长，所以「把风开大一点」
    /// 在电力上从来不是小事——这是本作三角约束的第二条边。
    /// </summary>
    public sealed class VentilationSystem
    {
        public const double FanRatedKw = 250.0;
        // 风量上限的标定依据：在目标作业深度 1420 m，瓦斯涌出约 1.17 单位，
        // 需要约 84 m³/s 才能把浓度压在 0.5% 报警线以下。上限设为 95 意味着
        // 深部作业必须把主扇拉到接近满速，而满速主扇又会吃掉机组的全部余量。
        public const double MaxAirflow = 95.0;        // m³/s，主扇满速且风门全开
        public const double DilutionCoefficient = 0.028;
        public const double FanTimeConstant = 6.5;    // 风机惯量大，转速变化慢
        public const double ShaftVolume = 42000.0;    // m³，参与稀释的巷道容积
        public const double GasAlarmPercent = 0.5;
        public const double GasCriticalPercent = 1.0;
        public const double GasEvacuationPercent = 2.0;

        // —— 状态 ——
        public double FanSpeed;            // 0..1
        public double Airflow;             // m³/s
        public double GasPercent;
        public double FanMotorKw;
        public bool FanEnergized;
        public bool GasAlarmActive;
        public bool GasAlarmSilenced;
        public double PeakGasPercent;

        // —— 磨损 ——
        public double FanBearingWear;

        bool wasEnergized;

        public double DamperFactor { get; private set; }

        public void Reset()
        {
            FanSpeed = 0.0;
            Airflow = 0.0;
            GasPercent = 0.08;
            FanMotorKw = 0.0;
            FanEnergized = false;
            GasAlarmActive = false;
            GasAlarmSilenced = false;
            PeakGasPercent = 0.08;
            wasEnergized = false;
            DamperFactor = 0.0;
        }

        public void HandlePulse(SimPulse pulse)
        {
            if (pulse == SimPulse.SilenceGasAlarm && GasAlarmActive)
            {
                GasAlarmSilenced = true;
            }
        }

        /// <summary>
        /// 在母线电压已知的前提下，计算本步风机的实际出力与申请功率。
        /// 必须在 PowerSystem.Step 之前调用，供其汇总负载。
        /// </summary>
        public double ComputeDemandKw(double dt, in SimInputs input, double busVoltage)
        {
            FanEnergized = input.MainFanSwitch && input.BreakerVentilation && busVoltage > PowerSystem.UndervoltageThreshold;

            double voltageScale = FanEnergized ? Clamp01(busVoltage / PowerSystem.NominalVoltage) : 0.0;
            double target = FanEnergized ? Clamp01(input.FanSpeedWheel) * voltageScale : 0.0;

            FanSpeed += (target - FanSpeed) * Math.Min(1.0, dt / FanTimeConstant);
            if (FanSpeed < 1e-4)
            {
                FanSpeed = 0.0;
            }

            // 风机定律：功率与转速的三次方成正比。
            FanMotorKw = FanRatedKw * FanSpeed * FanSpeed * FanSpeed;
            return FanMotorKw;
        }

        public void Step(double dt, in SimInputs input, double depthMeters, DeterministicRandom rng, List<SimEvent> events, double tick)
        {
            DamperFactor = Clamp01((Clamp01(input.Damper1) + Clamp01(input.Damper2) + Clamp01(input.Damper3)) / 3.0);

            double directional = input.ReverseAirflow ? -1.0 : 1.0;
            Airflow = MaxAirflow * FanSpeed * DamperFactor * directional;

            if (FanEnergized && !wasEnergized)
            {
                events.Add(new SimEvent(SimEventKind.FanStarted, tick));
            }
            else if (!FanEnergized && wasEnergized)
            {
                events.Add(new SimEvent(SimEventKind.FanStopped, tick));
            }
            wasEnergized = FanEnergized;

            // 瓦斯涌出随开采深度上升，并带有小幅随机波动。反风时稀释效率大幅下降。
            double emission = 0.62 * (1.0 + depthMeters / 1600.0) + rng.Range(-0.05, 0.07);
            if (input.GasDrainagePump)
            {
                emission *= 0.55;
            }

            double dilution = Math.Abs(Airflow) * (input.ReverseAirflow ? 0.35 : 1.0);
            double dC = (emission - GasPercent * dilution * DilutionCoefficient) / ShaftVolume * 1000.0;
            GasPercent = Math.Max(0.0, GasPercent + dC * dt);

            if (GasPercent > PeakGasPercent)
            {
                PeakGasPercent = GasPercent;
            }

            bool shouldAlarm = GasPercent >= GasAlarmPercent;
            if (shouldAlarm && !GasAlarmActive)
            {
                GasAlarmActive = true;
                GasAlarmSilenced = false;
                events.Add(new SimEvent(SimEventKind.GasAlarmRaised, tick, GasPercent));
            }
            else if (!shouldAlarm && GasAlarmActive)
            {
                GasAlarmActive = false;
                GasAlarmSilenced = false;
                events.Add(new SimEvent(SimEventKind.GasAlarmCleared, tick, GasPercent));
            }

            if (GasPercent >= GasCriticalPercent)
            {
                events.Add(new SimEvent(SimEventKind.GasCritical, tick, GasPercent));
            }

            if (FanSpeed > 0.05)
            {
                FanBearingWear = Math.Min(1.0, FanBearingWear + dt * 0.000012 * (1.0 + FanSpeed * 2.0));
            }
        }

        static double Clamp01(double v) => v < 0.0 ? 0.0 : v > 1.0 ? 1.0 : v;
    }
}
