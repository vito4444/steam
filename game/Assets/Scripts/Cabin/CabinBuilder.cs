using System.Collections.Generic;
using Maner.Controls;
using UnityEngine;

namespace Maner.Cabin
{
    /// <summary>
    /// 程序化搭建整座控制舱。玩家整局游戏都待在这一间屋子里，
    /// 所以全部渲染预算都压在这 5 米见方的空间上。
    /// 房间、控制台、面板、控件、管线、舷窗全部由代码生成，零建模资产。
    /// </summary>
    public sealed class CabinBuilder
    {
        public const float RoomWidth = 5.2f;
        public const float RoomDepth = 3.6f;
        public const float RoomHeight = 2.72f;

        /// <summary>三块面板的水平朝向角，负值在左。</summary>
        static readonly float[] PanelYaw = { -40f, 0f, 40f };
        const float PanelTilt = 14f;
        const float PanelArcRadius = 1.62f;
        // 盘面中心高度。1.36 时面板顶边正好卡在通往舷窗的视线上，抬眼看不见井筒；
        // 降到 1.20 之后视线越过盘面，而且这个高度本来就更接近真实控制台的人体工学——
        // 盘面在腰到胸之间，操作员低头就能看全。
        const float PanelCenterHeight = 1.20f;
        static readonly Vector3 PanelArcPivot = new Vector3(0f, 0f, -0.58f);

        public readonly Dictionary<ControlId, ControlVisual> Visuals = new Dictionary<ControlId, ControlVisual>();
        public CabinMaterials Materials { get; private set; }
        public Transform Root { get; private set; }
        public Transform[] PanelRoots { get; private set; }

        public GameObject Build(Transform parent = null)
        {
            Materials = new CabinMaterials();

            var root = new GameObject("CabinRoot");
            if (parent != null)
            {
                root.transform.SetParent(parent, false);
            }
            Root = root.transform;

            BuildShell(root.transform);
            BuildPortholeWall(root.transform);
            BuildPipework(root.transform);

            PanelRoots = new Transform[PanelYaw.Length];
            for (int i = 0; i < PanelYaw.Length; i++)
            {
                PanelRoots[i] = BuildPanel(root.transform, (PanelId)i, PanelYaw[i]);
            }

            CabinClutter.Build(root.transform, PanelRoots, Materials);

            foreach (var def in ConsoleLayout.All)
            {
                var visual = ControlMeshFactory.Build(def, PanelRoots[(int)def.Panel], Materials);
                Visuals[def.Id] = visual;
                if (def.IsInteractive)
                {
                    visual.AttachAudio(def);
                }
                visual.SnapValue(def.Kind == ControlKind.Gauge ? 0.0 : def.DefaultValue);
            }

            return root;
        }

