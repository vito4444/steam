using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Maner.Sim;

namespace Maner.Shift
{
    /// <summary>
    /// 跨班次持久化的全部状态。设备磨损是这里最重要的一项：
    /// 今天烧了泵，明天就得带着一台坏泵干活，这是本作后果系统的骨架。
    /// </summary>
    public sealed class CampaignSave
    {
        public int Version = 1;
        public int ShiftsCompleted;
        public int TotalOrdersCompleted;
        public int TotalOrdersFailed;
        public int TotalIncidents;
        public int CrewMorale;
        public double GeneratorWear;
        public double BrakePadWear;
        public double MotorWear;
        public double FanBearingWear;
        public double FuelRemaining = 1.0;
        public string LastGrade = "-";
        public readonly List<string> UnlockedEvents = new List<string>();

        public void AbsorbSettlement(ShiftSettlement s, int morale, IEnumerable<string> events)
        {
            ShiftsCompleted++;
            TotalOrdersCompleted += s.CompletedOrders;
            TotalOrdersFailed += s.TimedOutOrders;
            if (s.WorstIncident != IncidentSeverity.None)
            {
                TotalIncidents++;
            }

            // 磨损只增不减：设备不会自己变好。
            GeneratorWear = Math.Max(GeneratorWear, s.GeneratorWear);
            BrakePadWear = Math.Max(BrakePadWear, s.BrakePadWear);
            MotorWear = Math.Max(MotorWear, s.MotorWear);
            FanBearingWear = Math.Max(FanBearingWear, s.FanBearingWear);
            FuelRemaining = s.FuelRemaining;
            CrewMorale += morale;
            LastGrade = s.Grade.ToString();

            foreach (var e in events)
            {
                if (!UnlockedEvents.Contains(e))
                {
                    UnlockedEvents.Add(e);
                }
            }
        }
    }

    /// <summary>
    /// 存档读写。刻意用一个自己实现的扁平文本格式，而不是 JsonUtility：
    /// 这样存档逻辑不依赖 Unity，可以在纯 C# 单元测试里完整覆盖，
    /// 而「存了又读回来是否一模一样」正是最需要被测试守住的一条。
    /// </summary>
    public static class SaveSystem
    {
        public const string FileName = "maner_campaign.sav";

        public static string Serialize(CampaignSave save)
        {
            var sb = new StringBuilder();
            var c = CultureInfo.InvariantCulture;
            sb.Append("maner-save\n");
            sb.Append($"version={save.Version}\n");
            sb.Append($"shifts={save.ShiftsCompleted}\n");
            sb.Append($"ordersDone={save.TotalOrdersCompleted}\n");
            sb.Append($"ordersFailed={save.TotalOrdersFailed}\n");
            sb.Append($"incidents={save.TotalIncidents}\n");
            sb.Append($"morale={save.CrewMorale}\n");
            sb.Append($"wearGenerator={save.GeneratorWear.ToString("R", c)}\n");
            sb.Append($"wearBrake={save.BrakePadWear.ToString("R", c)}\n");
            sb.Append($"wearMotor={save.MotorWear.ToString("R", c)}\n");
            sb.Append($"wearFan={save.FanBearingWear.ToString("R", c)}\n");
            sb.Append($"fuel={save.FuelRemaining.ToString("R", c)}\n");
            sb.Append($"grade={save.LastGrade}\n");
            foreach (var e in save.UnlockedEvents)
            {
                sb.Append($"event={e}\n");
            }
            return sb.ToString();
        }

        public static CampaignSave Deserialize(string text)
        {
            var save = new CampaignSave();
            save.UnlockedEvents.Clear();
            var c = CultureInfo.InvariantCulture;

            foreach (var rawLine in text.Split('\n'))
            {
                string line = rawLine.Trim();
                if (line.Length == 0 || line == "maner-save")
                {
                    continue;
                }

                int eq = line.IndexOf('=');
                if (eq <= 0)
                {
                    continue;
                }

                string key = line.Substring(0, eq);
                string value = line.Substring(eq + 1);

                switch (key)
                {
                    case "version": save.Version = ParseInt(value); break;
                    case "shifts": save.ShiftsCompleted = ParseInt(value); break;
                    case "ordersDone": save.TotalOrdersCompleted = ParseInt(value); break;
                    case "ordersFailed": save.TotalOrdersFailed = ParseInt(value); break;
                    case "incidents": save.TotalIncidents = ParseInt(value); break;
                    case "morale": save.CrewMorale = ParseInt(value); break;
                    case "wearGenerator": save.GeneratorWear = ParseDouble(value, c); break;
                    case "wearBrake": save.BrakePadWear = ParseDouble(value, c); break;
                    case "wearMotor": save.MotorWear = ParseDouble(value, c); break;
                    case "wearFan": save.FanBearingWear = ParseDouble(value, c); break;
                    case "fuel": save.FuelRemaining = ParseDouble(value, c); break;
                    case "grade": save.LastGrade = value; break;
                    case "event": save.UnlockedEvents.Add(value); break;
                }
            }

            return save;
        }

        static int ParseInt(string s) => int.TryParse(s, out int v) ? v : 0;

        static double ParseDouble(string s, IFormatProvider provider) =>
            double.TryParse(s, NumberStyles.Float, provider, out double v) ? v : 0.0;

        public static void Write(string directory, CampaignSave save)
        {
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, FileName);
            string tmp = path + ".tmp";
            // 先写临时文件再替换，避免写到一半掉电留下半截存档。
            File.WriteAllText(tmp, Serialize(save));
            if (File.Exists(path))
            {
                File.Delete(path);
            }
            File.Move(tmp, path);
        }

        public static CampaignSave Read(string directory)
        {
            string path = Path.Combine(directory, FileName);
            return File.Exists(path) ? Deserialize(File.ReadAllText(path)) : new CampaignSave();
        }

        public static bool Exists(string directory) => File.Exists(Path.Combine(directory, FileName));
    }
}
