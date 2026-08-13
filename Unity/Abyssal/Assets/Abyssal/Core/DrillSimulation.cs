using System;
using System.Collections.Generic;

namespace Abyssal.Core
{
    /// <summary>
    /// 钻进过程的耦合仿真。这是整个游戏的核心系统。
    ///
    /// 设计目标是让玩家面对一个「窄窗口」：任何一个参数单独看都有很宽的可调范围，
    /// 但要同时满足进尺、不井涌、不漏失、不卡钻、不烧钻头这五个约束，
    /// 可行解就被压成很小的一块。地层切换时这块可行域会移动，玩家必须察觉并跟上。
    ///
    /// 类里不引用任何 UnityEngine 类型，可以直接在命令行下跑批量参数扫描。
    /// </summary>
    public sealed class DrillSimulation
    {
        // ------------------------------------------------------------------ 系数
        // 这些系数不是从教科书抄的，是为了让玩法窗口落在「有挑战但可掌握」的区间调出来的。
        // 改动它们会直接改变游戏难度，改之前请先跑 ParameterSweep 看解空间形状。

        const double RopCoefficient = 22.0;
        const double RopRpmExponent = 0.6;
        const double RopWearPenalty = 2.5;
        const double RopCuttingsPenalty = 0.7;

        const double FounderBase = 80.0;      // kN
        const double FounderPerHardness = 120.0;
        const double FounderOverloadSlope = 1.5;

        const double CuttingsGenPerRop = 0.0058;
        const double CuttingsClearPerPump = 0.020;

        const double TorqueFriction = 0.25;
        const double TorqueCuttingsFactor = 2.0;
        const double TorqueHardnessFactor = 0.4;
        const double TorqueStuckFactor = 3.0;

        const double MudInletTemperature = 20.0;
        const double CoolingPerPumpRate = 0.90;
        const double FrictionHeatCoefficient = 0.0040;
        const double TemperatureTimeConstant = 60.0;

        const double WearCoefficient = 9.0e-5;
        const double WearTempOnset = Limits.BitTemperatureWarning;
        const double WearTempScale = 60.0;

        const double InfluxCoefficient = 1.2e-4;
        const double LossCoefficient = 1.0e-4;
        const double GasExpansionFactor = 1.8;

        const double StuckOnsetCuttings = 0.50;
        const double StuckGrowthRate = 0.055;
        const double StuckRecoveryRate = 0.085;
        const double MobilityFromRotation = 0.25;
        const double MobilityFromCirculation = 0.75;

        const double FatiguePerTorqueExcess = 0.0035;

        const double MudDensityChangeRate = 4.5;   // kg/m³ 每秒，换浆需要时间

        // ------------------------------------------------------------------ 状态

        public DrillState State { get; }
        public WellProfile Well { get; }

        readonly List<DrillEvent> _events = new List<DrillEvent>(8);
        Formation _lastFormation;
        bool _kickAnnounced;
        bool _lossAnnounced;
        double _kickAnnouncedVolume;

        public DrillSimulation(WellProfile well, DrillState initialState = null)
        {
            Well = well ?? throw new ArgumentNullException(nameof(well));
            State = initialState ?? new DrillState();
            State.CurrentFormation = Well.FormationAt(State.Depth);
            _lastFormation = State.CurrentFormation;
            RefreshHydraulics(DrillControls.Idle);
        }

        /// <summary>本步产生的事件。每次 <see cref="Step"/> 调用后会被重建。</summary>
        public IReadOnlyList<DrillEvent> Events => _events;

        /// <summary>
        /// 推进仿真 <paramref name="dt"/> 秒。
        /// dt 建议不超过 0.25 秒，更大的步长会让井涌的指数增长失真。
        /// </summary>
        public void Step(DrillControls rawControls, double dt)
        {
            if (dt <= 0.0) return;
            _events.Clear();

            var c = rawControls.Clamped();
            var s = State;
            s.ElapsedSeconds += dt;

            UpdateFormation(s);
            UpdateMudDensity(s, c, dt);
            RefreshHydraulics(c);
            UpdateWellControl(s, c, dt);
            UpdatePenetration(s, c, dt);
            UpdateCuttings(s, c, dt);
            UpdateTorqueAndStuck(s, c, dt);
            UpdateTemperature(s, c, dt);
            UpdateBitWear(s, c, dt);
            UpdateIntegrity(s, dt);
        }

