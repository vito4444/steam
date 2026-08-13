using UnityEngine;
using UnityEngine.Tilemaps;
using Undertown.Core.World;

namespace Undertown.Game.Presentation
{
    /// <summary>
    /// Draws the map onto two tilemaps: the layer the player is looking at, and a dimmed
    /// ghost of the other one. The ghost is not decoration - deciding where to dig means
    /// knowing what sits directly above or below, and the two layers share an X/Y grid
    /// precisely so that relationship can be read off the screen.
    /// </summary>
    public sealed class WorldRenderer : MonoBehaviour
    {
        [SerializeField] private Tilemap _primary;
        [SerializeField] private Tilemap _ghost;

        private GridMap _map;
        private int _activeDepth;

        public int ActiveDepth => _activeDepth;
        public bool ViewingSurface => _activeDepth == GridMap.SurfaceDepth;

        public void Bind(GridMap map)
        {
            _map = map;
            _activeDepth = GridMap.SurfaceDepth;
            Redraw();
        }

        /// <summary>Surface and the first underground layer are the two views the player toggles between.</summary>
        public void ToggleLayer()
        {
            SetDepth(ViewingSurface ? 1 : GridMap.SurfaceDepth);
        }

        public void SetDepth(int depth)
        {
            if (_map == null) return;
            _activeDepth = Mathf.Clamp(depth, 0, _map.DepthCount - 1);
            Redraw();
        }

        public void Redraw()
        {
            if (_map == null || _primary == null || _ghost == null) return;

            _primary.ClearAllTiles();
            _ghost.ClearAllTiles();

            int ghostDepth = ViewingSurface ? 1 : GridMap.SurfaceDepth;

            var positions = new Vector3Int[_map.Width * _map.Height];
            var primaryTiles = new TileBase[positions.Length];
            var ghostTiles = new TileBase[positions.Length];

            int i = 0;
            for (int y = 0; y < _map.Height; y++)
            for (int x = 0; x < _map.Width; x++, i++)
            {
                positions[i] = new Vector3Int(x, y, 0);
                primaryTiles[i] = ProceduralTileArt.TileFor(_map.Get(new Coord(x, y, _activeDepth)));
                ghostTiles[i] = ProceduralTileArt.TileFor(_map.Get(new Coord(x, y, ghostDepth)));
            }

            _primary.SetTiles(positions, primaryTiles);
            _ghost.SetTiles(positions, ghostTiles);

            _primary.color = Color.white;
            _ghost.color = new Color(1f, 1f, 1f, 0.22f);
        }

        public void Configure(Tilemap primary, Tilemap ghost)
        {
            _primary = primary;
            _ghost = ghost;
        }
    }
}
