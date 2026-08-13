using System.Collections.Generic;
using UnityEngine;
using Worker.Core;

namespace Worker.Game
{
    /// <summary>
    /// World-space feedback for the cursor: the placement footprint, the demolition
    /// target, and whatever is currently selected.
    ///
    /// Placement legality is shown by colour before the click rather than by refusing
    /// the click afterwards. Silently ignoring an illegal placement is the most common
    /// way a builder game feels broken.
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    public sealed class SelectionOverlay : MonoBehaviour
    {
        private const int SortingOverlay = 100;

        private PlayerController _player;
        private SimRunner _runner;
        private Transform _root;

        private readonly List<SpriteRenderer> _footprintPool = new List<SpriteRenderer>();
        private int _footprintUsed;

        private SpriteRenderer _selectionBox;
        private SpriteRenderer _hoverBox;

        private void Awake()
        {
            _player = GetComponent<PlayerController>();
            _runner = GetComponent<SimRunner>();

            var holder = new GameObject("SelectionOverlay");
            holder.transform.SetParent(transform, false);
            _root = holder.transform;

            _selectionBox = CreateQuad("Selection");
            _hoverBox = CreateQuad("Hover");
        }

        private void LateUpdate()
        {
            var world = _runner.World;
            if (world == null) return;

            DrawPlacementPreview();
            DrawSelection();
            DrawHover(world);
        }

        private void DrawPlacementPreview()
        {
            _footprintUsed = 0;

            if (_player.Mode == InteractionMode.Build)
            {
                var color = _player.PlacementValid ? Palette.PlacementValid : Palette.PlacementInvalid;
                var tint = color.ToUnity();
                tint.a = 0.45f;

                foreach (var tile in _player.PreviewFootprint())
                {
                    var quad = RentFootprintQuad();
                    quad.color = tint;
                    quad.transform.position = new Vector3(tile.X + 0.5f, tile.Y + 0.5f, 0f);
                    quad.transform.localScale = new Vector3(0.92f, 0.92f, 1f);
                }
            }

            for (int i = _footprintUsed; i < _footprintPool.Count; i++)
            {
                _footprintPool[i].enabled = false;
            }
        }

        private void DrawSelection()
        {
            var building = _player.SelectedBuilding;
            var worker = _player.SelectedWorker;

            if (building != null)
            {
                _selectionBox.enabled = true;
                _selectionBox.color = WithAlpha(Palette.TextPrimary, 0.30f);
                _selectionBox.transform.position = new Vector3(
                    building.Origin.X + building.Width * 0.5f,
                    building.Origin.Y + building.Height * 0.5f,
                    0f);
                _selectionBox.transform.localScale = new Vector3(building.Width + 0.25f, building.Height + 0.25f, 1f);
                return;
            }

            if (worker != null)
            {
                _selectionBox.enabled = true;
                _selectionBox.color = WithAlpha(Palette.TextPrimary, 0.35f);
                _selectionBox.transform.position = new Vector3(worker.Pos.X + 0.5f, worker.Pos.Y + 0.5f, 0f);
                _selectionBox.transform.localScale = new Vector3(1.2f, 1.2f, 1f);
                return;
            }

            _selectionBox.enabled = false;
        }

        private void DrawHover(SimWorld world)
        {
            if (!_player.HoveringMap || _player.Mode == InteractionMode.Build)
            {
                _hoverBox.enabled = false;
                return;
            }

            if (_player.Mode == InteractionMode.Demolish)
            {
                var target = world.BuildingAt(_player.HoveredTile);
                bool protectedBuilding = target != null
                                         && (target.Kind == BuildingKind.Intake || target.Kind == BuildingKind.Shipping);

                if (target == null)
                {
                    _hoverBox.enabled = false;
                    return;
                }

                _hoverBox.enabled = true;
                _hoverBox.color = WithAlpha(protectedBuilding ? Palette.Warning : Palette.PlacementInvalid, 0.45f);
                _hoverBox.transform.position = new Vector3(
                    target.Origin.X + target.Width * 0.5f,
                    target.Origin.Y + target.Height * 0.5f,
                    0f);
                _hoverBox.transform.localScale = new Vector3(target.Width, target.Height, 1f);
                return;
            }

            _hoverBox.enabled = true;
            _hoverBox.color = WithAlpha(Palette.TextPrimary, 0.12f);
            _hoverBox.transform.position = new Vector3(_player.HoveredTile.X + 0.5f, _player.HoveredTile.Y + 0.5f, 0f);
            _hoverBox.transform.localScale = Vector3.one;
        }

        private SpriteRenderer RentFootprintQuad()
        {
            if (_footprintUsed < _footprintPool.Count)
            {
                var existing = _footprintPool[_footprintUsed++];
                existing.enabled = true;
                return existing;
            }

            var quad = CreateQuad("Footprint");
            _footprintPool.Add(quad);
            _footprintUsed++;
            return quad;
        }

        private SpriteRenderer CreateQuad(string name)
        {
            var holder = new GameObject(name);
            holder.transform.SetParent(_root, false);

            var renderer = holder.AddComponent<SpriteRenderer>();
            renderer.sprite = ProceduralSprites.White();
            renderer.sortingOrder = SortingOverlay;
            renderer.enabled = false;

            // The white sprite is 4x4 pixels at 4 pixels per unit, so a unit scale quad
            // covers exactly one tile.
            return renderer;
        }

        private static Color WithAlpha(RgbColor color, float alpha)
        {
            var unity = color.ToUnity();
            unity.a = alpha;
            return unity;
        }
    }
}
