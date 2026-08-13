using Monster.Rules;
using TMPro;
using UnityEngine;

namespace Monster.Presentation
{
    /// <summary>Anything in the booth with text printed on it: a permit, a monitor
    /// readout, a manual page, a switch label.
    ///
    /// All of it is world-space TextMeshPro rather than a screen overlay, because this
    /// concept has no HUD at all. Everything the player reads is a thing on the desk, and
    /// keeping that literally true in the scene graph is what stops a heads-up display
    /// creeping in later.</summary>
    public sealed class PrintedSurface : MonoBehaviour
    {
        [SerializeField] private TextMeshPro title;
        [SerializeField] private TextMeshPro body;
        [SerializeField] private TextMeshPro portrait;
        [SerializeField] private TextMeshPro footer;

        public void Bind(TextMeshPro titleField, TextMeshPro bodyField, TextMeshPro portraitField,
            TextMeshPro footerField)
        {
            title = titleField;
            body = bodyField;
            portrait = portraitField;
            footer = footerField;
        }

        public void Show(DocumentContent content)
        {
            if (content == null)
            {
                Clear();
                return;
            }

            SetText(title, content.Title);
            SetText(body, BuildBody(content));
            SetText(footer, content.Footer);
            SetText(portrait, content.Comparison.HasValue && content.Portrait.HasValue
                ? PortraitCode.SideBySide(content.Portrait.Value, content.Comparison.Value)
                : content.Portrait?.ToBlockRows());
        }

        public void Clear()
        {
            SetText(title, string.Empty);
            SetText(body, string.Empty);
            SetText(footer, string.Empty);
            SetText(portrait, string.Empty);
        }

        /// <summary>Fields are printed with the label padded to a fixed width so the values
        /// form a column. That only works in a monospaced face, which is why the booth's
        /// font is a typewriter one.</summary>
        private static string BuildBody(DocumentContent content)
        {
            var builder = new System.Text.StringBuilder();
            for (var i = 0; i < content.Fields.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append('\n');
                }

                builder.Append(content.Fields[i].ToPrintedLine(content.LabelWidth));
            }

            return builder.ToString();
        }

        private static void SetText(TextMeshPro field, string value)
        {
            if (field == null)
            {
                return;
            }

            field.text = value ?? string.Empty;
            field.ForceMeshUpdate();
        }
    }
}