        // ------------------------------------------------------------------ 分步

        void UpdateFormation(DrillState s)
        {
            s.CurrentFormation = Well.FormationAt(s.Depth);
            if (!ReferenceEquals(s.CurrentFormation, _lastFormation))
            {
                _lastFormation = s.CurrentFormation;
                _events.Add(DrillEvent.FormationChanged);
            }
        }

        /// <summary>泥浆密度不会瞬间跟上旋钮，换浆需要把新泥浆泵下去循环一周。</summary>
        static void UpdateMudDensity(DrillState s, DrillControls c, double dt)
        {
            double delta = c.TargetMudDensity - s.MudDensity;
            double maxStep = MudDensityChangeRate * dt;
            s.MudDensity += Math.Abs(delta) <= maxStep ? delta : Math.Sign(delta) * maxStep;
        }

        void RefreshHydraulics(DrillControls c)
        {
            var s = State;
            s.IsCirculating = c.PumpRate > 2.0;
            s.BottomholePressure = Physics.BottomholePressure(s.MudDensity, s.Depth, c.PumpRate);

            // 关井后井筒被封死，侵入的地层流体会把压力顶上去。
            if (c.BlowoutPreventerClosed && s.KickVolume > 0.0)
            {
                double confinement = 1.0 - 0.85 * c.ChokeOpening;
                s.ShutInPressure = s.KickVolume * 340.0 * confinement * (s.KickIsGas ? GasExpansionFactor : 1.0);
                s.BottomholePressure += s.ShutInPressure;
            }
            else
            {
                s.ShutInPressure = 0.0;
            }

            s.EquivalentCirculatingDensity =
                Physics.EquivalentCirculatingDensity(s.BottomholePressure, s.Depth);
        }

        /// <summary>
        /// 井控：比较井底压力和地层的孔隙压力、破裂压力。
        /// 压不住就涌，压太狠就漏。两者都通过泥浆池体积的变化暴露给玩家。
        /// </summary>
        void UpdateWellControl(DrillState s, DrillControls c, double dt)
        {
            var f = s.CurrentFormation;
            double pore = f.PorePressureGradient * s.Depth;
            double frac = f.FracturePressureGradient * s.Depth;

            double underbalance = pore - s.BottomholePressure;
            double overbalance = s.BottomholePressure - frac;

            // 井涌。防喷器关闭后流入速率大幅下降，但不会立刻归零。
            if (underbalance > 0.0 && f.Permeability > 0.0)
            {
                double rate = underbalance * f.Permeability * InfluxCoefficient;
                if (c.BlowoutPreventerClosed) rate *= 0.12;

                s.KickVolume += rate * dt;
                s.PitVolume += rate * dt;
                if (f.GasBearing) s.KickIsGas = true;

                if (!_kickAnnounced && s.KickVolume > 0.25)
                {
                    _kickAnnounced = true;
                    _kickAnnouncedVolume = s.KickVolume;
                    _events.Add(DrillEvent.KickStarted);
                }
                else if (_kickAnnounced && s.KickVolume > _kickAnnouncedVolume * 2.0)
                {
                    _kickAnnouncedVolume = s.KickVolume;
                    _events.Add(DrillEvent.KickWorsening);
                }

                // 含气井涌上返膨胀，超过这个量就压不回去了。
                double blowoutThreshold = s.KickIsGas ? 8.0 : 16.0;
                if (s.KickVolume > blowoutThreshold && !c.BlowoutPreventerClosed)
                    _events.Add(DrillEvent.Blowout);
            }
            else if (s.KickVolume > 0.0)
            {
                // 重新压住后侵入的流体被循环出去。
                double removal = (s.IsCirculating ? 0.020 : 0.004) * dt;
                double removed = Math.Min(s.KickVolume, removal);
                s.KickVolume -= removed;
                s.PitVolume -= removed;
                if (s.KickVolume <= 1e-4)
                {
                    s.KickVolume = 0.0;
                    s.KickIsGas = false;
                    _kickAnnounced = false;
                }
            }

            // 漏失。
            if (overbalance > 0.0 && f.Permeability > 0.0)
            {
                double rate = overbalance * f.Permeability * LossCoefficient;
                s.LossVolume += rate * dt;
                s.PitVolume -= rate * dt;

                if (!_lossAnnounced && s.LossVolume > 0.4)
                {
                    _lossAnnounced = true;
                    _events.Add(DrillEvent.LossStarted);
                }
            }
            else if (_lossAnnounced && overbalance < -200.0)
            {
                _lossAnnounced = false;
            }

            s.PitVolume = Math.Max(0.0, s.PitVolume);
        }

