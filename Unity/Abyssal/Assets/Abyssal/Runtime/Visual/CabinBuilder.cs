using System.Collections.Generic;
using UnityEngine;
using Abyssal.Core;

namespace Abyssal.Visual
{
    /// <summary>
    /// 程序化搭建整个控制舱。
    ///
    /// 舱室尺寸和元件布局是按第一人称构图倒推的：玩家站在 (0, 1.68, -0.55) 看向 +Z 时，
    /// 视野里应该有超过四十个可辨识的仪表、旋钮和指示灯，且视线中心恰好落在
    /// 主面板的上排大表盘上。这个密度是方案 A 画面说服力的来源——
    /// 参考对象 IRON NEST 的整个游戏都发生在一个同样密集的操作位里。
    ///
    /// 坐标约定：面板物体的正面朝其局部 -Z（Unity 内置 Quad 的法线方向）。
    /// </summary>
    public sealed class CabinBuilder
    {
        // 舱室尺寸，米。
        const float CabinWidth = 4.2f;
        const float CabinDepth = 5.0f;
        const float CabinHeight = 2.60f;

        /// <summary>玩家站位与眼高。摄像机初始位置就放在这里。</summary>
        public static readonly Vector3 EyePosition = new Vector3(0f, 1.68f, -0.55f);

        public GameObject Root { get; private set; }
        public Transform CameraAnchor { get; private set; }

        public readonly Dictionary<string, Gauge> Gauges = new Dictionary<string, Gauge>();
        public readonly Dictionary<string, Knob> Knobs = new Dictionary<string, Knob>();
        public readonly Dictionary<string, Toggle> Levers = new Dictionary<string, Toggle>();
        public readonly Dictionary<string, IndicatorLamp> Lamps = new Dictionary<string, IndicatorLamp>();

        public GameObject Build(string rootName = "ControlCabin")
        {
            Root = new GameObject(rootName);

            BuildShell();
            BuildMainConsole();
            BuildDeskProps();
            BuildSideConsoles();
            BuildOverheadPanel();
            BuildPorthole();
            BuildPipework();
            BuildCabling();
            BuildLighting();
            BuildCameraAnchor();

            return Root;
        }

        // ------------------------------------------------------------------ 舱体

        void BuildShell()
        {
            var shell = new GameObject("Shell");
            shell.transform.SetParent(Root.transform, false);

            Box(shell.transform, "Deck", new Vector3(0f, -0.05f, CabinDepth * 0.5f - 1.2f),
                Vector3.zero, new Vector3(CabinWidth, 0.10f, CabinDepth), MaterialLibrary.DeckPlate);

            Box(shell.transform, "Overhead", new Vector3(0f, CabinHeight + 0.05f, CabinDepth * 0.5f - 1.2f),
                Vector3.zero, new Vector3(CabinWidth, 0.10f, CabinDepth), MaterialLibrary.BulkheadSteel);

            Box(shell.transform, "BulkheadFore", new Vector3(0f, CabinHeight * 0.5f, CabinDepth - 1.2f),
                Vector3.zero, new Vector3(CabinWidth, CabinHeight, 0.10f), MaterialLibrary.BulkheadSteel);

            Box(shell.transform, "BulkheadAft", new Vector3(0f, CabinHeight * 0.5f, -1.25f),
                Vector3.zero, new Vector3(CabinWidth, CabinHeight, 0.10f), MaterialLibrary.BulkheadSteel);

            Box(shell.transform, "BulkheadPort", new Vector3(-CabinWidth * 0.5f, CabinHeight * 0.5f, CabinDepth * 0.5f - 1.2f),
                Vector3.zero, new Vector3(0.10f, CabinHeight, CabinDepth), MaterialLibrary.BulkheadSteel);

            Box(shell.transform, "BulkheadStbd", new Vector3(CabinWidth * 0.5f, CabinHeight * 0.5f, CabinDepth * 0.5f - 1.2f),
                Vector3.zero, new Vector3(0.10f, CabinHeight, CabinDepth), MaterialLibrary.BulkheadSteel);

            // 踢脚线上的警示带。工业空间里这种小面积高饱和色是重要的视觉锚点。
            Box(shell.transform, "HazardStrip", new Vector3(0f, 0.06f, 2.28f),
                Vector3.zero, new Vector3(CabinWidth - 0.2f, 0.12f, 0.02f), MaterialLibrary.Hazard);
        }

