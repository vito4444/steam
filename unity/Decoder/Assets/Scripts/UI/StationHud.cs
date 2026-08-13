using System;
using System.Collections.Generic;
using System.Text;
using Decoder.Gameplay;
using Decoder.Signal;
using UnityEngine;
using UnityEngine.UI;

namespace Decoder.UI
{
    /// <summary>
    /// 工位界面。整套 UI 由代码生成，没有预制体，
    /// 这样改版只需要改参数再重跑一次场景生成，不需要在编辑器里手工摆。
    ///
    /// 视觉上模仿 CRT 单色终端：磷光绿等宽文字、扫描线、边框。
    /// 这一层是玩家真正操作的界面，也是截图自检里最能看出功能是否成立的部分。
    /// </summary>
    public sealed class StationHud : MonoBehaviour
    {
        // P1 磷光的实际观感偏黄绿，而且远没有纯绿那么艳。之前那组值
        // 让画面的平均饱和度冲到 0.54，而 UI 占了很大屏占比，
        // 整个场景都被带成了荧光绿。
        private static readonly Color Phosphor = new Color(0.66f, 0.95f, 0.68f, 1f);
        // 暗档从 0.24 提到 0.34：它要负责显示上报单上还没填的占位符，
        // 太暗玩家根本注意不到那里能填。
        private static readonly Color PhosphorDim = new Color(0.50f, 0.72f, 0.53f, 1f);
        private static readonly Color Amber = new Color(0.95f, 0.76f, 0.42f, 1f);
        private static readonly Color Alert = new Color(1f, 0.36f, 0.28f, 1f);
        // 面板压得比较黑：这些字要盖在被台灯照亮的桌面上，
        // 半透明的底在亮处会让磷光绿彻底糊掉。
        private static readonly Color PanelBg = new Color(0.015f, 0.035f, 0.02f, 0.93f);

        [Header("接线")]
        public RadioReceiver receiver;

        [Header("班次")]
        [Tooltip("留空则使用内置的第一班内容")]
        public string shiftTitle = "";

        private Font _font;
        private Text _frequencyText;
        private Text _signalText;
        private Image _signalBar;
        private Text _tuneHint;
        private Text _copiedText;
        private Text _lookupText;
        private Text _decodedText;
        private Text _levelText;
        private Text _verdictText;
        private Text _hoverText;
        private Text _logText;
        private Text _bannerText;
        private Text _liveCopyText;
        private Text _assistText;
        private Text _replyText;
        private Text _noteText;

        private MorseReceiver _morse;
        private CopyAssist _assist = CopyAssist.Characters;
        private string _lastStationCallsign;

        private RectTransform _archivePanel;
        private Text _archiveText;
        private Text _fistText;
        private Text _fistArchiveText;
        private string _fistCallsign;
        private Text _padPageText;
        private Text _padDigitsText;
        private Text _solvedText;
        private Text _formCallsignText;
        private Text _formFrequencyText;
        private Text _focusHintText;

        /// <summary>键盘输入当前落在哪个字段上。</summary>
        private enum InputFocus
        {
            Copy,
            Callsign,
            Frequency,
        }

        private CampaignState _campaign = new CampaignState();

        /// <summary>本班是否已经上报完毕，等玩家按 N 交班。</summary>
        private bool _shiftComplete;

        private const float AutoSaveIntervalSeconds = 20f;
        private float _autoSaveTimer;
        private InputFocus _focus = InputFocus.Copy;
        private int _padPage = 1;
        private readonly StringBuilder _callsignBuffer = new StringBuilder(8);
        private readonly StringBuilder _frequencyBuffer = new StringBuilder(10);
        private readonly StringBuilder _solvedBuffer = new StringBuilder(64);

        private ShiftDefinition _shift;
        private ChineseTelegraphCode _telegraph;
        private readonly StringBuilder _copyBuffer = new StringBuilder(64);
        private ThreatLevel _selectedLevel = ThreatLevel.Routine;
        private readonly List<string> _log = new List<string>();

        /// <summary>当前抄收缓冲区的内容。测试与存档读取这个值。</summary>
        public string CopiedBuffer => _copyBuffer.ToString();

        public ThreatLevel SelectedLevel => _selectedLevel;

        /// <summary>向抄收纸追加一个字符。自动演练用它模拟玩家敲键盘。</summary>
        public void AppendCopiedCharacter(char c)
        {
            if (char.IsControl(c))
            {
                return;
            }

            _copyBuffer.Append(char.ToUpperInvariant(c));
            RefreshCopyArea();
        }

        public void ClearCopiedBuffer()
        {
            _copyBuffer.Clear();
            RefreshCopyArea();
        }

        /// <summary>开合档案。自动演练用它模拟玩家按 F2。</summary>
        public void ToggleArchive(bool open)
        {
            if (_archivePanel == null)
            {
                return;
            }

            _archivePanel.gameObject.SetActive(open);
            if (open)
            {
                RefreshArchivePanel();
            }
        }

        /// <summary>查一组电码。自动演练用它模拟玩家按 L。</summary>
        public void LookUpOneGroup()
        {
            LookUpNextGroup();
        }

        /// <summary>用当前页解密。自动演练用它模拟玩家按 D。</summary>
        public void SolveCurrentPad()
        {
            SolveWithPad();
        }

        /// <summary>翻到指定页。自动演练用它模拟玩家翻密码本。</summary>
        public void TurnPadTo(int page)
        {
            SetPadPage(page);
        }

