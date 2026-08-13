using UnityEngine;
using Undertown.Core.Buildings;
using Undertown.Core.World;

namespace Undertown.Game.Presentation
{
    /// <summary>
    /// The translucent preview under the cursor. Tinted green or red before the click rather
    /// than reporting a refusal after it, so the rules of placement are learned by moving the
    /// mouse instead of by being told no.
    /// </summary>
    public sealed class PlacementGhost : MonoBehaviour
    {
        private static readonly Color Allowed = new Color(0.45f, 1f, 0.5f, 0.55f);
        private static readonly Color Refused = new Color(1f, 0.35f, 0.3f, 0.5f);

        private SpriteRenderer _renderer;

        private void Awake()
        {
            _renderer = gameObject.AddComponent<SpriteRenderer>();
            _renderer.sortingOrder = 30;
            _renderer.enabled = false;
        }

        public void Hide()
        {
            if (_renderer != null) _renderer.enabled = false;
        }

        public void ShowBuilding(BuildingKind kind, Coord cell, bool allowed)
        {
            var sprite = ProceduralBuildingArt.For(kind);
            if (sprite == null) { Hide(); return; }

            _renderer.enabled = true;
            _renderer.sprite = sprite;
            _renderer.color = allowed ? Allowed : Refused;
            transform.position = new Vector3(cell.X, cell.Y, 0f);
        }

        public void ShowCell(Coord cell, bool allowed)
        {
            _renderer.enabled = true;
            _renderer.sprite = ProceduralTileArt.SelectionSprite;
            _renderer.color = allowed ? Allowed : Refused;
            transform.position = new Vector3(cell.X + 0.5f, cell.Y + 0.5f, 0f);
        }
    }
}
