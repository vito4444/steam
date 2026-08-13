using System;
using System.Collections.Generic;

namespace Maner.Sim
{
    /// <summary>
    /// 柴油发电机组与母线。这是整座井的心脏：卷扬机与主扇都挂在它下面，
    /// 三者共用一条母线，因此任何一路的负载变化都会立刻传导给另外两路。
    /// 玩家必须学会的第一件事就是：不能在提升满载罐笼的同时把主扇开到最大。
    /// </summary>
    public sealed class PowerSystem
    {
        // 额定容量是本作最重要的一个平衡数字：它必须大到能单独带动满载提升（约 960 kW），
        // 又必须小到无法同时带动满载提升与主扇满速（合计约 1280 kW）。
        // 玩家因此被迫在「提得快」与「风送得足」之间做取舍，这就是核心张力三角。
        public const double RatedKw = 1100.0;
        public const double RatedRpm = 750.0;
        public const double NominalVoltage = 380.0;
        public const double DroopFactor = 0.055;      // 负载下垂：满载时转速下降 5.5%
        public const double ArmatureDropVolts = 52.0; // 满载时的电枢压降
        public const double OverloadRatio = 1.10;
        public const double OverloadTripSeconds = 8.0;
        public const double UndervoltageThreshold = 300.0;
        public const double CoolantTripCelsius = 105.0;
        public const double BatteryBusVoltage = 220.0;

        // —— 状态 ——
        public bool GeneratorRunning;
        public double Rpm;
        public double Frequency;
        public double BusVoltage;
        public double LoadKw;
        public double LightingKw;
        public double AuxiliaryKw;
        public bool MainBreakerTripped;
        public double OverloadTimer;
        public double FuelLevel = 1.0;
        public double CoolantTemp = 22.0;
        public double BatteryCharge = 1.0;
        public bool RunningOnBattery;

        // —— 磨损，跨班次持久化 ——
        public double GeneratorWear;

        double startupRamp;

        public bool BusLive => BusVoltage > UndervoltageThreshold;

        public void Reset()
        {
            GeneratorRunning = false;
            Rpm = 0.0;
            Frequency = 0.0;
            BusVoltage = 0.0;
            LoadKw = 0.0;
            LightingKw = 0.0;
            AuxiliaryKw = 0.0;
            MainBreakerTripped = false;
            OverloadTimer = 0.0;
            FuelLevel = 1.0;
            CoolantTemp = 22.0;
            BatteryCharge = 1.0;
            RunningOnBattery = false;
            startupRamp = 0.0;
        }

        public void HandlePulse(SimPulse pulse)
        {
            if (pulse == SimPulse.ResetOverload && MainBreakerTripped)
            {
                MainBreakerTripped = false;
                OverloadTimer = 0.0;
            }
        }

        /// <summary>
        /// 推进一步。externalKw 是卷扬机与通风在本步申请的功率，
        /// 它们在调用本方法之前已经根据上一步的母线电压完成了各自的出力计算。
        /// </summary>
        public void Step(double dt, in SimInputs input, double externalKw, List<SimEvent> events, double tick)
        {
            bool wasRunning = GeneratorRunning;

            LightingKw = input.BreakerLighting && !MainBreakerTripped ? 24.0 : 0.0;
            AuxiliaryKw = input.BreakerAuxiliary && !MainBreakerTripped ? 40.0 : 0.0;
            LoadKw = externalKw + LightingKw + AuxiliaryKw;

            bool wantRun = input.GeneratorMaster && input.FuelValve > 0.2 && FuelLevel > 0.0;

            if (wantRun && !GeneratorRunning)
            {
                startupRamp = 0.0;
                GeneratorRunning = true;
                events.Add(new SimEvent(SimEventKind.GeneratorStarted, tick));
            }
            else if (!wantRun && GeneratorRunning)
            {
                GeneratorRunning = false;
                events.Add(new SimEvent(SimEventKind.GeneratorStopped, tick));
            }

            if (GeneratorRunning)
            {
                // 启动是一个需要时间的过程：从盘车到并网约 6 秒，玩家必须等。
                startupRamp = Math.Min(1.0, startupRamp + dt / 6.0);

                double loadRatio = LoadKw / RatedKw;
                double governedRpm = RatedRpm * (1.0 - DroopFactor * Math.Max(0.0, loadRatio));
                double targetRpm = governedRpm * startupRamp * Clamp01(input.FuelValve * 1.25);
                Rpm += (targetRpm - Rpm) * Math.Min(1.0, dt * 1.8);

                FuelLevel = Math.Max(0.0, FuelLevel - dt * (0.000045 + 0.00028 * Math.Max(0.0, loadRatio)));
                if (FuelLevel <= 0.0)
                {
                    events.Add(new SimEvent(SimEventKind.FuelExhausted, tick));
                }

                double heatIn = 0.55 + 1.65 * Math.Max(0.0, loadRatio);
                double heatOut = input.CoolantPump ? 1.9 : 0.32;
                CoolantTemp += dt * (heatIn - heatOut * (CoolantTemp - 22.0) / 60.0);

                if (CoolantTemp > CoolantTripCelsius)
                {
                    GeneratorWear = Math.Min(1.0, GeneratorWear + dt * 0.02);
                    events.Add(new SimEvent(SimEventKind.CoolantOverheat, tick, CoolantTemp));
                }

                if (loadRatio > OverloadRatio)
                {
                    OverloadTimer += dt;
                    if (OverloadTimer >= OverloadTripSeconds && !MainBreakerTripped)
                    {
                        MainBreakerTripped = true;
                        events.Add(new SimEvent(SimEventKind.MainBreakerTripped, tick, loadRatio));
                    }
                }
                else
                {
                    OverloadTimer = Math.Max(0.0, OverloadTimer - dt * 0.6);
                }
            }
            else
            {
                Rpm = Math.Max(0.0, Rpm - dt * 220.0);
                CoolantTemp += dt * (22.0 - CoolantTemp) / 240.0;
                startupRamp = 0.0;
            }

            Frequency = Rpm / RatedRpm * 50.0;

            double previousVoltage = BusVoltage;
            if (MainBreakerTripped)
            {
                BusVoltage = 0.0;
            }
            else
            {
                double loadRatio = LoadKw / RatedKw;
                double generated = 420.0 * Clamp01(input.Excitation) * (Frequency / 50.0)
                                   - ArmatureDropVolts * Math.Max(0.0, loadRatio);
                BusVoltage = Math.Max(0.0, generated);
            }

            RunningOnBattery = false;
            if (input.BatteryTie && BatteryCharge > 0.0 && BusVoltage < BatteryBusVoltage)
            {
                // 应急电池只能撑住照明与信号，撑不住卷扬机，这是刻意的设计约束。
                BusVoltage = BatteryBusVoltage;
                RunningOnBattery = true;
                BatteryCharge = Math.Max(0.0, BatteryCharge - dt * 0.0035);
            }

            if (previousVoltage > UndervoltageThreshold && BusVoltage <= UndervoltageThreshold)
            {
                events.Add(new SimEvent(SimEventKind.BusUndervoltage, tick, BusVoltage));
            }

            if (wasRunning && !GeneratorRunning && FuelLevel <= 0.0)
            {
                GeneratorWear = Math.Min(1.0, GeneratorWear + 0.01);
            }
        }

        static double Clamp01(double v) => v < 0.0 ? 0.0 : v > 1.0 ? 1.0 : v;
    }
}