        /// <summary>填写上报单上的呼号与频率。自动演练用它模拟玩家敲键盘。</summary>
        public void FillForm(string callsign, string frequency)
        {
            _callsignBuffer.Clear();
            _callsignBuffer.Append(callsign ?? string.Empty);
            _frequencyBuffer.Clear();
            _frequencyBuffer.Append(frequency ?? string.Empty);
            RefreshForm();
        }

        /// <summary>顶部横幅。平时显示班次标题，自动演练时显示当前步骤。</summary>
        public void SetStatusBanner(string text)
        {
            if (_bannerText != null)
            {
                _bannerText.text = text;
            }
        }

        private void Awake()
        {
            _font = Resources.Load<Font>("Fonts/NotoSansSC-Regular");
            if (_font == null)
            {
                // 字体缺失时退回内置字体，界面会变成没有中文的样子，
                // 但至少能看出是字体没打包进来，而不是整个界面消失。
                _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                Debug.LogWarning("[StationHud] 未找到 Resources/Fonts/NotoSansSC-Regular，中文将无法显示");
            }

            _telegraph = ChineseTelegraphCode.Shared;
            // 读档决定从第几班开始。存档损坏或不存在都会拿到一个空进度，
            // 玩家从头开始，而不是看到一个错误弹窗。
            _campaign = SaveSystem.Load();
            var all = ShiftLibrary.All();
            _shift = all[Mathf.Clamp(_campaign.shiftIndex, 0, all.Length - 1)];
            _morse = new MorseReceiver(12f);
            BuildUi();
            LoadShift(_shift);
        }

        public void LoadShift(ShiftDefinition shift)
        {
            _shift = shift;
            if (receiver != null && shift != null)
            {
                receiver.bandLowKHz = shift.bandLowKHz;
                receiver.bandHighKHz = shift.bandHighKHz;
                receiver.noiseSeed = shift.noiseSeed;
                receiver.LoadStations(shift.BuildStations(_telegraph));
            }

            _copyBuffer.Clear();
            _callsignBuffer.Clear();
            _frequencyBuffer.Clear();
            _solvedBuffer.Clear();
            _focus = InputFocus.Copy;
            _padPage = 1;
            _log.Clear();
            SetStatusBanner(string.IsNullOrEmpty(shiftTitle) ? shift?.title ?? "" : shiftTitle);
            AppendLog($"值班开始 · {shift?.inGameDate}");
            AppendLog("按住鼠标右键转头，左键拖动旋钮搜频");
            AppendLog("听到电码后用键盘抄下数字或字母");
            AppendLog("Tab 换填写栏 · [ ] 翻密码本 · D 解密 · L 查电码表 · F2 档案");
            AppendLog(_campaign.StandingLine());
            RestoreProgress();
            RefreshPad();
            RefreshForm();
        }

        private void Update()
        {
            RefreshReadouts();
            AdvanceMorseReceiver();
            HandleTypedInput();
            RefreshFistPanel();
            HandleHotkeys();

            // 定时落盘。玩家抄了十分钟才崩溃或断电的话，
            // 光靠退出回调救不了他。
            _autoSaveTimer += Time.unscaledDeltaTime;
            if (_autoSaveTimer >= AutoSaveIntervalSeconds)
            {
                _autoSaveTimer = 0f;
                CaptureProgress();
                SaveSystem.Save(_campaign);
            }
        }

        /// <summary>
        /// 推进接收解码器。它跟着当前调谐到的电台走：换台就换速度并清空，
        /// 因为不同电台的发报速度不同，沿用上一台的单位时长会把点划全判错。
        /// </summary>
        private void AdvanceMorseReceiver()
        {
            var synth = receiver != null ? receiver.Synthesizer : null;
            if (synth == null)
            {
                return;
            }

            var station = synth.CurrentStation;
            var callsign = station?.Callsign;
            if (callsign != _lastStationCallsign)
            {
                _lastStationCallsign = callsign;
                _morse.Reset();
                if (station != null)
                {
                    _morse.SetSpeed(station.WordsPerMinute);
                }
            }

            // 传真是连续载波，喂给电码解码器只会解出一串乱码，
            // 而那串乱码看上去和真的抄收结果一模一样，玩家会照着它抄。
            if (station?.Facsimile != null)
            {
                _morse.Reset();
                if (_liveCopyText != null)
                {
                    _liveCopyText.text = DescribeFacsimile(station, synth.ElapsedSeconds);
                }

                return;
            }

            // 信号太弱时不喂数据。这一点很重要：辅助工具不该比玩家的耳朵更灵，
            // 否则玩家会发现盯着转写带比调准频率更省事，搜频这一层玩法就废了。
            var readable = station != null && receiver.SignalLevel > 0.45f;
            _morse.Advance(synth.ElapsedSeconds, readable && station.IsKeyDown(synth.ElapsedSeconds));

            if (_liveCopyText != null)
            {
                _liveCopyText.text = _assist == CopyAssist.None
                    ? "（辅助已关闭）"
                    : _morse.Format(_assist);
            }
        }