        // ------------------------------------------------------------------ 主控制台

        void BuildMainConsole()
        {
            var console = new GameObject("MainConsole");
            console.transform.SetParent(Root.transform, false);

            // 台体。前面板略微前倾，符合人站着操作的角度。
            Box(console.transform, "Pedestal", new Vector3(0f, 0.42f, 1.10f),
                Vector3.zero, new Vector3(2.80f, 0.84f, 0.62f), MaterialLibrary.ConsoleShell);

            Box(console.transform, "Worktop", new Vector3(0f, 0.855f, 0.92f),
                new Vector3(-6f, 0f, 0f), new Vector3(2.80f, 0.035f, 0.34f), MaterialLibrary.DarkPlastic);

            // 倾斜的主仪表面板。绕 X 轴正向旋转让法线朝向斜上方，正对玩家视线。
            var slanted = MakePanel(console.transform, "SlantPanel",
                new Vector3(0f, 1.24f, 1.06f), new Vector3(22f, 0f, 0f),
                new Vector2(2.72f, 0.74f), "DRILLING CONTROL", 3);

            PopulateSlantPanel(slanted);

            // 面板上方的垂直告警屏。
            var upright = MakePanel(console.transform, "UprightPanel",
                new Vector3(0f, 1.78f, 1.32f), new Vector3(4f, 0f, 0f),
                new Vector2(2.72f, 0.46f), "WELL STATUS", 5);

            PopulateUprightPanel(upright);
        }

        void PopulateSlantPanel(Transform panel)
        {
            // 上排：五个主仪表。这一排落在玩家视线正中，是整个画面的焦点。
            AddGauge(panel, "wob", new Vector2(-1.06f, 0.19f), 0.238f,
                Dial("WOB", "KN", 0f, 300f, danger: 0.80f, normalFrom: 0.35f, normalTo: 0.72f));
            AddGauge(panel, "rpm", new Vector2(-0.53f, 0.19f), 0.238f,
                Dial("RPM", "REV/MIN", 0f, 200f, danger: 0.88f, normalFrom: 0.40f, normalTo: 0.80f));
            AddGauge(panel, "torque", new Vector2(0f, 0.19f), 0.238f,
                Dial("TORQUE", "KN.M", 0f, 50f, danger: 0.76f, normalFrom: 0.15f, normalTo: 0.65f));
            AddGauge(panel, "ecd", new Vector2(0.53f, 0.19f), 0.238f,
                Dial("ECD", "KG/M3", 1000f, 2000f, danger: 2f, normalFrom: 0.18f, normalTo: 0.55f));
            AddGauge(panel, "pump", new Vector2(1.06f, 0.19f), 0.238f,
                Dial("PUMP", "L/S", 0f, 60f, danger: 2f, normalFrom: 0.42f, normalTo: 0.80f));

            // 中排：两块屏幕夹三个小仪表。
            InstrumentFactory.Screen(panel, new Vector2(-1.01f, -0.06f), new Vector2(0.40f, 0.27f),
                "MUD LOG", 11);
            AddGauge(panel, "rop", new Vector2(-0.50f, -0.05f), 0.166f,
                Dial("ROP", "M/H", 0f, 40f, danger: 2f, normalFrom: 0.25f, normalTo: 0.85f));
            AddGauge(panel, "pit", new Vector2(-0.17f, -0.05f), 0.166f,
                Dial("PIT VOL", "M3", 50f, 75f, danger: 2f, normalFrom: 0.40f, normalTo: 0.60f));
            AddGauge(panel, "temp", new Vector2(0.16f, -0.05f), 0.166f,
                Dial("BIT TEMP", "DEG C", 0f, 200f, danger: 0.55f, normalFrom: 0.20f, normalTo: 0.52f));
            AddGauge(panel, "wear", new Vector2(0.49f, -0.05f), 0.166f,
                Dial("BIT WEAR", "PERCENT", 0f, 100f, danger: 0.70f, normalFrom: 0f, normalTo: 0.55f));
            InstrumentFactory.Screen(panel, new Vector2(1.01f, -0.06f), new Vector2(0.40f, 0.27f),
                "ANNULUS", 23);

            // 下排：操作件。旋钮控制连续量，拨杆控制离散状态。
            AddKnob(panel, "wob", new Vector2(-1.12f, -0.27f), 0.088f, "WOB");
            AddKnob(panel, "rpm", new Vector2(-0.86f, -0.27f), 0.088f, "ROTARY");
            AddKnob(panel, "mud", new Vector2(-0.60f, -0.27f), 0.088f, "MUD WT");
            AddKnob(panel, "pump", new Vector2(-0.34f, -0.27f), 0.088f, "PUMP");
            AddKnob(panel, "choke", new Vector2(-0.08f, -0.27f), 0.088f, "CHOKE");

            AddLever(panel, "bit", new Vector2(0.24f, -0.26f), 0.19f, "ON BTM");
            AddLever(panel, "bop", new Vector2(0.54f, -0.26f), 0.19f, "BOP");
            AddLever(panel, "degas", new Vector2(0.84f, -0.26f), 0.19f, "DEGAS");

            AddLamp(panel, "run", new Vector2(1.12f, -0.20f), 0.042f,
                new Color(0.30f, 0.95f, 0.55f), "RUN");
            AddLamp(panel, "hold", new Vector2(1.12f, -0.33f), 0.042f,
                new Color(1.00f, 0.76f, 0.30f), "HOLD");
        }

