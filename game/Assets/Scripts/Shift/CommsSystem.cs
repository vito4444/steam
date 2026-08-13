using System;
using System.Collections.Generic;

namespace Maner.Shift
{
    public enum CallSource
    {
        /// <summary>井下班组，通过罐笼口的电话打上来。</summary>
        CrewBelow,
        /// <summary>地面上级调度，语气永远像在催。</summary>
        HighCommand,
        /// <summary>来路不明的通话。这一层是可选的恐怖层。</summary>
        Unknown,
    }

    public sealed class ReplyOption
    {
        public string Text;
        /// <summary>选择本项后触发的事件标识，班次导演与后续通话都以此为条件。</summary>
        public string EventId;
        /// <summary>本项对井下人员情绪的影响，会累积进后果账本。</summary>
        public int MoraleDelta;
        /// <summary>选择本项后接着播放的通话，为空则不追加。</summary>
        public string FollowUpCallId;
    }

    public sealed class CallDefinition
    {
        public string Id;
        public CallSource Source;
        /// <summary>通话正文，同时用作字幕与语音合成的输入。</summary>
        public string Line;
        /// <summary>语音文件名，不含扩展名。由 tools/generate_voice.sh 预生成。</summary>
        public string VoiceClip;
        public List<ReplyOption> Replies = new List<ReplyOption>();
        /// <summary>无人接听时自动挂断的秒数，0 表示一直响。</summary>
        public double RingTimeoutSeconds = 20.0;
    }

    public enum CallState
    {
        Idle,
        Ringing,
        Connected,
        AwaitingReply,
    }

    /// <summary>
    /// 舱内那部电话。它是玩家了解井下状况的唯一渠道——你看不见他们，
    /// 只能听见他们。应答选择会改变后续事件，这是叙事分支的载体。
    /// 本类不依赖 Unity，语音播放由表现层按 VoiceClip 名称去取。
    /// </summary>
    public sealed class CommsSystem
    {
        readonly Dictionary<string, CallDefinition> catalog = new Dictionary<string, CallDefinition>();
        readonly List<(double at, string callId)> schedule = new List<(double, string)>();
        readonly Queue<string> pending = new Queue<string>();
        readonly List<string> triggeredEvents = new List<string>();
        readonly List<string> transcript = new List<string>();

        int nextScheduleIndex;
        double ringElapsed;

        public CallState State { get; private set; } = CallState.Idle;
        public CallDefinition Current { get; private set; }
        public int Morale { get; private set; }
        public int MissedCalls { get; private set; }
        public IReadOnlyList<string> TriggeredEvents => triggeredEvents;
        public IReadOnlyList<string> Transcript => transcript;

        public void Register(CallDefinition call) => catalog[call.Id] = call;

        public void Schedule(string callId, double atSeconds)
        {
            schedule.Add((atSeconds, callId));
            schedule.Sort((a, b) => a.at.CompareTo(b.at));
        }

        public void Reset()
        {
            pending.Clear();
            triggeredEvents.Clear();
            transcript.Clear();
            nextScheduleIndex = 0;
            ringElapsed = 0.0;
            State = CallState.Idle;
            Current = null;
            Morale = 0;
            MissedCalls = 0;
        }

        public void Tick(double shiftTime, double deltaTime)
        {
            while (nextScheduleIndex < schedule.Count && schedule[nextScheduleIndex].at <= shiftTime)
            {
                pending.Enqueue(schedule[nextScheduleIndex].callId);
                nextScheduleIndex++;
            }

            if (State == CallState.Idle && pending.Count > 0)
            {
                StartRinging(pending.Dequeue());
            }

            if (State == CallState.Ringing)
            {
                ringElapsed += deltaTime;
                if (Current.RingTimeoutSeconds > 0.0 && ringElapsed >= Current.RingTimeoutSeconds)
                {
                    MissedCalls++;
                    Morale -= 2;
                    transcript.Add($"[{shiftTime:0.0}s] 未接来电：{Current.Id}");
                    triggeredEvents.Add($"missed:{Current.Id}");
                    State = CallState.Idle;
                    Current = null;
                }
            }
        }

        void StartRinging(string callId)
        {
            if (!catalog.TryGetValue(callId, out var call))
            {
                return;
            }
            Current = call;
            State = CallState.Ringing;
            ringElapsed = 0.0;
        }

        /// <summary>摘机。接通后进入应答选择，若无选项则直接挂断。</summary>
        public bool Answer(double shiftTime)
        {
            if (State != CallState.Ringing || Current == null)
            {
                return false;
            }

            transcript.Add($"[{shiftTime:0.0}s] {SourceName(Current.Source)}：{Current.Line}");
            State = Current.Replies.Count > 0 ? CallState.AwaitingReply : CallState.Connected;

            if (State == CallState.Connected)
            {
                HangUp();
            }
            return true;
        }

