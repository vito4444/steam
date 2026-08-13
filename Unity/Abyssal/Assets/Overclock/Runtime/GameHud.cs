using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Overclock.Core;

namespace Overclock
{
    /// <summary>
    /// 游戏内 HUD。
    ///
    /// 玩家每一秒都在回答同一个问题：现在要不要加东西、加在哪、加什么。
    /// HUD 的职责就是把回答这个问题所需的四个数字同时摆在眼前——
    /// 还差多少吞吐、还剩多少预算、最热的地方有多热、还要撑多久。
    /// 少任何一个，玩家就得靠猜。
    ///
    /// 全部用代码构建，纹理来自 <see cref="HudTextures"/>，不依赖任何 UI 预制体。
    /// </summary>
    public sealed class GameHud
    {
        /// <summary>玩家能选的元件，顺序对应快捷键 1–6。</summary>
        public static readonly ComponentKind[] Palette =
        {
            ComponentKind.Trace,
            ComponentKind.Buffer,
            ComponentKind.Bus,
            ComponentKind.Splitter,
            ComponentKind.Compressor,
            ComponentKind.HeatSink,
        };

        public Canvas Canvas { get; private set; }

        RawImage _throughputFill;
        RawImage _holdFill;
        RawImage _thermalFill;
        RawImage _throughputLabel;
        RawImage _statusLabel;
        RawImage _layerLabel;
        RawImage _budgetLabel;
        RawImage _thermalLabel;
        RawImage _hintLabel;

        readonly List<RawImage> _slotFrames = new List<RawImage>();
        readonly List<RawImage> _slotCosts = new List<RawImage>();

        int _selected;
        string _lastThroughput = "";
        string _lastLayer = "";
        string _lastBudget = "";
        string _lastThermal = "";
        string _lastStatus = "";

        public void Build(Transform parent, Camera camera = null)
        {
            var go = new GameObject("HUD");
            if (parent != null) go.transform.SetParent(parent, false);

            Canvas = go.AddComponent<Canvas>();

            // 必须用 ScreenSpaceCamera 而不是 Overlay。Overlay 的 Canvas 不经过相机，
            // 渲染到 RenderTexture 时整个 HUD 都不会出现——批处理截图里会完全看不到。
            if (camera != null)
            {
                Canvas.renderMode = RenderMode.ScreenSpaceCamera;
                Canvas.worldCamera = camera;
                Canvas.planeDistance = 1.0f;
            }
            else
            {
                Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            }

            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            go.AddComponent<GraphicRaycaster>();

            BuildTopBar(go.transform);
            BuildThermalBar(go.transform);
            BuildPalette(go.transform);
            BuildHint(go.transform);
        }

        // ------------------------------------------------------------------ 构建

        void BuildTopBar(Transform root)
        {
            // 吞吐量是本层的唯一通关条件，所以它占据顶部正中最显眼的位置。
            var panel = MakeImage(root, "ThroughputPanel", HudTextures.Gauge(760, 44, 0.78f),
                new Vector2(0.5f, 1f), new Vector2(0f, -46f), new Vector2(760f, 44f));

            _throughputFill = MakeImage(panel.transform, "Fill", HudTextures.Solid(),
                new Vector2(0f, 0.5f), new Vector2(3f, 0f), new Vector2(0f, 36f));
            _throughputFill.color = OverclockPalette.Data;
            _throughputFill.rectTransform.pivot = new Vector2(0f, 0.5f);
            _throughputFill.transform.SetSiblingIndex(0);

            _throughputLabel = MakeImage(panel.transform, "Label", HudTextures.Text("", 3),
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(10f, 21f));

            _statusLabel = MakeImage(root, "Status", HudTextures.Text("", 3),
                new Vector2(0.5f, 1f), new Vector2(0f, -92f), new Vector2(10f, 21f));

            // 左上：层数和预算。预算决定玩家还能不能补救，必须常驻。
            var left = MakeImage(root, "LeftPanel", HudTextures.Panel(300, 96),
                new Vector2(0f, 1f), new Vector2(170f, -68f), new Vector2(300f, 96f));

            _layerLabel = MakeImage(left.transform, "Layer", HudTextures.Text("", 3),
                new Vector2(0f, 1f), new Vector2(18f, -22f), new Vector2(10f, 21f));
            _layerLabel.rectTransform.pivot = new Vector2(0f, 0.5f);

            _budgetLabel = MakeImage(left.transform, "Budget", HudTextures.Text("", 3),
                new Vector2(0f, 1f), new Vector2(18f, -62f), new Vector2(10f, 21f));
            _budgetLabel.rectTransform.pivot = new Vector2(0f, 0.5f);
        }

