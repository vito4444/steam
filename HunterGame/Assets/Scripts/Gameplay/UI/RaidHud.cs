using Hunter.Gameplay.Actors;
using Hunter.Gameplay.Items;
using Hunter.Gameplay.Run;
using UnityEngine;
using UnityEngine.UI;

namespace Hunter.Gameplay.UI
{
    /// The raid HUD, built entirely in code so the playable scene stays reproducible from
    /// source with no prefab assets to drift out of sync.
    ///
    /// It shows exactly the four numbers a hunter has to weigh: how loaded they are, what
    /// the haul is worth, how dangerous it has become, and how long is left. Those are the
    /// same four inputs LootValuation uses, so the HUD is a readout of the decision
    /// function rather than a separate opinion about it.
    ///
    /// Canvas runs in Screen Space - Camera mode rather than Overlay: Overlay canvases are
    /// skipped by Camera.Render(), which would make the HUD invisible to the screenshot
    /// tooling the project relies on for self-review.
    public class RaidHud : MonoBehaviour
    {
        [SerializeField] RunController run;
        [SerializeField] Camera hudCamera;
        [SerializeField] Damageable playerHealth;

        static readonly Color Gold = new(1f, 0.78f, 0.36f);
        static readonly Color Dim = new(0.62f, 0.60f, 0.55f);
        static readonly Color Danger = new(0.95f, 0.38f, 0.24f);
        static readonly Color Panel = new(0.015f, 0.015f, 0.02f, 0.80f);

        Text _clockText;
        Text _phaseText;
        Text _haulText;
        Text _weightText;
        Text _portalText;
        Text _promptText;
        Bar _weightBar;
        Bar _threatBar;
        Bar _healthBar;
        Font _font;

        void Awake() => EnsureBuilt();

        /// Safe to call from editor tooling. Screenshots render one frame with no Awake,
        /// and a HUD that only exists in play mode cannot be reviewed from a still.
        public void EnsureBuilt()
        {
            if (_clockText != null) return;

            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (_font == null) _font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            Build();
        }

        void Build()
        {
            var canvasGo = new GameObject("HudCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);

            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = hudCamera != null ? hudCamera : Camera.main;
            canvas.planeDistance = 0.5f;
            canvas.sortingOrder = 100;

            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            var root = canvasGo.transform;

            // Bottom left: the carry decision.
            // Everything the hunter has to weigh lives in one block. An earlier revision
            // put threat in a centre-anchored panel and most of the bar ended up off
            // screen; one column also reads faster mid-fight.
            var bag = MakePanel(root, "BagPanel", new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(46f, 42f), new Vector2(470f, 232f));
            _haulText = MakeLabel(bag, "HaulLabel", new Vector2(20f, -14f), 38, Gold, TextAnchor.UpperLeft);
            _weightText = MakeLabel(bag, "WeightLabel", new Vector2(20f, -62f), 21, Dim, TextAnchor.UpperLeft);
            _weightBar = MakeBar(bag, "WeightBar", new Vector2(20f, -92f), new Vector2(430f, 18f), Gold);
            MakeLabel(bag, "HealthLabel", new Vector2(20f, -120f), 17, Dim, TextAnchor.UpperLeft).text = "VITALITY";
            _healthBar = MakeBar(bag, "HealthBar", new Vector2(20f, -142f), new Vector2(430f, 12f),
                new Color(0.80f, 0.26f, 0.22f));
            MakeLabel(bag, "ThreatLabel", new Vector2(20f, -166f), 17, Dim, TextAnchor.UpperLeft).text = "THREAT";
            _threatBar = MakeBar(bag, "ThreatBar", new Vector2(20f, -188f), new Vector2(430f, 12f), Danger);

            // Top right: the clock.
            var clock = MakePanel(root, "ClockPanel", new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-46f, -42f), new Vector2(330f, 108f));
            clock.pivot = new Vector2(1f, 1f);
            clock.anchoredPosition = new Vector2(-46f, -42f);
            _clockText = MakeLabel(clock, "Clock", new Vector2(-20f, -12f), 48, Dim, TextAnchor.UpperRight);
            _phaseText = MakeLabel(clock, "Phase", new Vector2(-20f, -68f), 21, Dim, TextAnchor.UpperRight);

            // Centre: the extraction countdown, hidden until the bell rings.
            _portalText = MakeLabel(root, "Portal", Vector2.zero, 54, Gold, TextAnchor.MiddleCenter);
            var portalRect = _portalText.rectTransform;
            portalRect.anchorMin = portalRect.anchorMax = new Vector2(0.5f, 0.68f);
            portalRect.pivot = new Vector2(0.5f, 0.5f);
            portalRect.anchoredPosition = Vector2.zero;
            portalRect.sizeDelta = new Vector2(900f, 90f);
            _portalText.gameObject.SetActive(false);

            // Interaction prompt, just under the reticle line. Without it a player has no
            // way to learn that containers and the bell are interactive at all.
            _promptText = MakeLabel(root, "Prompt", Vector2.zero, 26, Gold, TextAnchor.MiddleCenter);
            var promptRect = _promptText.rectTransform;
            promptRect.anchorMin = promptRect.anchorMax = new Vector2(0.5f, 0.40f);
            promptRect.pivot = new Vector2(0.5f, 0.5f);
            promptRect.anchoredPosition = Vector2.zero;
            promptRect.sizeDelta = new Vector2(900f, 44f);
            _promptText.gameObject.SetActive(false);
        }

        /// Null or empty hides the prompt.
        public void SetPrompt(string text)
        {
            if (_promptText == null) return;

            bool show = !string.IsNullOrEmpty(text);
            if (_promptText.gameObject.activeSelf != show) _promptText.gameObject.SetActive(show);
            if (show) _promptText.text = text;
        }

