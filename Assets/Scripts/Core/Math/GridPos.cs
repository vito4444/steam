using System;

namespace Worker.Core
{
    /// <summary>
    /// Integer tile coordinate. Every position in the simulation is expressed in
    /// tiles; sub-tile positions only exist in the presentation layer.
    /// </summary>
    [Serializable]
    public readonly struct GridPos : IEquatable<GridPos>
    {
        public readonly int X;
        public readonly int Y;

        public GridPos(int x, int y)
        {
            X = x;
            Y = y;
        }

        public static readonly GridPos Zero = new GridPos(0, 0);
        public static readonly GridPos Invalid = new GridPos(int.MinValue, int.MinValue);

        public bool IsValid => X != int.MinValue;

        public static GridPos operator +(GridPos a, GridPos b) => new GridPos(a.X + b.X, a.Y + b.Y);
        public static GridPos operator -(GridPos a, GridPos b) => new GridPos(a.X - b.X, a.Y - b.Y);
        public static bool operator ==(GridPos a, GridPos b) => a.X == b.X && a.Y == b.Y;
        public static bool operator !=(GridPos a, GridPos b) => !(a == b);

        public GridPos Step(Direction dir) => this + dir.Offset();

        /// <summary>Manhattan distance. The only metric the sim uses, since movement is 4-directional.</summary>
        public int ManhattanTo(GridPos other)
        {
            int dx = X - other.X;
            int dy = Y - other.Y;
            if (dx < 0) dx = -dx;
            if (dy < 0) dy = -dy;
            return dx + dy;
        }

        public bool Equals(GridPos other) => X == other.X && Y == other.Y;
        public override bool Equals(object obj) => obj is GridPos other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                return (X * 397) ^ Y;
            }
        }

        public override string ToString() => "(" + X + "," + Y + ")";
    }
}