        // ————————————————— 房间壳体 —————————————————
        void BuildShell(Transform parent)
        {
            var b = new MeshBuilder();
            float hw = RoomWidth * 0.5f;
            float hd = RoomDepth * 0.5f;

            b.AddBox(new Vector3(0f, -0.06f, 0f), new Vector3(RoomWidth, 0.12f, RoomDepth), default, 1.6f);
            b.AddBox(new Vector3(0f, RoomHeight + 0.06f, 0f), new Vector3(RoomWidth, 0.12f, RoomDepth), default, 1.2f);
            b.AddBox(new Vector3(-hw - 0.06f, RoomHeight * 0.5f, 0f), new Vector3(0.12f, RoomHeight, RoomDepth), default, 1.2f);
            b.AddBox(new Vector3(hw + 0.06f, RoomHeight * 0.5f, 0f), new Vector3(0.12f, RoomHeight, RoomDepth), default, 1.2f);
            b.AddBox(new Vector3(0f, RoomHeight * 0.5f, -hd - 0.06f), new Vector3(RoomWidth, RoomHeight, 0.12f), default, 1.2f);

            Spawn("Shell", parent, b.ToMesh("Cabin_Shell"), Materials.Concrete);

            // 地面钢格栅的横梁，靠密集的细长条制造纹理密度。
            var grate = new MeshBuilder();
            for (int i = -8; i <= 8; i++)
            {
                grate.AddBox(new Vector3(i * 0.3f, 0.012f, 0f), new Vector3(0.045f, 0.024f, RoomDepth - 0.1f));
            }
            for (int i = -5; i <= 5; i++)
            {
                grate.AddBox(new Vector3(0f, 0.006f, i * 0.32f), new Vector3(RoomWidth - 0.1f, 0.012f, 0.035f));
            }
            Spawn("FloorGrate", parent, grate.ToMesh("Cabin_FloorGrate"), Materials.RustedIron);

            // 墙裙与踢脚，打断大面积平墙。
            var trim = new MeshBuilder();
            trim.AddBox(new Vector3(0f, 0.09f, -hd + 0.04f), new Vector3(RoomWidth, 0.18f, 0.06f));
            trim.AddBox(new Vector3(-hw + 0.04f, 0.09f, 0f), new Vector3(0.06f, 0.18f, RoomDepth));
            trim.AddBox(new Vector3(hw - 0.04f, 0.09f, 0f), new Vector3(0.06f, 0.18f, RoomDepth));
            Spawn("Trim", parent, trim.ToMesh("Cabin_Trim"), Materials.FrameSteel);
        }

        // ————————————————— 舷窗墙 —————————————————
        // 面板后方是一面厚钢板墙，中央开一扇圆形舷窗，窗外是井筒。
        // 玩家看不见井下发生了什么，只能通过仪表和电话推断——这是本作恐惧感的来源。
        void BuildPortholeWall(Transform parent)
        {
            float wallZ = RoomDepth * 0.5f + 0.06f;
            float holeRadius = 0.46f;
            // 舷窗抬到面板上沿之上，操作员抬眼就能看见井筒，低头就是仪表。
            const float holeY = 2.04f;
            var b = new MeshBuilder();

            // 用四块板围出中间的洞，再用环形法兰盖住方洞与圆窗之间的转角。
            float hw = RoomWidth * 0.5f;
            float topBandHeight = RoomHeight - (holeY + holeRadius);
            b.AddBox(new Vector3(0f, RoomHeight - topBandHeight * 0.5f, wallZ), new Vector3(RoomWidth, topBandHeight, 0.12f));
            b.AddBox(new Vector3(0f, (holeY - holeRadius) * 0.5f, wallZ), new Vector3(RoomWidth, holeY - holeRadius, 0.12f));
            b.AddBox(new Vector3(-hw * 0.5f - holeRadius * 0.5f, holeY, wallZ), new Vector3(RoomWidth - holeRadius * 2f, holeRadius * 2f, 0.12f));
            b.AddBox(new Vector3(hw * 0.5f + holeRadius * 0.5f, holeY, wallZ), new Vector3(RoomWidth - holeRadius * 2f, holeRadius * 2f, 0.12f));
            Spawn("PortholeWall", parent, b.ToMesh("Cabin_PortholeWall"), Materials.FrameSteel);

            var flange = new MeshBuilder();
            flange.AddTorus(new Vector3(0f, holeY, wallZ - 0.06f), holeRadius + 0.03f, 0.055f, 30, 8);
            for (int i = 0; i < 12; i++)
            {
                float a = i / 12f * Mathf.PI * 2f;
                flange.AddCylinder(
                    new Vector3(Mathf.Cos(a) * (holeRadius + 0.11f), holeY + Mathf.Sin(a) * (holeRadius + 0.11f), wallZ - 0.07f),
                    0.022f, 0.03f, 6, Quaternion.Euler(90f, 0f, 0f));
            }
            Spawn("PortholeFlange", parent, flange.ToMesh("Cabin_PortholeFlange"), Materials.Brass);

            // 窗外：一段向下延伸的井筒内壁。它几乎全黑，只有边缘被舱内漏出的光扫到一点，
            // 这正是要的效果——你知道那里有很深的东西，但你看不清。
            var shaft = new MeshBuilder();
            for (int i = 0; i < 12; i++)
            {
                float y = holeY + 0.9f - i * 0.62f;
                shaft.AddTorus(new Vector3(0f, y, wallZ + 1.7f), 1.05f, 0.075f, 18, 6, Quaternion.Euler(90f, 0f, 0f));
            }
            for (int i = 0; i < 8; i++)
            {
                float a = i / 8f * Mathf.PI * 2f;
                shaft.AddBox(
                    new Vector3(Mathf.Cos(a) * 1.05f, holeY - 2.5f, wallZ + 1.7f + Mathf.Sin(a) * 1.05f),
                    new Vector3(0.07f, 7.4f, 0.07f));
            }
            Spawn("ShaftInterior", parent, shaft.ToMesh("Cabin_ShaftInterior"), Materials.RustedIron);
        }

