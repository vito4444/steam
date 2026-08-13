using System;

namespace Worker.Core
{
    public enum TerrainKind : byte
    {
        /// <summary>Outside the factory shell. Walkable but ugly; buildings cannot be placed.</summary>
        Yard = 0,

        /// <summary>Factory floor. Buildable.</summary>
        Floor = 1
    }

    /// <summary>
    /// Flat tile grid holding terrain and building occupancy. All lookups are
    /// bounds-checked and out-of-range reads report as unwalkable rather than throwing,
    /// which keeps pathfinding and placement previews branch-light.
    /// </summary>
    public sealed class TileMap
    {
        public readonly int Width;
        public readonly int Height;

        private readonly byte[] _terrain;
        private readonly int[] _buildingId;
        private readonly bool[] _walkable;

        public TileMap(int width, int height)
        {
            if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            Width = width;
            Height = height;
            int count = width * height;
            _terrain = new byte[count];
            _buildingId = new int[count];
            _walkable = new bool[count];
            for (int i = 0; i < count; i++) _walkable[i] = true;
        }

        public int TileCount => Width * Height;

        public bool InBounds(GridPos pos)
            => pos.X >= 0 && pos.X < Width && pos.Y >= 0 && pos.Y < Height;

        public int IndexOf(GridPos pos) => pos.Y * Width + pos.X;

        public GridPos PosOf(int index) => new GridPos(index % Width, index / Width);

        public TerrainKind GetTerrain(GridPos pos)
            => InBounds(pos) ? (TerrainKind)_terrain[IndexOf(pos)] : TerrainKind.Yard;

        public void SetTerrain(GridPos pos, TerrainKind kind)
        {
            if (!InBounds(pos)) return;
            _terrain[IndexOf(pos)] = (byte)kind;
        }

        public void FillTerrain(GridPos origin, int width, int height, TerrainKind kind)
        {
            for (int dy = 0; dy < height; dy++)
            {
                for (int dx = 0; dx < width; dx++)
                {
                    SetTerrain(new GridPos(origin.X + dx, origin.Y + dy), kind);
                }
            }
        }

        /// <summary>Building occupying this tile, or 0 when free.</summary>
        public int GetBuildingId(GridPos pos)
            => InBounds(pos) ? _buildingId[IndexOf(pos)] : 0;

        public bool IsFree(GridPos pos) => InBounds(pos) && _buildingId[IndexOf(pos)] == 0;

        public bool IsWalkable(GridPos pos) => InBounds(pos) && _walkable[IndexOf(pos)];

        /// <summary>Marks every footprint tile as occupied by the given building.</summary>
        public void Occupy(BuildingInstance building)
        {
            bool walkable = building.Def.Walkable;
            foreach (var tile in building.Footprint())
            {
                if (!InBounds(tile)) continue;
                int index = IndexOf(tile);
                _buildingId[index] = building.Id;
                _walkable[index] = walkable;
            }
        }

        public void Vacate(BuildingInstance building)
        {
            foreach (var tile in building.Footprint())
            {
                if (!InBounds(tile)) continue;
                int index = IndexOf(tile);
                _buildingId[index] = 0;
                _walkable[index] = true;
            }
        }

        /// <summary>True when every tile of the footprint is in bounds, on factory floor, and unoccupied.</summary>
        public bool CanPlace(BuildingKind kind, GridPos origin, Direction facing)
        {
            var probe = new BuildingInstance(-1, kind, origin, facing);
            foreach (var tile in probe.Footprint())
            {
                if (!InBounds(tile)) return false;
                if (GetTerrain(tile) != TerrainKind.Floor) return false;
                if (!IsFree(tile)) return false;
            }
            return true;
        }
    }
}
