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
        private WorldRenderer _world;

        private void Awake()
        {
            _renderer = gameObject.AddComponent<SpriteRenderer>();
            _renderer.sortingOrder = 5000;
            _renderer.enabled = false;
        }

        public void Bind(WorldRenderer world) => _world = world;

        public void Hide()
        {
            if (_renderer != null) _renderer.enabled = false;
        }

        public void ShowBuilding(BuildingKind kind, Coord cell, bool allowed)
        {
            var sprite = IsoBuildingArt.For(kind);
            if (sprite == null || _world == null) { Hide(); return; }

            _renderer.enabled = true;
            _renderer.sprite = sprite;
            _renderer.color = allowed ? Allowed : Refused;
            transform.position = _world.CellCentre(cell);
        }

        public void ShowCell(Coord cell, bool allowed)
        {
            if (_world == null) { Hide(); return; }

            _renderer.enabled = true;
            _renderer.sprite = IsoTileArt.SelectionSprite;
            _renderer.color = allowed ? Allowed : Refused;
            transform.position = _world.CellCentre(cell);
        }
    }
}