        /// <summary>
        /// 传真接收的进度说明。
        ///
        /// 不用抄写，所以这一栏改说"图扫到哪儿了"。扫描进度得给出来：
        /// 玩家需要知道还要在这个频率上再守多久，才能决定要不要为了另一个
        /// 频率上的东西放弃这幅图。这个取舍是第五班的全部内容。
        /// </summary>
        private string DescribeFacsimile(SignalSynthesizer.Station station, double elapsedSeconds)
        {
            var image = station.Facsimile;
            var progress = station.FacsimileProgress(elapsedSeconds);
            if (progress < 0d)
            {
                return "图像信号 · 等待开始";
            }

            if (progress < FacsimileSignal.LeaderSeconds)
            {
                return "图像信号 · 引导音，马上开扫";
            }

            if (progress >= station.TotalSeconds)
            {
                var wait = station.TotalSeconds + 3f - progress;
                return wait > 0f
                    ? $"图像信号 · 本幅已完，下一幅 {wait:F0} 秒后"
                    : "图像信号 · 等待下一幅";
            }

            var line = FacsimileSignal.LineSeconds(image.Width, station.FacsimilePixelSeconds);
            var row = Mathf.Min(image.Height,
                Mathf.FloorToInt((float)((progress - FacsimileSignal.LeaderSeconds) / line)) + 1);
            var remaining = station.TotalSeconds - progress;
            return $"图像信号 · 第 {row}/{image.Height} 行，还需 {remaining:F0} 秒\n不用抄，盯住屏幕";
        }

        private void HandleHotkeys()
        {
            if (Input.GetKeyDown(KeyCode.F1))
            {
                _assist = (CopyAssist)(((int)_assist + 1) % 3);
                _assistText.text = AssistLabel(_assist);
                AppendLog($"接收辅助切换为「{AssistLabel(_assist)}」");
            }

            if (Input.GetKeyDown(KeyCode.LeftArrow))
            {
                CycleLevel(-1);
            }
            else if (Input.GetKeyDown(KeyCode.RightArrow))
            {
                CycleLevel(1);
            }

            // 交班。上报完才给按，否则玩家可能在还没听完的时候就跳过去了。
            if (_shiftComplete && Input.GetKeyDown(KeyCode.N))
            {
                AdvanceToNextShift();
                return;
            }

            if (Input.GetKeyDown(KeyCode.F2))
            {
                var open = !_archivePanel.gameObject.activeSelf;
                _archivePanel.gameObject.SetActive(open);
                if (open)
                {
                    RefreshArchivePanel();
                }
            }

            if (Input.GetKeyDown(KeyCode.Tab))
            {
                _focus = (InputFocus)(((int)_focus + 1) % 3);
                RefreshForm();
            }

            // 翻密码本。页码要玩家自己从报头读出来再翻过去，
            // 游戏不会替他翻——翻错页解出来就是一串通顺不了的数字。
            if (Input.GetKeyDown(KeyCode.LeftBracket))
            {
                SetPadPage(_padPage - 1);
            }
            else if (Input.GetKeyDown(KeyCode.RightBracket))
            {
                SetPadPage(_padPage + 1);
            }

            if (Input.GetKeyDown(KeyCode.D))
            {
                SolveWithPad();
            }

            if (Input.GetKeyDown(KeyCode.L))
            {
                LookUpNextGroup();
            }
        }

        /// <summary>
        /// 交班，进入下一班。
        ///
        /// 战役打完之后不再往前推：停在最后一班，横幅上说明这一点。
        /// 与其造一个空班次让玩家对着静默的频段坐着，不如明说内容到这里为止。
        /// </summary>
        private void AdvanceToNextShift()
        {
            var all = ShiftLibrary.All();
            if (_campaign.shiftIndex >= all.Length)
            {
                SetStatusBanner("垂直切片的内容到这里为止。");
                AppendLog("后面的班次还没有写。你的记录已经存下来了。");
                _shiftComplete = false;
                return;
            }

            _shiftComplete = false;
            _autoSaveTimer = 0f;
            LoadShift(all[_campaign.shiftIndex]);
        }

        /// <summary>
        /// 把上次退出时的现场摆回来。班次对不上就不恢复——
        /// 否则第三班开局会顶着第二班的抄收纸。
        /// </summary>
        private void RestoreProgress()
        {
            var progress = _campaign.progress;
            if (!progress.BelongsTo(_shift.shiftId))
            {
                return;
            }

            _copyBuffer.Append(progress.copied);
            _solvedBuffer.Append(progress.solved);
            _lookupText.text = progress.lookup;
            _callsignBuffer.Append(progress.callsign);
            _frequencyBuffer.Append(progress.frequency);
            _padPage = progress.padPage;
            _selectedLevel = progress.level;

            if (progress.solved.Length > 0)
            {
                _solvedText.text = GroupForReading(progress.solved);
            }

            _levelText.text = LevelLabel(_selectedLevel);
            RefreshCopyArea();
            RefreshForm();
            RefreshPad();
            AppendLog("接上次的班。桌上的东西还是你走时那样。");
        }

        /// <summary>
        /// 把当前现场记进存档。班次已经上报完就不再记——
        /// 那时候该留下的是记录，不是抄收纸。
        /// </summary>
        private void CaptureProgress()
        {
            if (_shift == null || _shiftComplete)
            {
                return;
            }

            _campaign.progress = new ShiftProgress
            {
                active = true,
                shiftId = _shift.shiftId,
                copied = _copyBuffer.ToString(),
                solved = _solvedBuffer.ToString(),
                lookup = _lookupText != null ? _lookupText.text : string.Empty,
                callsign = _callsignBuffer.ToString(),
                frequency = _frequencyBuffer.ToString(),
                padPage = _padPage,
                level = _selectedLevel,
            };
        }

        private void OnApplicationQuit()
        {
            CaptureProgress();
            SaveSystem.Save(_campaign);
        }

        private void OnApplicationPause(bool paused)
        {
            // 移动端和某些窗口管理器下拿不到退出回调，暂停是最后的机会。
            if (paused)
            {
                CaptureProgress();
                SaveSystem.Save(_campaign);
            }
        }