        // ————————————————— 管线 —————————————————
        void BuildPipework(Transform parent)
        {
            var b = new MeshBuilder();
            float hw = RoomWidth * 0.5f;
            float hd = RoomDepth * 0.5f;

            b.AddTube(new List<Vector3>
            {
                new Vector3(-hw + 0.14f, RoomHeight - 0.22f, -hd + 0.2f),
                new Vector3(-hw + 0.14f, RoomHeight - 0.22f, hd - 0.2f),
                new Vector3(hw - 0.5f, RoomHeight - 0.22f, hd - 0.2f),
            }, 0.055f, 8);

            b.AddTube(new List<Vector3>
            {
                new Vector3(-hw + 0.30f, RoomHeight - 0.36f, -hd + 0.2f),
                new Vector3(-hw + 0.30f, RoomHeight - 0.36f, hd - 0.35f),
            }, 0.038f, 7);

            b.AddTube(new List<Vector3>
            {
                new Vector3(hw - 0.16f, 0.30f, -hd + 0.15f),
                new Vector3(hw - 0.16f, RoomHeight - 0.5f, -hd + 0.15f),
                new Vector3(hw - 0.16f, RoomHeight - 0.5f, hd - 0.4f),
            }, 0.062f, 8);

            // 管卡
            for (int i = 0; i < 6; i++)
            {
                float z = -hd + 0.4f + i * 0.55f;
                b.AddBox(new Vector3(-hw + 0.14f, RoomHeight - 0.14f, z), new Vector3(0.14f, 0.05f, 0.05f));
            }

            Spawn("Pipework", parent, b.ToMesh("Cabin_Pipework"), Materials.RustedIron);
        }

