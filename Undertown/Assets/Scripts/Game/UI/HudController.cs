using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using Undertown.Core.Buildings;
using Undertown.Core.Economy;
using Undertown.Core.Sim;
using Undertown.Game.InputHandling;

namespace Undertown.Game.UI
{
    /// <summary>
    /// The bottom bar. Four regions, in the order the player consults them during a season:
    /// how long until someone comes to count, how much trouble the town is already in, what
    /// the books currently say, and what can be built about it.
    ///
    /// The ledger panel is the one that decides whether this game works. An audit is an
    /// abstract thing to be threatened by, so the numbers behind it have to be visible at
    /// all times rather than surfaced only when the inspector knocks.
    /// </summary>
    public sealed class HudController : MonoBehaviour
    {
        private const float BarHeight = 232f;
        private const float SectionHeight = BarHeight - 24f;

        private TownState _town;

        private Text _seasonText;
        private Text _phaseText;
        private Text _inspectionText;
        private RectTransform _dialHand;

        private Image _suspicionFill;
        private Text _suspicionValue;
        private Text _suspicionBand;

        private readonly Dictionary<MaterialId, Text> _ledgerRows = new Dictionary<MaterialId, Text>();
        private readonly Dictionary<MaterialId, Button> _writeOffButtons = new Dictionary<MaterialId, Button>();
        private Button _bribeButton;
        private Text _spoilText;
        private Text _layerText;
        private Text _toolText;
        private Text _logText;
        private Text _exposureText;
        private Text _workforceText;
        private Button _wageButton;
        private Text _wageLabel;

        private int _previewedRevision = -1;
        private int _previewedSuspicion;

        private PlayerTools _tools;
        private readonly List<(Button button, BuildingKind kind)> _buildSlots = new List<(Button, BuildingKind)>();
        private Button _excavateButton;
        private Button _demolishButton;

        public void Bind(TownState town, PlayerTools tools)
        {
            _town = town;
            _tools = tools;
            Build();
            Refresh();
        }

        private void Update()
        {
            if (_town != null) Refresh();
        }