        void PopulateUprightPanel(Transform panel)
        {
            // 一整排告警灯。绝大多数时间它们都是暗的，这让任何一个亮起都极其显眼。
            (string key, string label, Color color)[] alarms =
            {
                ("kick", "KICK", new Color(1.00f, 0.22f, 0.16f)),
                ("loss", "LOSS", new Color(1.00f, 0.55f, 0.12f)),
                ("gas", "GAS", new Color(1.00f, 0.16f, 0.40f)),
                ("torq", "TORQ", new Color(1.00f, 0.76f, 0.20f)),
                ("stuck", "STUCK", new Color(1.00f, 0.62f, 0.10f)),
                ("temp", "TEMP", new Color(1.00f, 0.42f, 0.14f)),
                ("wear", "BIT", new Color(0.98f, 0.85f, 0.30f)),
                ("pump", "PUMP", new Color(0.35f, 0.80f, 1.00f)),
                ("pwr", "PWR", new Color(0.30f, 0.95f, 0.55f)),
                ("comm", "COMM", new Color(0.35f, 0.80f, 1.00f)),
            };

            for (int i = 0; i < alarms.Length; i++)
            {
                float x = -1.17f + i * 0.26f;
                AddLamp(panel, alarms[i].key, new Vector2(x, 0.10f), 0.050f,
                        alarms[i].color, alarms[i].label);
            }

            InstrumentFactory.Screen(panel, new Vector2(-0.72f, -0.10f), new Vector2(0.62f, 0.19f),
                "DEPTH TREND", 37);
            InstrumentFactory.Screen(panel, new Vector2(0.72f, -0.10f), new Vector2(0.62f, 0.19f),
                "PORE PRESS", 41);
            AddGauge(panel, "depth", new Vector2(0f, -0.10f), 0.175f,
                Dial("DEPTH", "METRES", 1800f, 3400f, danger: 2f, normalFrom: 0f, normalTo: 0f));
        }

        // ------------------------------------------------------------------ 台面道具

