using System.Collections.Generic;
using Maner.Controls;
using Maner.Shift;
using UnityEngine;

namespace Maner.Cabin
{
    /// <summary>
    /// 控制台上的实体信息装置：绿字 CRT 监视器、电报纸带、任务卡、电话。
    ///
    /// 这些是本作的主要界面。指令不是弹在屏幕角上的一行字，而是从电报机里
    /// 吐出来的一条纸带；深度不是进度条，而是 CRT 上一条缓慢下滑的曲线。
    /// 玩家要低头去读它们，这个动作本身就是沉浸感的一部分。
    /// </summary>
    public sealed class CabinFixtures : MonoBehaviour
    {
        const int CrtWidth = 320;
        const int CrtHeight = 240;
        const int TapeWidth = 512;
        const int TapeHeight = 128;
        const int CardWidth = 384;
        const int CardHeight = 256;

        Texture2D crtTexture;
        Texture2D tapeTexture;
        Texture2D cardTexture;
        Color32[] crtBuffer;
        readonly List<float> depthHistory = new List<float>(256);

        CabinRuntime runtime;
        float refreshTimer;
        string lastOrderText = "";

        public void Build(CabinRuntime cabinRuntime, CabinBuilder builder)
        {
            runtime = cabinRuntime;

            crtTexture = NewTexture(CrtWidth, CrtHeight, "T_Crt");
            tapeTexture = NewTexture(TapeWidth, TapeHeight, "T_Tape");
            cardTexture = NewTexture(CardWidth, CardHeight, "T_OrderCard");
            crtBuffer = new Color32[CrtWidth * CrtHeight];

            // 中央面板的上方空档，正好摆一台监视器。
            var center = builder.PanelRoots[1];
            BuildCrt(center, builder.Materials);
            BuildTeleprinter(center, builder.Materials);
            BuildOrderCard(builder.PanelRoots[0], builder.Materials);
            BuildTelephone(builder.PanelRoots[2], builder.Materials);

            RedrawCrt();
            RedrawTape();
            RedrawCard();
        }

        static Texture2D NewTexture(int w, int h, string name) => new Texture2D(w, h, TextureFormat.RGBA32, false)
        {
            name = name,
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
        };

        GameObject Spawn(string name, Transform parent, Mesh mesh, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            return go;
        }

        // ————————————————— CRT 监视器 —————————————————
        void BuildCrt(Transform panel, CabinMaterials mats)
        {
            float w = ConsoleLayout.PanelWidth;
            var holder = new GameObject("CrtMonitor");
            holder.transform.SetParent(panel, false);
            holder.transform.localPosition = new Vector3(w * 0.5f, 1.30f, 0.02f);

            var shell = new MeshBuilder();
            shell.AddBox(new Vector3(0f, 0f, -0.10f), new Vector3(0.40f, 0.34f, 0.22f));
            shell.AddBox(new Vector3(0f, 0f, 0.005f), new Vector3(0.42f, 0.36f, 0.03f));
            Spawn("Shell", holder.transform, shell.ToMesh("Crt_Shell"), mats.Bakelite);

            var screen = new MeshBuilder();
            screen.AddFace(new Vector3(0f, 0f, 0.022f), 0.33f, 0.25f, default, true);
            var screenMat = new Material(mats.CrtScreen) { name = "M_CrtLive" };
            screenMat.SetTexture("_BaseMap", crtTexture);
            screenMat.SetColor("_BaseColor", Color.white);
            CabinMaterials.EnsureUnitTiling(screenMat);
            Spawn("Screen", holder.transform, screen.ToMesh("Crt_Screen"), screenMat);
        }

        // ————————————————— 电报机与纸带 —————————————————
        void BuildTeleprinter(Transform panel, CabinMaterials mats)
        {
            float w = ConsoleLayout.PanelWidth;
            var holder = new GameObject("Teleprinter");
            holder.transform.SetParent(panel, false);
            holder.transform.localPosition = new Vector3(w * 0.5f - 0.46f, 1.24f, 0.02f);

            var body = new MeshBuilder();
            body.AddBox(new Vector3(0f, 0f, -0.06f), new Vector3(0.26f, 0.14f, 0.16f));
            body.AddCylinder(new Vector3(-0.07f, 0.02f, -0.02f), 0.035f, 0.10f, 12, Quaternion.Euler(0f, 90f, 0f));
            Spawn("Body", holder.transform, body.ToMesh("Tele_Body"), mats.FrameSteel);

            // 从机器口吐出来的一条纸带，略微下垂。
            var tape = new MeshBuilder();
            tape.AddFace(new Vector3(0f, -0.14f, 0.012f), 0.30f, 0.075f, Quaternion.Euler(-16f, 0f, 0f), true);
            var tapeMat = new Material(mats.GaugeFace) { name = "M_Tape" };
            tapeMat.SetTexture("_BaseMap", tapeTexture);
            tapeMat.SetColor("_BaseColor", new Color(0.72f, 0.68f, 0.58f));
            CabinMaterials.EnsureUnitTiling(tapeMat);
            Spawn("Tape", holder.transform, tape.ToMesh("Tele_Tape"), tapeMat);
        }

