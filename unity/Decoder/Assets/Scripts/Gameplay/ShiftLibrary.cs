namespace Decoder.Gameplay
{
    /// <summary>
    /// 内置班次内容。目前只有第一班，用于垂直切片验证整条玩法链路。
    /// 后续班次会改为从数据文件加载，届时这里只保留第一班作为教学关。
    /// </summary>
    public static class ShiftLibrary
    {
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
    }
}
