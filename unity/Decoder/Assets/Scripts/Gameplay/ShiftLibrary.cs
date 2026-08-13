using Decoder.Signal;
namespace Decoder.Gameplay
{
    /// <summary>
    /// 内置班次内容。目前只有第一班，用于垂直切片验证整条玩法链路。
    /// 后续班次会改为从数据文件加载，届时这里只保留第一班作为教学关。
    /// </summary>
    public static class ShiftLibrary
    {
        /// <summary>
        /// 常驻电台的发报人手法。
        ///
        /// 集中定义是为了让同一个人在不同班次里保持同一双手——
        /// 玩家要花几个班次熟悉这些节奏，之后才可能察觉出不对。
        /// 数字来自真实手键发报的常见范围：教科书的三倍划长，
        /// 实际落在二点四到三点八之间；老手的抖动大约半成到一成。
        /// </summary>
        private static OperatorFist M08Operator => new OperatorFist
        {
            // 划拖得偏长，字之间赶得紧。是个熟手，但有自己的习惯。
            dahRatio = 3.35f,
            charGapRatio = 2.55f,
            wordGapRatio = 6.8f,
            jitter = 0.055f,
        };

        /// <summary>顶替 M08 的那个人。手法和本人差得很远，但呼号一模一样。</summary>
        private static OperatorFist M08Impostor => new OperatorFist
        {
            dahRatio = 2.7f,
            charGapRatio = 3.5f,
            wordGapRatio = 7.4f,
            jitter = 0.13f,
        };

        private static OperatorFist M14Operator => new OperatorFist
        {
            dahRatio = 3.05f,
            charGapRatio = 3.15f,
            wordGapRatio = 7.1f,
            jitter = 0.075f,
        };

        /// <summary>另一个例行台。发得慢而规整，像在照本宣科。</summary>
        private static OperatorFist D14Operator => new OperatorFist
        {
            dahRatio = 2.85f,
            charGapRatio = 3.35f,
            wordGapRatio = 7.2f,
            jitter = 0.065f,
        };

        /// <summary>业余台。手很不稳，一听就知道是自己在家练的。</summary>
        private static OperatorFist AmateurOperator => new OperatorFist
        {
            dahRatio = 3.6f,
            charGapRatio = 3.9f,
            wordGapRatio = 7.6f,
            jitter = 0.19f,
        };

        /// <summary>
        /// 第一班。设计意图：
        ///
        /// 频段里同时有三个台，其中两个是无关的日常通联，一个是主线。
        /// 玩家必须自己判断哪个值得上报——这是整个游戏反复要玩家做的判断，
        /// 第一班就把它摆出来，但代价很轻。
        ///
        /// 主线用中文电码而不是明码，是为了在第一分钟就让玩家撞上这个机制：
        /// 听到的是数字，但数字不是内容，要查表才变成字。
        /// </summary>
        public static ShiftDefinition FirstShift()
        {
            var shift = new ShiftDefinition
            {
                shiftId = "shift-01",
                title = "第一班 · 交接",
                inGameDate = "1985-11-04",
                noiseSeed = 19851104,
                bandLowKHz = 6800f,
                bandHighKHz = 7200f,
            };

            // 主线：中文电码。语速压到 9 WPM，第一次接触摩尔斯的玩家也跟得上。
            shift.transmissions.Add(new TransmissionEntry
            {
                callsign = "M08",
                fist = M08Operator,
                fistSeed = 101,
                frequencyKHz = 6955f,
                kind = SignalKind.ChineseTelegraph,
                wordsPerMinute = 9f,
                strength = 0.95f,
                startOffsetSeconds = 1.5f,
                plainText = "北风已起",
                correctLevel = ThreatLevel.Attention,
                isPrimary = true,
                debriefNote = "值班军官记下了这条。他没有解释「北风」是什么。",
            });

            // 干扰一：邻国业余电台的例行呼叫。听着像回事，其实什么都不是。
            shift.transmissions.Add(new TransmissionEntry
            {
                callsign = "R7X",
                fist = AmateurOperator,
                fistSeed = 301,
                frequencyKHz = 7042f,
                kind = SignalKind.PlainMorse,
                wordsPerMinute = 14f,
                strength = 0.8f,
                plainText = "CQ CQ DE R7X K",
                correctLevel = ThreatLevel.Routine,
                debriefNote = "业余电台的例行呼叫。上报它只会浪费别人的时间。",
            });

            // 干扰二：远处的弱台，强度低到需要仔细对准才听得清。
            shift.transmissions.Add(new TransmissionEntry
            {
                callsign = "D14",
                fist = D14Operator,
                fistSeed = 501,
                frequencyKHz = 7128f,
                kind = SignalKind.PlainMorse,
                wordsPerMinute = 16f,
                strength = 0.45f,
                startOffsetSeconds = 4f,
                plainText = "QRT QRT DE D14",
                correctLevel = ThreatLevel.Routine,
                debriefNote = "对方停止发报了。",
            });

            return shift;
        }