        private void SetPadPage(int page)
        {
            _padPage = Mathf.Clamp(page, 0, 999);
            RefreshPad();
        }

        /// <summary>
        /// 用当前翻到的这一页解密抄收纸上的内容。
        /// 页码不对就会得到一串解不通的数字，游戏不会提示——
        /// 玩家得自己发现译不出字，回头核对报头。
        /// </summary>
        private void SolveWithPad()
        {
            var wire = ChineseTelegraphCode.ToDigitStream(_copyBuffer.ToString());
            if (wire.Length <= OneTimePad.PageIndicatorDigits)
            {
                AppendLog("抄收的内容还不够解密，至少要有报头加一组。");
                return;
            }

            var solved = OneTimePad.Solve(wire, _shift?.Primary?.padBookSeed ?? 19851104, _padPage);
            _solvedBuffer.Clear();
            _solvedBuffer.Append(solved);
            _solvedText.text = GroupForReading(solved);
            AppendLog($"用第 {_padPage} 页解出 {solved.Length} 位数字。");
        }

        /// <summary>
        /// 查电码表。一次查一组四位，从还没查过的位置往后推。
        /// 这一步刻意做成玩家的动作而不是自动翻译：查表是这个职业的核心动作，
        /// 替玩家做掉，中文电码就只剩一个设定而不是玩法。
        /// </summary>
        private void LookUpNextGroup()
        {
            // 优先查解密后的结果，没有再查抄收原文——
            // 明码电文不需要解密这一步。
            var source = _solvedBuffer.Length > 0
                ? _solvedBuffer.ToString()
                : ChineseTelegraphCode.ToDigitStream(_copyBuffer.ToString());

            var already = _lookupText.text.Length * ChineseTelegraphCode.CodeLength;
            if (already + ChineseTelegraphCode.CodeLength > source.Length)
            {
                AppendLog("没有更多完整的电码组可查了。");
                return;
            }

            var group = source.Substring(already, ChineseTelegraphCode.CodeLength);
            var found = _telegraph.TryGetCharacter(group, out var character);
            _lookupText.text += found ? character.ToString() : "□";
            AppendLog(found
                ? $"{group} 查得「{character}」"
                : $"{group} 在码表里查不到。");
        }

        /// <summary>
        /// 更新节奏分析。
        ///
        /// 只描述听到的东西，不下结论：面板不会说"这不是本人"，
        /// 它只把这次的手法和档案里的手法并排放着。判断是玩家的事。
        /// </summary>
        private void RefreshFistPanel()
        {
            if (_fistText == null || receiver == null)
            {
                return;
            }

            var station = receiver.Synthesizer.CurrentStation;
            if (station == null || receiver.Synthesizer.CurrentSignalLevel < 0.35f)
            {
                if (_fistCallsign != null)
                {
                    _fistCallsign = null;
                    _fistText.text = "没有可分析的信号。";
                    _fistArchiveText.text = string.Empty;
                }

                return;
            }

            // 同一个台不用每帧重算，手法在一条电文里是不变的。
            if (_fistCallsign == station.Callsign)
            {
                return;
            }

            _fistCallsign = station.Callsign;

            // 传真是机器逐行扫出来的，没有人在敲键。硬去量它的"手法"只会
            // 从连续载波里量出一组毫无意义的数字，而玩家会把它当真。
            if (station.Facsimile != null)
            {
                _fistText.text = "机器扫描，没有手法可认。";
                _fistArchiveText.text = string.Empty;
                return;
            }

            var measured = FistAnalyzer.Measure(station.Timeline);
            _fistText.text = measured.Describe();

            // 对照的是玩家自己听出来的档案，不是内容表里的标准答案。
            // 第一次听到某个台时本来就没有对照——那正是他还没积累到的东西。
            var archived = _campaign.ArchivedFist(station.Callsign);
            _fistArchiveText.text = archived.IsValid
                ? archived.Describe()
                : "第一次听到这个呼号。";

            _campaign.NoteFist(station.Callsign, _shift != null ? _shift.shiftId : string.Empty, measured);
            RefreshArchivePanel();
        }

        /// <summary>
        /// 把档案摊开。每个台一行：呼号、第一次听到的班次、听过几次，
        /// 以及当时听出来的手法。
        /// </summary>
        private void RefreshArchivePanel()
        {
            if (_archiveText == null || !_archivePanel.gameObject.activeSelf)
            {
                return;
            }

            if (_campaign.fistArchive.Count == 0)
            {
                _archiveText.text = "还没有听过任何电台。";
                return;
            }

            var builder = new StringBuilder();
            foreach (var record in _campaign.fistArchive)
            {
                builder.Append(record.callsign)
                    .Append("    ")
                    .Append(ShiftTitle(record.firstHeardShift))
                    .Append(" 第一次听到")
                    .Append(record.timesHeard > 1 ? $"，之后又听过 {record.timesHeard - 1} 次" : string.Empty)
                    .Append('\n')
                    .Append("        ")
                    .Append(record.ToFist().Describe())
                    .Append("\n\n");
            }

            _archiveText.text = builder.ToString();
        }

        private static string ShiftTitle(string shiftId)
        {
            foreach (var shift in ShiftLibrary.All())
            {
                if (shift.shiftId == shiftId)
                {
                    return shift.title;
                }
            }

            return shiftId;
        }