        void UpdatePenetration(DrillState s, DrillControls c, double dt)
        {
            if (!c.BitOnBottom || c.RotarySpeed < 1.0 || s.StuckSeverity > 0.95 || s.BitWear >= 1.0)
            {
                s.RateOfPenetration = 0.0;
                return;
            }

            var f = s.CurrentFormation;

            // 钻压响应是一个三角形：升到 founder point 之前越压越快，
            // 越过之后钻头被岩屑糊住，反而越压越慢。这是真实钻井里的核心权衡。
            double founder = FounderBase + FounderPerHardness * f.Hardness;
            double wobNorm = c.WeightOnBit / founder;
            double wobShape = wobNorm <= 1.0
                ? wobNorm
                : Math.Max(0.0, 1.0 - (wobNorm - 1.0) * FounderOverloadSlope);

            double rpmFactor = Math.Pow(c.RotarySpeed / 100.0, RopRpmExponent);
            double wearFactor = 1.0 + RopWearPenalty * s.BitWear;
            double cleaning = 1.0 - RopCuttingsPenalty * s.CuttingsLoad;
            double stuckFactor = 1.0 - s.StuckSeverity;

            s.RateOfPenetration = Math.Max(0.0,
                RopCoefficient * wobShape * rpmFactor * cleaning * stuckFactor / (f.Hardness * wearFactor));

            double advance = s.RateOfPenetration / 3600.0 * dt;
            s.Depth += advance;
            s.FootageThisShift += advance;

            // 井眼变深，需要泥浆填充。这是泥浆池的正常缓慢下降，
            // 玩家必须学会把它和真正的漏失区分开。
            s.PitVolume -= advance * Limits.HoleCrossSection;
        }

        static void UpdateCuttings(DrillState s, DrillControls c, double dt)
        {
            double generated = s.RateOfPenetration * CuttingsGenPerRop;
            double cleared = c.PumpRate * CuttingsClearPerPump * s.CuttingsLoad;
            s.CuttingsLoad = Physics.Clamp01(s.CuttingsLoad + (generated - cleared) * dt);
        }

        void UpdateTorqueAndStuck(DrillState s, DrillControls c, double dt)
        {
            var f = s.CurrentFormation;

            if (c.RotarySpeed < 1.0)
            {
                s.Torque = 0.0;
            }
            else
            {
                double effectiveWob = c.BitOnBottom ? c.WeightOnBit : c.WeightOnBit * 0.15;
                s.Torque = TorqueFriction * effectiveWob * Physics.BitRadius
                           * (1.0 + TorqueCuttingsFactor * s.CuttingsLoad)
                           * (1.0 + TorqueHardnessFactor * f.Hardness)
                           * (1.0 + TorqueStuckFactor * s.StuckSeverity);

                if (s.Torque > Limits.TorqueLimit)
                    _events.Add(DrillEvent.TorqueOverLimit);
            }

            // 岩屑堆积到一定程度就开始卡钻。
            // 把岩屑真正带出井眼的是泥浆循环，转动只能搅动局部、防止岩屑床固结，
            // 所以循环在解卡里占主导权重。这一条直接决定了「泵停了就要立刻处理」这个玩法规则。
            double previous = s.StuckSeverity;
            double mobility = Physics.Clamp01(c.RotarySpeed / 80.0) * MobilityFromRotation
                              + Physics.Clamp01(c.PumpRate / 30.0) * MobilityFromCirculation;

            if (s.CuttingsLoad > StuckOnsetCuttings)
            {
                double pressure = (s.CuttingsLoad - StuckOnsetCuttings) / (1.0 - StuckOnsetCuttings);
                s.StuckSeverity = Physics.Clamp01(
                    s.StuckSeverity + (pressure * (1.0 - mobility) * StuckGrowthRate
                                       - mobility * StuckRecoveryRate * 0.4) * dt);
            }
            else
            {
                s.StuckSeverity = Physics.Clamp01(s.StuckSeverity - StuckRecoveryRate * mobility * dt);
            }

            if (previous < 0.30 && s.StuckSeverity >= 0.30) _events.Add(DrillEvent.StuckPipeWarning);
            if (previous < 0.70 && s.StuckSeverity >= 0.70) _events.Add(DrillEvent.StuckPipeSevere);
        }

