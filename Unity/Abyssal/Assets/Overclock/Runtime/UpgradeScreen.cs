using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Overclock.Core;

namespace Overclock
{
    /// <summary>
    /// 层间的三选一升级界面，以及层失败和整局结束的结算画面。
    ///
    /// 这是 roguelite 结构里唯一让玩家停下来思考的地方。三张卡必须能被一眼比较：
    /// 标题说是什么，副标题说属于哪条构筑轴线，正文说具体改了什么数值。
    /// 含糊的描述（「显著提升效率」）会让选择退化成瞎猜。
    /// </summary>
    public sealed class UpgradeScreen
    {
        public enum Mode { Hidden, Upgrade, LayerFailed, RunComplete }

        public Mode Current { get; private set; } = Mode.Hidden;

        /// <summary>玩家选中的升级下标，没选时为 -1。每帧读完要自己清。</summary>
        public int PickedIndex { get; private set; } = -1;

        /// <summary>结算画面上玩家确认了继续。</summary>
        public bool Acknowledged { get; private set; }

        GameObject _root;
        RawImage _title;
        RawImage _subtitle;
        readonly List<GameObject> _cards = new List<GameObject>();
        readonly List<RawImage> _cardFrames = new List<RawImage>();
        readonly List<RawImage> _cardName = new List<RawImage>();
        readonly List<RawImage> _cardAxis = new List<RawImage>();
        readonly List<RawImage> _cardBody = new List<RawImage>();

        List<Upgrade> _offers = new List<Upgrade>();
        int _hovered = -1;

        public void Build(Transform canvasRoot)
        {
            _root = new GameObject("UpgradeScreen");
            _root.transform.SetParent(canvasRoot, false);

            var rt = _root.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            // 全屏压暗。不压暗的话卡片会淹没在背后发光的电路网里。
            var dim = MakeImage(_root.transform, "Dim", HudTextures.Solid(),
                Vector2.zero, Vector2.zero, Vector2.zero);
            var dimRt = dim.rectTransform;
            dimRt.anchorMin = Vector2.zero;
            dimRt.anchorMax = Vector2.one;
            dimRt.offsetMin = Vector2.zero;
            dimRt.offsetMax = Vector2.zero;
            dim.color = new Color(0f, 0f, 0f, 0.82f);

            _title = MakeImage(_root.transform, "Title", HudTextures.Text("LAYER CLEAR", 5),
                new Vector2(0.5f, 0.5f), new Vector2(0f, 268f), Vector2.zero);
            _subtitle = MakeImage(_root.transform, "Subtitle",
                HudTextures.Text("SELECT ONE ARCHITECTURE UPGRADE", 2, true),
                new Vector2(0.5f, 0.5f), new Vector2(0f, 224f), Vector2.zero);

            const float cardW = 400f;
            const float cardH = 300f;
            const float gap = 34f;

            for (int i = 0; i < 3; i++)
            {
                float x = (i - 1) * (cardW + gap);

                var card = new GameObject($"Card{i}");
                card.transform.SetParent(_root.transform, false);
                var cardRt = card.AddComponent<RectTransform>();
                cardRt.anchorMin = new Vector2(0.5f, 0.5f);
                cardRt.anchorMax = new Vector2(0.5f, 0.5f);
                cardRt.sizeDelta = new Vector2(cardW, cardH);
                cardRt.anchoredPosition = new Vector2(x, 24f);

                var frame = MakeImage(card.transform, "Frame", HudTextures.Card(false),
                    new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(cardW, cardH));
                frame.raycastTarget = true;

                _cardName.Add(MakeImage(card.transform, "Name", HudTextures.Text("", 4),
                    new Vector2(0.5f, 1f), new Vector2(0f, -54f), Vector2.zero));
                _cardAxis.Add(MakeImage(card.transform, "Axis", HudTextures.Text("", 2, true),
                    new Vector2(0.5f, 1f), new Vector2(0f, -98f), Vector2.zero));
                _cardBody.Add(MakeImage(card.transform, "Body", HudTextures.Text("", 2),
                    new Vector2(0.5f, 0.5f), new Vector2(0f, -6f), Vector2.zero));

                MakeImage(card.transform, "Key", HudTextures.Text($"PRESS {i + 1}", 2, true),
                    new Vector2(0.5f, 0f), new Vector2(0f, 40f), Vector2.zero);

                _cards.Add(card);
                _cardFrames.Add(frame);
            }

            _root.SetActive(false);
        }

        /// <summary>弹出三选一。</summary>
        public void ShowUpgrades(List<Upgrade> offers, int layerIndex)
        {
            _offers = offers;
            Current = Mode.Upgrade;
            PickedIndex = -1;
            Acknowledged = false;
            _root.SetActive(true);

            SetText(_title, $"LAYER {layerIndex:00} CLEAR", 5);
            SetText(_subtitle, "SELECT ONE ARCHITECTURE UPGRADE", 2, true);

            for (int i = 0; i < _cards.Count; i++)
            {
                bool has = i < offers.Count;
                _cards[i].SetActive(has);
                if (!has) continue;

                SetText(_cardName[i], Ascii(offers[i].Name), 4);
                SetText(_cardAxis[i], AxisLabel(offers[i].Axis), 2, true);
                SetText(_cardBody[i], Ascii(offers[i].Description), 2);
                _cardFrames[i].texture = HudTextures.Card(false);
            }
        }

