using System.Collections.Generic;
using Maner.Sim;

namespace Maner.Controls
{
    /// <summary>
    /// 控制台的完整布局表。三块面板呈 U 形环绕操作员，
    /// 面板局部坐标以左下角为原点，单位米，面板尺寸 1.42 × 1.12。
    ///
    /// 这张表是唯一的事实来源：几何生成、丝印绘制、交互检测、
    /// 音频参数与自动化测试全都从这里读，不存在第二份布局定义。
    /// </summary>
    public static class ConsoleLayout
    {
        public const float PanelWidth = 1.42f;
        public const float PanelHeight = 1.12f;

        static readonly ControlDefinition[] Definitions =
        {
            // ================= 供电盘 =================
            new ControlDefinition(ControlId.GaugeBusVoltage, ControlKind.Gauge, PanelId.Power, "母线电压 V", 0.24f, 0.88f, 0.19f),
            new ControlDefinition(ControlId.GaugeLoad, ControlKind.Gauge, PanelId.Power, "负载 kW", 0.56f, 0.88f, 0.19f),
            new ControlDefinition(ControlId.GaugeFrequency, ControlKind.Gauge, PanelId.Power, "频率 Hz", 0.88f, 0.88f, 0.19f),
            new ControlDefinition(ControlId.GaugeCoolantTemp, ControlKind.Gauge, PanelId.Power, "水温 °C", 1.18f, 0.88f, 0.16f),
            new ControlDefinition(ControlId.GaugeFuel, ControlKind.Gauge, PanelId.Power, "燃油", 1.18f, 0.62f, 0.14f),

            new ControlDefinition(ControlId.GeneratorMaster, ControlKind.KnifeSwitch, PanelId.Power, "机组总闸",
                0.22f, 0.32f, 0.19f, throwThreshold: 0.7f, audioBaseHz: 130f),
            new ControlDefinition(ControlId.FuelValve, ControlKind.ValveWheel, PanelId.Power, "燃油阀",
                0.58f, 0.32f, 0.21f, turnsToFull: 2.5f, audioBaseHz: 190f),
            new ControlDefinition(ControlId.CoolantPump, ControlKind.ToggleLever, PanelId.Power, "冷却泵",
                0.88f, 0.36f, 0.11f, audioBaseHz: 310f),
            new ControlDefinition(ControlId.Excitation, ControlKind.RotaryKnob, PanelId.Power, "励磁调节",
                1.16f, 0.36f, 0.12f, defaultValue: 0.9, audioBaseHz: 420f),
            new ControlDefinition(ControlId.BreakerHoist, ControlKind.Breaker, PanelId.Power, "卷扬分路",
                0.20f, 0.10f, 0.10f, audioBaseHz: 150f),
            new ControlDefinition(ControlId.BreakerVentilation, ControlKind.Breaker, PanelId.Power, "通风分路",
                0.44f, 0.10f, 0.10f, audioBaseHz: 155f),
            new ControlDefinition(ControlId.BreakerLighting, ControlKind.Breaker, PanelId.Power, "照明分路",
                0.68f, 0.10f, 0.10f, audioBaseHz: 160f),
            new ControlDefinition(ControlId.BreakerAuxiliary, ControlKind.Breaker, PanelId.Power, "辅助分路",
                0.92f, 0.10f, 0.10f, audioBaseHz: 165f),
            new ControlDefinition(ControlId.BatteryTie, ControlKind.KnifeSwitch, PanelId.Power, "电池并网",
                1.18f, 0.14f, 0.15f, throwThreshold: 0.65f, audioBaseHz: 120f),
            new ControlDefinition(ControlId.ResetOverload, ControlKind.PushButton, PanelId.Power, "过载复位",
                1.20f, 0.50f, 0.075f, pulse: SimPulse.ResetOverload, audioBaseHz: 640f),

            // ================= 通风盘 =================
            new ControlDefinition(ControlId.GaugeGas, ControlKind.Gauge, PanelId.Ventilation, "瓦斯 %", 0.30f, 0.88f, 0.22f),
            new ControlDefinition(ControlId.GaugeAirflow, ControlKind.Gauge, PanelId.Ventilation, "风量 m³/s", 0.66f, 0.88f, 0.19f),
            new ControlDefinition(ControlId.GaugeFanSpeed, ControlKind.Gauge, PanelId.Ventilation, "主扇转速", 1.02f, 0.88f, 0.19f),

            new ControlDefinition(ControlId.MainFanSwitch, ControlKind.KnifeSwitch, PanelId.Ventilation, "主扇启停",
                0.20f, 0.56f, 0.18f, throwThreshold: 0.7f, audioBaseHz: 135f),
            new ControlDefinition(ControlId.FanSpeedWheel, ControlKind.ValveWheel, PanelId.Ventilation, "主扇调速",
                0.58f, 0.54f, 0.25f, turnsToFull: 3.5f, audioBaseHz: 175f),
            new ControlDefinition(ControlId.Damper1, ControlKind.ValveWheel, PanelId.Ventilation, "一号风门",
                0.28f, 0.20f, 0.18f, turnsToFull: 2f, audioBaseHz: 200f),
            new ControlDefinition(ControlId.Damper2, ControlKind.ValveWheel, PanelId.Ventilation, "二号风门",
                0.64f, 0.20f, 0.18f, turnsToFull: 2f, audioBaseHz: 205f),
            new ControlDefinition(ControlId.Damper3, ControlKind.ValveWheel, PanelId.Ventilation, "三号风门",
                1.00f, 0.20f, 0.18f, turnsToFull: 2f, audioBaseHz: 210f),
            new ControlDefinition(ControlId.GasDrainagePump, ControlKind.ToggleLever, PanelId.Ventilation, "抽放泵",
                1.22f, 0.58f, 0.11f, audioBaseHz: 300f),
            new ControlDefinition(ControlId.ReverseAirflow, ControlKind.KnifeSwitch, PanelId.Ventilation, "反风",
                1.22f, 0.36f, 0.14f, throwThreshold: 0.8f, audioBaseHz: 125f),
            new ControlDefinition(ControlId.SilenceGasAlarm, ControlKind.PushButton, PanelId.Ventilation, "报警消音",
                1.22f, 0.16f, 0.08f, pulse: SimPulse.SilenceGasAlarm, audioBaseHz: 700f),

            // ================= 卷扬盘 =================
            new ControlDefinition(ControlId.GaugeDepth, ControlKind.Gauge, PanelId.Hoist, "深度 m", 0.28f, 0.88f, 0.23f),
            new ControlDefinition(ControlId.GaugeCageSpeed, ControlKind.Gauge, PanelId.Hoist, "罐速 m/s", 0.64f, 0.88f, 0.19f),
            new ControlDefinition(ControlId.GaugePayload, ControlKind.Gauge, PanelId.Hoist, "载重 kg", 0.96f, 0.88f, 0.17f),
            new ControlDefinition(ControlId.GaugeRopeTension, ControlKind.Gauge, PanelId.Hoist, "绳张力 kN", 1.24f, 0.88f, 0.15f),

            new ControlDefinition(ControlId.HoistPower, ControlKind.KnifeSwitch, PanelId.Hoist, "卷扬电源",
                0.18f, 0.58f, 0.18f, throwThreshold: 0.7f, audioBaseHz: 128f),
            new ControlDefinition(ControlId.Throttle, ControlKind.ThrottleHandle, PanelId.Hoist, "调速手柄",
                0.52f, 0.44f, 0.28f, audioBaseHz: 95f),
            new ControlDefinition(ControlId.Brake, ControlKind.ThrottleHandle, PanelId.Hoist, "制动手柄",
                0.84f, 0.44f, 0.23f, audioBaseHz: 110f),
            new ControlDefinition(ControlId.CageLock, ControlKind.ToggleLever, PanelId.Hoist, "罐笼锁定",
                1.12f, 0.62f, 0.12f, defaultValue: 1.0, audioBaseHz: 260f),
            // 三档拨杆，中位为「停」，所以默认值是 0.5 而不是 0。
            new ControlDefinition(ControlId.Direction, ControlKind.ToggleLever, PanelId.Hoist, "提升 停 下放",
                1.12f, 0.40f, 0.13f, defaultValue: 0.5, detents: 3, audioBaseHz: 240f),
            new ControlDefinition(ControlId.CageLight, ControlKind.ToggleLever, PanelId.Hoist, "罐笼照明",
                1.30f, 0.62f, 0.10f, audioBaseHz: 330f),
            new ControlDefinition(ControlId.RopeSpeedTrim, ControlKind.RotaryKnob, PanelId.Hoist, "绳速微调",
                1.30f, 0.40f, 0.11f, defaultValue: 0.5, audioBaseHz: 450f),
            new ControlDefinition(ControlId.SignalBell, ControlKind.PushButton, PanelId.Hoist, "信号铃",
                0.18f, 0.16f, 0.085f, pulse: SimPulse.SignalBell, audioBaseHz: 880f),
            new ControlDefinition(ControlId.ConfirmBottomSignal, ControlKind.PushButton, PanelId.Hoist, "井底确认",
                0.42f, 0.16f, 0.08f, pulse: SimPulse.ConfirmBottomSignal, audioBaseHz: 760f),
            new ControlDefinition(ControlId.ResetOverspeed, ControlKind.PushButton, PanelId.Hoist, "超速复位",
                0.66f, 0.16f, 0.08f, pulse: SimPulse.ResetOverspeed, audioBaseHz: 600f),
        };

        static readonly Dictionary<ControlId, int> IndexById = BuildIndex();

        static Dictionary<ControlId, int> BuildIndex()
        {
            var map = new Dictionary<ControlId, int>(Definitions.Length);
            for (int i = 0; i < Definitions.Length; i++)
            {
                map[Definitions[i].Id] = i;
            }
            return map;
        }

        public static IReadOnlyList<ControlDefinition> All => Definitions;

        public static ControlDefinition Get(ControlId id) => Definitions[IndexById[id]];

        public static bool TryGet(ControlId id, out ControlDefinition definition)
        {
            if (IndexById.TryGetValue(id, out int index))
            {
                definition = Definitions[index];
                return true;
            }
            definition = default;
            return false;
        }

        public static IEnumerable<ControlDefinition> OnPanel(PanelId panel)
        {
            foreach (var d in Definitions)
            {
                if (d.Panel == panel)
                {
                    yield return d;
                }
            }
        }

        public static int InteractiveCount
        {
            get
            {
                int n = 0;
                foreach (var d in Definitions)
                {
                    if (d.IsInteractive)
                    {
                        n++;
                    }
                }
                return n;
            }
        }
    }
}
