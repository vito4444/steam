using System.Collections.Generic;
using UnityEngine;
using Undertown.Core.Sim;
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
        private readonly HashSet<Coord> _built = new HashSet<Coord>();
        private TownState _town;
        private GridMap _map;
        private WorldRenderer _world;
        private int _depth = -1;

        public void Bind(TownState town, WorldRenderer world)
        {
            _town = town;
            _map = town.Map;
            _world = world;
            Rebuild(world.ActiveDepth);
        }

        public void Rebuild(int depth)
        {
            if (_map == null || _world == null) return;
            _depth = depth;
            NoteWhatIsBuilt();

            int used = 0;
            for (int y = 0; y < _map.Height; y++)
            for (int x = 0; x < _map.Width; x++)
            {
                var cell = new Coord(x, y, depth);
                var kind = _map.Get(cell);

                var bed = GardenOn(cell, kind);
                if (bed != null) Place(ref used, cell, bed, offset: -2);

                Sprite art = IsoPropArt.HasProp(kind)
                    ? IsoPropArt.For(kind, IsoTileArt.VariantAt(x, y))
                    : ClutterOn(cell, kind);

                if (art != null) Place(ref used, cell, art, offset: -1);

                var fence = IsoFenceArt.For(FenceEdgesAt(cell, kind));
                if (fence != null) Place(ref used, cell, fence, offset: 1);

                if (kind == TileKind.Water)
                {
                    var shore = IsoShoreArt.For(LandEdgesAt(cell), Hash(x, y) % IsoShoreArt.Variants);
                    if (shore != null) Place(ref used, cell, shore, offset: 1);
                }
            }

            for (int i = used; i < _pool.Count; i++)
                if (_pool[i] != null) _pool[i].enabled = false;
        }

        /// <summary>
        /// Records which cells are built on. Scenery and fencing both have to keep off them:
        /// a building does not change the tile underneath it, so without this a fence line
        /// runs straight through the walls and out over the roof.
        /// </summary>
        private void NoteWhatIsBuilt()
        {
            _built.Clear();
            if (_town == null) return;

            foreach (var building in _town.Buildings)
            {
                var def = building.Def;
                if (def == null) continue;

                for (int dy = 0; dy < def.Height; dy++)
                for (int dx = 0; dx < def.Width; dx++)
                    _built.Add(building.Origin.Offset(dx, dy));
            }
        }

        private void Place(ref int used, Coord cell, Sprite art, int offset)
        {
            var sprite = Take(used++);
            sprite.sprite = art;
            sprite.transform.position = _world.CellCentre(cell);

            // Scenery goes behind whatever the player builds on the cell; fencing goes in
            // front of it, since a fence along the near edge of a plot stands between the
            // viewer and the building inside it.
            sprite.sortingOrder = WorldRenderer.SortingOrderFor(cell) + offset;
            sprite.enabled = true;
        }

        /// <summary>
        /// Which edges of a cell carry fencing: every edge where enclosed ground meets a lane.
        ///
        /// Derived rather than authored. Fencing every plot boundary by hand would mean the
        /// layout and the fences could drift apart the moment either changed, and the player
        /// can pave and build at runtime, so the rule has to hold for ground the town founder
        /// never saw.
        /// </summary>
        private int FenceEdgesAt(Coord cell, TileKind kind)
        {
            if (!IsEnclosed(kind) || _built.Contains(cell)) return 0;

            int mask = 0;
            if (IsLane(_map.Get(cell.Offset(0, -1)))) mask |= IsoFenceArt.South;
            if (IsLane(_map.Get(cell.Offset(1, 0)))) mask |= IsoFenceArt.East;
            if (IsLane(_map.Get(cell.Offset(0, 1)))) mask |= IsoFenceArt.North;
            if (IsLane(_map.Get(cell.Offset(-1, 0)))) mask |= IsoFenceArt.West;
            return mask;
        }

        /// <summary>
        /// Ground worth fencing: someone's plot rather than open country or a way through.
        ///
        /// Worn earth counts, because a working yard is as much a plot as a grass one. What
        /// keeps a gate in the fence is that paths to doors are laid as lane, not as earth -
        /// see TownFounder - so the opening is a lane meeting a lane and no rail is drawn.
        /// </summary>
        private static bool IsEnclosed(TileKind kind) =>
            kind == TileKind.Grass || kind == TileKind.Dirt || kind == TileKind.ClayDeposit;

        private static bool IsLane(TileKind kind) => kind == TileKind.Road;

        /// <summary>Which edges of a water cell face dry ground, and so carry a bank.</summary>
        private int LandEdgesAt(Coord cell)
        {
            int mask = 0;
            if (IsDry(_map.Get(cell.Offset(0, -1)))) mask |= IsoShoreArt.South;
            if (IsDry(_map.Get(cell.Offset(1, 0)))) mask |= IsoShoreArt.East;
            if (IsDry(_map.Get(cell.Offset(0, 1)))) mask |= IsoShoreArt.North;
            if (IsDry(_map.Get(cell.Offset(-1, 0)))) mask |= IsoShoreArt.West;
            return mask;
        }

        private static bool IsDry(TileKind kind) => kind != TileKind.Water;

        /// <summary>
        /// Which plot cells are under cultivation. Roughly half of the fenced grass, in runs
        /// rather than singly, so the beds group into gardens instead of speckling the town
        /// with isolated squares of soil.
        ///
        /// Grass is the only candidate: worn earth is a working yard and paving is a way
        /// through, and putting a seed bed on either would say the wrong thing about ground
        /// the player is meant to read at a glance.
        /// </summary>
        private Sprite GardenOn(Coord cell, TileKind kind)
        {
            if (kind != TileKind.Grass || _built.Contains(cell)) return null;

            // One cell from a lane, not two. At the wider radius the beds spilled past the
            // last fence into open country, and a dug row with no plot around it reads as a
            // field nobody owns rather than as somebody's garden.
            if (!NearALane(cell, radius: 1)) return null;

            // Coarse cells of two-by-two decide together, which is what gives runs; the finer
            // hash then breaks up their edges so the gardens do not come out as blocks.
            int clump = Hash(cell.X >> 1, cell.Y >> 1);
            if (clump % 5 >= 2) return null;

            int h = Hash(cell.X, cell.Y);
            if (h % 7 == 0) return null;

            return IsoGardenArt.For((clump / 5) % IsoGardenArt.Variants);
        }

        private bool NearALane(Coord cell, int radius = 2)
        {
            for (int dy = -radius; dy <= radius; dy++)
            for (int dx = -radius; dx <= radius; dx++)
                if (IsLane(_map.Get(cell.Offset(dx, dy)))) return true;
            return false;
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
        private Sprite ClutterOn(Coord cell, TileKind kind)
        {
            if (_built.Contains(cell)) return null;

            bool yard = kind == TileKind.Dirt || kind == TileKind.Road;

            // Grass counts too, but only inside the town, or the whole map ends up strewn with
            // barrels. Proximity to a lane is the test: open country has no lanes in it, and
            // any grass within a cell or two of one is somebody's plot.
            bool plot = kind == TileKind.Grass && NearALane(cell);
            bool country = kind == TileKind.Grass && !plot;

            int h = Hash(cell.X, cell.Y);
            if (yard || plot)
            {
                if (h % (yard ? 5 : 9) != 0) return null;
                return IsoPropArt.ForClutter(TownClutter[(h / 9) % TownClutter.Length]);
            }

            // Open country gets bushes, weeds and loose stone. The land beyond the town was a
            // flat green field with nothing in it, which made the settlement look pasted onto a
            // backdrop rather than standing in a landscape. These are decoration only: they
            // come from the cell coordinate, never from the map, so nothing the simulation
            // cares about - timber, clay, room to build - changes because of them.
            if (!country || h % 3 != 0) return null;
            return IsoPropArt.ForClutter(CountryClutter[(h / 3) % CountryClutter.Length], (h / 31) & 3);
        }

        private static readonly IsoPropArt.Clutter[] TownClutter =
        {
            IsoPropArt.Clutter.Woodpile, IsoPropArt.Clutter.Crates, IsoPropArt.Clutter.Barrels,
            IsoPropArt.Clutter.Cart, IsoPropArt.Clutter.Well, IsoPropArt.Clutter.Fence,
        };

        /// <summary>
        /// Weighted by repetition. Trees are here rather than in the map because forest is not
        /// walkable and scenery must not close a route the pathfinder was counting on: these
        /// stand on ordinary grass, so the country reads as open woodland and stays crossable.
        /// </summary>
        private static readonly IsoPropArt.Clutter[] CountryClutter =
        {
            IsoPropArt.Clutter.LoneTree, IsoPropArt.Clutter.Shrub, IsoPropArt.Clutter.TallGrass,
            IsoPropArt.Clutter.Broadleaf, IsoPropArt.Clutter.LoneTree, IsoPropArt.Clutter.Stones,
            IsoPropArt.Clutter.Shrub, IsoPropArt.Clutter.TallGrass, IsoPropArt.Clutter.Broadleaf,
        };

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
