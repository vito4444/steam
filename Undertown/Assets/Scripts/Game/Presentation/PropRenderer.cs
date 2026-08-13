using System.Collections.Generic;
using UnityEngine;
using Undertown.Core.World;

namespace Undertown.Game.Presentation
{
    /// <summary>
    /// Places the standing scenery for whichever layer is being viewed, sorted into the same
    /// order as buildings and people so everything above ground level occludes correctly.
    /// </summary>
    public sealed class PropRenderer : MonoBehaviour
    {
        private readonly List<SpriteRenderer> _pool = new List<SpriteRenderer>();
        private GridMap _map;
        private WorldRenderer _world;
        private int _depth = -1;

        public void Bind(GridMap map, WorldRenderer world)
        {
            _map = map;
            _world = world;
            Rebuild(world.ActiveDepth);
        }

        public void Rebuild(int depth)
        {
            if (_map == null || _world == null) return;
            _depth = depth;

            int used = 0;
            for (int y = 0; y < _map.Height; y++)
            for (int x = 0; x < _map.Width; x++)
            {
                var cell = new Coord(x, y, depth);
                var kind = _map.Get(cell);
                if (!IsoPropArt.HasProp(kind)) continue;

                var sprite = Take(used++);
                sprite.sprite = IsoPropArt.For(kind, IsoTileArt.VariantAt(x, y));
                sprite.transform.position = _world.CellCentre(cell);

                // One below the building on the same cell: scenery is behind anything the
                // player builds there, in front of anything further north.
                sprite.sortingOrder = WorldRenderer.SortingOrderFor(cell) - 1;
                sprite.enabled = true;
            }

            for (int i = used; i < _pool.Count; i++)
                if (_pool[i] != null) _pool[i].enabled = false;
        }

        private SpriteRenderer Take(int index)
        {
            while (_pool.Count <= index)
            {
                var go = new GameObject($"prop_{_pool.Count}");
                go.transform.SetParent(transform, worldPositionStays: false);
                _pool.Add(go.AddComponent<SpriteRenderer>());
            }
            return _pool[index];
        }
    }
}