        // ————————————————— 任务卡 —————————————————
        void BuildOrderCard(Transform panel, CabinMaterials mats)
        {
            var holder = new GameObject("OrderCard");
            holder.transform.SetParent(panel, false);
            holder.transform.localPosition = new Vector3(0.30f, 1.26f, 0.02f);
            holder.transform.localRotation = Quaternion.Euler(0f, 0f, -3.5f);

            var board = new MeshBuilder();
            board.AddBox(new Vector3(0f, 0f, -0.006f), new Vector3(0.30f, 0.21f, 0.012f));
            Spawn("Board", holder.transform, board.ToMesh("Card_Board"), mats.FrameSteel);

            var card = new MeshBuilder();
            card.AddFace(new Vector3(0f, 0f, 0.004f), 0.276f, 0.186f, default, true);
            var cardMat = new Material(mats.GaugeFace) { name = "M_OrderCard" };
            cardMat.SetTexture("_BaseMap", cardTexture);
            cardMat.SetColor("_BaseColor", new Color(0.68f, 0.63f, 0.52f));
            CabinMaterials.EnsureUnitTiling(cardMat);
            Spawn("Paper", holder.transform, card.ToMesh("Card_Paper"), cardMat);

            var clip = new MeshBuilder();
            clip.AddBox(new Vector3(0f, 0.10f, 0.012f), new Vector3(0.05f, 0.03f, 0.016f));
            Spawn("Clip", holder.transform, clip.ToMesh("Card_Clip"), mats.Brass);
        }

        // ————————————————— 电话 —————————————————
        void BuildTelephone(Transform panel, CabinMaterials mats)
        {
            var holder = new GameObject("Telephone");
            holder.transform.SetParent(panel, false);
            holder.transform.localPosition = new Vector3(0.22f, 1.22f, 0.02f);

            var body = new MeshBuilder();
            body.AddBox(new Vector3(0f, 0f, -0.07f), new Vector3(0.20f, 0.24f, 0.16f));
            body.AddCylinder(new Vector3(0f, 0.10f, -0.02f), 0.055f, 0.02f, 14, Quaternion.Euler(90f, 0f, 0f));
            Spawn("Body", holder.transform, body.ToMesh("Phone_Body"), mats.Bakelite);

            // 听筒挂在机身上方的托架里。
            var handset = new MeshBuilder();
            handset.AddBox(new Vector3(0f, 0.15f, 0.01f), new Vector3(0.19f, 0.045f, 0.05f));
            handset.AddCylinder(new Vector3(-0.085f, 0.13f, 0.01f), 0.036f, 0.045f, 12);
            handset.AddCylinder(new Vector3(0.085f, 0.13f, 0.01f), 0.036f, 0.045f, 12);
            Spawn("Handset", holder.transform, handset.ToMesh("Phone_Handset"), mats.Bakelite);

            var bell = new MeshBuilder();
            bell.AddSphere(new Vector3(0f, -0.02f, 0.06f), 0.032f, 7, 11);
            var lamp = Spawn("Bell", holder.transform, bell.ToMesh("Phone_Bell"), mats.LampOff);
            bellRenderer = lamp.GetComponent<Renderer>();
        }

        Renderer bellRenderer;

