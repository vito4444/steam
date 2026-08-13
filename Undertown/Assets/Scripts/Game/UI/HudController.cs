using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using Undertown.Core.Economy;
using Undertown.Core.Sim;

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
        private Text _spoilText;
        private Text _layerText;

        public void Bind(TownState town)
        {
            _town = town;
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
            UiFactory.Place((RectTransform)title.transform, 16f, 164f, 200f, 26f);

            var track = UiFactory.Fill(section.transform, "Track", new Color32(0x0C, 0x0A, 0x08, 0xFF));
            UiFactory.Place((RectTransform)track.transform, 16f, 128f, 288f, 28f);

            _suspicionFill = UiFactory.Fill(track.transform, "Fill", ProceduralUiArt.Danger);
            var fillRect = (RectTransform)_suspicionFill.transform;
            fillRect.anchorMin = new Vector2(0f, 0f);
            fillRect.anchorMax = new Vector2(0f, 1f);
            fillRect.pivot = new Vector2(0f, 0.5f);
            fillRect.anchoredPosition = new Vector2(2f, 0f);
            fillRect.sizeDelta = new Vector2(0f, -4f);

            _suspicionValue = UiFactory.Label(section.transform, "Value", "", 26, ProceduralUiArt.Ink, TextAnchor.MiddleRight);
            UiFactory.Place((RectTransform)_suspicionValue.transform, 200f, 160f, 104f, 30f);

            _suspicionBand = UiFactory.Label(section.transform, "Band", "", 20, ProceduralUiArt.Ink);
            UiFactory.Place((RectTransform)_suspicionBand.transform, 16f, 92f, 288f, 26f);

            _spoilText = UiFactory.Label(section.transform, "Spoil", "", 17, ProceduralUiArt.InkDim);
            UiFactory.Place((RectTransform)_spoilText.transform, 16f, 60f, 288f, 26f);
        }

        private void BuildLedger(RectTransform bar)
        {
            var section = UiFactory.Panel(bar, "Ledger", ProceduralUiArt.Inset);
            UiFactory.Place((RectTransform)section.transform, 742f, 12f, 590f, SectionHeight);

            var title = UiFactory.Label(section.transform, "Title", "LEDGER", 20, ProceduralUiArt.InkDim);
            UiFactory.Place((RectTransform)title.transform, 16f, 174f, 200f, 24f);

            var header = UiFactory.Label(section.transform, "Header",
                Row("MATERIAL", "BOUGHT", "MADE", "USED", "LOSS", "STOCK"), 16, ProceduralUiArt.InkDim);
            UiFactory.Place((RectTransform)header.transform, 16f, 150f, 558f, 22f);

            float y = 126f;
            foreach (var id in Materials.All)
            {
                if (!Materials.IsAudited(id)) continue;
                var row = UiFactory.Label(section.transform, $"Row_{id}", "", 16, ProceduralUiArt.Ink);
                UiFactory.Place((RectTransform)row.transform, 16f, y, 558f, 19f);
                _ledgerRows[id] = row;
                y -= 19f;
            }
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

            // Placeholder slots. Wiring these to real placement is the next increment; the
            // slots exist now so the bar's proportions can be judged against the target art.
            string[] slots = { "Lodge", "Sawpit", "Clay", "Field", "Brewery", "Store", "House", "Tunnel", "Still", "Wall" };
            for (int i = 0; i < slots.Length; i++)
            {
                float x = 16f + (i % 5) * 106f;
                float y = 104f - (i / 5) * 66f;

                var slot = UiFactory.Panel(section.transform, $"Slot_{slots[i]}");
                UiFactory.Place((RectTransform)slot.transform, x, y, 98f, 60f);

                var caption = UiFactory.Label(slot.transform, "Caption", slots[i], 16, ProceduralUiArt.InkDim, TextAnchor.MiddleCenter);
                UiFactory.Stretch((RectTransform)caption.transform, 4f, 4f, 4f, 4f);
            }
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

            foreach (var pair in _ledgerRows)
            {
                var flow = _town.Books.Flow(pair.Key);
                pair.Value.text = Row(
                    Materials.DisplayName(pair.Key),
                    flow.Purchased.ToString(),
                    flow.Produced.ToString(),
                    flow.Consumed.ToString(),
                    flow.DeclaredLoss.ToString(),
                    _town.Stock.Get(pair.Key).ToString());
                pair.Value.color = Materials.IsContraband(pair.Key)
                    ? (Color)ProceduralUiArt.Contraband
                    : (Color)ProceduralUiArt.Ink;
            }
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
            sb.Append(e.PadLeft(9));
            sb.Append(f.PadLeft(11));
            return sb.ToString();
        }
    }
}
