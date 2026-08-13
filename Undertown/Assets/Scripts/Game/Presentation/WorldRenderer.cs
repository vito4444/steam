using UnityEngine;
using UnityEngine.Tilemaps;
using Undertown.Core.Sim;
using Undertown.Core.World;

namespace Undertown.Game.Presentation
{
    /// <summary>
    /// Draws the map on an isometric grid as two stacked tilemaps: the layer being looked at,
    /// and an overlay carrying the information the player needs from the other one.
    ///
    /// The overlay is load-bearing, not decoration. Deciding where to dig means knowing what
    /// sits directly above the chamber, and the two layers share a cell grid precisely so
    /// that relationship can be read off the screen. The two directions need different
    /// things: looking underground you want the town above you, while looking at the surface
    /// you only want to see where your own tunnels already run.
    /// </summary>
    public sealed class WorldRenderer : MonoBehaviour
    {
        private const float SurfaceOverlayAlpha = 0.62f;
        private const float UndergroundOverlayAlpha = 0.16f;

        [SerializeField] private Tilemap _primary;
        [SerializeField] private Tilemap _overlay;
        [SerializeField] private Grid _grid;

        private GridMap _map;
        private DigOrders _digs;
        private int _activeDepth;

        public int ActiveDepth => _activeDepth;
        public bool ViewingSurface => _activeDepth == GridMap.SurfaceDepth;
        public Grid Grid => _grid;

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
                primaryTiles[i] = IsoTileArt.TileFor(
                    _map.Get(new Coord(x, y, _activeDepth)), IsoTileArt.VariantAt(x, y));
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
                        ? IsoTileArt.DeclaredHollowMarker
                        : IsoTileArt.HiddenHollowMarker;
                }
                return null;
            }

            // Outstanding dig orders take priority over the town overhead: the player needs to
            // see what they have queued before they need to see what is above it.
            if (_digs != null && _digs.IsOrdered(new Coord(x, y, _activeDepth)))
                return IsoTileArt.DigOrderMarker;

            return IsoTileArt.TileFor(
                _map.Get(new Coord(x, y, GridMap.SurfaceDepth)), IsoTileArt.VariantAt(x, y));
        }

        public void Configure(Grid grid, Tilemap primary, Tilemap overlay)
        {
            _grid = grid;
            _primary = primary;
            _overlay = overlay;
        }

        /// <summary>World position of a cell's centre, for placing anything that is not a tile.</summary>
        public Vector3 CellCentre(Coord cell) =>
            _grid.GetCellCenterWorld(new Vector3Int(cell.X, cell.Y, 0));

        /// <summary>The cell under a world position, on the layer currently being viewed.</summary>
        public Coord CellAt(Vector3 world)
        {
            var cell = _grid.WorldToCell(world);
            return new Coord(cell.x, cell.y, _activeDepth);
        }

        /// <summary>
        /// Draw order for anything standing on a cell.
        ///
        /// Under this projection a cell's distance from the viewer is x plus y: the larger the
        /// sum, the higher up the screen it sits and the further away it is. Distant things
        /// must be drawn first, so the ordering runs the other way from the coordinate, and
        /// the sum is subtracted from a constant larger than any map to keep it positive and
        /// clear of the tilemaps underneath. Getting this backwards is not subtle - near
        /// scenery gets painted over by whatever stands behind it, and trees lose their
        /// crowns to the empty ground one cell north.
        ///
        /// The gap of four leaves room to place scenery, buildings and people against each
        /// other within a single cell.
        /// </summary>
        public static int SortingOrderFor(Coord cell) => (1024 - cell.X - cell.Y) * 4;
    }
}
