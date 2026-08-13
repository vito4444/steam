using System;

namespace Undertown.Core.World
{
    /// <summary>
    /// A cell address. Depth 0 is the surface; depths 1 and below are underground, one
    /// step per metre of digging. Surface and underground share the same X/Y grid on
    /// purpose: the central decision of the game is where a chamber sits relative to the
    /// inspector's patrol route above it, and that only reads if the two layers line up.
    /// </summary>
    [Serializable]
    public readonly struct Coord : IEquatable<Coord>
    {
        public readonly short X;
        public readonly short Y;
        public readonly byte Depth;

        public Coord(int x, int y, int depth = 0)
        {
            X = (short)x;
            Y = (short)y;
            Depth = (byte)depth;
        }

        public bool IsSurface => Depth == 0;

        public Coord AtDepth(int depth) => new Coord(X, Y, depth);
        public Coord Offset(int dx, int dy) => new Coord(X + dx, Y + dy, Depth);

        /// <summary>Manhattan distance within a layer. Returns -1 for cells on different layers.</summary>
        public int ManhattanTo(Coord other)
        {
            if (other.Depth != Depth) return -1;
            return Math.Abs(other.X - X) + Math.Abs(other.Y - Y);
        }

        public bool Equals(Coord other) => X == other.X && Y == other.Y && Depth == other.Depth;
        public override bool Equals(object obj) => obj is Coord c && Equals(c);
        public override int GetHashCode() => (X << 18) ^ (Y << 4) ^ Depth;
        public override string ToString() => Depth == 0 ? $"({X},{Y})" : $"({X},{Y},-{Depth})";

        public static bool operator ==(Coord a, Coord b) => a.Equals(b);
        public static bool operator !=(Coord a, Coord b) => !a.Equals(b);

        public static readonly Coord[] Neighbours4 =
        {
            new Coord(1, 0), new Coord(-1, 0), new Coord(0, 1), new Coord(0, -1),
        };
    }
}
