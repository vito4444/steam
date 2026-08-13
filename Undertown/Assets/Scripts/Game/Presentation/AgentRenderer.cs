using System.Collections.Generic;
using UnityEngine;
using Undertown.Core.Agents;
using Undertown.Core.Sim;

namespace Undertown.Game.Presentation
{
    /// <summary>
    /// Draws the townsfolk and the inspector. The inspector wears imperial red against a
    /// warm earth palette on purpose: the player has to be able to find him on screen
    /// instantly, because everything they do while he is in town depends on where he is.
    /// </summary>
    public sealed class AgentRenderer : MonoBehaviour
    {
        private static readonly Color32 TownsfolkCoat = new Color32(0x4E, 0x5D, 0x74, 0xFF);
        private static readonly Color32 DiggerCoat = new Color32(0x6B, 0x5A, 0x3C, 0xFF);
        private static readonly Color32 InspectorCoat = new Color32(0xC8, 0x2A, 0x22, 0xFF);
        private static readonly Color32 DisloyalTrim = new Color32(0xC9, 0x8B, 0x3A, 0xFF);

        private TownState _town;
        private readonly Dictionary<Villager, SpriteRenderer> _villagers = new Dictionary<Villager, SpriteRenderer>();
        private SpriteRenderer _inspector;
        private int _activeDepth;

        public void Bind(TownState town)
        {
            _town = town;
            foreach (var pair in _villagers) if (pair.Value != null) Destroy(pair.Value.gameObject);
            _villagers.Clear();
        }

        public void SetActiveDepth(int depth) => _activeDepth = depth;

        private void LateUpdate()
        {
            if (_town == null) return;

            foreach (var villager in _town.Villagers) SyncVillager(villager);
            SyncInspector();
        }

        private void SyncVillager(Villager villager)
        {
            if (!_villagers.TryGetValue(villager, out var sprite) || sprite == null)
            {
                var coat = villager.Role == VillagerRole.Digger ? DiggerCoat : TownsfolkCoat;
                sprite = Spawn($"villager_{villager.Name}", ProceduralAgentArt.Person(coat, villager.WillTalk ? DisloyalTrim : coat));
                _villagers[villager] = sprite;
            }

            Position(sprite, villager.Position.X, villager.Position.Y, villager.Position.Depth);
        }

        private void SyncInspector()
        {
            var inspector = _town.ActiveInspector;
            if (inspector == null || inspector.Task == InspectorTask.Gone)
            {
                if (_inspector != null) _inspector.gameObject.SetActive(false);
                return;
            }

            if (_inspector == null)
                _inspector = Spawn("inspector", ProceduralAgentArt.Person(InspectorCoat, new Color32(0xF0, 0xE0, 0xC0, 0xFF), tall: true));

            _inspector.gameObject.SetActive(true);
            Position(_inspector, inspector.Position.X, inspector.Position.Y, inspector.Position.Depth);
        }

        private SpriteRenderer Spawn(string name, Sprite sprite)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, worldPositionStays: false);
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sortingOrder = 20;
            return renderer;
        }

        private void Position(SpriteRenderer sprite, int x, int y, int depth)
        {
            sprite.transform.position = new Vector3(x + 0.5f, y + 0.5f, 0f);

            bool onActiveLayer = depth == _activeDepth;
            sprite.color = onActiveLayer ? Color.white : new Color(1f, 1f, 1f, 0.3f);
            sprite.sortingOrder = onActiveLayer ? 20 : 5;
        }
    }
}
