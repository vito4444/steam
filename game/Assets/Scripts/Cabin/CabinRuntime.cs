using Maner.Controls;
using Maner.Sim;
using UnityEngine;

namespace Maner.Cabin
{
    /// <summary>
    /// 把仿真、控制台状态与舱内表现接在一起的运行时枢纽。
    /// 场景里只需要一个空对象挂上它，整座控制舱会在 Awake 阶段被完整生成出来。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CabinRuntime : MonoBehaviour
    {
        public ShaftSimulation Simulation { get; private set; }
        public ConsoleState Console { get; private set; }
        public CabinBuilder Builder { get; private set; }

        [SerializeField] bool buildOnAwake = true;
        [SerializeField] float timeScale = 1f;

        double accumulator;

        void Awake()
        {
            if (buildOnAwake)
            {
                Initialize();
            }
        }

        public void Initialize()
        {
            if (Builder != null)
            {
                return;
            }

            Builder = new CabinBuilder();
            Builder.Build(transform);
            CabinLighting.Apply(Builder.Root, Builder.Materials);

            Simulation = new ShaftSimulation();
            Console = new ConsoleState();

            PushAllVisuals(true);
        }

        void Update()
        {
            if (Simulation == null)
            {
                return;
            }

            Console.DrainPulses(Simulation);
            Simulation.SetInputs(Console.BuildInputs());
            Simulation.Advance(Time.deltaTime * timeScale, ref accumulator);
            Console.PushGaugeReadings(Simulation);
            PushAllVisuals(false);
        }

        void PushAllVisuals(bool snap)
        {
            foreach (var def in ConsoleLayout.All)
            {
                if (!Builder.Visuals.TryGetValue(def.Id, out var visual))
                {
                    continue;
                }

                double normalized = NormalizedFor(def);
                if (snap)
                {
                    visual.SnapValue(normalized);
                }
                else
                {
                    visual.SetValue(normalized);
                }
            }

            UpdateLamps();
        }

        double NormalizedFor(in ControlDefinition def)
        {
            if (def.Kind != ControlKind.Gauge)
            {
                return Console.Get(def.Id);
            }

            var (min, max) = ConsoleState.GaugeRange(def.Id);
            double raw = Console.Get(def.Id);
            double t = (raw - min) / (max - min);
            return t < 0.0 ? 0.0 : t > 1.0 ? 1.0 : t;
        }

        void UpdateLamps()
        {
            SetBreakerLamp(ControlId.BreakerHoist, Console.GetBool(ControlId.BreakerHoist));
            SetBreakerLamp(ControlId.BreakerVentilation, Console.GetBool(ControlId.BreakerVentilation));
            SetBreakerLamp(ControlId.BreakerLighting, Console.GetBool(ControlId.BreakerLighting));
            SetBreakerLamp(ControlId.BreakerAuxiliary, Console.GetBool(ControlId.BreakerAuxiliary));
        }

        void SetBreakerLamp(ControlId id, bool closed)
        {
            if (!Builder.Visuals.TryGetValue(id, out var visual))
            {
                return;
            }

            LampState state = Console.IsTripped(id) ? LampState.Red
                : closed ? LampState.Green
                : LampState.Off;
            visual.SetLamp(Builder.Materials, state);
        }

        /// <summary>供机器人操作员与自动化测试使用：直接把某个控件推到指定位置。</summary>
        public void SetControl(ControlId id, double value)
        {
            Console.Set(id, value);
            if (Builder != null && Builder.Visuals.TryGetValue(id, out var visual))
            {
                visual.SetValue(value);
            }
        }

        public void PressControl(ControlId id)
        {
            if (Console.Press(id) && Builder != null && Builder.Visuals.TryGetValue(id, out var visual))
            {
                visual.SnapValue(1.0);
                visual.SetValue(0.0);
            }
        }
    }
}