        /// <summary>作出应答。不同选项触发不同的事件标识，后续通话据此分叉。</summary>
        public bool Reply(int optionIndex, double shiftTime)
        {
            if (State != CallState.AwaitingReply || Current == null ||
                optionIndex < 0 || optionIndex >= Current.Replies.Count)
            {
                return false;
            }

            var option = Current.Replies[optionIndex];
            transcript.Add($"[{shiftTime:0.0}s] 调度员：{option.Text}");
            triggeredEvents.Add(option.EventId);
            Morale += option.MoraleDelta;

            if (!string.IsNullOrEmpty(option.FollowUpCallId))
            {
                pending.Enqueue(option.FollowUpCallId);
            }

            HangUp();
            return true;
        }

        void HangUp()
        {
            State = CallState.Idle;
            Current = null;
            ringElapsed = 0.0;
        }

        public static string SourceName(CallSource source) => source switch
        {
            CallSource.CrewBelow => "井下班组",
            CallSource.HighCommand => "地面调度",
            _ => "未知来电",
        };

        /// <summary>首个班次的通话本。语音由 tools/generate_voice.sh 预先合成。</summary>
        public static CommsSystem BuildFirstShift()
        {
            var comms = new CommsSystem();

            comms.Register(new CallDefinition
            {
                Id = "crew_ready",
                Source = CallSource.CrewBelow,
                Line = "调度室，三班组十二人已经在罐笼口了，等你放我们下去。",
                VoiceClip = "crew_ready",
                Replies = new List<ReplyOption>
                {
                    new ReplyOption { Text = "收到，先等风量上来。", EventId = "reply:wait_for_air", MoraleDelta = 1 },
                    new ReplyOption { Text = "马上放，站稳了。", EventId = "reply:immediate", MoraleDelta = -1 },
                },
            });

            comms.Register(new CallDefinition
            {
                Id = "command_pressure",
                Source = CallSource.HighCommand,
                Line = "调度，上级看着今班的产量呢，别在地面上磨蹭。",
                VoiceClip = "command_pressure",
                Replies = new List<ReplyOption>
                {
                    new ReplyOption { Text = "按规程来，慢一点也得来。", EventId = "reply:by_the_book", MoraleDelta = 2 },
                    new ReplyOption { Text = "明白，这就加快。", EventId = "reply:comply", MoraleDelta = -2 },
                },
            });

            // 故障发生后的求助。两个选项各自触发不同的后续通话，
            // 这是验收标准第 4 条要求的分支。
            comms.Register(new CallDefinition
            {
                Id = "crew_power_lost",
                Source = CallSource.CrewBelow,
                Line = "调度！这边灯全灭了，罐笼卡在半道上不动了，你那边什么情况？",
                VoiceClip = "crew_power_lost",
                RingTimeoutSeconds = 25.0,
                Replies = new List<ReplyOption>
                {
                    new ReplyOption
                    {
                        Text = "分路跳闸了，我正在复位，别动任何东西。",
                        EventId = "reply:fault_acknowledged",
                        MoraleDelta = 2,
                        FollowUpCallId = "crew_calm",
                    },
                    new ReplyOption
                    {
                        Text = "不知道，你们自己想办法。",
                        EventId = "reply:fault_dismissed",
                        MoraleDelta = -4,
                        FollowUpCallId = "crew_panic",
                    },
                },
            });

            comms.Register(new CallDefinition
            {
                Id = "crew_calm",
                Source = CallSource.CrewBelow,
                Line = "行，我们不动，等你的信号。",
                VoiceClip = "crew_calm",
            });

            comms.Register(new CallDefinition
            {
                Id = "crew_panic",
                Source = CallSource.CrewBelow,
                Line = "什么叫自己想办法？这里离底板还有八百米啊！",
                VoiceClip = "crew_panic",
            });

            // 恐怖层：一通没有来源的通话。整个班次里只出现一次，不解释。
            comms.Register(new CallDefinition
            {
                Id = "unknown_whisper",
                Source = CallSource.Unknown,
                Line = "……喂……你们那边，是不是还有一个人在下面。",
                VoiceClip = "unknown_whisper",
                RingTimeoutSeconds = 12.0,
            });

            comms.Schedule("crew_ready", 45.0);
            comms.Schedule("command_pressure", 130.0);
            comms.Schedule("crew_power_lost", 205.0);
            comms.Schedule("unknown_whisper", 330.0);

            return comms;
        }
    }
}
