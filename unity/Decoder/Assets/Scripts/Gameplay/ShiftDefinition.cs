using System;
using System.Collections.Generic;
using Decoder.Signal;
using UnityEngine;

namespace Decoder.Gameplay
{
    /// <summary>威胁等级。玩家上报时必须自己判断，判错会有后果。</summary>
    public enum ThreatLevel
    {
        /// <summary>例行：日常通联，不需要惊动任何人。</summary>
        Routine = 0,

        /// <summary>注意：值得记录，但不必立即上报。</summary>
        Attention = 1,

        /// <summary>紧急：需要值班军官立刻知道。</summary>
        Urgent = 2,

        /// <summary>最高优先：越级直报，会惊动整条指挥链。</summary>
        Flash = 3,
    }

    /// <summary>信号的编码体制。决定玩家用哪套设备去解。</summary>
    public enum SignalKind
    {
        /// <summary>明码摩尔斯，直接听写字母。</summary>
        PlainMorse,

        /// <summary>中文电码，听写四位数字后查码表。</summary>
        ChineseTelegraph,

        /// <summary>一次性密码本加密的数字电文。</summary>
        OneTimePad,
    }

    /// <summary>
    /// 一条待截收的信号。这是内容设计的最小单位，
    /// 一个班次由若干条组成，其中一条是主线，其余是支线或干扰。
    /// </summary>
    [Serializable]
    public sealed class TransmissionEntry
    {
        public string callsign = "UNKNOWN";
        public float frequencyKHz = 7000f;
        public SignalKind kind = SignalKind.PlainMorse;

        /// <summary>发报速度，每分钟字数。新手班次用 10 到 12，后期升到 18 以上。</summary>
        public float wordsPerMinute = 12f;

        /// <summary>信号强度，影响能否听清。远方弱台会低于 0.5。</summary>
        public float strength = 1f;

        /// <summary>发报起始偏移，让不同电台错开而不是同时开始。</summary>
        public float startOffsetSeconds;

        /// <summary>
        /// 电文的明文内容。中文电码信号填汉字，明码信号填英文，
        /// 由 BuildStation 负责转成实际发送的字符流。
        /// </summary>
        public string plainText = string.Empty;

        /// <summary>这条信号的正确威胁等级。玩家判错等级会有后果。</summary>
        public ThreatLevel correctLevel = ThreatLevel.Routine;

        /// <summary>是否是本班次的主线信号。主线漏收会推进剧情的失败分支。</summary>
        public bool isPrimary;

        [Header("一次性密码本")]
        [Tooltip("密码本册子的种子。同一册子在整个战役里保持不变")]
        public int padBookSeed = 19851104;

        [Tooltip("这条电文用的页码。玩家要从报头读出来，翻到对应页才解得开")]
        public int padPage;

        /// <summary>解出后向玩家展示的提示，用于串联剧情，可留空。</summary>
        public string debriefNote = string.Empty;

        /// <summary>
        /// 实际在空中传输的字符流。中文电码要先转成数字，
        /// 因为电报线路上传的是数字而不是汉字。
        /// </summary>
        public string ResolveAirText(ChineseTelegraphCode telegraph)
        {
            switch (kind)
            {
                case SignalKind.ChineseTelegraph:
                    if (telegraph == null)
                    {
                        throw new ArgumentNullException(nameof(telegraph),
                            "中文电码信号需要码表才能转成数字流");
                    }

                    return ChineseTelegraphCode.ToDigitStream(telegraph.EncodeText(plainText));

                case SignalKind.OneTimePad:
                {
                    if (telegraph == null)
                    {
                        throw new ArgumentNullException(nameof(telegraph),
                            "加密电文的明文是中文，需要码表才能先转成数字");
                    }

                    // 先把汉字转成电码数字，再整体加密。顺序不能反：
                    // 密码本作用在数字流上，而不是作用在汉字上。
                    var plainDigits = ChineseTelegraphCode.ToDigitStream(
                        telegraph.EncodeText(plainText));
                    return OneTimePad.BuildTransmission(plainDigits, padBookSeed, padPage);
                }

                case SignalKind.PlainMorse:
                default:
                    return plainText.ToUpperInvariant();
            }
        }

        public SignalSynthesizer.Station BuildStation(ChineseTelegraphCode telegraph)
        {
            return new SignalSynthesizer.Station(
                callsign,
                frequencyKHz,
                ResolveAirText(telegraph),
                wordsPerMinute,
                strength)
            {
                StartOffsetSeconds = startOffsetSeconds,
            };
        }
    }

    /// <summary>一个夜班。</summary>
    [Serializable]
    public sealed class ShiftDefinition
    {
        public string shiftId = "shift-01";
        public string title = "第一班";

        /// <summary>班次内的日期，用于密码本页码和剧情呈现。</summary>
        public string inGameDate = "1985-11-04";

        /// <summary>噪声种子。固定下来，读档回到同一班次听到的底噪一致。</summary>
        public int noiseSeed = 20260813;

        public float bandLowKHz = 6800f;
        public float bandHighKHz = 7200f;

        public List<TransmissionEntry> transmissions = new List<TransmissionEntry>();

        public TransmissionEntry Primary
        {
            get
            {
                foreach (var entry in transmissions)
                {
                    if (entry.isPrimary)
                    {
                        return entry;
                    }
                }

                return null;
            }
        }

        public List<SignalSynthesizer.Station> BuildStations(ChineseTelegraphCode telegraph)
        {
            var stations = new List<SignalSynthesizer.Station>(transmissions.Count);
            foreach (var entry in transmissions)
            {
                stations.Add(entry.BuildStation(telegraph));
            }

            return stations;
        }
    }
}