        /// <summary>
        /// 第二班。设计意图：
        ///
        /// 引入一次性密码本。玩家第一次遇到"抄下来的数字查不到字"的情况——
        /// 因为它们是密文。报头的三位数字是页码指示，翻到密码本对应那一页
        /// 逐位相减才是真正的电码。游戏不会明说这一点，
        /// 但值班日志里会有前一班留下的一句话把玩家推向密码本。
        ///
        /// 干扰信号从两条加到三条，其中一条是同样用密码本、但页码不同的邻站通联。
        /// 玩家如果拿主线的页码去解它，会得到一串通顺不了的数字——
        /// 这正是让玩家理解"页码是关键"的时刻。
        /// </summary>
        public static ShiftDefinition SecondShift()
        {
            var shift = new ShiftDefinition
            {
                shiftId = "shift-02",
                title = "第二班 · 页码",
                inGameDate = "1985-11-05",
                noiseSeed = 19851105,
                bandLowKHz = 6800f,
                bandHighKHz = 7200f,
            };

            shift.transmissions.Add(new TransmissionEntry
            {
                callsign = "M08",
                fist = M08Operator,
                fistSeed = 102,
                frequencyKHz = 7012f,
                kind = SignalKind.OneTimePad,
                wordsPerMinute = 11f,
                strength = 0.92f,
                startOffsetSeconds = 2f,
                plainText = "货已上车",
                padPage = 23,
                correctLevel = ThreatLevel.Urgent,
                isPrimary = true,
                debriefNote = "值班军官问你从哪一页解的。你说二十三。他点了点头，没再说话。",
            });

            shift.transmissions.Add(new TransmissionEntry
            {
                callsign = "M14",
                fist = M14Operator,
                fistSeed = 201,
                frequencyKHz = 6862f,
                kind = SignalKind.OneTimePad,
                wordsPerMinute = 13f,
                strength = 0.6f,
                startOffsetSeconds = 9f,
                plainText = "一切正常",
                padPage = 71,
                correctLevel = ThreatLevel.Routine,
                debriefNote = "邻站的例行通联。他们用的不是二十三页。",
            });

            shift.transmissions.Add(new TransmissionEntry
            {
                callsign = "R7X",
                fist = AmateurOperator,
                fistSeed = 303,
                frequencyKHz = 7108f,
                kind = SignalKind.PlainMorse,
                wordsPerMinute = 15f,
                strength = 0.78f,
                plainText = "QRZ DE R7X PSE K",
                correctLevel = ThreatLevel.Routine,
                debriefNote = "还是那个业余台。他大概永远等不到回应。",
            });

            return shift;
        }

