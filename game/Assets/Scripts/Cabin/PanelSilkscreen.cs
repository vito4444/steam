using System.Collections.Generic;
using Maner.Controls;
using UnityEngine;

namespace Maner.Cabin
{
    /// <summary>
    /// 面板丝印贴图的运行时生成：分区框线、控件标签、阀轮刻度、磨损痕迹。
    ///
    /// 丝印全部用英文短码而不是中文，一是点阵字库做不了中文，二是这批设备
    /// 在世界观里就是外购的，面板上印的本来就是外文。中文界面由 UI 层负责。
    /// </summary>
    public static class PanelSilkscreen
    {
        const int TextureWidth = 1024;
        const int TextureHeight = 808;

        static readonly Dictionary<ControlId, string> Silk = new Dictionary<ControlId, string>
        {
            [ControlId.GeneratorMaster] = "GEN MASTER",
            [ControlId.FuelValve] = "FUEL",
            [ControlId.CoolantPump] = "COOLANT",
            [ControlId.Excitation] = "EXCITER",
            [ControlId.BreakerHoist] = "HOIST",
            [ControlId.BreakerVentilation] = "VENT",
            [ControlId.BreakerLighting] = "LIGHT",
            [ControlId.BreakerAuxiliary] = "AUX",
            [ControlId.BatteryTie] = "BATT TIE",
            [ControlId.ResetOverload] = "RESET",

            [ControlId.MainFanSwitch] = "MAIN FAN",
            [ControlId.FanSpeedWheel] = "FAN SPEED",
            [ControlId.Damper1] = "DAMPER 1",
            [ControlId.Damper2] = "DAMPER 2",
            [ControlId.Damper3] = "DAMPER 3",
            [ControlId.GasDrainagePump] = "DRAIN",
            [ControlId.ReverseAirflow] = "REVERSE",
            [ControlId.SilenceGasAlarm] = "SILENCE",

            [ControlId.HoistPower] = "HOIST PWR",
            [ControlId.Throttle] = "THROTTLE",
            [ControlId.Brake] = "BRAKE",
            [ControlId.CageLock] = "CAGE LOCK",
            [ControlId.Direction] = "UP STOP DN",
            [ControlId.CageLight] = "CAGE LAMP",
            [ControlId.RopeSpeedTrim] = "TRIM",
            [ControlId.SignalBell] = "BELL",
            [ControlId.ConfirmBottomSignal] = "CONFIRM",
            [ControlId.ResetOverspeed] = "OVSP RST",
        };

        static readonly Dictionary<PanelId, string> PanelTitle = new Dictionary<PanelId, string>
        {
            [PanelId.Power] = "POWER GENERATION",
            [PanelId.Ventilation] = "VENTILATION",
            [PanelId.Hoist] = "WINDING ENGINE",
        };