        void BuildThermalBar(Transform root)
        {
            // 右上：最热的那一格离熔点还有多远。
            // 这是玩家唯一的「危险倒计时」，条一满就有东西要烧了。
            var panel = MakeImage(root, "ThermalPanel", HudTextures.Panel(340, 96),
                new Vector2(1f, 1f), new Vector2(-190f, -68f), new Vector2(340f, 96f));

            _thermalLabel = MakeImage(panel.transform, "Label", HudTextures.Text("", 3),
                new Vector2(0f, 1f), new Vector2(18f, -22f), new Vector2(10f, 21f));
            _thermalLabel.rectTransform.pivot = new Vector2(0f, 0.5f);

            var track = MakeImage(panel.transform, "Track", HudTextures.Gauge(300, 22, 1f),
                new Vector2(0.5f, 0f), new Vector2(0f, 24f), new Vector2(300f, 22f));

            _thermalFill = MakeImage(track.transform, "Fill", HudTextures.Solid(),
                new Vector2(0f, 0.5f), new Vector2(3f, 0f), new Vector2(0f, 14f));
            _thermalFill.color = OverclockPalette.Heat;
            _thermalFill.rectTransform.pivot = new Vector2(0f, 0.5f);

            // 达标保持进度，挂在吞吐条下方。
            var holdTrack = MakeImage(root, "HoldTrack", HudTextures.Gauge(520, 18, 1f),
                new Vector2(0.5f, 1f), new Vector2(0f, -122f), new Vector2(520f, 18f));

            _holdFill = MakeImage(holdTrack.transform, "Fill", HudTextures.Solid(),
                new Vector2(0f, 0.5f), new Vector2(3f, 0f), new Vector2(0f, 11f));
            _holdFill.color = OverclockPalette.Sink;
            _holdFill.rectTransform.pivot = new Vector2(0f, 0.5f);
        }

        void BuildPalette(Transform root)
        {
            const float slot = 104f;
            const float gap = 12f;
            float total = Palette.Length * slot + (Palette.Length - 1) * gap;
            float startX = -total * 0.5f + slot * 0.5f;

            MakeImage(root, "PaletteBacking", HudTextures.Panel(760, 156),
                new Vector2(0.5f, 0f), new Vector2(0f, 104f),
                new Vector2(total + 40f, 152f));

            for (int i = 0; i < Palette.Length; i++)
            {
                var kind = Palette[i];

                var frame = MakeImage(root, $"Slot{i}", HudTextures.Slot(i == 0, true),
                    new Vector2(0.5f, 0f), new Vector2(startX + i * (slot + gap), 112f),
                    new Vector2(slot, slot));
                _slotFrames.Add(frame);

                MakeImage(frame.transform, "Icon", HudTextures.ComponentIcon(kind),
                    new Vector2(0.5f, 0.5f), new Vector2(0f, 12f), new Vector2(60f, 60f));

                // 快捷键角标。
                MakeImage(frame.transform, "Key", HudTextures.Text((i + 1).ToString(), 2, true),
                    new Vector2(0f, 1f), new Vector2(12f, -12f), new Vector2(10f, 14f));

                var cost = MakeImage(frame.transform, "Cost",
                    HudTextures.Text($"{ComponentLibrary.Of(kind).Cost}", 2),
                    new Vector2(0.5f, 0f), new Vector2(0f, 13f), new Vector2(10f, 14f));
                _slotCosts.Add(cost);
            }
        }