        private void RefreshPad()
        {
            if (_padPageText == null)
            {
                return;
            }

            var seed = _shift?.Primary?.padBookSeed ?? 19851104;
            _padPageText.text = $"第 {_padPage:D3} 页";
            var page = OneTimePad.GeneratePage(seed, _padPage);
            _padDigitsText.text = GroupForReading(page.Substring(0, Mathf.Min(40, page.Length)));
        }

        private void RefreshForm()
        {
            if (_formCallsignText == null)
            {
                return;
            }

            _formCallsignText.text = _callsignBuffer.Length > 0
                ? _callsignBuffer.ToString()
                : "___";
            _formFrequencyText.text = _frequencyBuffer.Length > 0
                ? _frequencyBuffer.ToString()
                : "____";

            // 没填的栏也要看得清。玩家得先看见那里能填，才会去填。
            _formCallsignText.color = _focus == InputFocus.Callsign ? Amber
                : _callsignBuffer.Length > 0 ? Phosphor : PhosphorDim;
            _formFrequencyText.color = _focus == InputFocus.Frequency ? Amber
                : _frequencyBuffer.Length > 0 ? Phosphor : PhosphorDim;
            _copiedText.color = _focus == InputFocus.Copy ? Phosphor : PhosphorDim;

            _focusHintText.text = _focus == InputFocus.Copy ? "正在填：抄收纸"
                : _focus == InputFocus.Callsign ? "正在填：呼号"
                : "正在填：频率";
        }

        private static string AssistLabel(CopyAssist assist)
        {
            switch (assist)
            {
                case CopyAssist.Symbols: return "点划";
                case CopyAssist.Characters: return "字符";
                default: return "关闭";
            }
        }

        // ---------- 数据刷新 ----------

        private void RefreshReadouts()
        {
            if (receiver == null)
            {
                return;
            }

            _frequencyText.text = $"{receiver.tunedKHz,9:F2} kHz";

            var level = receiver.SignalLevel;
            _signalBar.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(level), 1f);
            _signalBar.color = level > 0.75f ? Phosphor : level > 0.3f ? Amber : Alert;
            _signalText.text = level <= 0.001f
                ? "无信号"
                : $"信号 {Mathf.RoundToInt(level * 100f),3}%";

            // 失配方向提示。这是玩家判断该往哪边转的主要视觉线索，
            // 听力线索是音调，两者互为备份，听障玩家只靠这里也能玩。
            var detune = receiver.DetuneKHz;
            if (level <= 0.001f)
            {
                _tuneHint.text = "";
            }
            else if (Mathf.Abs(detune) < 0.06f)
            {
                _tuneHint.text = "◆ 已对准";
                _tuneHint.color = Phosphor;
            }
            else
            {
                _tuneHint.text = detune > 0f ? "▶ 向上微调" : "◀ 向下微调";
                _tuneHint.color = Amber;
            }

            var station = receiver.CurrentStation;
            _hoverText.text = station != null && level > 0.25f
                ? $"截获呼号 {station.Callsign}"
                : "";
        }

        private void HandleTypedInput()
        {
            var typed = Input.inputString;
            if (string.IsNullOrEmpty(typed))
            {
                return;
            }

            var target = _focus == InputFocus.Callsign ? _callsignBuffer
                : _focus == InputFocus.Frequency ? _frequencyBuffer
                : _copyBuffer;

            foreach (var c in typed)
            {
                if (c == '\b')
                {
                    if (target.Length > 0)
                    {
                        target.Length--;
                    }
                }
                else if (c == '\n' || c == '\r')
                {
                    SubmitReport();
                }
                else if (c == '\t')
                {
                    // 制表符由 HandleHotkeys 处理，这里吞掉避免它落进文本
                }
                else if (!char.IsControl(c))
                {
                    // 频率栏只收数字和小数点，免得玩家把呼号敲进去还不自知
                    if (_focus == InputFocus.Frequency && !char.IsDigit(c) && c != '.')
                    {
                        continue;
                    }

                    target.Append(char.ToUpperInvariant(c));
                }
            }

            RefreshCopyArea();
            RefreshForm();
        }

        private void RefreshCopyArea()
        {
            var raw = _copyBuffer.ToString();
            _copiedText.text = string.IsNullOrEmpty(raw)
                ? "<等待抄收>"
                : GroupForReading(raw);

            // 这里刻意不自动翻译。查表是这个职业的核心动作，
            // 替玩家做掉之后，中文电码就只剩一个设定而不是玩法。
            // 译文由玩家按 L 一组一组查出来。
        }

        /// <summary>四位一组显示，和真实报务纸的分组习惯一致，也方便玩家核对。</summary>
        private static string GroupForReading(string raw)
        {
            var builder = new StringBuilder(raw.Length + raw.Length / 4);
            for (var i = 0; i < raw.Length; i++)
            {
                if (i > 0 && i % 4 == 0)
                {
                    builder.Append(' ');
                }

                builder.Append(raw[i]);
            }

            return builder.ToString();
        }

        // ---------- 上报 ----------

        public void CycleLevel(int direction)
        {
            var next = (int)_selectedLevel + direction;
            next = Mathf.Clamp(next, 0, (int)ThreatLevel.Flash);
            _selectedLevel = (ThreatLevel)next;
            _levelText.text = LevelLabel(_selectedLevel);
        }

