using UnityEngine;
using UnityEngine.UI;
using Worker.Core;

namespace Worker.Game
{
    /// <summary>
    /// Helpers for assembling uGUI hierarchies from code.
    ///
    /// The interface is built in source rather than authored as prefabs for the same
    /// reason the scene is: prefabs and scenes are opaque YAML that cannot be reviewed
    /// in a diff or merged, and they break silently when a field is renamed. Layout in
    /// code costs a little verbosity and buys reviewability.
    /// </summary>
    public static class UiKit
    {
        private static Font _font;

        /// <summary>
        /// Unity's built-in font. Arial was removed in newer versions; LegacyRuntime.ttf
        /// is its replacement and needs no imported asset, which keeps the repository
        /// free of a binary font until the real one is chosen.
        /// </summary>
        public static Font Font
        {
            get
            {
                if (_font == null) _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                return _font;
            }
        }

        public static Canvas CreateCanvas(string name, Transform parent)
        {
            var holder = new GameObject(name, typeof(RectTransform));
            holder.transform.SetParent(parent, false);

            var canvas = holder.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = holder.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            // Matching width keeps the build bar's proportions stable on ultrawide
            // displays, where matching height would blow the panels up.
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            holder.AddComponent<GraphicRaycaster>();
            return canvas;
        }

        public static RectTransform CreatePanel(string name, Transform parent, RgbColor color,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            var holder = new GameObject(name, typeof(RectTransform));
            holder.transform.SetParent(parent, false);

            var image = holder.AddComponent<Image>();
            image.color = color.ToUnity();

            var rect = holder.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;

            return rect;
        }

        public static Text CreateText(string name, Transform parent, string content, int size,
            RgbColor color, TextAnchor alignment = TextAnchor.UpperLeft)
        {
            var holder = new GameObject(name, typeof(RectTransform));
            holder.transform.SetParent(parent, false);

            var text = holder.AddComponent<Text>();
            text.font = Font;
            text.fontSize = size;
            text.text = content;
            text.color = color.ToUnity();
            text.alignment = alignment;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;

            return text;
        }

        /// <summary>Positions a rect against its parent's top-left corner, in pixels.</summary>
        public static RectTransform PlaceTopLeft(this RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
            return rect;
        }

        public static Button CreateButton(string name, Transform parent, string label, int fontSize,
            RgbColor background, RgbColor foreground)
        {
            var holder = new GameObject(name, typeof(RectTransform));
            holder.transform.SetParent(parent, false);

            var image = holder.AddComponent<Image>();
            image.color = background.ToUnity();

            var button = holder.AddComponent<Button>();
            button.targetGraphic = image;

            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.25f, 1.25f, 1.25f, 1f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            colors.selectedColor = Color.white;
            button.colors = colors;

            // The label child is created even for an empty caption. Callers that fill it
            // in later look it up with GetComponentInChildren, and skipping creation here
            // hands them null.
            var text = CreateText("Label", holder.transform, label ?? string.Empty, fontSize,
                foreground, TextAnchor.MiddleCenter);

            var textRect = text.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            return button;
        }

        public static Image CreateImage(string name, Transform parent, Sprite sprite, RgbColor tint)
        {
            var holder = new GameObject(name, typeof(RectTransform));
            holder.transform.SetParent(parent, false);

            var image = holder.AddComponent<Image>();
            image.sprite = sprite;
            image.color = tint.ToUnity();
            image.raycastTarget = false;
            image.preserveAspect = true;

            return image;
        }

        /// <summary>A thin horizontal bar used for progress, stamina and morale readouts.</summary>
        public static Image CreateBar(string name, Transform parent, RgbColor fill)
        {
            var track = CreateImage(name + "Track", parent, null, Palette.ProgressTrack);
            var fillImage = CreateImage(name + "Fill", track.transform, null, fill);

            var rect = fillImage.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = new Vector2(1f, 1f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.pivot = new Vector2(0f, 0.5f);

            return fillImage;
        }

        /// <summary>Sets a bar fill created by <see cref="CreateBar"/> to a 0..1 fraction.</summary>
        public static void SetBarFill(Image fill, float fraction)
        {
            var rect = fill.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = new Vector2(Mathf.Clamp01(fraction), 1f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        public static string FormatMoney(int cents)
        {
            int whole = cents / 100;
            return (whole < 0 ? "-$" : "$") + Mathf.Abs(whole).ToString("N0",
                System.Globalization.CultureInfo.InvariantCulture);
        }

        public static string ItemLabel(ItemId item)
        {
            switch (item)
            {
                case ItemId.Log: return "Log";
                case ItemId.Plank: return "Plank";
                case ItemId.ChairLeg: return "Chair leg";
                case ItemId.Seat: return "Seat";
                case ItemId.WoodChair: return "Wooden chair";
                case ItemId.Fabric: return "Fabric";
                case ItemId.MetalRod: return "Metal rod";
                case ItemId.OfficeChair: return "Office chair";
                case ItemId.Bookshelf: return "Bookshelf";
                default: return "None";
            }
        }

        public static string BuildingLabel(BuildingKind kind)
        {
            switch (kind)
            {
                case BuildingKind.Intake: return "Intake";
                case BuildingKind.Storage: return "Shelf";
                case BuildingKind.Sawbench: return "Sawbench";
                case BuildingKind.Lathe: return "Lathe";
                case BuildingKind.AssemblyBench: return "Assembly";
                case BuildingKind.Shipping: return "Dispatch";
                case BuildingKind.Conveyor: return "Belt";
                case BuildingKind.BreakRoom: return "Break room";
                case BuildingKind.Wall: return "Wall";
                default: return kind.ToString();
            }
        }

        public static string RecipeLabel(RecipeId recipe)
        {
            switch (recipe)
            {
                case RecipeId.SawLogs: return "Log to planks";
                case RecipeId.TurnLegs: return "Plank to legs";
                case RecipeId.CutSeat: return "Planks to seat";
                case RecipeId.AssembleWoodChair: return "Assemble chair";
                default: return "Idle";
            }
        }

        public static string TaskLabel(WorkerUnit worker)
        {
            if (worker.Task == null) return "Idle";

            switch (worker.Task.Kind)
            {
                case TaskKind.Haul: return "Carrying " + ItemLabel(worker.Task.Item).ToLowerInvariant();
                case TaskKind.Operate: return "Working a bench";
                case TaskKind.Rest: return "On a break";
                default: return "Idle";
            }
        }
    }
}