        void Update()
        {
            if (runtime == null || runtime.Simulation == null)
            {
                return;
            }

            // CRT 每秒刷新数次即可，重画整张贴图不便宜。
            refreshTimer += Time.deltaTime;
            if (refreshTimer >= 0.2f)
            {
                refreshTimer = 0f;
                depthHistory.Add((float)runtime.Simulation.Hoist.Depth);
                if (depthHistory.Count > CrtWidth / 2)
                {
                    depthHistory.RemoveAt(0);
                }
                RedrawCrt();
            }

            string orderText = runtime.Director.Current?.Text ?? "";
            if (orderText != lastOrderText)
            {
                lastOrderText = orderText;
                RedrawTape();
                RedrawCard();
            }

            if (bellRenderer != null && runtime.Comms != null)
            {
                bool ringing = runtime.Comms.State == CallState.Ringing;
                bool flash = ringing && Mathf.Repeat(Time.time, 0.7f) < 0.4f;
                bellRenderer.sharedMaterial = flash
                    ? runtime.Builder.Materials.LampRed
                    : runtime.Builder.Materials.LampOff;
            }
        }

        // ————————————————— 贴图绘制 —————————————————
        void RedrawCrt()
        {
            var bg = new Color32(6, 14, 8, 255);
            var grid = new Color32(14, 46, 20, 255);
            var trace = new Color32(96, 255, 128, 255);
            var dim = new Color32(40, 140, 60, 255);

            for (int i = 0; i < crtBuffer.Length; i++)
            {
                crtBuffer[i] = bg;
            }

            for (int x = 0; x < CrtWidth; x += 32)
            {
                for (int y = 0; y < CrtHeight; y++)
                {
                    crtBuffer[y * CrtWidth + x] = grid;
                }
            }
            for (int y = 0; y < CrtHeight; y += 24)
            {
                for (int x = 0; x < CrtWidth; x++)
                {
                    crtBuffer[y * CrtWidth + x] = grid;
                }
            }

            // 深度曲线：向下为深，与真实井筒方向一致。
            for (int i = 0; i < depthHistory.Count; i++)
            {
                float t = depthHistory[i] / 2000f;
                int x = 8 + i * 2;
                int y = Mathf.Clamp(Mathf.RoundToInt(t * (CrtHeight - 60)) + 34, 0, CrtHeight - 1);
                if (x >= CrtWidth)
                {
                    break;
                }
                for (int k = -1; k <= 1; k++)
                {
                    int yy = Mathf.Clamp(y + k, 0, CrtHeight - 1);
                    crtBuffer[yy * CrtWidth + x] = trace;
                    if (x + 1 < CrtWidth)
                    {
                        crtBuffer[yy * CrtWidth + x + 1] = trace;
                    }
                }
            }

            var sim = runtime != null ? runtime.Simulation : null;
            if (sim != null)
            {
                BitmapFont.Draw(crtBuffer, CrtWidth, CrtHeight, "SHAFT PROFILE", 8, 6, 2, trace);
                BitmapFont.Draw(crtBuffer, CrtWidth, CrtHeight, $"DEPTH {sim.Hoist.Depth,7:0.0} M", 8, CrtHeight - 34, 2, dim);
                BitmapFont.Draw(crtBuffer, CrtWidth, CrtHeight, $"RATE  {sim.Hoist.Velocity,7:0.00} M/S", 8, CrtHeight - 18, 2, dim);
            }

            // 扫描线，让它看起来像块真正的显像管。
            for (int y = 0; y < CrtHeight; y += 2)
            {
                int row = y * CrtWidth;
                for (int x = 0; x < CrtWidth; x++)
                {
                    var c = crtBuffer[row + x];
                    crtBuffer[row + x] = new Color32(
                        (byte)(c.r * 0.62f), (byte)(c.g * 0.62f), (byte)(c.b * 0.62f), 255);
                }
            }

            FlipVertical(crtBuffer, CrtWidth, CrtHeight);
            crtTexture.SetPixels32(crtBuffer);
            crtTexture.Apply(false);
        }

        void RedrawTape()
        {
            var buffer = new Color32[TapeWidth * TapeHeight];
            var paper = new Color32(214, 206, 184, 255);
            var ink = new Color32(38, 34, 30, 255);

            for (int i = 0; i < buffer.Length; i++)
            {
                buffer[i] = paper;
            }

            // 纸带两侧的送纸孔。
            for (int x = 10; x < TapeWidth; x += 26)
            {
                for (int dy = -3; dy <= 3; dy++)
                {
                    for (int dx = -3; dx <= 3; dx++)
                    {
                        if (dx * dx + dy * dy > 9)
                        {
                            continue;
                        }
                        Plot(buffer, TapeWidth, TapeHeight, x + dx, 12 + dy, ink);
                        Plot(buffer, TapeWidth, TapeHeight, x + dx, TapeHeight - 12 + dy, ink);
                    }
                }
            }

            var director = runtime != null ? runtime.Director : null;
            string line = director?.Current != null
                ? $"ORDER {director.Current.Id:00} - {OrderCode(director.Current)}"
                : "NO PENDING ORDER";
            BitmapFont.Draw(buffer, TapeWidth, TapeHeight, line, 18, 46, 4, ink);

            FlipVertical(buffer, TapeWidth, TapeHeight);
            tapeTexture.SetPixels32(buffer);
            tapeTexture.Apply(false);
        }

