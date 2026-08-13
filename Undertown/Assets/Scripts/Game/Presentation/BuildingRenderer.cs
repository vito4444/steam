using System.Collections.Generic;
using UnityEngine;
using Undertown.Core.Buildings;
using Undertown.Core.Sim;

namespace Undertown.Game.Presentation
{
    /// <summary>
    /// Spawns a sprite per placed building and shows or hides them as the player switches
    /// layers. Buildings on the layer that is not being viewed are drawn faintly rather
    /// than hidden outright, because knowing which chamber sits under which workshop is the
    /// information the player is switching layers to get.
    /// </summary>
    public sealed class BuildingRenderer : MonoBehaviour
    {
        private readonly Dictionary<Building, SpriteRenderer> _sprites = new Dictionary<Building, SpriteRenderer>();
        private TownState _town;
        private WorldRenderer _world;

        public void Bind(TownState town, WorldRenderer world)
        {
            _town = town;
            _world = world;
            Rebuild();
        }

        public void Rebuild()
        {
            foreach (var pair in _sprites) if (pair.Value != null) Destroy(pair.Value.gameObject);
            _sprites.Clear();
            if (_town == null) return;

            foreach (var building in _town.Buildings) Spawn(building);
        }

        private void Spawn(Building building)
        {
            var def = building.Def;
            if (def == null || _world == null) return;

            var go = new GameObject($"{building.Kind}@{building.Origin}");
            go.transform.SetParent(transform, worldPositionStays: false);
            go.transform.position = _world.CellCentre(building.Origin);

            var sprite = go.AddComponent<SpriteRenderer>();

            // Keyed off the origin so a given house always looks the same, including after a
            // rebuild and across runs of the same seed.
            int variant = Mathf.Abs(building.Origin.X * 31 + building.Origin.Y * 17);
            sprite.sprite = IsoBuildingArt.For(building.Kind, variant);

            // A building sorts by its nearest corner - the origin, which is the cell lowest on
            // screen. Sorting by the far corner instead puts the whole structure deeper than it
            // is, and anything standing on the cells it covers draws on top of it: workers
            // inside a workshop appear to be standing on its roof. With the near corner, a
            // figure in front of the door is drawn over the wall and a figure inside is hidden
            // by it, which is what being indoors should look like.
            sprite.sortingOrder = WorldRenderer.SortingOrderFor(building.Origin);

            _sprites[building] = sprite;
        }

        /// <summary>Call after the viewed layer changes so the fading follows the camera.</summary>
        public void ApplyLayerVisibility(int activeDepth)
        {
            _activeDepth = activeDepth;
            RefreshTint();
        }

        private int _activeDepth;

        private void LateUpdate()
        {
            if (_town != null) RefreshTint();
        }

        /// <summary>
        /// A stopped workshop is drawn cold and dim. Whether the still is running is the
        /// single most consequential piece of state in the game, so it cannot be something
        /// the player has to click the building to find out.
        /// </summary>
        private void RefreshTint()
        {
            foreach (var pair in _sprites)
            {
                var building = pair.Key;
                var def = building.Def;
                if (def == null || pair.Value == null) continue;

                bool onActiveLayer = building.Origin.Depth == _activeDepth;
                if (!onActiveLayer)
                {
                    // Faint, cold and dark. A building is an opaque block of colour, so what
                    // reads as a discreet ghost for a tile still dominates the screen for a
                    // house: underground, the town overhead was drowning out the tunnels the
                    // player switched layers to look at.
                    pair.Value.color = new Color(0.34f, 0.36f, 0.42f, 0.30f);
                    continue;
                }

                if (def.WorkerSlots > 0 && !building.Working)
                {
                    pair.Value.color = new Color(0.52f, 0.55f, 0.62f, 1f);
                    continue;
                }

                pair.Value.color = building.Starved
                    ? new Color(0.85f, 0.72f, 0.5f, 1f)
                    : Color.white;
            }
        }

        /// <summary>Adds a building that appeared after the initial bind.</summary>
        public void Track(Building building, int activeDepth)
        {
            if (building == null || _sprites.ContainsKey(building)) return;
            Spawn(building);
            ApplyLayerVisibility(activeDepth);
        }
    }
}