        private void Build()
        {
            var canvasObject = new GameObject("HudCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, worldPositionStays: false);

            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 1f;

            var bar = UiFactory.Panel(canvasObject.transform, "BottomBar");
            var barRect = (RectTransform)bar.transform;
            barRect.anchorMin = new Vector2(0f, 0f);
            barRect.anchorMax = new Vector2(1f, 0f);
            barRect.pivot = new Vector2(0.5f, 0f);
            barRect.anchoredPosition = Vector2.zero;
            barRect.sizeDelta = new Vector2(0f, BarHeight);

            BuildClock(barRect);
            BuildSuspicion(barRect);
            BuildLedger(barRect);
            BuildBuildMenu(barRect);
            BuildLayerBadge(canvasObject.transform);
        }

        private void BuildClock(RectTransform bar)
        {
            var section = UiFactory.Panel(bar, "Clock", ProceduralUiArt.Inset);
            UiFactory.Place((RectTransform)section.transform, 14f, 12f, 380f, SectionHeight);

            var dial = UiFactory.Root(section.transform, "Dial");
            var dialImage = dial.gameObject.AddComponent<Image>();
            dialImage.sprite = ProceduralUiArt.SeasonDial;
            UiFactory.Place(dial, 12f, 30f, 148f, 148f);

            _dialHand = UiFactory.Root(dial, "Hand");
            var handImage = _dialHand.gameObject.AddComponent<Image>();
            handImage.sprite = ProceduralUiArt.DialHand;
            _dialHand.anchorMin = _dialHand.anchorMax = new Vector2(0.5f, 0.5f);
            _dialHand.pivot = new Vector2(0.5f, 0f);
            _dialHand.anchoredPosition = Vector2.zero;
            _dialHand.sizeDelta = new Vector2(11f, 60f);

            _seasonText = UiFactory.Label(section.transform, "Season", "", 22, ProceduralUiArt.Ink);
            UiFactory.Place((RectTransform)_seasonText.transform, 170f, 150f, 200f, 30f);

            _phaseText = UiFactory.Label(section.transform, "Phase", "", 17, ProceduralUiArt.InkDim);
            UiFactory.Place((RectTransform)_phaseText.transform, 170f, 122f, 200f, 26f);

            _inspectionText = UiFactory.Label(section.transform, "NextInspection", "", 18, ProceduralUiArt.Danger);
            UiFactory.Place((RectTransform)_inspectionText.transform, 170f, 56f, 200f, 56f);
        }

        private void BuildSuspicion(RectTransform bar)
        {
            var section = UiFactory.Panel(bar, "Suspicion", ProceduralUiArt.Inset);
            UiFactory.Place((RectTransform)section.transform, 408f, 12f, 320f, SectionHeight);

            var title = UiFactory.Label(section.transform, "Title", "SUSPICION", 20, ProceduralUiArt.InkDim);
            UiFactory.Place((RectTransform)title.transform, 16f, 176f, 200f, 26f);

            var track = UiFactory.Fill(section.transform, "Track", new Color32(0x0C, 0x0A, 0x08, 0xFF));
            UiFactory.Place((RectTransform)track.transform, 16f, 142f, 288f, 28f);

            _suspicionFill = UiFactory.Fill(track.transform, "Fill", ProceduralUiArt.Danger);
            var fillRect = (RectTransform)_suspicionFill.transform;
            fillRect.anchorMin = new Vector2(0f, 0f);
            fillRect.anchorMax = new Vector2(0f, 1f);
            fillRect.pivot = new Vector2(0f, 0.5f);
            fillRect.anchoredPosition = new Vector2(2f, 0f);
            fillRect.sizeDelta = new Vector2(0f, -4f);

            _suspicionValue = UiFactory.Label(section.transform, "Value", "", 26, ProceduralUiArt.Ink, TextAnchor.MiddleRight);
            UiFactory.Place((RectTransform)_suspicionValue.transform, 200f, 172f, 104f, 30f);

            _suspicionBand = UiFactory.Label(section.transform, "Band", "", 20, ProceduralUiArt.Ink);
            UiFactory.Place((RectTransform)_suspicionBand.transform, 16f, 110f, 288f, 26f);

            _spoilText = UiFactory.Label(section.transform, "Spoil", "", 17, ProceduralUiArt.InkDim);
            UiFactory.Place((RectTransform)_spoilText.transform, 16f, 84f, 288f, 26f);

            // Loyalty belongs beside suspicion rather than in a panel of its own: an
            // inspector's questions are answered out of the town's discontent, so the two
            // numbers are read together or not at all.
            _workforceText = UiFactory.Label(section.transform, "Workforce", "", 17, ProceduralUiArt.InkDim);
            UiFactory.Place((RectTransform)_workforceText.transform, 16f, 56f, 288f, 26f);

            _wageButton = MakeButton(section.transform, "Wages", "", 16f, 12f, 288f, 34f);
            _wageButton.onClick.AddListener(CycleWages);
        }

        /// <summary>
        /// Wages are the one lever that trades coin directly for silence, so it sits one
        /// click away rather than behind a management screen.
        /// </summary>
        private void CycleWages()
        {
            _town.Wages = _town.Wages == WageLevel.Generous ? WageLevel.Meagre : _town.Wages + 1;
            _town.Record($"wages set to {NeedsSystem.WageLabel(_town.Wages)}");
        }

        private void BuildLedger(RectTransform bar)
        {
            var section = UiFactory.Panel(bar, "Ledger", ProceduralUiArt.Inset);
            UiFactory.Place((RectTransform)section.transform, 742f, 12f, 590f, SectionHeight);

            var title = UiFactory.Label(section.transform, "Title", "LEDGER", 20, ProceduralUiArt.InkDim);
            UiFactory.Place((RectTransform)title.transform, 16f, 174f, 200f, 24f);

            // Sits on the title row rather than beside the verdict line: the verdict can run
            // long ("+56 suspicion -> 56% Fines Pending") and was overlapping the button.
            _bribeButton = MakeButton(section.transform, "Bribe", "", 386f, 170f, 188f, 28f);
            _bribeButton.onClick.AddListener(() => BookCooking.BribeTheClerk(_town));

            var header = UiFactory.Label(section.transform, "Header",
                Row("MATERIAL", "MADE", "USED", "LOSS", "STOCK", "GAP"), 16, ProceduralUiArt.InkDim);
            UiFactory.Place((RectTransform)header.transform, 16f, 150f, 558f, 22f);

            float y = 126f;
            foreach (var id in Materials.All)
            {
                if (!Materials.IsAudited(id)) continue;

                var row = UiFactory.Label(section.transform, $"Row_{id}", "", 16, ProceduralUiArt.Ink);
                UiFactory.Place((RectTransform)row.transform, 16f, y, 470f, 19f);
                _ledgerRows[id] = row;

                // A write-off button per row, because spoilage is declared against a specific
                // material and the player needs to see the hole it is closing while they do it.
                var material = id;
                var writeOff = MakeButton(section.transform, $"WriteOff_{id}", "write off", 490f, y - 1f, 84f, 20f);
                writeOff.onClick.AddListener(() => WriteOffGap(material));
                _writeOffButtons[id] = writeOff;

                y -= 19f;
            }

            // The verdict line. Everything above it is evidence; this is what the evidence
            // adds up to, and it is the number the player actually steers by.
            _exposureText = UiFactory.Label(section.transform, "Exposure", "", 18, ProceduralUiArt.Ink);
            UiFactory.Place((RectTransform)_exposureText.transform, 16f, 8f, 558f, 22f);
        }

        /// <summary>
        /// Writes off as much of a material's shortfall as the region's normal loss rate can
        /// still explain. Deliberately capped at the plausible figure rather than the whole
        /// gap: the decision the player faces is how much they can account for, not how much
        /// they dare type in.
        /// </summary>
        private void WriteOffGap(MaterialId material)
        {
            int plausible = BookCooking.PlausibleSpoilage(_town, material);
            int gap = _town.LedgerGap(material);
            if (gap <= 0) return;

            int written = BookCooking.DeclareSpoilage(_town, material, Mathf.Min(gap, plausible));
            if (written == 0)
                _town.Record($"nothing more can be written off against {Materials.DisplayName(material)}");
        }

        private void BuildBuildMenu(RectTransform bar)
        {
            var section = UiFactory.Panel(bar, "BuildMenu", ProceduralUiArt.Inset);
            var rect = (RectTransform)section.transform;
            rect.anchorMin = new Vector2(1f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(1f, 0f);
            rect.anchoredPosition = new Vector2(-14f, 12f);
            rect.sizeDelta = new Vector2(560f, SectionHeight);

            var title = UiFactory.Label(section.transform, "Title", "BUILD", 20, ProceduralUiArt.InkDim);
            UiFactory.Place((RectTransform)title.transform, 16f, 174f, 200f, 24f);

            // Surface trades on the top row, the works below on the bottom, so the two halves
            // of the town are never one misclick apart.
            var slots = new (BuildingKind kind, string caption)[]
            {
                (BuildingKind.House, "House"),
                (BuildingKind.Sawpit, "Sawpit"),
                (BuildingKind.ClayPit, "Clay Pit"),
                (BuildingKind.Field, "Field"),
                (BuildingKind.Brewery, "Brewery"),
                (BuildingKind.Warehouse, "Store"),
                (BuildingKind.Tunnel, "Tunnel"),
                (BuildingKind.Still, "Still"),
                (BuildingKind.UnderStore, "Cellar"),
                (BuildingKind.FalseWall, "Fake Wall"),
            };

            for (int i = 0; i < slots.Length; i++)
            {
                float x = 16f + (i % 5) * 106f;
                float y = 104f - (i / 5) * 66f;

                var kind = slots[i].kind;
                var button = MakeButton(section.transform, $"Slot_{kind}", slots[i].caption, x, y, 98f, 60f);
                button.onClick.AddListener(() => _tools.SelectBuilding(kind));
                _buildSlots.Add((button, kind));
            }

            _excavateButton = MakeButton(section.transform, "Excavate", "Dig  (E)", 16f, 8f, 152f, 52f);
            _excavateButton.onClick.AddListener(() => _tools.SelectExcavate());

            _demolishButton = MakeButton(section.transform, "Demolish", "Demolish  (X)", 176f, 8f, 152f, 52f);
            _demolishButton.onClick.AddListener(() => _tools.SelectDemolish());

            var cancel = MakeButton(section.transform, "Cancel", "Cancel  (Esc)", 336f, 8f, 152f, 52f);
            cancel.onClick.AddListener(() => _tools.Cancel());
        }

        private static Button MakeButton(Transform parent, string name, string caption,
            float x, float y, float width, float height)
        {
            var panel = UiFactory.Panel(parent, name);
            UiFactory.Place((RectTransform)panel.transform, x, y, width, height);

            var button = panel.gameObject.AddComponent<Button>();
            button.targetGraphic = panel;

            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.25f, 1.2f, 1.1f);
            colors.pressedColor = new Color(0.8f, 0.78f, 0.72f);
            button.colors = colors;

            var label = UiFactory.Label(panel.transform, "Caption", caption, 16, ProceduralUiArt.InkDim, TextAnchor.MiddleCenter);
            UiFactory.Stretch((RectTransform)label.transform, 4f, 4f, 4f, 4f);
            return button;
        }

        private void BuildLayerBadge(Transform canvas)
        {
            var badge = UiFactory.Panel(canvas, "LayerBadge");
            var rect = (RectTransform)badge.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(16f, -16f);
            rect.sizeDelta = new Vector2(520f, 44f);

            _layerText = UiFactory.Label(badge.transform, "Text", "", 20, ProceduralUiArt.Ink, TextAnchor.MiddleCenter);
            UiFactory.Stretch((RectTransform)_layerText.transform, 8f, 4f, 8f, 4f);

            var toolBadge = UiFactory.Panel(canvas, "ToolBadge");
            var toolRect = (RectTransform)toolBadge.transform;
            toolRect.anchorMin = toolRect.anchorMax = new Vector2(0f, 1f);
            toolRect.pivot = new Vector2(0f, 1f);
            toolRect.anchoredPosition = new Vector2(16f, -68f);
            toolRect.sizeDelta = new Vector2(520f, 40f);

            _toolText = UiFactory.Label(toolBadge.transform, "Text", "", 18, ProceduralUiArt.Contraband, TextAnchor.MiddleCenter);
            UiFactory.Stretch((RectTransform)_toolText.transform, 8f, 4f, 8f, 4f);
            toolBadge.gameObject.SetActive(false);

            // The log is where the audit explains itself. A player who is told only that
            // suspicion went up has no way to work out which of their arrangements failed.
            var logPanel = UiFactory.Panel(canvas, "Log");
            var logRect = (RectTransform)logPanel.transform;
            logRect.anchorMin = logRect.anchorMax = new Vector2(1f, 1f);
            logRect.pivot = new Vector2(1f, 1f);
            logRect.anchoredPosition = new Vector2(-16f, -16f);
            logRect.sizeDelta = new Vector2(560f, 220f);

            _logText = UiFactory.Label(logPanel.transform, "Text", "", 16, ProceduralUiArt.InkDim, TextAnchor.UpperLeft);
            UiFactory.Stretch((RectTransform)_logText.transform, 12f, 10f, 12f, 10f);
        }

        private void Refresh()
        {
            var clock = _town.Clock;

            _seasonText.text = $"Season {clock.Season + 1} · Day {clock.DayOfSeason}";
            _phaseText.text = $"{clock.PhaseName} · {clock.TimeOfDayLabel}";

            int daysOut = clock.DaysUntilInspection;
            string visit = SimClock.IsAuditDay(clock.NextInspectionDay) ? "SEASON AUDIT" : "INSPECTION";
            _inspectionText.text = daysOut == 0
                ? $"{visit} TODAY"
                : $"{visit}\nin {daysOut} day{(daysOut == 1 ? "" : "s")}";

            float dayProgress = (clock.DayOfSeason - 1 + clock.MinuteOfDay / (float)SimClock.TicksPerDay)
                                / SimClock.DaysPerSeason;
            _dialHand.localRotation = Quaternion.Euler(0f, 0f, -dayProgress * 360f);

            int suspicion = _town.Suspicion;
            _suspicionValue.text = $"{suspicion}%";
            _suspicionFill.rectTransform.sizeDelta = new Vector2(284f * suspicion / 100f - 4f, -4f);
            _suspicionBand.text = TownState.BandLabel(_town.Band);
            _suspicionBand.color = suspicion >= 50 ? (Color)ProceduralUiArt.Danger : (Color)ProceduralUiArt.Ink;

            int exposure = _town.SpoilExposure;
            _spoilText.text = exposure > 0
                ? $"spoil in the open: {_town.SurfaceSpoil} ({exposure} over)"
                : $"spoil in the open: {_town.SurfaceSpoil}";
            _spoilText.color = exposure > 0 ? (Color)ProceduralUiArt.Danger : (Color)ProceduralUiArt.InkDim;

            RefreshWorkforce();

            foreach (var pair in _ledgerRows)
            {
                var flow = _town.Books.Flow(pair.Key);
                int gap = _town.LedgerGap(pair.Key);

                // Contraband shows countable over actual. Printing only the countable figure
                // read as "the moonshine is gone" when what it means is "the inspector cannot
                // see it", which is the opposite of reassuring.
                int visible = _town.VisibleStock(pair.Key);
                int held = _town.Stock.Get(pair.Key);
                string stockText = Materials.IsContraband(pair.Key) && held != visible
                    ? $"{visible}/{held}"
                    : visible.ToString();

                pair.Value.text = Row(
                    Materials.DisplayName(pair.Key),
                    flow.Produced.ToString(),
                    flow.Consumed.ToString(),
                    flow.DeclaredLoss.ToString(),
                    stockText,
                    gap == 0 ? "-" : gap.ToString("+#;-#"));

                // A material whose books do not match the shelf is the thing to look at, so
                // it is coloured by its gap rather than by whether it is contraband.
                pair.Value.color = gap != 0
                    ? (Color)ProceduralUiArt.Danger
                    : Materials.IsContraband(pair.Key)
                        ? (Color)ProceduralUiArt.Contraband
                        : (Color)ProceduralUiArt.Ink;
            }

            RefreshAuditPreview();
            RefreshCountermeasures();
            RefreshTool();
            RefreshLog();
        }

        /// <summary>
        /// Runs the audit against the current books without an inspector present and reports
        /// what it would cost. Recomputed when the books or the stores actually change rather
        /// than on a timer, so this line can never disagree with the ledger rows above it.
        /// </summary>
        private void RefreshAuditPreview()
        {
            if (_exposureText == null) return;

            int revision = _town.Books.Revision + _town.Stock.Revision;
            if (revision != _previewedRevision)
            {
                _previewedRevision = revision;
                _previewedSuspicion = _town.DryRunAudit().TotalSuspicion;
            }

            int spoilExposure = _town.SpoilExposure;
            int total = _previewedSuspicion + (spoilExposure > 0 ? 2 + spoilExposure / 10 : 0);

            if (total <= 0)
            {
                _exposureText.text = "audited now: nothing to find";
                _exposureText.color = new Color32(0x7C, 0xA6, 0x6B, 0xFF);
                return;
            }

            int projected = Mathf.Min(100, _town.Suspicion + total);
            var band = TownState.BandFor(projected);
            _exposureText.text = $"audited now: +{total} suspicion  ->  {projected}%  {TownState.BandLabel(band)}";
            _exposureText.color = band >= SuspicionBand.Fined
                ? (Color)ProceduralUiArt.Danger
                : (Color)ProceduralUiArt.Contraband;
        }

        /// <summary>
        /// A write-off button is only offered where there is a hole to close and room in the
        /// region's loss rate to close it with. Greying out the rest keeps the interface from
        /// suggesting a move that would only make things worse.
        /// </summary>
        private void RefreshCountermeasures()
        {
            foreach (var pair in _writeOffButtons)
            {
                int gap = _town.LedgerGap(pair.Key);
                int plausible = BookCooking.PlausibleSpoilage(_town, pair.Key);
                bool useful = gap > 0 && plausible > 0 && _town.Coin > 0;

                pair.Value.interactable = useful;
                var label = pair.Value.GetComponentInChildren<Text>();
                if (label == null) continue;

                label.text = useful ? $"write off {Mathf.Min(gap, plausible)}" : "write off";
                label.color = useful ? (Color)ProceduralUiArt.Contraband : new Color32(0x5A, 0x52, 0x46, 0xFF);
            }

            if (_bribeButton == null) return;

            var bribeLabel = _bribeButton.GetComponentInChildren<Text>();
            bool alreadyPaid = _town.BriberyActive;
            bool affordable = _town.Coin >= BookCooking.BribeCost || _town.BlackCoin >= BookCooking.BribeCost;

            _bribeButton.interactable = !alreadyPaid && affordable && _town.InspectorLevel > 1;
            if (bribeLabel == null) return;

            bribeLabel.text = alreadyPaid
                ? "clerk paid"
                : _town.InspectorLevel > 1
                    ? $"bribe clerk ({BookCooking.BribeCost})"
                    : "bribe: no use yet";
            bribeLabel.color = alreadyPaid
                ? new Color32(0x7C, 0xA6, 0x6B, 0xFF)
                : _bribeButton.interactable
                    ? (Color)ProceduralUiArt.Contraband
                    : new Color32(0x5A, 0x52, 0x46, 0xFF);
        }

        private void RefreshWorkforce()
        {
            if (_workforceText == null) return;

            int people = _town.Villagers.Count;
            int loyalty = NeedsSystem.AverageLoyalty(_town);
            int hungry = NeedsSystem.CountHungry(_town);
            int talkers = NeedsSystem.CountWillTalk(_town);

            var text = $"{people} townsfolk · loyalty {loyalty}";
            if (hungry > 0) text += $" · {hungry} hungry";
            if (talkers > 0) text += $" · {talkers} would talk";

            _workforceText.text = text;
            _workforceText.color = talkers > 0
                ? (Color)ProceduralUiArt.Danger
                : hungry > 0
                    ? (Color)ProceduralUiArt.Contraband
                    : (Color)ProceduralUiArt.InkDim;

            if (_wageLabel == null && _wageButton != null)
                _wageLabel = _wageButton.GetComponentInChildren<Text>();

            if (_wageLabel != null)
            {
                int daily = NeedsSystem.WageCost(_town.Wages) * people;
                _wageLabel.text = $"wages: {NeedsSystem.WageLabel(_town.Wages)}  ({daily} coin/day)";
                _wageLabel.color = _town.Wages == WageLevel.Meagre
                    ? (Color)ProceduralUiArt.Danger
                    : (Color)ProceduralUiArt.InkDim;
            }
        }

        private void RefreshTool()
        {
            if (_toolText == null || _tools == null) return;

            string description = _tools.Describe();
            var badge = _toolText.transform.parent.gameObject;
            badge.SetActive(description != null);
            if (description != null) _toolText.text = description;

            // Highlight whichever slot is armed, so the cursor's behaviour is never a mystery.
            foreach (var slot in _buildSlots)
            {
                bool armed = _tools.Mode == ToolMode.Place && _tools.Selected == slot.kind;
                slot.button.image.color = armed ? new Color(1.4f, 1.25f, 0.9f) : Color.white;
            }

            if (_excavateButton != null)
                _excavateButton.image.color = _tools.Mode == ToolMode.Excavate ? new Color(1.4f, 1.25f, 0.9f) : Color.white;
            if (_demolishButton != null)
                _demolishButton.image.color = _tools.Mode == ToolMode.Demolish ? new Color(1.4f, 1.25f, 0.9f) : Color.white;
        }

        private void RefreshLog()
        {
            if (_logText == null) return;

            var log = _town.Log;
            int lines = Mathf.Min(9, log.Count);
            var sb = new StringBuilder();
            for (int i = log.Count - lines; i < log.Count; i++)
            {
                sb.Append(log[i]);
                if (i < log.Count - 1) sb.Append('\n');
            }
            _logText.text = sb.ToString();
        }

        public void SetLayerLabel(string text) { if (_layerText != null) _layerText.text = text; }

        /// <summary>Pads columns with spaces; the HUD font is not monospaced but the widths are close enough to line up.</summary>
        private static string Row(string a, string b, string c, string d, string e, string f)
        {
            var sb = new StringBuilder();
            sb.Append(a.PadRight(14));
            sb.Append(b.PadLeft(9));
            sb.Append(c.PadLeft(9));
            sb.Append(d.PadLeft(9));
            sb.Append(e.PadLeft(12));
            sb.Append(f.PadLeft(10));
            return sb.ToString();
        }
    }
}