        void RedrawCard()
        {
            var buffer = new Color32[CardWidth * CardHeight];
            var paper = new Color32(206, 194, 166, 255);
            var ink = new Color32(42, 36, 30, 255);
            var red = new Color32(150, 44, 32, 255);

            for (int y = 0; y < CardHeight; y++)
            {
                for (int x = 0; x < CardWidth; x++)
                {
                    float n = Mathf.PerlinNoise(x * 0.03f, y * 0.03f) * 0.12f + 0.94f;
                    buffer[y * CardWidth + x] = new Color32(
                        (byte)Mathf.Min(255, paper.r * n), (byte)Mathf.Min(255, paper.g * n),
                        (byte)Mathf.Min(255, paper.b * n), 255);
                }
            }

            for (int x = 12; x < CardWidth - 12; x++)
            {
                Plot(buffer, CardWidth, CardHeight, x, 14, ink);
                Plot(buffer, CardWidth, CardHeight, x, CardHeight - 14, ink);
            }

            var director = runtime != null ? runtime.Director : null;
            var order = director?.Current;

            BitmapFont.Draw(buffer, CardWidth, CardHeight, "SHIFT ORDER", 20, 26, 3, ink);
            BitmapFont.Draw(buffer, CardWidth, CardHeight, "SHAFT 7 - DISPATCH", 20, 52, 2, ink);

            if (order != null)
            {
                BitmapFont.Draw(buffer, CardWidth, CardHeight, $"NO. {order.Id:00}", 20, 92, 3, red);
                BitmapFont.Draw(buffer, CardWidth, CardHeight, OrderCode(order), 20, 128, 3, ink);
                BitmapFont.Draw(buffer, CardWidth, CardHeight, OrderDetail(order), 20, 162, 2, ink);
                BitmapFont.Draw(buffer, CardWidth, CardHeight,
                    $"{director.CurrentIndex + 1} OF {director.Orders.Count}", 20, 200, 2, ink);
            }
            else
            {
                BitmapFont.Draw(buffer, CardWidth, CardHeight, "SHIFT COMPLETE", 20, 120, 3, ink);
            }

            FlipVertical(buffer, CardWidth, CardHeight);
            cardTexture.SetPixels32(buffer);
            cardTexture.Apply(false);
        }

        static string OrderCode(Order order) => order.Kind switch
        {
            OrderKind.EnergizeBus => "ENERGIZE BUS",
            OrderKind.EstablishAirflow => "ESTABLISH AIR",
            OrderKind.LowerCage => "LOWER CAGE",
            OrderKind.RaiseCage => "RAISE CAGE",
            OrderKind.SuppressGas => "PURGE METHANE",
            _ => "CLEAR FAULT",
        };

        static string OrderDetail(Order order) => order.Kind switch
        {
            OrderKind.EnergizeBus => $"TARGET {order.Target:0} V  +/-{order.Tolerance:0}",
            OrderKind.EstablishAirflow => $"MIN {order.Target:0} M3/S",
            OrderKind.LowerCage => $"TO {order.Target:0} M",
            OrderKind.RaiseCage => $"TO {order.Target:0} M",
            OrderKind.SuppressGas => $"BELOW {order.Target:0.00} PCT",
            _ => "RESTORE POWER",
        };

        static void Plot(Color32[] buffer, int w, int h, int x, int y, Color32 c)
        {
            if (x >= 0 && x < w && y >= 0 && y < h)
            {
                buffer[y * w + x] = c;
            }
        }

        /// <summary>位图缓冲区按 Y 向下绘制，纹理坐标原点在左下，上传前翻转。</summary>
        static void FlipVertical(Color32[] buffer, int w, int h)
        {
            var row = new Color32[w];
            for (int y = 0; y < h / 2; y++)
            {
                int top = y * w;
                int bottom = (h - 1 - y) * w;
                System.Array.Copy(buffer, top, row, 0, w);
                System.Array.Copy(buffer, bottom, buffer, top, w);
                System.Array.Copy(row, 0, buffer, bottom, w);
            }
        }
    }
}
