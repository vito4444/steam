using System.Collections.Generic;
using Maner.Controls;
using UnityEngine;

namespace Maner.Cabin
{
    /// <summary>
    /// 舱内的杂物与装饰细节：指示灯排、铭牌、接线端子、线缆束、配电箱、工具箱、
    /// 挂在钩子上的安全帽与外套、墙上的图纸板。
    ///
    /// 这些东西一个都不参与玩法，但它们决定这间屋子看上去是不是有人在用。
    /// 一个只有仪表和旋钮的房间是渲染测试场景，加上油污、乱走的线缆和随手
    /// 搁在角落的工具箱之后，它才像一个上了三十年班的调度室。
    ///
    /// 全部由代码生成，控制在几千个三角形以内。
    /// </summary>
    public static class CabinClutter
    {
        public static void Build(Transform cabinRoot, Transform[] panelRoots, CabinMaterials mats)
        {
            for (int i = 0; i < panelRoots.Length; i++)
            {
                BuildPanelTrim((PanelId)i, panelRoots[i], mats);
            }

            BuildCableRuns(cabinRoot, mats);
            BuildWallFixtures(cabinRoot, mats);
            BuildFloorClutter(cabinRoot, mats);
        }

        /// <summary>面板上的非交互细节：指示灯排、铭牌、端子排、保险丝。</summary>
        static void BuildPanelTrim(PanelId id, Transform panel, CabinMaterials mats)
        {
            float w = ConsoleLayout.PanelWidth;
            float h = ConsoleLayout.PanelHeight;
            var rng = new System.Random(97 + (int)id * 13);

            var metal = new MeshBuilder();
            var lampsAmber = new MeshBuilder();
            var lampsOff = new MeshBuilder();

            // 顶部一排指示灯。亮灭交错，看着像一台正在工作的机器。
            for (int i = 0; i < 9; i++)
            {
                float x = 0.18f + i * (w - 0.36f) / 8f;
                float y = h - 0.055f;
                metal.AddCylinder(new Vector3(x, y, 0.010f), 0.017f, 0.020f, 8, Quaternion.Euler(90f, 0f, 0f));
                var target = rng.Next(100) < 34 ? lampsAmber : lampsOff;
                target.AddCylinder(new Vector3(x, y, 0.022f), 0.012f, 0.010f, 8, Quaternion.Euler(90f, 0f, 0f));
            }

            // 铭牌：一块带四颗铆钉的小金属牌。
            metal.AddBox(new Vector3(w - 0.16f, 0.055f, 0.008f), new Vector3(0.20f, 0.048f, 0.006f));
            for (int i = 0; i < 4; i++)
            {
                float bx = w - 0.16f + (i % 2 == 0 ? -0.085f : 0.085f);
                float by = 0.055f + (i < 2 ? 0.016f : -0.016f);
                metal.AddCylinder(new Vector3(bx, by, 0.013f), 0.004f, 0.004f, 5, Quaternion.Euler(90f, 0f, 0f));
            }

            // 接线端子排，压在面板下缘。
            for (int i = 0; i < 12; i++)
            {
                float x = 0.10f + i * 0.028f;
                metal.AddBox(new Vector3(x, 0.032f, 0.006f), new Vector3(0.016f, 0.030f, 0.010f));
            }

            // 保险丝座，三只一组。
            for (int i = 0; i < 3; i++)
            {
                float x = w * 0.5f - 0.06f + i * 0.06f;
                metal.AddCylinder(new Vector3(x, h - 0.14f, 0.016f), 0.014f, 0.032f, 8, Quaternion.Euler(90f, 0f, 0f));
            }

            Spawn($"Trim_{id}", panel, metal.ToMesh($"Trim_{id}"), mats.FrameSteel);
            Spawn($"LampsOn_{id}", panel, lampsAmber.ToMesh($"LampsOn_{id}"), mats.LampAmber);
            Spawn($"LampsOff_{id}", panel, lampsOff.ToMesh($"LampsOff_{id}"), mats.LampOff);
        }

        /// <summary>从控制台背后爬上墙的线缆束。让房间显得是被接起来的，而不是摆出来的。</summary>
        static void BuildCableRuns(Transform root, CabinMaterials mats)
        {
            var b = new MeshBuilder();
            float hw = CabinBuilder.RoomWidth * 0.5f;
            float hd = CabinBuilder.RoomDepth * 0.5f;

            // 三束线缆分别从三块面板后方引到墙角，再沿墙根走。
            float[] originX = { -1.55f, 0f, 1.55f };
            foreach (float x in originX)
            {
                float side = Mathf.Sign(x == 0f ? 1f : x);
                b.AddTube(new List<Vector3>
                {
                    new Vector3(x, 0.16f, 0.85f),
                    new Vector3(x * 1.25f, 0.10f, 1.25f),
                    new Vector3(side * (hw - 0.22f), 0.09f, hd - 0.35f),
                    new Vector3(side * (hw - 0.14f), 0.55f, hd - 0.20f),
                    new Vector3(side * (hw - 0.14f), CabinBuilder.RoomHeight - 0.62f, hd - 0.20f),
                }, 0.030f, 6);
            }

            // 沿墙根的一条主干管，带间隔的管卡。
            b.AddTube(new List<Vector3>
            {
                new Vector3(-hw + 0.18f, 0.14f, -hd + 0.30f),
                new Vector3(-hw + 0.18f, 0.14f, hd - 0.30f),
            }, 0.042f, 7);
            for (int i = 0; i < 5; i++)
            {
                b.AddBox(new Vector3(-hw + 0.18f, 0.14f, -hd + 0.5f + i * 0.6f), new Vector3(0.10f, 0.012f, 0.05f));
            }

            Spawn("CableRuns", root, b.ToMesh("Cabin_Cables"), mats.Bakelite);
        }