        void UpdateTemperature(DrillState s, DrillControls c, double dt)
        {
            double staticTemp = Physics.StaticFormationTemperature(s.Depth);
            double cooling = c.PumpRate * CoolingPerPumpRate
                             * Physics.Clamp01((s.BottomholeTemperature - MudInletTemperature) / 80.0);
            double frictionHeat = s.Torque * c.RotarySpeed * FrictionHeatCoefficient;

            double target = staticTemp - cooling + frictionHeat;
            s.BottomholeTemperature += (target - s.BottomholeTemperature) / TemperatureTimeConstant * dt;

            if (s.BottomholeTemperature > Limits.BitTemperatureWarning)
                _events.Add(DrillEvent.BitOverheating);
        }

        void UpdateBitWear(DrillState s, DrillControls c, double dt)
        {
            if (c.RotarySpeed < 1.0 || !c.BitOnBottom) return;

            var f = s.CurrentFormation;
            double founder = FounderBase + FounderPerHardness * f.Hardness;
            double loadFactor = 1.0 + c.WeightOnBit / founder;
            double tempFactor = 1.0 + Math.Max(0.0,
                (s.BottomholeTemperature - WearTempOnset) / WearTempScale);

            double before = s.BitWear;
            s.BitWear = Physics.Clamp01(s.BitWear
                + WearCoefficient * (c.RotarySpeed / 100.0) * f.Abrasiveness
                  * loadFactor * tempFactor * dt);

            if (before < 1.0 && s.BitWear >= 1.0) _events.Add(DrillEvent.BitWornOut);
        }

        void UpdateIntegrity(DrillState s, double dt)
        {
            if (s.Torque > Limits.TorqueLimit)
            {
                double excess = (s.Torque - Limits.TorqueLimit) / Limits.TorqueLimit;
                double before = s.DrillstringFatigue;
                s.DrillstringFatigue = Physics.Clamp01(
                    s.DrillstringFatigue + excess * FatiguePerTorqueExcess * dt);
                if (before < 1.0 && s.DrillstringFatigue >= 1.0)
                    _events.Add(DrillEvent.DrillstringFailed);
            }
        }

        // ------------------------------------------------------------------ 查询

        /// <summary>
        /// 当前深度下的安全泥浆密度窗口，kg/m³。
        /// 下限压住地层流体，上限不压裂地层（已扣掉环空摩阻）。
        /// 这个窗口不直接显示给玩家，玩家要靠岩屑、录井和经验去推断。
        /// </summary>
        public (double min, double max) SafeMudWindow(double pumpRate)
        {
            var f = Well.FormationAt(State.Depth);
            double d = State.Depth;
            double min = f.PorePressureGradient / Physics.G * 1000.0;
            double frictionAsDensity = d < 1.0
                ? 0.0
                : Physics.AnnularFrictionLoss(pumpRate, d) * 1000.0 / (Physics.G * d);
            double max = f.FracturePressureGradient / Physics.G * 1000.0 - frictionAsDensity;
            return (min, max);
        }

        /// <summary>相对标称值的泥浆池偏差，m³。正数代表增多（可能井涌），负数代表减少（可能漏失）。</summary>
        public double PitDeviation => State.PitVolume - Limits.NominalPitVolume;
    }
}