        /// <summary>
        /// 台面上的纸、笔、记录板和杯子。
        ///
        /// 这些东西没有任何机械功能，但它们承担两件事：把画面下半部那片空台面填满，
        /// 以及告诉玩家「有人在这里上班」。一个只有仪表没有生活痕迹的控制舱
        /// 看起来像展厅样品，而不是一个上了十年夜班的工位。
        /// </summary>
        void BuildDeskProps()
        {
            var props = new GameObject("DeskProps");
            props.transform.SetParent(Root.transform, false);

            // 摊开的记录纸。稍微歪一点，没人会把纸摆得笔直。
            MeshShapes.Create("ChartPaper", props.transform, MeshShapes.Disc(4),
                MaterialLibrary.Surface("chart",
                    ProceduralTextures.ChartPaper(3), new Color(0.92f, 0.88f, 0.78f), 0.02f, 0.16f),
                new Vector3(-0.62f, 0.876f, 0.86f), new Vector3(84f, 0f, 47f),
                new Vector3(0.44f, 0.44f, 1f));

            Box(props.transform, "ChartSheet", new Vector3(-0.62f, 0.874f, 0.86f),
                new Vector3(-6f, 7f, 0f), new Vector3(0.36f, 0.002f, 0.27f),
                MaterialLibrary.Surface("chart2",
                    ProceduralTextures.ChartPaper(7), new Color(0.88f, 0.84f, 0.74f), 0.02f, 0.14f));

            // 三支彩色铅笔。红铅笔在真实钻井记录里专门用来标关键点。
            (Color color, float x, float yaw)[] pencils =
            {
                (new Color(0.72f, 0.16f, 0.12f), -0.30f, 12f),
                (new Color(0.16f, 0.22f, 0.48f), -0.26f, -6f),
                (new Color(0.20f, 0.42f, 0.22f), -0.22f, 21f),
            };
            foreach (var (color, x, yaw) in pencils)
            {
                Primitive(PrimitiveType.Cylinder, "Pencil", props.transform,
                    new Vector3(x, 0.884f, 0.80f), new Vector3(90f, yaw, 0f),
                    new Vector3(0.008f, 0.075f, 0.008f),
                    MaterialLibrary.Solid($"pencil_{color.r:F2}{color.g:F2}", color, 0.05f, 0.30f));
            }

            // 记录板，靠在控制台侧面。
            var clipboard = Box(props.transform, "Clipboard", new Vector3(0.72f, 0.905f, 0.84f),
                new Vector3(-18f, -9f, 0f), new Vector3(0.23f, 0.010f, 0.31f),
                MaterialLibrary.Solid("board", new Color(0.30f, 0.24f, 0.17f), 0.10f, 0.28f));
            Box(clipboard.transform, "Sheet", new Vector3(0f, 0.62f, -0.04f), Vector3.zero,
                new Vector3(0.90f, 0.30f, 0.86f),
                MaterialLibrary.Surface("clipsheet",
                    ProceduralTextures.ChartPaper(5, 256, 384), new Color(0.90f, 0.87f, 0.79f), 0.02f, 0.12f));
            Box(clipboard.transform, "Clip", new Vector3(0f, 1.20f, 0.36f), Vector3.zero,
                new Vector3(0.42f, 0.55f, 0.06f), MaterialLibrary.PipeSteel);

            // 搪瓷杯。杯口那圈高光是画面里少数几个圆形亮斑之一。
            var mug = Primitive(PrimitiveType.Cylinder, "Mug", props.transform,
                new Vector3(0.36f, 0.917f, 0.79f), Vector3.zero,
                new Vector3(0.078f, 0.048f, 0.078f),
                MaterialLibrary.Solid("mug", new Color(0.74f, 0.72f, 0.66f), 0.20f, 0.62f));
            Primitive(PrimitiveType.Cylinder, "MugRim", mug.transform,
                new Vector3(0f, 1.0f, 0f), Vector3.zero, new Vector3(1.02f, 0.06f, 1.02f),
                MaterialLibrary.Solid("mugrim", new Color(0.24f, 0.14f, 0.10f), 0.10f, 0.30f));

            // 烟灰缸和一只对讲机手柄，进一步把台面填满。
            Primitive(PrimitiveType.Cylinder, "Ashtray", props.transform,
                new Vector3(0.98f, 0.876f, 0.90f), Vector3.zero,
                new Vector3(0.10f, 0.012f, 0.10f),
                MaterialLibrary.Solid("ashtray", new Color(0.26f, 0.25f, 0.23f), 0.30f, 0.45f));

            var handset = Box(props.transform, "Handset", new Vector3(-1.02f, 0.895f, 0.82f),
                new Vector3(0f, 24f, 0f), new Vector3(0.055f, 0.045f, 0.20f),
                MaterialLibrary.DarkPlastic);
            Primitive(PrimitiveType.Cylinder, "Cord", handset.transform,
                new Vector3(0f, -0.2f, -0.9f), new Vector3(64f, 0f, 0f),
                new Vector3(0.22f, 1.4f, 0.22f), MaterialLibrary.DarkPlastic);
        }

