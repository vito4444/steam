using System;
using System.Text;

namespace Decoder.Signal
{
    /// <summary>接收辅助的档位。玩家在设置里选，不影响评分，也不进成就。</summary>
    public enum CopyAssist
    {
        /// <summary>什么都不给。纯靠听，老手模式。</summary>
        None = 0,

        /// <summary>显示收到的点划，玩家自己查码表还原成字符。</summary>
        Symbols = 1,

        /// <summary>直接显示还原后的字符。新手和听障玩家的默认档。</summary>
        Characters = 2,
    }

    /// <summary>
    /// 摩尔斯接收解码器。把键控的通断时序还原成点划，再还原成字符。
    ///
    /// 这是玩家侧的辅助工具，不是判分器——判分看的是玩家自己抄下来的东西。
    /// 它存在的意义有两个：给新手一个爬坡的台阶，以及给听障玩家一条完整的替代路径。
    /// 正因为它可能是某些玩家读电码的唯一途径，它的输出必须和发报端严格一致，
    /// 不能是"大致对"。
    ///
    /// 分界阈值取标准时长的中点而不是标准值本身：点是 1 单位、划是 3 单位，
    /// 分界放在 2 单位处两边都有一倍的余量，键控软化带来的边沿偏移不会导致误判。
    /// </summary>
    public sealed class MorseReceiver
    {
        /// <summary>点与划的分界，单位数。</summary>
        public const float DitDahThreshold = 2f;

        /// <summary>字符间隔的判定下限。标准是 3 单位。</summary>
        public const float CharacterGapThreshold = 2f;

        /// <summary>词间隔的判定下限。标准是 7 单位。</summary>
        public const float WordGapThreshold = 5f;

        private readonly StringBuilder _pending = new StringBuilder(8);
        private readonly StringBuilder _transcript = new StringBuilder(128);

        private float _unitSeconds;
        private bool _started;
        private bool _lastKeyDown;
        private double _lastEdgeTime;
        private double _lastTime;
        private bool _wordBreakPending;

        public MorseReceiver(float wordsPerMinute)
        {
            SetSpeed(wordsPerMinute);
        }

        /// <summary>当前正在接收、尚未成形的那个字符的点划。</summary>
        public string PendingSymbols => _pending.ToString();

        /// <summary>已经成形的字符序列。</summary>
        public string Transcript => _transcript.ToString();

        /// <summary>已解码的字符数，用于界面判断有没有新内容。</summary>
        public int TranscriptLength => _transcript.Length;

        public float UnitSeconds => _unitSeconds;

        /// <summary>
        /// 换台时调用。不同电台速度不同，沿用上一台的单位时长会把点划全判错。
        /// </summary>
        public void SetSpeed(float wordsPerMinute)
        {
            _unitSeconds = MorseCode.UnitSeconds(wordsPerMinute);
        }

        public void Reset()
        {
            _pending.Clear();
            _transcript.Clear();
            _started = false;
            _lastKeyDown = false;
            _lastEdgeTime = 0d;
            _lastTime = 0d;
            _wordBreakPending = false;
        }

        /// <summary>
        /// 按当前时刻的键控状态推进解码。每帧调用一次即可，
        /// 帧率不需要很高：判定依据是电平变化之间的时长，不是采样次数。
        /// </summary>
        /// <summary>
        /// 喂数据的最大步长。
        ///
        /// 这个解码器只看每次调用时的瞬时键控状态，所以调用间隔必须小于最短的元素。
        /// 18 字每分时一个单位只有 67 毫秒，按帧喂的话帧率掉到每秒十几帧就已经
        /// 采不到一个点了：一整串划会被读成几个孤立的字符，转写带上出来的是一行
        /// 看着像模像样的错字，而听障玩家没有任何办法察觉它是错的。
        ///
        /// 调用方拿这个步长把两次调用之间的时间切开逐段喂，帧率就不再影响解码结果。
        /// </summary>
        public double SuggestedStepSeconds => System.Math.Max(0.004, _unitSeconds * 0.5);