        // ————————————————— 面板与控制台 —————————————————
        Transform BuildPanel(Transform parent, PanelId id, float yaw)
        {
            var rotation = Quaternion.Euler(-PanelTilt, yaw + 180f, 0f);
            Vector3 center = PanelArcPivot
                             + Quaternion.Euler(0f, yaw, 0f) * (Vector3.forward * PanelArcRadius)
                             + Vector3.up * PanelCenterHeight;

            var panelRoot = new GameObject($"Panel_{id}");
            panelRoot.transform.SetParent(parent, false);
            panelRoot.transform.rotation = rotation;
            // ConsoleLayout 的坐标原点在面板左下角，这里把 root 摆到左下角的世界位置。
            panelRoot.transform.position = center - rotation * new Vector3(ConsoleLayout.PanelWidth * 0.5f, ConsoleLayout.PanelHeight * 0.5f, 0f);

            float w = ConsoleLayout.PanelWidth;
            float h = ConsoleLayout.PanelHeight;

            var b = new MeshBuilder();
            b.AddBox(new Vector3(w * 0.5f, h * 0.5f, -0.045f), new Vector3(w, h, 0.07f), default, 2.2f);
            // 面板四周的加强边框
            b.AddBox(new Vector3(w * 0.5f, h + 0.035f, -0.02f), new Vector3(w + 0.07f, 0.07f, 0.10f));
            b.AddBox(new Vector3(w * 0.5f, -0.035f, -0.02f), new Vector3(w + 0.07f, 0.07f, 0.10f));
            b.AddBox(new Vector3(-0.035f, h * 0.5f, -0.02f), new Vector3(0.07f, h + 0.14f, 0.10f));
            b.AddBox(new Vector3(w + 0.035f, h * 0.5f, -0.02f), new Vector3(0.07f, h + 0.14f, 0.10f));
            // 分区横梁，把面板切成上中下三块，视觉上更像真设备
            b.AddBox(new Vector3(w * 0.5f, h * 0.735f, 0.004f), new Vector3(w, 0.018f, 0.014f));
            b.AddBox(new Vector3(w * 0.5f, h * 0.285f, 0.004f), new Vector3(w, 0.018f, 0.014f));
            b.AddScrewGrid(new Vector3(0.03f, 0.03f, 0f), Vector3.right, Vector3.up, Vector3.forward,
                w - 0.06f, h - 0.06f, 2, 2, 0.012f);
            Spawn($"PanelBody_{id}", panelRoot.transform, b.ToMesh($"Panel_{id}_Body"), Materials.FrameSteel);

            // 面板正面单独一片 UV 0..1 的平面，用来承载运行时生成的丝印贴图。
            var faceBuilder = new MeshBuilder();
            faceBuilder.AddFace(new Vector3(w * 0.5f, h * 0.5f, 0.020f), w, h, default, true);
            bool panelDebug = false;
            foreach (var a in System.Environment.GetCommandLineArgs())
            {
                if (a == "-manerPanelDebug") { panelDebug = true; }
            }
            var faceMaterial = new Material(panelDebug ? Materials.LampRed : Materials.PanelSteel) { name = $"M_PanelFace_{id}" };
            if (!panelDebug)
            {
                faceMaterial.SetTexture("_BaseMap", PanelSilkscreen.Create(id, (int)id + 7));
                CabinMaterials.EnsureUnitTiling(faceMaterial);
                faceMaterial.SetColor("_BaseColor", Color.white);
            }
            Spawn($"PanelFace_{id}", panelRoot.transform, faceBuilder.ToMesh($"Panel_{id}_Face"), faceMaterial);

            // 面板下方的台面与柜体
            var desk = new MeshBuilder();
            desk.AddBox(new Vector3(w * 0.5f, -0.10f, 0.20f), new Vector3(w + 0.06f, 0.05f, 0.44f));
            desk.AddBox(new Vector3(w * 0.5f, -0.52f, -0.02f), new Vector3(w + 0.02f, 0.80f, 0.30f));
            desk.AddBox(new Vector3(w * 0.5f, -0.94f, 0.02f), new Vector3(w + 0.06f, 0.06f, 0.34f));

            // 面板上方的仪表架。真实控制室里显示器与电报机都架在盘面之上，
            // 操作员低头读表、抬头读屏，两层信息各有各的位置。
            // 中央面板刻意不建：正前方要留出通往舷窗的视线，抬眼就是井筒。
            if (id != PanelId.Ventilation)
            {
                desk.AddBox(new Vector3(w * 0.5f, h + 0.24f, -0.13f), new Vector3(w + 0.05f, 0.48f, 0.30f));
                desk.AddBox(new Vector3(w * 0.5f, h + 0.50f, -0.02f), new Vector3(w + 0.09f, 0.05f, 0.34f));
            }
            Spawn($"Desk_{id}", panelRoot.transform, desk.ToMesh($"Panel_{id}_Desk"), Materials.FrameSteel);

            return panelRoot.transform;
        }

        GameObject Spawn(string name, Transform parent, Mesh mesh, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = material;
            return go;
        }
    }
}