        /// <summary>墙上的配电箱、图纸板与挂钩。</summary>
        static void BuildWallFixtures(Transform root, CabinMaterials mats)
        {
            float hw = CabinBuilder.RoomWidth * 0.5f;
            float hd = CabinBuilder.RoomDepth * 0.5f;

            var steel = new MeshBuilder();

            // 左墙的配电箱，箱门半开。
            steel.AddBox(new Vector3(-hw + 0.16f, 1.55f, 0.35f), new Vector3(0.14f, 0.52f, 0.38f));
            steel.AddBox(new Vector3(-hw + 0.30f, 1.55f, 0.56f), new Vector3(0.02f, 0.50f, 0.36f),
                Quaternion.Euler(0f, -28f, 0f));
            for (int i = 0; i < 4; i++)
            {
                steel.AddCylinder(new Vector3(-hw + 0.24f, 1.72f - i * 0.10f, 0.35f), 0.016f, 0.05f, 6,
                    Quaternion.Euler(0f, 0f, 90f));
            }

            // 右墙的图纸板，几张纸钉在上面。
            steel.AddBox(new Vector3(hw - 0.14f, 1.58f, 0.10f), new Vector3(0.03f, 0.62f, 0.86f));
            Spawn("WallSteel", root, steel.ToMesh("Cabin_WallSteel"), mats.FrameSteel);

            var paper = new MeshBuilder();
            paper.AddFace(new Vector3(hw - 0.165f, 1.62f, 0.02f), 0.60f, 0.44f, Quaternion.Euler(0f, 90f, 0f));
            paper.AddFace(new Vector3(hw - 0.163f, 1.34f, 0.42f), 0.24f, 0.30f, Quaternion.Euler(0f, 90f, 4f));
            Spawn("WallPaper", root, paper.ToMesh("Cabin_WallPaper"), mats.GaugeFace);

            // 门边的挂钩，挂着安全帽与一件外套。
            var hook = new MeshBuilder();
            hook.AddBox(new Vector3(-hw + 0.16f, 1.72f, -hd + 0.55f), new Vector3(0.10f, 0.03f, 0.24f));
            for (int i = 0; i < 3; i++)
            {
                hook.AddCylinder(new Vector3(-hw + 0.21f, 1.68f, -hd + 0.46f + i * 0.09f), 0.010f, 0.06f, 5,
                    Quaternion.Euler(90f, 0f, 0f));
            }
            Spawn("Hooks", root, hook.ToMesh("Cabin_Hooks"), mats.FrameSteel);

            var helmet = new MeshBuilder();
            helmet.AddSphere(new Vector3(-hw + 0.24f, 1.60f, -hd + 0.46f), 0.11f, 6, 10);
            helmet.AddTorus(new Vector3(-hw + 0.24f, 1.53f, -hd + 0.46f), 0.115f, 0.018f, 12, 5);
            Spawn("Helmet", root, helmet.ToMesh("Cabin_Helmet"), mats.LampAmber);

            var coat = new MeshBuilder();
            coat.AddBox(new Vector3(-hw + 0.26f, 1.30f, -hd + 0.64f), new Vector3(0.14f, 0.62f, 0.26f),
                Quaternion.Euler(4f, 0f, 2f));
            Spawn("Coat", root, coat.ToMesh("Cabin_Coat"), mats.RustedIron);
        }

        /// <summary>地面杂物：工具箱、水桶、几段备用钢丝绳。</summary>
        static void BuildFloorClutter(Transform root, CabinMaterials mats)
        {
            float hw = CabinBuilder.RoomWidth * 0.5f;
            float hd = CabinBuilder.RoomDepth * 0.5f;

            var steel = new MeshBuilder();

            // 工具箱，箱盖略微翘着。
            steel.AddBox(new Vector3(hw - 0.55f, 0.11f, -hd + 0.62f), new Vector3(0.46f, 0.22f, 0.26f),
                Quaternion.Euler(0f, 12f, 0f));
            steel.AddBox(new Vector3(hw - 0.55f, 0.235f, -hd + 0.60f), new Vector3(0.46f, 0.03f, 0.26f),
                Quaternion.Euler(-7f, 12f, 0f));
            steel.AddCylinder(new Vector3(hw - 0.55f, 0.27f, -hd + 0.62f), 0.012f, 0.20f, 6,
                Quaternion.Euler(0f, 12f, 90f));

            // 水桶。
            steel.AddCylinder(new Vector3(-hw + 0.55f, 0.13f, -hd + 0.52f), 0.13f, 0.26f, 12, default, true, 1.12f);
            steel.AddTorus(new Vector3(-hw + 0.55f, 0.26f, -hd + 0.52f), 0.145f, 0.010f, 14, 5);

            Spawn("FloorSteel", root, steel.ToMesh("Cabin_FloorSteel"), mats.RustedIron);

            // 盘在地上的一卷备用钢丝绳。
            var rope = new MeshBuilder();
            for (int i = 0; i < 4; i++)
            {
                rope.AddTorus(new Vector3(hw - 1.15f, 0.035f + i * 0.028f, -hd + 0.50f),
                    0.20f - i * 0.018f, 0.016f, 16, 5);
            }
            Spawn("RopeCoil", root, rope.ToMesh("Cabin_RopeCoil"), mats.BrightSteel);
        }

        static void Spawn(string name, Transform parent, Mesh mesh, Material material)
        {
            if (mesh.vertexCount == 0)
            {
                return;
            }
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
        }
    }
}
