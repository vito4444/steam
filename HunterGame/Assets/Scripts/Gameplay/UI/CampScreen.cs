using System;
using System.Collections.Generic;
using System.Linq;
using Hunter.Gameplay.Camp;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Hunter.Gameplay.UI
{
    /// The between-raid screen: spend aurum, see what it bought, launch the next run.
    ///
    /// Camp progression existed only as data before this. Aurum accumulated and facilities
    /// could be upgraded through code, but nothing in the build let a player see a balance
    /// or buy anything, so the entire meta loop was unreachable from the game itself.
    public class CampScreen : MonoBehaviour
    {
        static readonly Color Gold = new(1f, 0.78f, 0.36f);
        static readonly Color Dim = new(0.62f, 0.60f, 0.55f);
        static readonly Color Faint = new(0.42f, 0.41f, 0.38f);
        static readonly Color Danger = new(0.95f, 0.38f, 0.24f);
        static readonly Color Backdrop = new(0.028f, 0.026f, 0.032f, 1f);
        static readonly Color RowIdle = new(0.075f, 0.072f, 0.082f, 1f);
        static readonly Color RowSelected = new(0.20f, 0.155f, 0.075f, 1f);

        [SerializeField] Camera hudCamera;
        [SerializeField] RaidHud raidHud;

        readonly List<Row> _rows = new();
        FacilityKind[] _order = Array.Empty<FacilityKind>();

        static Sprite _solid;

        Image _backdrop;
        Text _aurumText;
        Text _recordText;
        Text _footerText;
        Text _flashText;
        GameObject _root;
        Font _font;
        float _flashUntil;

        public CampState Camp { get; set; }
        public int SelectedIndex { get; private set; }
        public bool IsOpen => _root != null && _root.activeSelf;

        /// Raised when the player leaves the camp to start a raid.
        public event Action RaidRequested;

        void Awake()
        {
            EnsureBuilt();
            // Built eagerly so the first Open is instant, but a raid starts in the world.
            Close();
        }

        /// Safe to call from editor tooling, which renders a single frame with no Awake.
        public void EnsureBuilt()
        {
            if (_root != null) return;

            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (_font == null) _font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            Build();
        }

        void Build()
        {
            var canvasGo = new GameObject("CampCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);

            var canvas = canvasGo.GetComponent<Canvas>();
            // Matches RaidHud: an Overlay canvas does not appear in an off-screen render
            // target, which would make the camp screen impossible to review from a still.
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = hudCamera != null ? hudCamera : Camera.main;
            canvas.planeDistance = 0.45f;
            canvas.sortingOrder = 200;

            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            _root = canvasGo;

            var backdrop = new GameObject("Backdrop", typeof(Image));
            backdrop.transform.SetParent(canvasGo.transform, false);
            _backdrop = backdrop.GetComponent<Image>();
            _backdrop.sprite = SolidSprite();
            _backdrop.color = Backdrop;
            // Fully opaque, not merely dark. The canvas composites before tonemapping, and
            // the raid's sun sits far above 1.0 in HDR, so even 94% coverage let it burn
            // straight through the shop.
            //
            // Deliberately far larger than any viewport. A screen-space-camera canvas sizes
            // itself from the camera's pixel rect, which differs between the editor's batch
            // resolution and the render target, and a backdrop that only covers part of the
            // frame lets the raid show through the shop.
            Stretch(backdrop.GetComponent<RectTransform>(), padding: 4000f);

            var title = MakeLabel(canvasGo.transform, "Title", new Vector2(120f, -70f), 46, Gold,
                TextAnchor.UpperLeft);
            title.text = "CAMP";

            _aurumText = MakeLabel(canvasGo.transform, "Aurum", new Vector2(-120f, -66f), 52, Gold,
                TextAnchor.UpperRight);
            _recordText = MakeLabel(canvasGo.transform, "Record", new Vector2(-120f, -124f), 20, Dim,
                TextAnchor.UpperRight);

            var subtitle = MakeLabel(canvasGo.transform, "Subtitle", new Vector2(122f, -124f), 20, Faint,
                TextAnchor.UpperLeft);
            subtitle.text = "SPEND WHAT YOU CARRIED OUT";

            _order = Enum.GetValues(typeof(FacilityKind)).Cast<FacilityKind>().ToArray();
            const float rowHeight = 104f;
            const float firstRowY = -196f;
            for (int i = 0; i < _order.Length; i++)
            {
                _rows.Add(BuildRow(canvasGo.transform, _order[i], firstRowY - i * rowHeight, rowHeight - 12f));
            }

            _flashText = MakeLabel(canvasGo.transform, "Flash", new Vector2(120f, firstRowY - _order.Length * rowHeight - 6f),
                24, Danger, TextAnchor.UpperLeft);
            _flashText.gameObject.SetActive(false);

            _footerText = MakeLabel(canvasGo.transform, "Footer",
                new Vector2(120f, firstRowY - _order.Length * rowHeight - 48f), 22, Faint, TextAnchor.UpperLeft);
            _footerText.text = "\u2191\u2193  SELECT      ENTER  UPGRADE      TAB  BEGIN RAID";

            SetSelected(0);
        }

        Row BuildRow(Transform parent, FacilityKind kind, float y, float height)
        {
            var panel = new GameObject($"Row_{kind}", typeof(Image));
            panel.transform.SetParent(parent, false);
            var panelImage = panel.GetComponent<Image>();
            panelImage.sprite = SolidSprite();
            panelImage.color = RowIdle;

            var rect = panel.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.offsetMin = new Vector2(120f, 0f);
            rect.offsetMax = new Vector2(-120f, 0f);
            rect.anchoredPosition = new Vector2(120f, y);
            rect.sizeDelta = new Vector2(-240f, height);

            var name = MakeLabel(panel.transform, "Name", new Vector2(24f, -14f), 30, Gold, TextAnchor.UpperLeft);
            name.text = FacilityInfo.DisplayName(kind);

            var tagline = MakeLabel(panel.transform, "Tagline", new Vector2(24f, -50f), 19, Faint, TextAnchor.UpperLeft);
            tagline.text = FacilityInfo.Tagline(kind);

            var level = MakeLabel(panel.transform, "Level", new Vector2(430f, -16f), 22, Dim, TextAnchor.UpperLeft);
            var effect = MakeLabel(panel.transform, "Effect", new Vector2(430f, -48f), 20, Dim, TextAnchor.UpperLeft);
            var cost = MakeLabel(panel.transform, "Cost", new Vector2(-24f, -20f), 30, Gold, TextAnchor.UpperRight);

            return new Row(kind, panel.GetComponent<Image>(), name, level, effect, cost);
        }

        // ----- state -----

        public void Open(CampState camp)
        {
            EnsureBuilt();
            Camp = camp;
            _root.SetActive(true);
            // The raid HUD reports a run that is already over, and its panels sit on top of
            // the shop rows.
            if (raidHud != null) raidHud.gameObject.SetActive(false);
            SetSelected(0);
            Refresh();
        }

        public void Close()
        {
            if (_root != null) _root.SetActive(false);
            if (raidHud != null) raidHud.gameObject.SetActive(true);
        }

        public void MoveSelection(int delta)
        {
            if (_rows.Count == 0) return;
            int next = SelectedIndex + delta;
            // Clamping rather than wrapping: a list this short reads as a column, and
            // wrapping past the ends makes it easy to overshoot a purchase.
            SetSelected(Mathf.Clamp(next, 0, _rows.Count - 1));
        }

        void SetSelected(int index)
        {
            SelectedIndex = index;
            for (int i = 0; i < _rows.Count; i++) _rows[i].Panel.color = i == index ? RowSelected : RowIdle;
        }

        public FacilityKind SelectedFacility => _order.Length == 0
            ? FacilityKind.Vault
            : _order[Mathf.Clamp(SelectedIndex, 0, _order.Length - 1)];

        /// Returns true when aurum actually changed hands.
        public bool ConfirmPurchase()
        {
            if (Camp == null) return false;

            var kind = SelectedFacility;
            var facility = Camp.GetFacility(kind);

            if (facility.IsMaxed)
            {
                Flash($"{FacilityInfo.DisplayName(kind)} IS AT MAXIMUM");
                return false;
            }

            if (!Camp.CanAfford(kind))
            {
                Flash($"NEED {facility.NextCost - Camp.Aurum} MORE AURUM");
                return false;
            }

            bool bought = Camp.TryUpgrade(kind);
            if (bought) Flash($"{FacilityInfo.DisplayName(kind)} \u2192 LEVEL {facility.Level}", Gold);
            Refresh();
            return bought;
        }

        void Flash(string message, Color? color = null)
        {
            if (_flashText == null) return;
            _flashText.text = message;
            _flashText.color = color ?? Danger;
            _flashText.gameObject.SetActive(true);
            _flashUntil = Time.unscaledTime + 2.5f;
        }

        public void Refresh()
        {
            if (Camp == null) return;

            _aurumText.text = $"{Camp.Aurum} AURUM";
            _recordText.text = Camp.RunsSurvived + Camp.RunsLost == 0
                ? "NO RAIDS RUN"
                : $"{Camp.RunsSurvived} OUT   \u00b7   {Camp.RunsLost} LOST   \u00b7   {Camp.SurvivalRate * 100f:0}% SURVIVED";

            foreach (var row in _rows)
            {
                var facility = Camp.GetFacility(row.Kind);
                row.Level.text = $"LV {facility.Level} / {facility.MaxLevel}    {FacilityInfo.CurrentEffect(row.Kind, facility.Level)}";

                if (facility.IsMaxed)
                {
                    row.Effect.text = "fully upgraded";
                    row.Cost.text = "MAX";
                    row.Cost.color = Faint;
                    continue;
                }

                row.Effect.text = FacilityInfo.NextEffect(row.Kind, facility.Level);
                row.Cost.text = facility.NextCost.ToString();
                // Unaffordable prices are dimmed rather than hidden so the player can see
                // what to save toward.
                row.Cost.color = Camp.CanAfford(row.Kind) ? Gold : Faint;
            }
        }

        /// Fills the screen with representative values so a still frame shows real content.
        public void PopulateForCapture(CampState camp = null)
        {
            EnsureBuilt();

            if (camp == null)
            {
                camp = new CampState();
                camp.BankHaul(new List<Items.ItemInstance>());
                camp.LoadFrom(new CampState.SaveData
                {
                    aurum = 1840,
                    runsSurvived = 6,
                    runsLost = 3,
                    facilityKinds = new[] { (int)FacilityKind.Vault, (int)FacilityKind.Forge, (int)FacilityKind.Shrine },
                    facilityLevels = new[] { 2, 1, 1 },
                    lifetimeEarnings = 7420,
                    lifetimeLosses = 2180,
                });
            }

            Open(camp);
            SetSelected(1);

        }

        void Update()
        {
            if (!IsOpen) return;

            if (_flashText != null && _flashText.gameObject.activeSelf && Time.unscaledTime > _flashUntil)
                _flashText.gameObject.SetActive(false);

            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            if (keyboard.upArrowKey.wasPressedThisFrame || keyboard.wKey.wasPressedThisFrame) MoveSelection(-1);
            if (keyboard.downArrowKey.wasPressedThisFrame || keyboard.sKey.wasPressedThisFrame) MoveSelection(1);
            if (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame) ConfirmPurchase();

            if (keyboard.tabKey.wasPressedThisFrame)
            {
                Close();
                RaidRequested?.Invoke();
            }
        }

        // ----- helpers -----

        static Sprite SolidSprite()
        {
            if (_solid != null) return _solid;

            var texture = new Texture2D(4, 4, TextureFormat.RGBA32, mipChain: false)
            {
                name = "CampSolid",
                hideFlags = HideFlags.DontSave,
            };
            var pixels = new Color32[16];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(255, 255, 255, 255);
            texture.SetPixels32(pixels);
            texture.Apply();

            _solid = Sprite.Create(texture, new Rect(0f, 0f, 4f, 4f), new Vector2(0.5f, 0.5f), 100f);
            _solid.name = "CampSolid";
            _solid.hideFlags = HideFlags.DontSave;
            return _solid;
        }

        static void Stretch(RectTransform rect, float padding = 0f)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.offsetMin = new Vector2(-padding, -padding);
            rect.offsetMax = new Vector2(padding, padding);
        }

        Text MakeLabel(Transform parent, string name, Vector2 position, int size, Color color, TextAnchor anchor)
        {
            var go = new GameObject(name, typeof(Text));
            go.transform.SetParent(parent, false);

            var text = go.GetComponent<Text>();
            text.font = _font;
            text.fontSize = size;
            text.color = color;
            text.alignment = anchor;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;

            var rect = go.GetComponent<RectTransform>();
            bool rightAligned = anchor is TextAnchor.UpperRight or TextAnchor.MiddleRight;
            rect.anchorMin = rect.anchorMax = new Vector2(rightAligned ? 1f : 0f, 1f);
            rect.pivot = new Vector2(rightAligned ? 1f : 0f, 1f);
            rect.anchoredPosition = position;
            rect.sizeDelta = new Vector2(900f, size + 12f);
            return text;
        }

        sealed class Row
        {
            public Row(FacilityKind kind, Image panel, Text name, Text level, Text effect, Text cost)
            {
                Kind = kind;
                Panel = panel;
                Name = name;
                Level = level;
                Effect = effect;
                Cost = cost;
            }

            public FacilityKind Kind { get; }
            public Image Panel { get; }
            public Text Name { get; }
            public Text Level { get; }
            public Text Effect { get; }
            public Text Cost { get; }
        }
    }
}
