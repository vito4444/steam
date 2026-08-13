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

        public void Bind(TownState town)
        {
            _town = town;
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
            if (def == null) return;

            var go = new GameObject($"{building.Kind}@{building.Origin}");
            go.transform.SetParent(transform, worldPositionStays: false);
            go.transform.position = new Vector3(building.Origin.X, building.Origin.Y, 0f);

            var sprite = go.AddComponent<SpriteRenderer>();
            sprite.sprite = ProceduralBuildingArt.For(building.Kind);
            sprite.sortingOrder = def.Underground ? 4 : 6;

            _sprites[building] = sprite;
        }

        /// <summary>Call after the viewed layer changes so the fading follows the camera.</summary>
        public void ApplyLayerVisibility(int activeDepth)
        {
            foreach (var pair in _sprites)
            {
                var def = pair.Key.Def;
                if (def == null || pair.Value == null) continue;

                int buildingDepth = pair.Key.Origin.Depth;
                bool onActiveLayer = buildingDepth == activeDepth;
                pair.Value.color = onActiveLayer ? Color.white : new Color(1f, 1f, 1f, 0.35f);
                pair.Value.sortingOrder = onActiveLayer ? 6 : 4;
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
