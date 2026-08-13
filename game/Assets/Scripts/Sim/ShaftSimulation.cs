using System;
using System.Collections.Generic;

namespace Maner.Sim
{
    /// <summary>
    /// 深井仿真顶层。三套子系统共用一条母线，构成本作的核心张力三角：
    /// 卷扬机要电，主扇也要电，而主扇一旦供不上风，井下的瓦斯就会往上爬。
    ///
    /// 本类刻意不引用任何 Unity 类型，也不依赖帧率。它以固定步长推进，
    /// 相同的初始状态加相同的输入序列必然得到逐位相同的结果，
    /// 因此可以在无渲染的情况下以任意倍速跑完整个班次用于平衡性测试。
    /// </summary>
    public sealed class ShaftSimulation
    {
        public const double FixedDeltaTime = 1.0 / 50.0;

        public readonly PowerSystem Power = new PowerSystem();
        public readonly VentilationSystem Ventilation = new VentilationSystem();
        public readonly HoistSystem Hoist = new HoistSystem();

        public readonly List<SimEvent> Events = new List<SimEvent>(64);

        public double Tick { get; private set; }
        public long StepCount { get; private set; }

        DeterministicRandom rng;
        readonly Queue<SimPulse> pendingPulses = new Queue<SimPulse>();
        SimInputs currentInputs = SimInputs.Neutral;

        public ShaftSimulation(ulong seed = 0x4D414E4552UL)
        {
            rng = new DeterministicRandom(seed);
            Reset(seed);
        }

        public SimInputs Inputs => currentInputs;

        public void Reset(ulong seed)
        {
            rng = new DeterministicRandom(seed);
            Power.Reset();
            Ventilation.Reset();
            Hoist.Reset();
            Events.Clear();
            pendingPulses.Clear();
            currentInputs = SimInputs.Neutral;
            Tick = 0.0;
            StepCount = 0;
        }

        public void SetInputs(in SimInputs inputs) => currentInputs = inputs;

        public void QueuePulse(SimPulse pulse) => pendingPulses.Enqueue(pulse);

        /// <summary>推进一个固定步长。返回本步新产生的事件数量。</summary>
        public int Step()
        {
            int eventsBefore = Events.Count;

            while (pendingPulses.Count > 0)
            {
                var pulse = pendingPulses.Dequeue();
                Power.HandlePulse(pulse);
                Ventilation.HandlePulse(pulse);
                Hoist.HandlePulse(pulse, Events, Tick);
            }

            double dt = FixedDeltaTime;
            double busVoltage = Power.BusVoltage;

            // 两个大功率设备先按上一步的母线电压算出各自的功率需求，
            // 再把总需求交给发电机组，最后用新的母线电压推进各自的物理状态。
            // 这一步的时序就是玩家能感受到的「电压跌落 → 风机掉转速」。
            double fanKw = Ventilation.ComputeDemandKw(dt, currentInputs, busVoltage);
            double hoistKw = Hoist.ComputeDemandKw(currentInputs, busVoltage);

            Power.Step(dt, currentInputs, fanKw + hoistKw, Events, Tick);

            Hoist.Step(dt, currentInputs, Power.BusVoltage, Events, Tick);
            Ventilation.Step(dt, currentInputs, Hoist.Depth, rng, Events, Tick);

            Tick += dt;
            StepCount++;
            return Events.Count - eventsBefore;
        }

        /// <summary>按真实时间推进，内部拆成整数个固定步长。返回实际推进的步数。</summary>
        public int Advance(double deltaTime, ref double accumulator)
        {
            accumulator += deltaTime;
            int steps = 0;
            // 单帧最多补 8 步，避免长时间卡顿后出现螺旋式追赶。
            while (accumulator >= FixedDeltaTime && steps < 8)
            {
                Step();
                accumulator -= FixedDeltaTime;
                steps++;
            }
            return steps;
        }

        /// <summary>
        /// 仿真状态的 64 位指纹。用于确定性回归：相同输入序列跑两次必须得到相同的值。
        /// 只纳入会影响后续演化的状态，不纳入事件列表这类纯输出。
        /// </summary>
        public ulong StateHash()
        {
            ulong h = 1469598103934665603UL;

            Mix(ref h, StepCount);
            Mix(ref h, Tick);

            Mix(ref h, Power.GeneratorRunning);
            Mix(ref h, Power.Rpm);
            Mix(ref h, Power.Frequency);
            Mix(ref h, Power.BusVoltage);
            Mix(ref h, Power.LoadKw);
            Mix(ref h, Power.MainBreakerTripped);
            Mix(ref h, Power.OverloadTimer);
            Mix(ref h, Power.FuelLevel);
            Mix(ref h, Power.CoolantTemp);
            Mix(ref h, Power.BatteryCharge);
            Mix(ref h, Power.GeneratorWear);

            Mix(ref h, Ventilation.FanSpeed);
            Mix(ref h, Ventilation.Airflow);
            Mix(ref h, Ventilation.GasPercent);
            Mix(ref h, Ventilation.FanMotorKw);
            Mix(ref h, Ventilation.GasAlarmActive);
            Mix(ref h, Ventilation.FanBearingWear);

            Mix(ref h, Hoist.Depth);
            Mix(ref h, Hoist.Velocity);
            Mix(ref h, Hoist.PayloadKg);
            Mix(ref h, Hoist.MotorKw);
            Mix(ref h, Hoist.RopeTensionN);
            Mix(ref h, Hoist.RopeFatigue);
            Mix(ref h, Hoist.OverspeedTripped);
            Mix(ref h, Hoist.BrakePadWear);
            Mix(ref h, Hoist.MotorWear);

            var (s0, s1) = rng.SaveState();
            Mix(ref h, s0);
            Mix(ref h, s1);

            return h;
        }

        static void Mix(ref ulong hash, double value) => Mix(ref hash, (ulong)BitConverter.DoubleToInt64Bits(value));

        static void Mix(ref ulong hash, long value) => Mix(ref hash, (ulong)value);

        static void Mix(ref ulong hash, bool value) => Mix(ref hash, value ? 0x9E3779B9UL : 0x85EBCA6BUL);

        static void Mix(ref ulong hash, ulong value)
        {
            for (int i = 0; i < 8; i++)
            {
                hash ^= (value >> (i * 8)) & 0xFF;
                hash *= 1099511628211UL;
            }
        }
    }
}