        /// <summary>
        /// 沿舱壁和台体走的线缆。它们是提升细节密度最便宜的手段：
        /// 一根细圆柱几乎不占预算，但能在画面里制造大量边缘。
        /// </summary>
        void BuildCabling()
        {
            var cables = new GameObject("Cabling");
            cables.transform.SetParent(Root.transform, false);

            // 控制台底部的线束。
            for (int i = 0; i < 5; i++)
            {
                float x = -1.05f + i * 0.52f;
                Primitive(PrimitiveType.Cylinder, $"Drop{i}", cables.transform,
                    new Vector3(x, 0.22f, 1.44f), new Vector3(14f, 0f, 3f),
                    new Vector3(0.022f, 0.22f, 0.022f), MaterialLibrary.DarkPlastic);
            }

            // 舱壁上的水平走线。
            float[] heights = { 1.94f, 2.06f, 2.16f };
            for (int i = 0; i < heights.Length; i++)
            {
                Primitive(PrimitiveType.Cylinder, $"PortRun{i}", cables.transform,
                    new Vector3(-2.03f, heights[i], 0.90f), new Vector3(90f, 0f, 0f),
                    new Vector3(0.018f, 1.9f, 0.018f), MaterialLibrary.DarkPlastic);
                Primitive(PrimitiveType.Cylinder, $"StbdRun{i}", cables.transform,
                    new Vector3(2.03f, heights[i], 0.90f), new Vector3(90f, 0f, 0f),
                    new Vector3(0.018f, 1.9f, 0.018f), MaterialLibrary.DarkPlastic);
            }

            // 线缆卡箍。
            for (int i = 0; i < 6; i++)
            {
                float z = -0.6f + i * 0.62f;
                Primitive(PrimitiveType.Cube, $"PortClamp{i}", cables.transform,
                    new Vector3(-2.01f, 2.05f, z), Vector3.zero,
                    new Vector3(0.014f, 0.30f, 0.030f), MaterialLibrary.PipeSteel);
            }

            // 舱壁上的接线箱，给左右两侧的空墙一点内容。
            for (int i = 0; i < 2; i++)
            {
                int side = i == 0 ? -1 : 1;
                var box = Box(cables.transform, $"JunctionBox{i}",
                    new Vector3(side * 1.95f, 1.58f, 2.10f), new Vector3(0f, side * 8f, 0f),
                    new Vector3(0.10f, 0.34f, 0.26f), MaterialLibrary.ConsoleShell);
                Box(box.transform, "Latch", new Vector3(-0.55f, 0f, 0f), Vector3.zero,
                    new Vector3(0.30f, 0.14f, 0.20f), MaterialLibrary.RustAccent);
            }
        }

        // ------------------------------------------------------------------ 侧控制台

        void BuildSideConsoles()
        {
            BuildSideConsole(-1, "PortConsole", "MUD SYSTEM");
            BuildSideConsole(+1, "StbdConsole", "POWER PLANT");
        }