        public void Advance(double timeSeconds, bool keyDown)
        {
            if (!_started)
            {
                _started = true;
                _lastKeyDown = keyDown;
                _lastEdgeTime = timeSeconds;
                _lastTime = timeSeconds;
                return;
            }

            // 时间倒流意味着换班或读档，直接重来，不要拿旧状态硬算。
            if (timeSeconds < _lastTime)
            {
                Reset();
                _started = true;
                _lastKeyDown = keyDown;
                _lastEdgeTime = timeSeconds;
                _lastTime = timeSeconds;
                return;
            }

            _lastTime = timeSeconds;

            if (keyDown == _lastKeyDown)
            {
                // 电平没变。抬起状态持续得够久就要收尾当前字符，
                // 这一步不能等到下一个下降沿才做，否则最后一个字符永远出不来。
                if (!keyDown)
                {
                    var idleUnits = (float)((timeSeconds - _lastEdgeTime) / _unitSeconds);
                    if (idleUnits >= CharacterGapThreshold)
                    {
                        FlushPendingCharacter();
                    }

                    if (idleUnits >= WordGapThreshold)
                    {
                        FlushWordBreak();
                    }
                }

                return;
            }

            var elapsedUnits = (float)((timeSeconds - _lastEdgeTime) / _unitSeconds);

            if (_lastKeyDown)
            {
                // 下降沿：刚结束的这一段载波是点还是划
                _pending.Append(elapsedUnits >= DitDahThreshold ? MorseCode.Dah : MorseCode.Dit);
            }
            else if (elapsedUnits >= CharacterGapThreshold)
            {
                // 上升沿，且之前静默得够久：上一个字符在此收尾
                FlushPendingCharacter();
                if (elapsedUnits >= WordGapThreshold)
                {
                    FlushWordBreak();
                }
            }

            _lastKeyDown = keyDown;
            _lastEdgeTime = timeSeconds;
        }

        private void FlushPendingCharacter()
        {
            if (_pending.Length == 0)
            {
                return;
            }

            if (_wordBreakPending)
            {
                _transcript.Append(' ');
                _wordBreakPending = false;
            }

            _transcript.Append(MorseCode.Decode(_pending.ToString()));
            _pending.Clear();
        }

        private void FlushWordBreak()
        {
            // 词间隔只在后面真的还有字符时才落成一个空格，
            // 否则电文末尾的长静默会在转写里拖出一串尾随空格。
            if (_transcript.Length > 0)
            {
                _wordBreakPending = true;
            }
        }

        /// <summary>按辅助档位给出要显示的文本。</summary>
        public string Format(CopyAssist assist)
        {
            switch (assist)
            {
                case CopyAssist.Symbols:
                    return _pending.Length > 0
                        ? $"{SymbolTranscript()} {_pending}"
                        : SymbolTranscript();
                case CopyAssist.Characters:
                    return _transcript.ToString();
                default:
                    return string.Empty;
            }
        }

        /// <summary>
        /// 把已还原的字符倒推回点划显示。
        /// 直接留存原始点划也可以，但那样在字符间隔上会和显示的分组对不齐。
        /// </summary>
        private string SymbolTranscript()
        {
            if (_transcript.Length == 0)
            {
                return string.Empty;
            }

            var builder = new StringBuilder(_transcript.Length * 5);
            foreach (var c in _transcript.ToString())
            {
                if (c == ' ')
                {
                    builder.Append(" / ");
                    continue;
                }

                if (builder.Length > 0 && !builder.ToString().EndsWith(" ", StringComparison.Ordinal))
                {
                    builder.Append(' ');
                }

                builder.Append(MorseCode.Encode(c.ToString()));
            }

            return builder.ToString();
        }
    }
}
