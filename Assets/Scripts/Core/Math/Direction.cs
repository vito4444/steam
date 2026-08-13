using System;

namespace Worker.Core
{
    public enum Direction : byte
    {
        North = 0,
        East = 1,
        South = 2,
        West = 3
    }

    public static class DirectionExtensions
    {
        private static readonly GridPos[] Offsets =
        {
            new GridPos(0, 1),
            new GridPos(1, 0),
            new GridPos(0, -1),
            new GridPos(-1, 0)
        };

        public static readonly Direction[] All =
        {
            Direction.North, Direction.East, Direction.South, Direction.West
        };

        public static GridPos Offset(this Direction dir) => Offsets[(int)dir];

        public static Direction Opposite(this Direction dir) => (Direction)(((int)dir + 2) & 3);

        public static Direction RotateCW(this Direction dir) => (Direction)(((int)dir + 1) & 3);

        public static Direction RotateCCW(this Direction dir) => (Direction)(((int)dir + 3) & 3);

        /// <summary>Degrees clockwise from north, for the presentation layer.</summary>
        public static int ToDegrees(this Direction dir) => (int)dir * 90;

        public static Direction FromDelta(GridPos delta)
        {
            if (delta.X == 0 && delta.Y > 0) return Direction.North;
            if (delta.X > 0 && delta.Y == 0) return Direction.East;
            if (delta.X == 0 && delta.Y < 0) return Direction.South;
            if (delta.X < 0 && delta.Y == 0) return Direction.West;
            throw new ArgumentException("Delta " + delta + " is not a unit cardinal step");
        }
    }
}