        /// <summary>层失败或整局结束的结算。</summary>
        public void ShowOutcome(bool runComplete, int layersCleared, string detail)
        {
            Current = runComplete ? Mode.RunComplete : Mode.LayerFailed;
            PickedIndex = -1;
            Acknowledged = false;
            _root.SetActive(true);

            SetText(_title, runComplete ? "CHIP STABILISED" : "THERMAL FAILURE", 5);
            SetText(_subtitle, $"{layersCleared} LAYERS CLEARED   {Ascii(detail)}", 2, true);

            for (int i = 0; i < _cards.Count; i++) _cards[i].SetActive(false);
        }

        public void Hide()
        {
            Current = Mode.Hidden;
            PickedIndex = -1;
            _root.SetActive(false);
        }

        /// <summary>
        /// 处理输入。数字键选卡，鼠标悬停高亮，点击确认。
        /// 结算画面按任意键继续。
        /// </summary>
        public void Tick(UnityEngine.InputSystem.Keyboard keyboard,
                         UnityEngine.InputSystem.Mouse mouse,
                         Camera camera)
        {
            if (Current == Mode.Hidden) return;

            if (Current != Mode.Upgrade)
            {
                if (keyboard != null && keyboard.anyKey.wasPressedThisFrame) Acknowledged = true;
                if (mouse != null && mouse.leftButton.wasPressedThisFrame) Acknowledged = true;
                return;
            }

            if (keyboard != null)
            {
                var keys = new[]
                {
                    UnityEngine.InputSystem.Key.Digit1,
                    UnityEngine.InputSystem.Key.Digit2,
                    UnityEngine.InputSystem.Key.Digit3,
                };
                for (int i = 0; i < keys.Length && i < _offers.Count; i++)
                    if (keyboard[keys[i]].wasPressedThisFrame) PickedIndex = i;
            }

            if (mouse == null) return;

            var screen = mouse.position.ReadValue();
            int nowHovered = -1;

            for (int i = 0; i < _offers.Count && i < _cards.Count; i++)
            {
                var rect = _cards[i].GetComponent<RectTransform>();
                if (!RectTransformUtility.RectangleContainsScreenPoint(rect, screen, camera)) continue;
                nowHovered = i;
                break;
            }

            if (nowHovered != _hovered)
            {
                _hovered = nowHovered;
                for (int i = 0; i < _cardFrames.Count; i++)
                    _cardFrames[i].texture = HudTextures.Card(i == _hovered);
            }

            if (_hovered >= 0 && mouse.leftButton.wasPressedThisFrame) PickedIndex = _hovered;
        }

        public void ConsumePick() => PickedIndex = -1;

        static string AxisLabel(BuildAxis axis)
        {
            switch (axis)
            {
                case BuildAxis.Superconductor: return "SUPERCONDUCTOR";
                case BuildAxis.BruteForce: return "BRUTE FORCE";
                case BuildAxis.Compression: return "COMPRESSION";
                default: return "PARALLEL";
            }
        }

        /// <summary>
        /// 点阵字库只有 ASCII。升级的中文名要转成对应的英文标识，
        /// 直接丢中文进去会画出一排空白。
        /// </summary>
        static string Ascii(string source)
        {
            switch (source)
            {
                case "低温蚀刻": return "CRYO ETCH";
                case "晶格对齐": return "LATTICE ALIGN";
                case "耐火封装": return "REFRACTORY SHELL";
                case "宽轨总线": return "WIDE RAIL";
                case "均热腔": return "VAPOR CHAMBER";
                case "高密封装": return "DENSE PACKAGING";
                case "批量编码": return "BATCH ENCODE";
                case "追加掩模": return "EXTRA MASK";
                case "冗余通路": return "REDUNDANT PATH";
                case "所有元件发热降低 18%": return "ALL HEAT -18%";
                case "发热降低 12%，吞吐降低 5%": return "HEAT -12%   THROUGHPUT -5%";
                case "所有元件熔点提高 28 度": return "MELTING POINT +28C";
                case "吞吐上限提高 15%，发热提高 8%": return "THROUGHPUT +15%   HEAT +8%";
                case "导热效率提高 30%": return "CONDUCTIVITY +30%";
                case "元件成本降低 25%": return "COMPONENT COST -25%";
                case "吞吐提高 10%，熔点降低 10 度": return "THROUGHPUT +10%   MELT -10C";
                case "每层额外获得 12 点预算": return "+12 BUDGET PER LAYER";
                case "每层额外 8 点预算，发热降低 6%": return "+8 BUDGET   HEAT -6%";
                default: return source;
            }
        }

        static void SetText(RawImage target, string value, int scale, bool dim = false)
        {
            var tex = HudTextures.Text(value, scale, dim);
            target.texture = tex;
            target.rectTransform.sizeDelta = new Vector2(tex.width, tex.height);
        }

        static RawImage MakeImage(Transform parent, string name, Texture2D texture,
                                  Vector2 anchor, Vector2 pos, Vector2 size)
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
            rt.anchoredPosition = pos;
            rt.sizeDelta = size == Vector2.zero && texture != null
                ? new Vector2(texture.width, texture.height)
                : size;

            return image;
        }
    }
}
