using System.Text;
using Maner.Controls;
using Maner.Shift;
using Maner.Sim;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Maner.Cabin
{
    /// <summary>
    /// 屏幕层界面。本作的信息主要靠舱内的实体仪表传达，屏幕上只保留三样东西：
    /// 准星与操作提示、当前指令、以及电话。辅助读数默认打开是为了压平学习曲线，
    /// 熟练之后可以按 H 关掉，让画面回到只有机器的状态。
    ///
    /// 整个界面用 IMGUI 绘制，不引入任何 UI 资产。中文字体从系统里按优先级挑，
    /// 挑不到就退回默认字体。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CabinHud : MonoBehaviour
    {
        static readonly string[] FontCandidates =
        {
            "Microsoft YaHei", "微软雅黑", "SimHei", "黑体",
            "Noto Sans CJK SC", "Source Han Sans SC",
            "WenQuanYi Micro Hei", "Droid Sans Fallback", "Arial Unicode MS",
        };

        CabinRuntime runtime;
        PlayerRig player;

        Font uiFont;
        GUIStyle label;
        GUIStyle heading;
        GUIStyle small;
        GUIStyle panel;
        Texture2D panelTex;
        Texture2D accentTex;
        bool showReadouts = true;
        bool stylesReady;

        public void Bind(CabinRuntime cabinRuntime, PlayerRig rig)
        {
            runtime = cabinRuntime;
            player = rig;
        }

        void EnsureStyles()
        {
            if (stylesReady)
            {
                return;
            }

            // 字体必须随游戏打包：发行版不能指望玩家机器上装了中文字体，
            // 而且实测在 Player 里 CreateDynamicFontFromOSFont 拿不到系统字体，
            // 表现为界面框画出来了但一个字都没有。
            uiFont = Resources.Load<Font>("Fonts/DroidSansFallback");
            if (uiFont == null)
            {
                uiFont = Font.CreateDynamicFontFromOSFont(FontCandidates, 16);
            }
            if (uiFont == null)
            {
                Debug.LogWarning("[HUD] 没有可用的中文字体，界面文字会缺字");
                uiFont = GUI.skin != null ? GUI.skin.font : null;
            }

            panelTex = MakeTex(new Color(0.035f, 0.033f, 0.030f, 0.86f));
            accentTex = MakeTex(new Color(0.86f, 0.62f, 0.24f, 0.95f));

            label = new GUIStyle
            {
                font = uiFont,
                fontSize = 16,
                normal = { textColor = new Color(0.88f, 0.84f, 0.74f) },
                wordWrap = true,
            };

            heading = new GUIStyle(label)
            {
                fontSize = 19,
                normal = { textColor = new Color(0.95f, 0.76f, 0.38f) },
            };

            small = new GUIStyle(label)
            {
                fontSize = 13,
                normal = { textColor = new Color(0.70f, 0.67f, 0.60f) },
            };

            panel = new GUIStyle
            {
                normal = { background = panelTex },
                padding = new RectOffset(14, 14, 10, 12),
            };

            stylesReady = true;
        }

        static Texture2D MakeTex(Color color)
        {
            var tex = new Texture2D(1, 1);
            tex.SetPixel(0, 0, color);
            tex.Apply();
            return tex;
        }

        void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null || runtime == null)
            {
                return;
            }

            if (keyboard.hKey.wasPressedThisFrame)
            {
                showReadouts = !showReadouts;
            }

            HandleCallInput(keyboard);
        }

        void HandleCallInput(Keyboard keyboard)
        {
            var comms = runtime.Comms;
            if (comms == null)
            {
                return;
            }

            if (comms.State == CallState.Ringing && keyboard.eKey.wasPressedThisFrame)
            {
                comms.Answer(runtime.Director.ShiftTime);
            }
            else if (comms.State == CallState.AwaitingReply)
            {
                if (keyboard.digit1Key.wasPressedThisFrame)
                {
                    comms.Reply(0, runtime.Director.ShiftTime);
                }
                else if (keyboard.digit2Key.wasPressedThisFrame)
                {
                    comms.Reply(1, runtime.Director.ShiftTime);
                }
            }
        }

        void OnGUI()
        {
            if (runtime == null || runtime.Simulation == null)
            {
                return;
            }

            EnsureStyles();

            if (runtime.Director.Phase == ShiftPhase.Settled)
            {
                DrawSettlement();
                return;
            }

            DrawCrosshair();
            DrawOrder();
            if (showReadouts)
            {
                DrawReadouts();
            }
            DrawInteractionHint();
            DrawCall();
            DrawAlarms();
        }

        void DrawCrosshair()
        {
            float cx = Screen.width * 0.5f;
            float cy = Screen.height * 0.5f;
            bool onControl = player != null && player.Hovered != null;
            bool dragging = player != null && player.IsDragging;

            float size = dragging ? 10f : onControl ? 8f : 3f;
            var color = dragging ? new Color(0.95f, 0.72f, 0.30f, 0.95f)
                : onControl ? new Color(0.90f, 0.86f, 0.76f, 0.85f)
                : new Color(0.80f, 0.78f, 0.72f, 0.42f);

            var prev = GUI.color;
            GUI.color = color;
            if (onControl || dragging)
            {
                // 瞄到控件时准星张开成四个角，明确「这个能抓」。
                GUI.DrawTexture(new Rect(cx - size, cy - size, size * 0.7f, 2f), accentTex);
                GUI.DrawTexture(new Rect(cx - size, cy - size, 2f, size * 0.7f), accentTex);
                GUI.DrawTexture(new Rect(cx + size * 0.3f, cy - size, size * 0.7f, 2f), accentTex);
                GUI.DrawTexture(new Rect(cx + size - 2f, cy - size, 2f, size * 0.7f), accentTex);
                GUI.DrawTexture(new Rect(cx - size, cy + size - 2f, size * 0.7f, 2f), accentTex);
                GUI.DrawTexture(new Rect(cx - size, cy + size * 0.3f, 2f, size * 0.7f), accentTex);
                GUI.DrawTexture(new Rect(cx + size * 0.3f, cy + size - 2f, size * 0.7f, 2f), accentTex);
                GUI.DrawTexture(new Rect(cx + size - 2f, cy + size * 0.3f, 2f, size * 0.7f), accentTex);
            }
            else
            {
                GUI.DrawTexture(new Rect(cx - 1.5f, cy - 1.5f, 3f, 3f), accentTex);
            }
            GUI.color = prev;
        }

        void DrawOrder()
        {
            var director = runtime.Director;
            var order = director.Current;

            GUILayout.BeginArea(new Rect(24, 22, 440, 130), panel);
            GUILayout.Label($"班次 · 第 {Mathf.Min(director.CurrentIndex + 1, director.Orders.Count)} / {director.Orders.Count} 条指令", heading);
            GUILayout.Space(4);
            GUILayout.Label(order != null ? order.Text : "全部指令已完成，等待收工。", label);
            GUILayout.Space(2);
            GUILayout.Label($"班次时间 {FormatTime(director.ShiftTime)}", small);
            GUILayout.EndArea();
        }

        void DrawReadouts()
        {
            var sim = runtime.Simulation;
            var sb = new StringBuilder();
            sb.AppendLine($"母线  {sim.Power.BusVoltage,6:0} V     负载 {sim.Power.LoadKw,6:0} kW");
            sb.AppendLine($"风量  {sim.Ventilation.Airflow,6:0.0} m³/s  瓦斯 {sim.Ventilation.GasPercent,5:0.00} %");
            sb.AppendLine($"深度  {sim.Hoist.Depth,6:0.0} m     罐速 {sim.Hoist.Velocity,5:0.0} m/s");
            sb.Append($"水温  {sim.Power.CoolantTemp,6:0} °C    燃油 {sim.Power.FuelLevel * 100.0,5:0} %");

            GUILayout.BeginArea(new Rect(24, Screen.height - 128, 430, 108), panel);
            GUILayout.Label("辅助读数（H 键隐藏）", small);
            GUILayout.Label(sb.ToString(), label);
            GUILayout.EndArea();
        }

        void DrawInteractionHint()
        {
            var target = player != null ? (player.Grabbed ?? player.Hovered) : null;
            if (target == null)
            {
                return;
            }

            var def = ConsoleLayout.Get(target.Id);
            string value = def.IsContinuous
                ? $"{runtime.Console.Get(def.Id) * 100.0:0} %"
                : runtime.Console.GetBool(def.Id) ? "接通" : "断开";

            string hint = player.IsDragging
                ? $"{def.Label}   {value}"
                : $"{def.Label}   {ControlInteraction.HintFor(def.Kind)}";

            float w = 460f;
            GUILayout.BeginArea(new Rect(Screen.width * 0.5f - w * 0.5f, Screen.height * 0.5f + 46f, w, 44f), panel);
            GUILayout.Label(hint, label);
            GUILayout.EndArea();
        }

        void DrawCall()
        {
            var comms = runtime.Comms;
            if (comms == null || comms.State == CallState.Idle)
            {
                return;
            }

            float w = 620f;
            float x = Screen.width * 0.5f - w * 0.5f;

            if (comms.State == CallState.Ringing)
            {
                // 铃声用闪烁提示，避免玩家埋头看表盘时错过。
                bool blink = Mathf.Repeat(Time.time, 0.9f) < 0.55f;
                GUILayout.BeginArea(new Rect(x, Screen.height - 210, w, 52), panel);
                GUILayout.Label(blink
                    ? $"电话响了 —— {CommsSystem.SourceName(comms.Current.Source)}   按 E 接听"
                    : $"电话响了 —— {CommsSystem.SourceName(comms.Current.Source)}", heading);
                GUILayout.EndArea();
                return;
            }

            if (comms.State == CallState.AwaitingReply && comms.Current != null)
            {
                GUILayout.BeginArea(new Rect(x, Screen.height - 250, w, 150), panel);
                GUILayout.Label($"{CommsSystem.SourceName(comms.Current.Source)}：{comms.Current.Line}", label);
                GUILayout.Space(8);
                for (int i = 0; i < comms.Current.Replies.Count; i++)
                {
                    GUILayout.Label($"[{i + 1}]  {comms.Current.Replies[i].Text}", heading);
                }
                GUILayout.EndArea();
            }
        }

        void DrawAlarms()
        {
            var sim = runtime.Simulation;
            var lines = new StringBuilder();

            if (sim.Power.MainBreakerTripped)
            {
                lines.AppendLine("总断路器跳闸 · 按过载复位按钮");
            }
            if (sim.Ventilation.GasAlarmActive)
            {
                lines.AppendLine($"瓦斯超限 {sim.Ventilation.GasPercent:0.00}% · 加大风量");
            }
            if (sim.Hoist.OverspeedTripped)
            {
                lines.AppendLine("卷扬超速保护动作 · 停稳后复位");
            }
            if (sim.Power.CoolantTemp > 92.0)
            {
                lines.AppendLine($"机组水温 {sim.Power.CoolantTemp:0}°C · 检查冷却泵");
            }
            foreach (var fault in runtime.Director.Faults.Active)
            {
                lines.AppendLine($"故障 · {FaultSystem.Describe(fault)}");
            }

            if (lines.Length == 0)
            {
                return;
            }

            var warn = new GUIStyle(label) { normal = { textColor = new Color(0.95f, 0.42f, 0.28f) } };
            GUILayout.BeginArea(new Rect(Screen.width - 430, 22, 406, 150), panel);
            GUILayout.Label(lines.ToString().TrimEnd(), warn);
            GUILayout.EndArea();
        }

        void DrawSettlement()
        {
            var s = runtime.Director.Settlement;
            if (s == null)
            {
                return;
            }

            float w = 640f;
            float h = 420f;
            var rect = new Rect(Screen.width * 0.5f - w * 0.5f, Screen.height * 0.5f - h * 0.5f, w, h);

            GUILayout.BeginArea(rect, panel);
            GUILayout.Label("班次结算", heading);
            GUILayout.Space(10);

            GUILayout.Label($"评级          {s.Grade}", heading);
            GUILayout.Space(6);
            GUILayout.Label($"指令完成      {s.CompletedOrders} / {s.TotalOrders}   （超时 {s.TimedOutOrders}）", label);
            GUILayout.Label($"故障处置      {s.FaultsResolved} / {s.FaultsTriggered}", label);
            GUILayout.Label($"最严重事故    {IncidentName(s.WorstIncident)}", label);
            GUILayout.Label($"班次用时      {FormatTime(s.ShiftSeconds)}", label);
            GUILayout.Space(10);

            GUILayout.Label("设备损耗", heading);
            GUILayout.Label($"机组          {s.GeneratorWear * 100.0:0.0} %", label);
            GUILayout.Label($"闸瓦          {s.BrakePadWear * 100.0:0.0} %", label);
            GUILayout.Label($"卷扬电机      {s.MotorWear * 100.0:0.0} %", label);
            GUILayout.Label($"主扇轴承      {s.FanBearingWear * 100.0:0.0} %", label);
            GUILayout.Label($"剩余燃油      {s.FuelRemaining * 100.0:0} %", label);
            GUILayout.Space(8);
            GUILayout.Label($"峰值瓦斯      {s.PeakGasPercent:0.00} %      最高罐速 {s.MaxCageSpeed:0.0} m/s", label);
            GUILayout.Space(10);
            GUILayout.Label("损耗会带进下一个班次。设备不会自己变好。", small);
            GUILayout.EndArea();
        }

        static string IncidentName(IncidentSeverity severity) => severity switch
        {
            IncidentSeverity.None => "无",
            IncidentSeverity.Minor => "轻微",
            IncidentSeverity.Serious => "重大",
            _ => "灾难性",
        };

        static string FormatTime(double seconds)
        {
            int total = Mathf.Max(0, Mathf.RoundToInt((float)seconds));
            return $"{total / 60:00}:{total % 60:00}";
        }
    }
}