        public static Texture2D Create(PanelId panel, int seed)
        {
            var buffer = new Color32[TextureWidth * TextureHeight];
            var rng = new System.Random(seed);

            var baseColor = new Color32(52, 62, 57, 255);
            var inkLight = new Color32(196, 200, 186, 255);
            var inkDim = new Color32(140, 146, 134, 255);
            var lineColor = new Color32(96, 106, 98, 255);

            // 底色加低频斑驳，模拟喷漆不均与长年油污。
            for (int y = 0; y < TextureHeight; y++)
            {
                for (int x = 0; x < TextureWidth; x++)
                {
                    float n = Mathf.PerlinNoise(x * 0.0075f + seed * 3.1f, y * 0.0075f + seed * 1.7f);
                    float n2 = Mathf.PerlinNoise(x * 0.045f, y * 0.045f) * 0.35f;
                    float shade = 0.80f + n * 0.30f + n2 * 0.16f;
                    // 下缘更脏，符合手常年摸到的位置。
                    shade *= Mathf.Lerp(0.86f, 1.03f, y / (float)TextureHeight);
                    buffer[y * TextureWidth + x] = new Color32(
                        (byte)Mathf.Clamp(baseColor.r * shade, 0, 255),
                        (byte)Mathf.Clamp(baseColor.g * shade, 0, 255),
                        (byte)Mathf.Clamp(baseColor.b * shade, 0, 255),
                        255);
                }
            }

            // 分区横线，与几何上的横梁对齐。
            DrawHLine(buffer, 0, TextureWidth, Mathf.RoundToInt(TextureHeight * (1f - 0.735f)), 2, lineColor);
            DrawHLine(buffer, 0, TextureWidth, Mathf.RoundToInt(TextureHeight * (1f - 0.285f)), 2, lineColor);

            // 面板标题印在左上角。
            BitmapFont.Draw(buffer, TextureWidth, TextureHeight, PanelTitle[panel], 18, 14, 3, inkDim);
            BitmapFont.Draw(buffer, TextureWidth, TextureHeight, $"TYPE SHU-{(int)panel + 61}   1974", 18, 44, 2, inkDim);

            foreach (var def in ConsoleLayout.OnPanel(panel))
            {
                if (!Silk.TryGetValue(def.Id, out string label))
                {
                    continue;
                }

                // 面板局部坐标（米）转贴图像素。贴图 V 轴与面板 Y 轴反向。
                int cx = Mathf.RoundToInt(def.X / ConsoleLayout.PanelWidth * TextureWidth);
                int cy = Mathf.RoundToInt((1f - def.Y / ConsoleLayout.PanelHeight) * TextureHeight);
                int halfSize = Mathf.RoundToInt(def.Size / ConsoleLayout.PanelWidth * TextureWidth * 0.5f);

                int scale = def.Size > 0.16f ? 2 : 1;
                int labelY = cy + halfSize + 6;
                BitmapFont.DrawCentered(buffer, TextureWidth, TextureHeight, label, cx, labelY, scale, inkLight);

                // 连续控件周围印一圈 0..10 刻度，让玩家能读出开度。
                if (def.IsContinuous)
                {
                    DrawDialScale(buffer, cx, cy, halfSize + 10, inkDim);
                }

                // 拨杆两端印上位置标记。
                if (def.Kind == ControlKind.ToggleLever && def.Detents == 2)
                {
                    BitmapFont.DrawCentered(buffer, TextureWidth, TextureHeight, "I", cx, cy - halfSize - 20, 1, inkDim);
                    BitmapFont.DrawCentered(buffer, TextureWidth, TextureHeight, "O", cx, cy + halfSize + 2, 1, inkDim);
                }
            }

            // 几道随机刮痕。
            for (int i = 0; i < 26; i++)
            {
                int x0 = rng.Next(TextureWidth);
                int y0 = rng.Next(TextureHeight);
                int x1 = x0 + rng.Next(-70, 70);
                int y1 = y0 + rng.Next(-12, 12);
                var scratch = new Color32(
                    (byte)(baseColor.r + 26), (byte)(baseColor.g + 28), (byte)(baseColor.b + 24), 255);
                DrawLine(buffer, x0, y0, x1, y1, scratch);
            }

            // 位图缓冲区按「左上角为原点、Y 向下」的习惯绘制，而 Unity 纹理坐标的原点
            // 在左下角。不翻转的话整张丝印会上下颠倒——表现为文字看着像被镜像，
            // 而面板标题会跑到面板底部。
            FlipVertically(buffer);

            var tex = new Texture2D(TextureWidth, TextureHeight, TextureFormat.RGBA32, true)
            {
                name = $"T_Panel_{panel}",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                anisoLevel = 8,
            };
            tex.SetPixels32(buffer);
            tex.Apply(true, false);
            return tex;
        }

        static void FlipVertically(Color32[] buffer)
        {
            var row = new Color32[TextureWidth];
            for (int y = 0; y < TextureHeight / 2; y++)
            {
                int top = y * TextureWidth;
                int bottom = (TextureHeight - 1 - y) * TextureWidth;
                System.Array.Copy(buffer, top, row, 0, TextureWidth);
                System.Array.Copy(buffer, bottom, buffer, top, TextureWidth);
                System.Array.Copy(row, 0, buffer, bottom, TextureWidth);
            }
        }

        static void DrawDialScale(Color32[] buffer, int cx, int cy, int radius, Color32 color)
        {
            for (int i = 0; i <= 10; i++)
            {
                float t = i / 10f;
                float deg = 135f - t * 270f;
                float rad = deg * Mathf.Deg2Rad;
                float dx = -Mathf.Sin(rad);
                float dy = -Mathf.Cos(rad);
                int len = i % 5 == 0 ? 9 : 5;
                DrawLine(buffer,
                    Mathf.RoundToInt(cx + dx * radius), Mathf.RoundToInt(cy + dy * radius),
                    Mathf.RoundToInt(cx + dx * (radius + len)), Mathf.RoundToInt(cy + dy * (radius + len)),
                    color);
            }
        }

        static void DrawHLine(Color32[] buffer, int x0, int x1, int y, int thickness, Color32 color)
        {
            for (int t = 0; t < thickness; t++)
            {
                int yy = y + t;
                if (yy < 0 || yy >= TextureHeight)
                {
                    continue;
                }
                for (int x = x0; x < x1; x++)
                {
                    buffer[yy * TextureWidth + x] = color;
                }
            }
        }

        static void DrawLine(Color32[] buffer, int x0, int y0, int x1, int y1, Color32 color)
        {
            int steps = Mathf.Max(Mathf.Abs(x1 - x0), Mathf.Abs(y1 - y0)) + 1;
            for (int i = 0; i <= steps; i++)
            {
                float t = (float)i / steps;
                int x = Mathf.RoundToInt(Mathf.Lerp(x0, x1, t));
                int y = Mathf.RoundToInt(Mathf.Lerp(y0, y1, t));
                if (x >= 0 && x < TextureWidth && y >= 0 && y < TextureHeight)
                {
                    buffer[y * TextureWidth + x] = color;
                }
            }
        }
    }
}