        void BuildSideConsole(int side, string name, string title)
        {
            var console = new GameObject(name);
            console.transform.SetParent(Root.transform, false);

            float x = side * 1.62f;
            float yaw = side * 34f;

            Box(console.transform, "Pedestal", new Vector3(x, 0.44f, 0.42f),
                new Vector3(0f, yaw, 0f), new Vector3(1.10f, 0.88f, 0.56f), MaterialLibrary.ConsoleShell);

            var panel = MakePanel(console.transform, "Panel",
                new Vector3(x, 1.16f, 0.36f), new Vector3(18f, yaw, 0f),
                new Vector2(1.04f, 0.62f), title, side + 7);

            string p = side < 0 ? "p" : "s";

            AddGauge(panel, $"{p}1", new Vector2(-0.30f, 0.14f), 0.165f,
                Dial(side < 0 ? "PIT LVL" : "GEN LOAD", side < 0 ? "M3" : "PERCENT",
                     0f, 100f, danger: 0.82f, normalFrom: 0.30f, normalTo: 0.70f));
            AddGauge(panel, $"{p}2", new Vector2(0.02f, 0.14f), 0.165f,
                Dial(side < 0 ? "VISC" : "BUS V", side < 0 ? "SEC" : "VOLT",
                     0f, 100f, danger: 2f, normalFrom: 0.35f, normalTo: 0.72f));
            AddGauge(panel, $"{p}3", new Vector2(0.32f, 0.10f), 0.128f,
                Dial(side < 0 ? "SAND" : "FUEL", "PERCENT",
                     0f, 100f, danger: 0.75f, normalFrom: 0f, normalTo: 0.60f));

            AddKnob(panel, $"{p}k1", new Vector2(-0.32f, -0.16f), 0.078f, side < 0 ? "SHAKER" : "THROTL");
            AddKnob(panel, $"{p}k2", new Vector2(-0.08f, -0.16f), 0.078f, side < 0 ? "DENSIF" : "EXCITE");
            AddLever(panel, $"{p}l1", new Vector2(0.18f, -0.15f), 0.17f, side < 0 ? "RECIRC" : "TIE");
            AddLamp(panel, $"{p}lamp", new Vector2(0.40f, -0.13f), 0.044f,
                    new Color(0.30f, 0.95f, 0.55f), "OK");
        }

        // ------------------------------------------------------------------ 其他

        void BuildOverheadPanel()
        {
            var panel = MakePanel(Root.transform, "OverheadPanel",
                new Vector3(0f, 2.28f, 0.86f), new Vector3(-38f, 0f, 0f),
                new Vector2(1.70f, 0.34f), "EMERGENCY", 13);

            AddLamp(panel, "esd", new Vector2(-0.52f, -0.02f), 0.062f,
                    new Color(1.00f, 0.18f, 0.12f), "ESD");
            AddLamp(panel, "muster", new Vector2(-0.17f, -0.02f), 0.062f,
                    new Color(1.00f, 0.62f, 0.12f), "MUSTER");
            AddLamp(panel, "fire", new Vector2(0.17f, -0.02f), 0.062f,
                    new Color(1.00f, 0.30f, 0.10f), "FIRE");
            AddLamp(panel, "abandon", new Vector2(0.52f, -0.02f), 0.062f,
                    new Color(1.00f, 0.10f, 0.30f), "ABANDON");
        }

        void BuildPorthole()
        {
            var port = new GameObject("Porthole");
            port.transform.SetParent(Root.transform, false);
            port.transform.localPosition = new Vector3(0f, 2.10f, 3.74f);

            // 舷窗外是彻底的黑。这里不放任何光源，深海本来就没有光。
            Primitive(PrimitiveType.Quad, "Void", port.transform,
                new Vector3(0f, 0f, -0.02f), Vector3.zero, new Vector3(0.86f, 0.86f, 1f),
                MaterialLibrary.Emissive("void", null, new Color(0.008f, 0.014f, 0.018f), 1f));

            Primitive(PrimitiveType.Cylinder, "Frame", port.transform,
                new Vector3(0f, 0f, 0.02f), new Vector3(90f, 0f, 0f),
                new Vector3(0.98f, 0.05f, 0.98f), MaterialLibrary.PipeSteel);

            // 舷窗周围的加强螺栓。
            for (int i = 0; i < 12; i++)
            {
                float a = i / 12f * Mathf.PI * 2f;
                Primitive(PrimitiveType.Cylinder, $"Bolt{i}", port.transform,
                    new Vector3(Mathf.Cos(a) * 0.53f, Mathf.Sin(a) * 0.53f, -0.03f),
                    new Vector3(90f, 0f, 0f), new Vector3(0.055f, 0.02f, 0.055f),
                    MaterialLibrary.RustAccent);
            }
        }

