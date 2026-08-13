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
        private static readonly Color Phosphor = new Color(0.42f, 1f, 0.52f, 1f);
        private static readonly Color PhosphorDim = new Color(0.24f, 0.62f, 0.32f, 1f);
        private static readonly Color Amber = new Color(1f, 0.72f, 0.26f, 1f);
        private static readonly Color Alert = new Color(1f, 0.36f, 0.28f, 1f);
        private static readonly Color PanelBg = new Color(0.02f, 0.05f, 0.03f, 0.82f);

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
            _shift = ShiftLibrary.FirstShift();
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
            _log.Clear();
            SetStatusBanner(string.IsNullOrEmpty(shiftTitle) ? shift?.title ?? "" : shiftTitle);
            AppendLog($"值班开始 · {shift?.inGameDate}");
            AppendLog("按住鼠标右键转头，左键拖动旋钮搜频");
            AppendLog("听到电码后用键盘抄下数字或字母");
        }

        private void Update()
        {
            RefreshReadouts();
            HandleTypedInput();
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

            foreach (var c in typed)
            {
                if (c == '\b')
                {
                    if (_copyBuffer.Length > 0)
                    {
                        _copyBuffer.Length--;
                    }
                }
                else if (c == '\n' || c == '\r')
                {
                    SubmitReport();
                }
                else if (!char.IsControl(c))
                {
                    _copyBuffer.Append(char.ToUpperInvariant(c));
                }
            }

            RefreshCopyArea();
        }

        private void RefreshCopyArea()
        {
            var raw = _copyBuffer.ToString();
            _copiedText.text = string.IsNullOrEmpty(raw)
                ? "<等待抄收>"
                : GroupForReading(raw);

            var digits = ChineseTelegraphCode.ToDigitStream(raw);
            _lookupText.text = digits.Length >= ChineseTelegraphCode.CodeLength
                ? _telegraph.DecodeDigits(digits)
                : "";
            _decodedText.text = _lookupText.text;
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

            var grade = ReportGrader.Grade(target, new ReportSubmission
            {
                Callsign = target.callsign,
                FrequencyKHz = receiver.tunedKHz,
                CopiedText = _copyBuffer.ToString(),
                Level = _selectedLevel,
            }, _telegraph);

            _verdictText.text = DescribeOutcome(grade);
            _verdictText.color = grade.Outcome == ReportOutcome.Clean ? Phosphor
                : grade.Outcome == ReportOutcome.Useless ? Alert : Amber;

            AppendLog($"已送出 {target.callsign} · 准确度 {grade.Accuracy:P0} · {LevelLabel(_selectedLevel)}");
            if (!string.IsNullOrEmpty(target.debriefNote))
            {
                AppendLog(target.debriefNote);
            }
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
            while (_log.Count > 6)
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

            // 底部：上报单
            var reportPanel = Panel(root, "ReportPanel",
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 36f), new Vector2(1180f, 160f),
                pivot: new Vector2(0.5f, 0f));
            Label(reportPanel, "电报上报单", 22, PhosphorDim, TextAnchor.UpperLeft,
                new Vector2(24f, -14f), new Vector2(300f, 30f));
            Label(reportPanel, "威胁等级", 22, PhosphorDim, TextAnchor.UpperLeft,
                new Vector2(24f, -50f), new Vector2(200f, 30f));
            _levelText = Label(reportPanel, LevelLabel(_selectedLevel), 32, Amber, TextAnchor.UpperLeft,
                new Vector2(24f, -80f), new Vector2(260f, 44f));
            Label(reportPanel, "← → 调整等级        回车 送出", 20, PhosphorDim, TextAnchor.UpperLeft,
                new Vector2(300f, -84f), new Vector2(460f, 30f));
            _verdictText = Label(reportPanel, "", 26, Phosphor, TextAnchor.UpperRight,
                new Vector2(-24f, -46f), new Vector2(620f, 96f), anchorRight: true);

            // 左下：值班日志
            var logPanel = Panel(root, "LogPanel",
                new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(40f, 216f), new Vector2(560f, 180f),
                pivot: new Vector2(0f, 0f));
            _logText = Label(logPanel, "", 20, PhosphorDim, TextAnchor.LowerLeft,
                new Vector2(20f, 16f), new Vector2(520f, 152f), anchorBottom: true);
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