        void BuildHint(Transform root)
        {
            _hintLabel = MakeImage(root, "Hint",
                HudTextures.Text("LMB PLACE   RMB REMOVE   1-6 SELECT   WASD PAN", 2, true),
                new Vector2(0.5f, 0f), new Vector2(0f, 18f), new Vector2(10f, 14f));
        }

        // ------------------------------------------------------------------ 刷新

        /// <summary>按当前层状态刷新 HUD。每帧调用。</summary>
        public void Refresh(SiliconLayer layer, int layerIndex, int selectedSlot)
        {
            float throughputRatio = layer.TargetThroughput <= 0.0
                ? 0f
                : Mathf.Clamp01((float)(layer.CurrentThroughput / layer.TargetThroughput));

            SetFillWidth(_throughputFill, 754f, throughputRatio);
            _throughputFill.color = throughputRatio >= 1f
                ? OverclockPalette.Sink
                : OverclockPalette.Data;

            SetText(ref _lastThroughput, _throughputLabel,
                $"{layer.CurrentThroughput:F1} / {layer.TargetThroughput:F0}", 3);

            float hold = layer.RequiredHoldTime <= 0.0
                ? 0f
                : Mathf.Clamp01((float)(layer.HoldProgress / layer.RequiredHoldTime));
            SetFillWidth(_holdFill, 514f, hold);

            float stress = (float)layer.PeakThermalStress();
            SetFillWidth(_thermalFill, 294f, stress);
            _thermalFill.color = OverclockPalette.ByThermalStress(stress) * 2.2f;

            SetText(ref _lastLayer, _layerLabel, $"LAYER {layerIndex:00}", 3);
            SetText(ref _lastBudget, _budgetLabel, $"BUDGET {layer.Budget}", 3);
            SetText(ref _lastThermal, _thermalLabel, $"PEAK {layer.PeakTemperature():F0}C", 3);

            string status;
            if (layer.Failed) status = "LAYER LOST";
            else if (layer.Cleared) status = "LAYER CLEAR";
            else if (stress > 0.88f) status = "CRITICAL - COMPONENTS FAILING";
            else if (throughputRatio >= 1f) status = "HOLDING TARGET";
            else status = "BELOW TARGET";
            SetText(ref _lastStatus, _statusLabel, status, 3);

            if (selectedSlot != _selected) _selected = selectedSlot;

            for (int i = 0; i < _slotFrames.Count; i++)
            {
                bool affordable = layer.Budget >= ComponentLibrary.Of(Palette[i]).Cost;
                _slotFrames[i].texture = HudTextures.Slot(i == _selected, affordable);
                _slotCosts[i].color = affordable ? Color.white : new Color(1f, 0.45f, 0.40f);
            }
        }

        static void SetFillWidth(RawImage image, float fullWidth, float ratio)
        {
            var size = image.rectTransform.sizeDelta;
            size.x = fullWidth * Mathf.Clamp01(ratio);
            image.rectTransform.sizeDelta = size;
        }

        /// <summary>
        /// 文本变了才重画贴图。点阵字每次生成都要走一遍像素循环，
        /// 逐帧无条件重画会在这些常驻标签上白白烧掉几毫秒。
        /// </summary>
        static void SetText(ref string cache, RawImage target, string value, int scale)
        {
            if (cache == value) return;
            cache = value;
            var tex = HudTextures.Text(value, scale);
            target.texture = tex;
            target.rectTransform.sizeDelta = new Vector2(tex.width, tex.height);
        }

        static RawImage MakeImage(Transform parent, string name, Texture2D texture,
                                  Vector2 anchor, Vector2 anchoredPos, Vector2 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            var image = go.AddComponent<RawImage>();
            image.texture = texture;
            image.raycastTarget = false;

            var rt = image.rectTransform;
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = texture != null && size.x <= 10f
                ? new Vector2(texture.width, texture.height)
                : size;

            return image;
        }
    }
}
