using System;

namespace Undertown.Core.World
{
    /// <summary>
    /// The town's terrain: one surface layer plus a stack of underground layers sharing
    /// the same X/Y footprint. Stored as a flat array so that a save is just the bytes and
    /// two runs from the same seed can be compared cell by cell.
    /// </summary>
    [Serializable]
    public sealed class GridMap
    {
        public const int SurfaceDepth = 0;

        public readonly int Width;
        public readonly int Height;

        /// <summary>Total layer count including the surface, so depth indices run 0..DepthCount-1.</summary>
        public readonly int DepthCount;

        private readonly TileKind[] _tiles;

        /// <summary>Marks cells the town has declared to the empire. Undeclared cavities are what sounding hunts for.</summary>
        private readonly bool[] _declared;

        public GridMap(int width, int height, int depthCount)
        {
            if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (depthCount < 1) throw new ArgumentOutOfRangeException(nameof(depthCount));

            Width = width;
            Height = height;
            DepthCount = depthCount;
            _tiles = new TileKind[width * height * depthCount];
            _declared = new bool[_tiles.Length];
        }

        public bool InBounds(Coord c) =>
            c.X >= 0 && c.X < Width && c.Y >= 0 && c.Y < Height && c.Depth < DepthCount;

        private int Index(Coord c) => (c.Depth * Height + c.Y) * Width + c.X;

        public TileKind Get(Coord c) => InBounds(c) ? _tiles[Index(c)] : TileKind.Bedrock;

        public void Set(Coord c, TileKind kind)
        {
            if (!InBounds(c)) throw new ArgumentOutOfRangeException(nameof(c), $"{c} outside {Width}x{Height}x{DepthCount}");
            if (c.Depth == SurfaceDepth && !Tiles.IsSurfaceKind(kind))
                throw new ArgumentException($"{kind} is a subsurface tile and cannot sit on the surface layer", nameof(kind));
            if (c.Depth != SurfaceDepth && Tiles.IsSurfaceKind(kind))
                throw new ArgumentException($"{kind} is a surface tile and cannot sit underground", nameof(kind));
            _tiles[Index(c)] = kind;
        }

        public bool IsDeclared(Coord c) => InBounds(c) && _declared[Index(c)];

        public void SetDeclared(Coord c, bool declared)
        {
            if (!InBounds(c)) throw new ArgumentOutOfRangeException(nameof(c));
            _declared[Index(c)] = declared;
        }

        /// <summary>
        /// Digs a cell out and returns the spoil produced. Spoil has to go somewhere, and
        /// where it goes is the player's problem: a heap of fresh earth on the surface is
        /// evidence just as surely as an open tunnel mouth.
        /// </summary>
        public int Excavate(Coord c)
        {
            var kind = Get(c);
            if (!Tiles.IsDiggable(kind)) return 0;
            Set(c, TileKind.Cavity);
            return 1;
        }

        /// <summary>
        /// What an inspector hears when tapping the surface cell above this column. Returns
        /// the depth of the shallowest undeclared hollow within reach, or -1 for solid ground.
        /// </summary>
        public int SoundColumn(Coord surfaceCell, int maxDepth)
        {
            for (int depth = 1; depth <= maxDepth && depth < DepthCount; depth++)
            {
                var probe = surfaceCell.AtDepth(depth);
                if (Tiles.SoundsHollow(Get(probe)) && !IsDeclared(probe)) return depth;
            }
            return -1;
        }

        /// <summary>Fills an entire layer, used by generation before carving features into it.</summary>
        public void FillLayer(int depth, TileKind kind)
        {
            for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
                Set(new Coord(x, y, depth), kind);
        }

        /// <summary>
        /// Order-independent fingerprint of the whole map, used by determinism tests to
        /// assert that two generations from one seed are identical.
        /// </summary>
        public uint Fingerprint()
        {
            unchecked
            {
                uint hash = 2166136261u;
                for (int i = 0; i < _tiles.Length; i++)
                {
                    hash = (hash ^ (byte)_tiles[i]) * 16777619u;
                    hash = (hash ^ (_declared[i] ? 1u : 0u)) * 16777619u;
                }
                return hash;
            }
        }
    }
}
