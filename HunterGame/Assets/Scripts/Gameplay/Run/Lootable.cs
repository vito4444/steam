using System;
using System.Collections.Generic;
using Hunter.Gameplay.Actors;
using Hunter.Gameplay.Items;
using UnityEngine;

namespace Hunter.Gameplay.Run
{
    /// A searchable container. Contents are rolled on first interaction rather than at
    /// spawn so a raid does not pay the generation cost for chests nobody opens, and so
    /// two hunters racing for the same chest genuinely race for unknown contents.
    public class Lootable : MonoBehaviour
    {
        public enum Tier { Common, Elite }

        [SerializeField] Tier tier = Tier.Common;
        [SerializeField] int minItems = 1;
        [SerializeField] int maxItems = 3;
        [SerializeField] float searchSeconds = 1.6f;
        [SerializeField] Light glow;

        readonly List<ItemInstance> _contents = new();
        System.Random _rng;
        bool _rolled;

        public bool Emptied { get; private set; }
        public float SearchSeconds => searchSeconds;
        public Tier ContainerTier => tier;

        public event Action<Lootable> Searched;

        public void Seed(int seed) => _rng = new System.Random(seed);

        public IReadOnlyList<ItemInstance> Peek()
        {
            EnsureRolled();
            return _contents;
        }

        void EnsureRolled()
        {
            if (_rolled) return;
            _rolled = true;
            _rng ??= new System.Random(GetInstanceID());

            var table = tier == Tier.Elite ? ItemCatalog.EliteCache() : ItemCatalog.CommonCache();
            float luck = tier == Tier.Elite ? 0.42f : 0f;
            int count = _rng.Next(minItems, maxItems + 1);
            _contents.AddRange(table.RollMany(_rng, count, luck));
        }

        public bool TryTake(ItemInstance item)
        {
            EnsureRolled();
            if (!_contents.Remove(item)) return false;

            if (_contents.Count == 0)
            {
                Emptied = true;
                if (glow != null) glow.enabled = false;
            }
            Searched?.Invoke(this);
            return true;
        }
    }
}