        void BuildPipework()
        {
            var pipes = new GameObject("Pipework");
            pipes.transform.SetParent(Root.transform, false);

            // 顶部管路。它们的作用是把天花板那片空白填满，
            // 同时给顶灯提供投影对象，让光影有层次。
            float[] offsets = { -1.55f, -1.22f, 1.18f, 1.48f, 1.72f };
            foreach (float x in offsets)
            {
                Primitive(PrimitiveType.Cylinder, $"Run{x:F2}", pipes.transform,
                    new Vector3(x, CabinHeight - 0.18f, 1.0f), new Vector3(90f, 0f, 0f),
                    new Vector3(0.10f, 2.4f, 0.10f), MaterialLibrary.PipeSteel);
            }

            for (int i = 0; i < 4; i++)
            {
                Primitive(PrimitiveType.Cylinder, $"Riser{i}", pipes.transform,
                    new Vector3(-1.92f, 0.9f + i * 0.05f, -0.4f + i * 0.9f),
                    new Vector3(0f, 0f, 0f), new Vector3(0.07f, 0.85f, 0.07f),
                    MaterialLibrary.PipeSteel);
            }

            // 舱壁上的阀门轮。
            for (int i = 0; i < 3; i++)
            {
                var wheel = Primitive(PrimitiveType.Cylinder, $"ValveWheel{i}", pipes.transform,
                    new Vector3(-2.02f, 1.42f, 0.15f + i * 0.86f), new Vector3(0f, 0f, 90f),
                    new Vector3(0.26f, 0.022f, 0.26f), MaterialLibrary.RustAccent);
                for (int s = 0; s < 4; s++)
                {
                    Primitive(PrimitiveType.Cube, $"Spoke{s}", wheel.transform,
                        Vector3.zero, new Vector3(0f, s * 45f, 0f),
                        new Vector3(1.9f, 0.7f, 0.16f), MaterialLibrary.RustAccent);
                }
            }
        }

        void BuildLighting()
        {
            var rig = new GameObject("Lighting");
            rig.transform.SetParent(Root.transform, false);

            // 环境光压到近乎为零。舱内所有的光都必须有一个看得见的来源，
            // 这是深海封闭空间可信度的基础。
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.030f, 0.024f, 0.017f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(0.040f, 0.030f, 0.021f);
            RenderSettings.fogDensity = 0.050f;

            // 唯一投影的主光：控制台上方的工作灯。
            // 色温压到钨丝灯的水平（偏橙），舱内所有的暖调都来自这一盏。
            var key = MakeLight(rig.transform, "WorkLamp", new Vector3(0f, 2.42f, 0.55f),
                LightType.Spot, new Color(1.00f, 0.70f, 0.42f), 11f, 7.5f);
            key.transform.localEulerAngles = new Vector3(62f, 0f, 0f);
            key.spotAngle = 96f;
            key.innerSpotAngle = 26f;
            key.shadows = LightShadows.Soft;
            key.shadowStrength = 0.78f;

            // 补光：舱后方一盏很弱的冷光。它的作用不是照明而是制造色温对比——
            // 有了这一点冷，主光的暖才读得出来。
            var fill = MakeLight(rig.transform, "AftFill", new Vector3(0f, 2.30f, -0.95f),
                LightType.Point, new Color(0.28f, 0.44f, 0.60f), 5.2f, 5.0f);
            fill.shadows = LightShadows.None;

            // 侧台的局部照明，让左右两块面板不至于完全隐没。
            MakeLight(rig.transform, "PortLamp", new Vector3(-1.62f, 1.86f, 0.30f),
                LightType.Point, new Color(1.00f, 0.62f, 0.34f), 3.0f, 2.6f).shadows = LightShadows.None;
            MakeLight(rig.transform, "StbdLamp", new Vector3(1.62f, 1.86f, 0.30f),
                LightType.Point, new Color(1.00f, 0.62f, 0.34f), 3.0f, 2.6f).shadows = LightShadows.None;

            // 仪表盘自身的辉光。让主面板附近的空气有一点被点亮的感觉。
            // 强度必须压得很低，否则近距离的点光衰减会在每个表盘上烧出一个白斑，
            // 把刻度整个洗掉。
            MakeLight(rig.transform, "PanelGlow", new Vector3(0f, 1.42f, 0.30f),
                LightType.Point, new Color(0.90f, 0.72f, 0.48f), 0.55f, 2.6f).shadows = LightShadows.None;

            // 台面照明。没有它整个画面下三分之一是死黑的，
            // 视觉重心会飘到上方的告警屏上，和参考构图对不上。
            var desk = MakeLight(rig.transform, "DeskLamp", new Vector3(0.05f, 1.74f, 0.02f),
                LightType.Spot, new Color(1.00f, 0.78f, 0.52f), 6.5f, 2.4f);
            desk.transform.localEulerAngles = new Vector3(84f, 0f, 0f);
            desk.spotAngle = 104f;
            desk.innerSpotAngle = 34f;
            desk.shadows = LightShadows.None;
        }