        RectTransform MakePanel(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 position, Vector2 size)
        {
            var go = new GameObject(name, typeof(Image));
            go.transform.SetParent(parent, false);

            var image = go.GetComponent<Image>();
            image.color = Panel;
            image.raycastTarget = false;

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = new Vector2(anchorMin.x, anchorMin.y);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }

        Text MakeLabel(Transform parent, string name, Vector2 position, int size, Color color,
            TextAnchor anchor)
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
            rect.sizeDelta = new Vector2(400f, size + 10f);
            return text;
        }

        /// Image.Type.Filled silently does nothing without a sprite, so bars are driven by
        /// the fill rect's right anchor instead.
        sealed class Bar
        {
            readonly RectTransform _rect;
            readonly Image _image;

            public Bar(RectTransform rect, Image image)
            {
                _rect = rect;
                _image = image;
            }

            public float Fill
            {
                set
                {
                    var max = _rect.anchorMax;
                    max.x = Mathf.Clamp01(value);
                    _rect.anchorMax = max;
                    _rect.offsetMin = Vector2.zero;
                    _rect.offsetMax = Vector2.zero;
                }
            }

            public Color Color { set => _image.color = value; }
        }

        Bar MakeBar(Transform parent, string name, Vector2 position, Vector2 size, Color color)
        {
            var trackGo = new GameObject(name + "Track", typeof(Image));
            trackGo.transform.SetParent(parent, false);
            var track = trackGo.GetComponent<Image>();
            track.color = new Color(0f, 0f, 0f, 0.55f);
            track.raycastTarget = false;

            var trackRect = trackGo.GetComponent<RectTransform>();
            trackRect.anchorMin = trackRect.anchorMax = new Vector2(0f, 1f);
            trackRect.pivot = new Vector2(0f, 1f);
            trackRect.anchoredPosition = position;
            trackRect.sizeDelta = size;

            var fillGo = new GameObject(name + "Fill", typeof(Image));
            fillGo.transform.SetParent(trackGo.transform, false);
            var fill = fillGo.GetComponent<Image>();
            fill.color = color;
            fill.raycastTarget = false;

            var fillRect = fillGo.GetComponent<RectTransform>();
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;
            return new Bar(fillRect, fill);
        }

        void LateUpdate()
        {
            if (run == null || run.Director == null) return;
            Refresh();
        }

        /// Fills the bag with a representative mid-raid haul so a still frame shows the
        /// HUD doing its job rather than reading all zeroes.
        public void PopulateForCapture()
        {
            EnsureBuilt();
            if (run == null) return;

            run.EnsureInitialised();
            run.Director.Begin();   // Insertion ignores Tick, so the clock would never move
            if (playerHealth != null) playerHealth.EnsureInitialised();

            if (run.Inventory.Count == 0)
            {
                var rng = new System.Random(4242);
                var table = ItemCatalog.EliteCache();
                for (int i = 0; i < 6; i++)
                {
                    var item = table.Roll(rng, 0.4f);
                    if (run.Inventory.TryAdd(item) != AddResult.Added) break;
                }
            }

            run.Director.Tick(430f);
            run.Director.AddThreat(0.52f);
            Refresh();
        }

        /// Public so the screenshot tooling can populate the HUD on a single rendered
        /// frame without entering play mode.
        public void Refresh()
        {
            var director = run.Director;
            var inventory = run.Inventory;

            float remaining = Mathf.Max(0f, (1f - director.RunProgress) * 1080f);
            _clockText.text = $"{Mathf.FloorToInt(remaining / 60f):00}:{Mathf.FloorToInt(remaining % 60f):00}";
            _clockText.color = remaining < 120f ? Danger : Dim;

            _phaseText.text = director.SovereignActive ? "THE SOVEREIGN WALKS" : PhaseLabel(director.Phase);
            _phaseText.color = director.SovereignActive ? Danger : Dim;

            _haulText.text = $"{inventory.TotalValue} AURUM";
            _weightText.text = $"LOAD  {inventory.TotalWeight:0.0} / {inventory.WeightLimit:0.0} kg" +
                               (inventory.SpeedMultiplier < 0.999f
                                   ? $"   ({inventory.SpeedMultiplier * 100f:0}% SPEED)"
                                   : "");
            _weightText.color = inventory.SpeedMultiplier < 0.999f ? Danger : Dim;

            _weightBar.Fill = inventory.LoadRatio;
            // The bar turns hostile exactly when movement starts to suffer, so the colour
            // change is information rather than decoration.
            _weightBar.Color = Color.Lerp(Gold, Danger,
                Mathf.InverseLerp(inventory.SoftCapRatio, 1f, inventory.LoadRatio));

            _threatBar.Fill = director.Threat;
            _threatBar.Color = Color.Lerp(new Color(0.62f, 0.44f, 0.22f), Danger, director.Threat);

            if (playerHealth != null) _healthBar.Fill = playerHealth.Health01;

            bool portalOpen = director.Phase == RunPhase.ExtractionWindow;
            if (_portalText.gameObject.activeSelf != portalOpen) _portalText.gameObject.SetActive(portalOpen);
            if (portalOpen) _portalText.text = $"PORTAL  {director.PortalSecondsRemaining:0.0}s";
        }

        static string PhaseLabel(RunPhase phase) => phase switch
        {
            RunPhase.Insertion => "INSERTION",
            RunPhase.Scavenging => "SCAVENGING",
            RunPhase.ExtractionWindow => "PORTAL OPEN",
            RunPhase.Extracted => "EXTRACTED",
            RunPhase.Died => "LOST EVERYTHING",
            RunPhase.TimedOut => "THE MIST TOOK IT ALL",
            _ => "",
        };
    }
}
