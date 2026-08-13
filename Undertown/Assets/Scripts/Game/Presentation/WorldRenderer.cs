using UnityEngine;
using UnityEngine.Tilemaps;
using Undertown.Core.Sim;
using Undertown.Core.World;

namespace Undertown.Game.Presentation
{
    /// <summary>
    /// Draws the map as two stacked tilemaps: the layer being looked at, and an overlay
    /// carrying the information the player needs from the other one.
    ///
    /// The overlay is load-bearing, not decoration. Deciding where to dig means knowing
    /// what sits directly above the chamber, and the two layers share an X/Y grid precisely
    /// so that relationship can be read off the screen. The two directions need different
    /// things, though: looking underground you want the whole town above you, while looking
    /// at the surface you only want to see where your own tunnels already run.
    /// </summary>
    public sealed class WorldRenderer : MonoBehaviour
    {
        private const float SurfaceOverlayAlpha = 0.42f;

        // Kept low deliberately. The town above is context for siting a chamber, not the
        // subject of the shot; at higher values the green of the surface swamps the earth
        // tones underground and the player loses track of which layer they are looking at.
        private const float UndergroundOverlayAlpha = 0.14f;

        [SerializeField] private Tilemap _primary;
        [SerializeField] private Tilemap _overlay;

        private GridMap _map;
        private DigOrders _digs;
        private int _activeDepth;

        public int ActiveDepth => _activeDepth;
        public bool ViewingSurface => _activeDepth == GridMap.SurfaceDepth;

        public void Bind(GridMap map, DigOrders digs = null)
        {
            _map = map;
            _digs = digs;
            _activeDepth = GridMap.SurfaceDepth;
            Redraw();
        }

        public void ToggleLayer() => SetDepth(ViewingSurface ? 1 : GridMap.SurfaceDepth);

        public void SetDepth(int depth)
        {
            if (_map == null) return;
            _activeDepth = Mathf.Clamp(depth, 0, _map.DepthCount - 1);
            Redraw();
        }

        public void Redraw()
        {
            if (_map == null || _primary == null || _overlay == null) return;

            int count = _map.Width * _map.Height;
            var positions = new Vector3Int[count];
            var primaryTiles = new TileBase[count];
            var overlayTiles = new TileBase[count];

            int i = 0;
            for (int y = 0; y < _map.Height; y++)
            for (int x = 0; x < _map.Width; x++, i++)
            {
                positions[i] = new Vector3Int(x, y, 0);
                primaryTiles[i] = ProceduralTileArt.TileFor(
                    _map.Get(new Coord(x, y, _activeDepth)), ProceduralTileArt.VariantAt(x, y));
                overlayTiles[i] = OverlayTileAt(x, y);
            }

            _primary.ClearAllTiles();
            _overlay.ClearAllTiles();
            _primary.SetTiles(positions, primaryTiles);
            _overlay.SetTiles(positions, overlayTiles);

            _primary.color = Color.white;
            _overlay.color = new Color(1f, 1f, 1f, ViewingSurface ? SurfaceOverlayAlpha : UndergroundOverlayAlpha);
        }

        private TileBase OverlayTileAt(int x, int y)
        {
            if (ViewingSurface)
            {
                // Only mark ground that is hollow underneath. Everything else stays clear so
                // the surface reads normally; the marks are the player's own tunnel network
                // seen from above, which is exactly what an inspector would find by sounding.
                for (int depth = 1; depth < _map.DepthCount; depth++)
                {
                    var probe = new Coord(x, y, depth);
                    if (!Tiles.SoundsHollow(_map.Get(probe))) continue;
                    return _map.IsDeclared(probe)
                        ? ProceduralTileArt.DeclaredHollowMarker
                        : ProceduralTileArt.HiddenHollowMarker;
                }
                return null;
            }

            // Outstanding dig orders take priority over the town overhead: the player needs to
            // see what they have queued before they need to see what is above it.
            if (_digs != null && _digs.IsOrdered(new Coord(x, y, _activeDepth)))
                return ProceduralTileArt.DigOrderMarker;

            // Otherwise show the whole town overhead so chambers can be sited away from roads
            // and buildings, which is where inspectors actually walk and tap.
            return ProceduralTileArt.TileFor(
                _map.Get(new Coord(x, y, GridMap.SurfaceDepth)), ProceduralTileArt.VariantAt(x, y));
        }

        public void Configure(Tilemap primary, Tilemap overlay)
        {
            _primary = primary;
            _overlay = overlay;
        }
    }
}