        void BuildCameraAnchor()
        {
            var anchor = new GameObject("CameraAnchor");
            anchor.transform.SetParent(Root.transform, false);
            anchor.transform.localPosition = EyePosition;
            anchor.transform.localEulerAngles = new Vector3(9f, 0f, 0f);
            CameraAnchor = anchor.transform;
        }

        // ------------------------------------------------------------------ 辅助

        ProceduralTextures.DialSpec Dial(string label, string unit, float min, float max,
                                         float danger, float normalFrom, float normalTo)
        {
            var spec = ProceduralTextures.DialSpec.Default(label, unit, min, max);
            spec.DangerFrom = danger;
            spec.NormalFrom = normalFrom;
            spec.NormalTo = normalTo;
            spec.MajorTicks = 6;
            spec.MinorPerMajor = 5;
            spec.Size = 384;
            return spec;
        }

        Transform MakePanel(Transform parent, string name, Vector3 localPos, Vector3 localEuler,
                            Vector2 size, string title, int seed)
        {
            var panel = new GameObject(name);
            panel.transform.SetParent(parent, false);
            panel.transform.localPosition = localPos;
            panel.transform.localEulerAngles = localEuler;

            int texW = Mathf.Clamp(Mathf.RoundToInt(size.x * 320f), 256, 1024);
            int texH = Mathf.Clamp(Mathf.RoundToInt(size.y * 320f), 128, 1024);

            Primitive(PrimitiveType.Quad, "Face", panel.transform,
                Vector3.zero, Vector3.zero, new Vector3(size.x, size.y, 1f),
                MaterialLibrary.Surface($"panel_{name}_{seed}",
                    ProceduralTextures.Panel(title, texW, texH, seed),
                    new Color(0.62f, 0.54f, 0.44f), 0.58f, 0.26f));

            // 面板背板，避免从侧面看穿。
            Primitive(PrimitiveType.Cube, "Backing", panel.transform,
                new Vector3(0f, 0f, 0.028f), Vector3.zero,
                new Vector3(size.x * 1.02f, size.y * 1.03f, 0.05f), MaterialLibrary.ConsoleShell);

            return panel.transform;
        }

        void AddGauge(Transform panel, string key, Vector2 uv, float diameter,
                      ProceduralTextures.DialSpec spec)
            => Gauges[key] = InstrumentFactory.Dial(panel, uv, diameter, spec);

        void AddKnob(Transform panel, string key, Vector2 uv, float diameter, string label)
            => Knobs[key] = InstrumentFactory.RotaryKnob(panel, uv, diameter, label);

        void AddLever(Transform panel, string key, Vector2 uv, float height, string label)
            => Levers[key] = InstrumentFactory.Lever(panel, uv, height, label);

        void AddLamp(Transform panel, string key, Vector2 uv, float diameter, Color color, string label)
            => Lamps[key] = InstrumentFactory.Button(panel, uv, diameter, color, label);

        static GameObject Primitive(PrimitiveType type, string name, Transform parent,
                                    Vector3 localPos, Vector3 localEuler, Vector3 localScale,
                                    Material material)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            var col = go.GetComponent<Collider>();
            if (col != null) Object.DestroyImmediate(col);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localEulerAngles = localEuler;
            go.transform.localScale = localScale;
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            return go;
        }

        static GameObject Box(Transform parent, string name, Vector3 pos, Vector3 euler,
                              Vector3 scale, Material material)
            => Primitive(PrimitiveType.Cube, name, parent, pos, euler, scale, material);

        static Light MakeLight(Transform parent, string name, Vector3 pos, LightType type,
                               Color color, float intensity, float range)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            var light = go.AddComponent<Light>();
            light.type = type;
            light.color = color;
            light.intensity = intensity;
            light.range = range;
            light.shadows = LightShadows.None;
            return light;
        }
    }
}