        /// <summary>
        /// 第三班。设计意图：
        ///
        /// 频段变拥挤。五条信号里只有一条是主线，其中两条同频相邻，
        /// 玩家得靠细调把它们分开——这是第一次真正需要用到微调旋钮。
        ///
        /// 主线电文本身是坏消息，但用的是最低的例行等级发出来的。
        /// 玩家如果只看发报方标的等级就照抄，会漏报；
        /// 要读懂内容才知道这条该往上提。这是整个战役第一次
        /// 让"判断"和"抄收"分开考。
        /// </summary>
        public static ShiftDefinition ThirdShift()
        {
            var shift = new ShiftDefinition
            {
                shiftId = "shift-03",
                title = "第三班 · 拥挤",
                inGameDate = "1985-11-09",
                noiseSeed = 19851109,
                bandLowKHz = 6800f,
                bandHighKHz = 7200f,
            };

            shift.transmissions.Add(new TransmissionEntry
            {
                callsign = "M08",
                fist = M08Operator,
                fistSeed = 103,
                frequencyKHz = 7043f,
                kind = SignalKind.OneTimePad,
                wordsPerMinute = 12f,
                strength = 0.88f,
                startOffsetSeconds = 3f,
                plainText = "桥已封锁",
                padPage = 46,
                correctLevel = ThreatLevel.Flash,
                isPrimary = true,
                debriefNote = "他们把这条按例行发出来。你没有照抄那个等级。",
            });

            // 与主线只差 4 kHz，靠粗调分不开，必须动微调。
            shift.transmissions.Add(new TransmissionEntry
            {
                callsign = "V13",
                fist = M14Operator,
                fistSeed = 401,
                frequencyKHz = 7047f,
                kind = SignalKind.ChineseTelegraph,
                wordsPerMinute = 14f,
                strength = 0.7f,
                startOffsetSeconds = 5f,
                plainText = "天气晴好",
                correctLevel = ThreatLevel.Routine,
                debriefNote = "气象通报。它离主线太近，很多人会把两条混在一起抄。",
            });

            shift.transmissions.Add(new TransmissionEntry
            {
                callsign = "M14",
                fist = M14Operator,
                fistSeed = 202,
                frequencyKHz = 6884f,
                kind = SignalKind.OneTimePad,
                wordsPerMinute = 13f,
                strength = 0.55f,
                startOffsetSeconds = 11f,
                plainText = "照常轮换",
                padPage = 88,
                correctLevel = ThreatLevel.Routine,
                debriefNote = "邻站换班。又是另一页。",
            });

            shift.transmissions.Add(new TransmissionEntry
            {
                callsign = "R7X",
                fist = AmateurOperator,
                fistSeed = 304,
                frequencyKHz = 7132f,
                kind = SignalKind.PlainMorse,
                wordsPerMinute = 16f,
                strength = 0.74f,
                plainText = "CQ CQ DE R7X K",
                correctLevel = ThreatLevel.Routine,
                debriefNote = "业余台。今晚他换了个频率，还是没人回他。",
            });

            shift.transmissions.Add(new TransmissionEntry
            {
                callsign = "B02",
                fist = M14Operator,
                fistSeed = 402,
                frequencyKHz = 6821f,
                kind = SignalKind.ChineseTelegraph,
                wordsPerMinute = 10f,
                strength = 0.42f,
                startOffsetSeconds = 17f,
                plainText = "无线电静默",
                correctLevel = ThreatLevel.Attention,
                debriefNote = "很弱的一条。宣布静默本身就是一件值得留意的事。",
            });

            return shift;
        }

        /// <summary>
        /// 第四班。设计意图：
        ///
        /// 前三班里玩家已经听了 M08 三次，那双手的节奏应该开始熟悉了：
        /// 划拖得偏长、字之间赶得紧、手很稳。这一班 M08 还在老频率上，
        /// 呼号对得上，密码本页码也是对的——但发报的不是他。
        ///
        /// 游戏不提示。不弹窗，不高亮，不在日志里写"注意异常"。
        /// 节奏分析面板会照常给出这次的手法描述，玩家要自己想起来
        /// 上次听到的不是这样。察觉不到就只是照常上报，
        /// 而这条电文的内容本身没有任何问题——问题在于发它的人。
        ///
        /// 这是整个战役里第一次，正确答案不在电文内容里。
        /// </summary>
        public static ShiftDefinition FourthShift()
        {
            var shift = new ShiftDefinition
            {
                shiftId = "shift-04",
                title = "第四班 · 另一双手",
                inGameDate = "1985-11-14",
                noiseSeed = 19851114,
                bandLowKHz = 6800f,
                bandHighKHz = 7200f,
            };

            shift.transmissions.Add(new TransmissionEntry
            {
                callsign = "M08",
                fist = M08Impostor,
                fistSeed = 104,
                isImpostor = true,
                frequencyKHz = 6955f,
                kind = SignalKind.OneTimePad,
                wordsPerMinute = 12f,
                strength = 0.9f,
                startOffsetSeconds = 4f,
                plainText = "计划不变",
                padPage = 61,
                correctLevel = ThreatLevel.Flash,
                isPrimary = true,
                debriefNote = "电文说计划不变。你报的不是电文说了什么，是发它的人不对。",
            });

            shift.transmissions.Add(new TransmissionEntry
            {
                callsign = "M14",
                fist = M14Operator,
                fistSeed = 203,
                frequencyKHz = 6903f,
                kind = SignalKind.OneTimePad,
                wordsPerMinute = 13f,
                strength = 0.62f,
                startOffsetSeconds = 10f,
                plainText = "等待指示",
                padPage = 34,
                correctLevel = ThreatLevel.Attention,
                debriefNote = "邻站也在等。他们大概也发现了什么。",
            });

            shift.transmissions.Add(new TransmissionEntry
            {
                callsign = "R7X",
                fist = AmateurOperator,
                fistSeed = 302,
                frequencyKHz = 7108f,
                kind = SignalKind.PlainMorse,
                wordsPerMinute = 15f,
                strength = 0.76f,
                plainText = "CQ DE R7X QSL PSE",
                correctLevel = ThreatLevel.Routine,
                debriefNote = "业余台还在。今晚你听他听得比平时久。",
            });

            return shift;
        }