        public void SubmitReport()
        {
            if (_shift == null || receiver == null)
            {
                return;
            }

            var station = receiver.CurrentStation;
            var target = FindEntry(station?.Callsign) ?? _shift.Primary;
            if (target == null)
            {
                AppendLog("这一班没有待上报的信号");
                return;
            }

            // 呼号和频率都取玩家自己填的。以前这两项是系统代填的，
            // 等于把"记录截获参数"这一步从玩家手里拿走了。
            float.TryParse(_frequencyBuffer.ToString(), out var reportedFrequency);

            // 加密电文按解密后的数字判分，玩家没解密就拿原始密文去比，自然对不上。
            var submittedText = _solvedBuffer.Length > 0
                ? _solvedBuffer.ToString()
                : _copyBuffer.ToString();

            var grade = ReportGrader.Grade(target, new ReportSubmission
            {
                Callsign = _callsignBuffer.ToString(),
                FrequencyKHz = reportedFrequency,
                CopiedText = submittedText,
                Level = _selectedLevel,
            }, _telegraph);

            _verdictText.text = DescribeOutcome(grade);
            _verdictText.color = grade.Outcome == ReportOutcome.Clean ? Phosphor
                : grade.Outcome == ReportOutcome.Useless ? Alert : Amber;

            _replyText.text = ReportAftermath.Reply(target, grade);
            _noteText.text = "黑板：" + ReportAftermath.DeskNote(target, grade);

            _campaign.RecordAndAdvance(new ReportRecord
            {
                shiftId = _shift.shiftId,
                callsign = target.callsign,
                outcome = grade.Outcome,
                submittedLevel = _selectedLevel,
                correctLevel = target.correctLevel,
                accuracy = grade.Accuracy,
            });
            SaveSystem.Save(_campaign);
            _shiftComplete = true;

            AppendLog("本班结束。按 N 交班。");
            AppendLog($"已送出 · 准确度 {grade.Accuracy:P0} · {LevelLabel(_selectedLevel)}" +
                      $" · 呼号{(grade.CallsignCorrect ? "对" : "错")}" +
                      $" · 频率{(grade.FrequencyCorrect ? "对" : "错")}");
            AppendLog("次日报纸：" + ReportAftermath.Headline(target, grade));
        }

        private TransmissionEntry FindEntry(string callsign)
        {
            if (string.IsNullOrEmpty(callsign) || _shift == null)
            {
                return null;
            }

            foreach (var entry in _shift.transmissions)
            {
                if (string.Equals(entry.callsign, callsign, StringComparison.OrdinalIgnoreCase))
                {
                    return entry;
                }
            }

            return null;
        }

        private static string DescribeOutcome(ReportGrade grade)
        {
            switch (grade.Outcome)
            {
                case ReportOutcome.Clean:
                    return $"上报清晰 · 译文「{grade.DecodedText}」";
                case ReportOutcome.Overreacted:
                    return $"等级报高了 · 译文「{grade.DecodedText}」";
                case ReportOutcome.Underreported:
                    return $"等级报低了 · 译文「{grade.DecodedText}」";
                case ReportOutcome.Garbled:
                    return $"电文有错漏 · 你抄成「{grade.DecodedText}」";
                default:
                    return "这份上报没有价值";
            }
        }

        private static string LevelLabel(ThreatLevel level)
        {
            switch (level)
            {
                case ThreatLevel.Attention: return "注意";
                case ThreatLevel.Urgent: return "紧急";
                case ThreatLevel.Flash: return "最高优先";
                default: return "例行";
            }
        }

        private void AppendLog(string line)
        {
            _log.Add(line);
            // 五行是这块面板装得下的极限。多一行就会顶出去。
            while (_log.Count > 5)
            {
                _log.RemoveAt(0);
            }

            _logText.text = string.Join("\n", _log);
        }

        // ---------- 界面构建 ----------

        private void BuildUi()
        {
            var canvasGo = new GameObject("StationCanvas",
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);

            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            var root = canvasGo.transform;

            // 班次横幅贴着左上角的接收机面板放，不占画面中央——
            // 那块要留给 CRT 示波器，它是玩家读电码的地方，任何东西挡住都不行。
            var bannerPanel = Panel(root, "BannerPanel",
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(40f, -32f), new Vector2(470f, 46f));
            _bannerText = Label(bannerPanel, "", 24, Amber, TextAnchor.MiddleLeft,
                new Vector2(20f, -23f), new Vector2(430f, 40f));
            _bannerText.rectTransform.pivot = new Vector2(0f, 0.5f);

            // 左上：接收机读数
            var receiverPanel = Panel(root, "ReceiverPanel",
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(40f, -96f), new Vector2(470f, 232f));
            Label(receiverPanel, "接 收 机", 22, PhosphorDim, TextAnchor.UpperLeft,
                new Vector2(20f, -16f), new Vector2(300f, 30f));
            _frequencyText = Label(receiverPanel, "0000.00 kHz", 42, Phosphor, TextAnchor.UpperLeft,
                new Vector2(20f, -50f), new Vector2(390f, 56f));
            _signalText = Label(receiverPanel, "无信号", 22, PhosphorDim, TextAnchor.UpperLeft,
                new Vector2(20f, -112f), new Vector2(200f, 28f));
            _tuneHint = Label(receiverPanel, "", 22, Amber, TextAnchor.UpperRight,
                new Vector2(-20f, -112f), new Vector2(220f, 28f), anchorRight: true);

            var barBg = Panel(receiverPanel, "SignalBarBg",
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(20f, -150f), new Vector2(390f, 18f),
                new Color(0.05f, 0.12f, 0.07f, 0.9f));
            var barGo = new GameObject("SignalBar", typeof(Image));
            barGo.transform.SetParent(barBg, false);
            _signalBar = barGo.GetComponent<Image>();
            _signalBar.color = Phosphor;
            var barRect = _signalBar.rectTransform;
            barRect.anchorMin = Vector2.zero;
            barRect.anchorMax = new Vector2(0f, 1f);
            barRect.offsetMin = Vector2.zero;
            barRect.offsetMax = Vector2.zero;
            barRect.pivot = new Vector2(0f, 0.5f);

            _hoverText = Label(receiverPanel, "", 22, Amber, TextAnchor.UpperLeft,
                new Vector2(20f, -180f), new Vector2(390f, 28f));

            // 右上：抄收与译文
            var copyPanel = Panel(root, "CopyPanel",
                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-40f, -96f), new Vector2(620f, 320f),
                pivot: new Vector2(1f, 1f));
            Label(copyPanel, "抄 收 纸", 22, PhosphorDim, TextAnchor.UpperLeft,
                new Vector2(20f, -16f), new Vector2(300f, 30f));
            _copiedText = Label(copyPanel, "<等待抄收>", 30, Phosphor, TextAnchor.UpperLeft,
                new Vector2(20f, -52f), new Vector2(580f, 96f));
            Label(copyPanel, "中文电码译文", 22, PhosphorDim, TextAnchor.UpperLeft,
                new Vector2(20f, -156f), new Vector2(300f, 30f));
            _lookupText = Label(copyPanel, "", 40, Amber, TextAnchor.UpperLeft,
                new Vector2(20f, -196f), new Vector2(580f, 96f));
            _decodedText = _lookupText;

