using UnityEngine;
using UnityEngine.UI;

namespace Undertown.Game.UI
{
    /// <summary>
    /// Terse constructors for uGUI objects. Every screen in this project is assembled from
    /// script rather than authored in the editor, so this exists to keep that assembly code
    /// readable instead of a wall of RectTransform assignments.
    /// </summary>
    public static class UiFactory
    {
        public static RectTransform Root(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, worldPositionStays: false);
            return (RectTransform)go.transform;
        }

        public static Image Panel(Transform parent, string name, Sprite sprite = null)
        {
            var rect = Root(parent, name);
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = sprite != null ? sprite : ProceduralUiArt.Panel;
            image.type = Image.Type.Sliced;
            return image;
        }

        public static Image Fill(Transform parent, string name, Color color)
        {
            var rect = Root(parent, name);
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = ProceduralUiArt.Solid;
            image.type = Image.Type.Simple;
            image.color = color;
            return image;
        }

        public static Text Label(Transform parent, string name, string text, int size, Color color,
            TextAnchor anchor = TextAnchor.MiddleLeft)
        {
            var rect = Root(parent, name);
            var label = rect.gameObject.AddComponent<Text>();
            label.font = ProceduralUiArt.Font;
            label.text = text;
            label.fontSize = size;
            label.color = color;
            label.alignment = anchor;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.supportRichText = true;
            return label;
        }

        /// <summary>Anchors a rect to a corner-relative box measured in pixels from the bottom left.</summary>
        public static RectTransform Place(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(0f, 0f);
            rect.pivot = new Vector2(0f, 0f);
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(width, height);
            return rect;
        }

        public static RectTransform Stretch(RectTransform rect, float left, float bottom, float right, float top)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
            return rect;
        }
    }
}
