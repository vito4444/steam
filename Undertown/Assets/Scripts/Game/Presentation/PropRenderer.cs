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

                Sprite art = IsoPropArt.HasProp(kind)
                    ? IsoPropArt.For(kind, IsoTileArt.VariantAt(x, y))
                    : ClutterOn(cell, kind);
                if (art == null) continue;

                var sprite = Take(used++);
                sprite.sprite = art;
                sprite.transform.position = _world.CellCentre(cell);

                // One below the building on the same cell: scenery is behind anything the
                // player builds there, in front of anything further north.
                sprite.sortingOrder = WorldRenderer.SortingOrderFor(cell) - 1;
                sprite.enabled = true;
            }

            for (int i = used; i < _pool.Count; i++)
                if (_pool[i] != null) _pool[i].enabled = false;
        }

        /// <summary>
        /// Sparse clutter on the town's trodden ground. Driven off the cell coordinate rather
        /// than a random draw so it is stable across redraws and identical between runs of the
        /// same seed - the rest of the game is deterministic and the scenery should not be the
        /// one thing that shifts under the player when they toggle a layer.
        ///
        /// It is not placed on cells a building occupies; that is checked by the caller only
        /// loosely, since a building sprite covers its own footprint anyway and a crate poking
        /// out from behind a wall reads as a yard rather than as a mistake.
        /// </summary>
        private static Sprite ClutterOn(Coord cell, TileKind kind)
        {
            if (kind != TileKind.Dirt && kind != TileKind.Road) return null;

            int h = Hash(cell.X, cell.Y);
            if (h % 9 != 0) return null;

            var choices = new[]
            {
                IsoPropArt.Clutter.Woodpile, IsoPropArt.Clutter.Crates, IsoPropArt.Clutter.Barrels,
                IsoPropArt.Clutter.Cart, IsoPropArt.Clutter.Well, IsoPropArt.Clutter.Fence,
            };
            return IsoPropArt.ForClutter(choices[(h / 9) % choices.Length]);
        }

        private static int Hash(int x, int y)
        {
            unchecked
            {
                int h = x * 374761393 + y * 668265263;
                h = (h ^ (h >> 13)) * 1274126177;
                return (h ^ (h >> 16)) & 0x7FFFFFFF;
            }
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