        /// <summary>
        /// 第五班。设计意图：
        ///
        /// 引入慢扫描传真。它和前四班的每一条信号都相反——不用抄、不用查表、不用解密，
        /// 但要求玩家在一个频率上一动不动地待够十几秒。前四班训练出来的习惯是
        /// 不停扫频找信号，这一班第一次惩罚那个习惯：手一动，图就废了。
        ///
        /// 更要紧的是它同时在两个频率上摆了东西。传真在扫的时候，M08 在另一头发
        /// 一条短电文。两件事都想要就两件事都做不好，玩家必须选一个——
        /// 而选哪个的后果要到交班之后才知道。这是本作第一次把"注意力"本身做成资源。
        /// </summary>
        public static ShiftDefinition FifthShift()
        {
            var shift = new ShiftDefinition
            {
                shiftId = "shift-05",
                title = "第五班 · 一幅图",
                inGameDate = "1985-11-19",
                noiseSeed = 19851119,
                bandLowKHz = 6800f,
                bandHighKHz = 7200f,
            };

            shift.transmissions.Add(new TransmissionEntry
            {
                callsign = "F31",
                frequencyKHz = 6968f,
                kind = SignalKind.Facsimile,
                facsimileSubject = FacsimileSubject.Facility,
                facsimileSeed = 51119,
                strength = 0.94f,
                startOffsetSeconds = 6f,
                plainText = "设施平面图",
                correctLevel = ThreatLevel.Flash,
                isPrimary = true,
                debriefNote = "围墙、主楼、两栋附属，右上角有人用圈标了一处。"
                              + "标记的位置在图上，不在电文里——发图的人知道收图的人认得那地方。",
            });

            // 传真扫到一半时 M08 开始发。两件事撞在一起是这一班的全部设计。
            shift.transmissions.Add(new TransmissionEntry
            {
                callsign = "M08",
                fist = M08Operator,
                fistSeed = 511,
                frequencyKHz = 7042f,
                kind = SignalKind.ChineseTelegraph,
                wordsPerMinute = 17f,
                strength = 0.81f,
                startOffsetSeconds = 11f,
                plainText = "图已发出",
                correctLevel = ThreatLevel.Attention,
                debriefNote = "他只说了这四个字。你如果在看图，就没听见。",
            });

            shift.transmissions.Add(new TransmissionEntry
            {
                callsign = "B02",
                fist = M14Operator,
                fistSeed = 512,
                frequencyKHz = 7154f,
                kind = SignalKind.PlainMorse,
                wordsPerMinute = 14f,
                strength = 0.58f,
                plainText = "QRT DE B02",
                correctLevel = ThreatLevel.Routine,
                debriefNote = "B02 关机了。这几天他关得一次比一次早。",
            });

            return shift;
        }

        /// <summary>按顺序返回全部班次。存档与班次推进用它。</summary>
        public static ShiftDefinition[] All()
        {
            return new[] { FirstShift(), SecondShift(), ThirdShift(), FourthShift(), FifthShift() };
        }

        /// <summary>
        /// 档案里记着的某个呼号的手法。
        ///
        /// 这是玩家过去几班积累下来的印象，节奏分析面板拿它做对照。
        /// 查不到就返回无效值，面板会说"档案里没有这个呼号"。
        /// </summary>
        public static OperatorFist KnownFistFor(string callsign)
        {
            switch (callsign)
            {
                case "M08":
                    return M08Operator;
                case "M14":
                case "V13":
                case "B02":
                    return M14Operator;
                case "R7X":
                    return AmateurOperator;
                case "D14":
                    return D14Operator;
                default:
                    return default;
            }
        }
    }
}
