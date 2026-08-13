using Maner.Controls;
using Maner.Shift;
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
        public ShiftDirector Director { get; private set; }
        public CommsSystem Comms { get; private set; }

        [SerializeField] bool buildOnAwake = true;
        [SerializeField] float timeScale = 1f;

        double accumulator;
        RobotOperator autoPilot;

        /// <summary>
        /// 自动驾驶模式。它让机器人操作员接管控制台，用于无人值守自检：
        /// 截图里的表盘会有真实读数、控件会真的在动，而不是一屋子静止的摆设。
        /// 命令行参数 -manerAutoPilot 开启。
        /// </summary>
        public bool AutoPilotEnabled => autoPilot != null;

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

            Director = new ShiftDirector();
            Director.Load(ShiftDirector.BuildFirstShift());
            Director.ScheduleFirstShiftFaults();
            Director.Begin();

            Comms = CommsSystem.BuildFirstShift();

            foreach (var arg in System.Environment.GetCommandLineArgs())
            {
                if (arg == "-manerAutoPilot")
                {
                    autoPilot = new RobotOperator();
                    break;
                }
            }

            Debug.Log($"[Cabin] 初始化完成 自动驾驶={AutoPilotEnabled} 指令数={Director.Orders.Count} " +
                      $"控件数={Builder.Visuals.Count}");

            BuildAmbientAudio();
            PushAllVisuals(true);
        }

        void Update()
        {
            if (Simulation == null)
            {
                return;
            }

            double delta = Time.deltaTime * timeScale;

            if (autoPilot != null && Director.Phase == ShiftPhase.Running)
            {
                autoPilot.Tick(Director.ShiftTime, Director, Simulation, Console);
            }

            Console.DrainPulses(Simulation);
            Simulation.SetInputs(Console.BuildInputs());
            int steps = Simulation.Advance(delta, ref accumulator);

            if (steps > 0)
            {
                double simulated = steps * ShaftSimulation.FixedDeltaTime;
                Director.Tick(simulated, Simulation, Console);
                Comms.Tick(Director.ShiftTime, simulated);

                // 自动驾驶下顺手把电话也接了，否则未接来电会一直扣士气。
                if (autoPilot != null && Comms.State == CallState.Ringing)
                {
                    Comms.Answer(Director.ShiftTime);
                    if (Comms.State == CallState.AwaitingReply)
                    {
                        Comms.Reply(0, Director.ShiftTime);
                    }
                }
            }

            Console.PushGaugeReadings(Simulation);
            PushAllVisuals(false);

            logTimer += Time.deltaTime;
            if (logTimer >= 5f)
            {
                logTimer = 0f;
                Debug.Log($"[Cabin] t={Director.ShiftTime:0.0}s 指令={Director.CurrentIndex + 1}/{Director.Orders.Count} " +
                          $"母线={Simulation.Power.BusVoltage:0}V 风量={Simulation.Ventilation.Airflow:0.0} " +
                          $"深度={Simulation.Hoist.Depth:0.0}m 瓦斯={Simulation.Ventilation.GasPercent:0.00}%");
            }
        }

        float logTimer;

        /// <summary>
        /// 舱内环境声。低频轰鸣、通风白噪与水泵搏动都由代码合成，
        /// 循环点做过交叉淡化，不会听到接缝。
        /// </summary>
        void BuildAmbientAudio()
        {
            var go = new GameObject("CabinAmbience");
            go.transform.SetParent(transform, false);
            var source = go.AddComponent<AudioSource>();
            source.clip = ProceduralAudio.CreateAmbientLoop();
            source.loop = true;
            source.spatialBlend = 0f;
            source.volume = 0.38f;
            source.playOnAwake = false;
            source.Play();
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