            // 右中：实时接收。这是给新手和听障玩家的台阶，
            // 三档可切，关掉之后这一块只显示一行提示，不占额外空间。
            var livePanel = Panel(root, "LivePanel",
                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-40f, -428f), new Vector2(620f, 152f),
                pivot: new Vector2(1f, 1f));
            Label(livePanel, "实时接收", 22, PhosphorDim, TextAnchor.UpperLeft,
                new Vector2(20f, -14f), new Vector2(240f, 30f));
            _assistText = Label(livePanel, AssistLabel(_assist), 20, Amber, TextAnchor.UpperRight,
                new Vector2(-20f, -14f), new Vector2(300f, 30f), anchorRight: true);
            Label(livePanel, "F1 切换辅助档", 18, PhosphorDim, TextAnchor.UpperRight,
                new Vector2(-20f, -40f), new Vector2(300f, 26f), anchorRight: true);
            _liveCopyText = Label(livePanel, "", 26, Phosphor, TextAnchor.UpperLeft,
                new Vector2(20f, -50f), new Vector2(580f, 92f));

            // 底部：上报单
            var reportPanel = Panel(root, "ReportPanel",
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 30f), new Vector2(1240f, 184f),
                pivot: new Vector2(0.5f, 0f));
            Label(reportPanel, "电报上报单", 22, PhosphorDim, TextAnchor.UpperLeft,
                new Vector2(24f, -14f), new Vector2(300f, 30f));
            // 上报单分四列排：等级在左，截获参数在中，操作提示在右，判定在最右。
            // 挤在一起会让玩家在最需要看清的时候读错自己填了什么。
            Label(reportPanel, "威胁等级", 24, PhosphorDim, TextAnchor.UpperLeft,
                new Vector2(28f, -58f), new Vector2(220f, 32f));
            _levelText = Label(reportPanel, LevelLabel(_selectedLevel), 32, Amber, TextAnchor.UpperLeft,
                new Vector2(28f, -94f), new Vector2(280f, 50f));

            Label(reportPanel, "呼号", 22, PhosphorDim, TextAnchor.UpperLeft,
                new Vector2(320f, -16f), new Vector2(140f, 28f));
            _formCallsignText = Label(reportPanel, "___", 26, PhosphorDim, TextAnchor.UpperLeft,
                new Vector2(320f, -46f), new Vector2(220f, 40f));
            Label(reportPanel, "频率 kHz", 22, PhosphorDim, TextAnchor.UpperLeft,
                new Vector2(320f, -94f), new Vector2(160f, 28f));
            _formFrequencyText = Label(reportPanel, "____", 26, PhosphorDim, TextAnchor.UpperLeft,
                new Vector2(320f, -124f), new Vector2(240f, 40f));

            _focusHintText = Label(reportPanel, "正在填：抄收纸", 24, Amber, TextAnchor.UpperLeft,
                new Vector2(600f, -16f), new Vector2(320f, 30f));
            Label(reportPanel, "Tab 换栏 · L 查电码表", 20, PhosphorDim, TextAnchor.UpperLeft,
                new Vector2(600f, -54f), new Vector2(340f, 28f));
            Label(reportPanel, "[ ] 翻页 · D 用当页解密", 20, PhosphorDim, TextAnchor.UpperLeft,
                new Vector2(600f, -86f), new Vector2(340f, 28f));
            Label(reportPanel, "← → 等级 · 回车 送出", 20, PhosphorDim, TextAnchor.UpperLeft,
                new Vector2(600f, -118f), new Vector2(340f, 28f));

            _verdictText = Label(reportPanel, "", 24, Phosphor, TextAnchor.UpperRight,
                new Vector2(-28f, -16f), new Vector2(280f, 150f), anchorRight: true);

            // 档案。平时收起来，按 F2 摊开。
            // 玩家隔了几天回来接着玩，记不住某个台上次听起来什么样，
            // 这层玩法唯一的线索不能只存在他脑子里。
            _archivePanel = Panel(root, "ArchivePanel",
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(900f, 560f),
                pivot: new Vector2(0.5f, 0.5f));
            Label(_archivePanel, "电台档案", 30, Amber, TextAnchor.UpperLeft,
                new Vector2(32f, -24f), new Vector2(400f, 40f));
            Label(_archivePanel, "F2 收起", 22, PhosphorDim, TextAnchor.UpperRight,
                new Vector2(-32f, -24f), new Vector2(200f, 32f), anchorRight: true);
            _archiveText = Label(_archivePanel, "", 22, Phosphor, TextAnchor.UpperLeft,
                new Vector2(32f, -76f), new Vector2(836f, 460f));
            _archivePanel.gameObject.SetActive(false);

            // 左下：节奏分析。手法是这个电台身份的一部分，
            // 呼号可以伪造，手伪造不了。面板只描述听到的东西，不下结论。
            var fistPanel = Panel(root, "FistPanel",
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(40f, -508f), new Vector2(470f, 144f));
            Label(fistPanel, "节奏分析", 22, PhosphorDim, TextAnchor.UpperLeft,
                new Vector2(20f, -14f), new Vector2(260f, 30f));
            _fistText = Label(fistPanel, "没有可分析的信号。", 20, Phosphor, TextAnchor.UpperLeft,
                new Vector2(20f, -42f), new Vector2(430f, 30f));
            Label(fistPanel, "档案", 20, PhosphorDim, TextAnchor.UpperLeft,
                new Vector2(20f, -76f), new Vector2(120f, 26f));
            _fistArchiveText = Label(fistPanel, "", 20, Amber, TextAnchor.UpperLeft,
                new Vector2(20f, -102f), new Vector2(430f, 30f));

            // 左中：密码本。玩家要自己从报头读页码再翻到那一页。
            var padPanel = Panel(root, "PadPanel",
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(40f, -336f), new Vector2(470f, 156f));
            Label(padPanel, "一次性密码本", 22, PhosphorDim, TextAnchor.UpperLeft,
                new Vector2(20f, -14f), new Vector2(260f, 30f));
            _padPageText = Label(padPanel, "第 001 页", 24, Amber, TextAnchor.UpperRight,
                new Vector2(-20f, -14f), new Vector2(200f, 30f), anchorRight: true);
            Label(padPanel, "[ ] 翻页 · D 解密", 18, PhosphorDim, TextAnchor.UpperRight,
                new Vector2(-20f, -42f), new Vector2(240f, 26f), anchorRight: true);
            _padDigitsText = Label(padPanel, "", 20, Phosphor, TextAnchor.UpperLeft,
                new Vector2(20f, -66f), new Vector2(430f, 80f));

            // 右上抄收纸下方补一块解密结果
            _solvedText = Label(copyPanel, "", 24, Phosphor, TextAnchor.UpperLeft,
                new Vector2(20f, -262f), new Vector2(580f, 48f));
            Label(copyPanel, "解密结果", 20, PhosphorDim, TextAnchor.UpperLeft,
                new Vector2(20f, -238f), new Vector2(240f, 26f));

            // 右下：后果。这套设计里没有分数，玩家只从回电和字条知道自己干得怎么样。
            var replyPanel = Panel(root, "ReplyPanel",
                new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-40f, 40f), new Vector2(620f, 176f),
                pivot: new Vector2(1f, 0f));
            Label(replyPanel, "回 电", 22, PhosphorDim, TextAnchor.UpperLeft,
                new Vector2(20f, -14f), new Vector2(240f, 30f));
            _replyText = Label(replyPanel, "", 24, Phosphor, TextAnchor.UpperLeft,
                new Vector2(20f, -46f), new Vector2(580f, 66f));
            _noteText = Label(replyPanel, "", 20, Amber, TextAnchor.UpperLeft,
                new Vector2(20f, -116f), new Vector2(580f, 52f));

            // 左下：值班日志
            var logPanel = Panel(root, "LogPanel",
                new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(40f, 236f), new Vector2(560f, 152f),
                pivot: new Vector2(0f, 0f));
            // 文字区要比面板矮。Text 不会自己裁切，装不下就直接画到面板外面去，
            // 压在上一块面板上。
            _logText = Label(logPanel, "", 20, PhosphorDim, TextAnchor.LowerLeft,
                new Vector2(20f, 16f), new Vector2(520f, 120f), anchorBottom: true);
        }

        private static RectTransform Panel(Transform parent, string name,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 position, Vector2 size,
            Color? background = null, Vector2? pivot = null)
        {
            var go = new GameObject(name, typeof(Image));
            go.transform.SetParent(parent, false);

            var image = go.GetComponent<Image>();
            image.color = background ?? PanelBg;
            image.raycastTarget = false;

            var rect = image.rectTransform;
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot ?? new Vector2(0f, 1f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }

        private Text Label(Transform parent, string content, int fontSize, Color color,
            TextAnchor alignment, Vector2 position, Vector2 size,
            bool anchorRight = false, bool anchorBottom = false)
        {
            var go = new GameObject("Label", typeof(Text));
            go.transform.SetParent(parent, false);

            var text = go.GetComponent<Text>();
            text.font = _font;
            text.fontSize = fontSize;
            text.color = color;
            text.text = content;
            text.alignment = alignment;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;

            var rect = text.rectTransform;
            var x = anchorRight ? 1f : 0f;
            var y = anchorBottom ? 0f : 1f;
            rect.anchorMin = new Vector2(x, y);
            rect.anchorMax = new Vector2(x, y);
            rect.pivot = new Vector2(x, y);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return text;
        }
    }
}
