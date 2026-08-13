using Undertown.Core.Determinism;

namespace Undertown.Core.World
{
    public struct MapSettings
    {
        public int Width;
        public int Height;

        /// <summary>Surface plus underground layers. Six gives the surface and five metres of digging.</summary>
        public int DepthCount;

        public int ForestDensityPerMille;
        public int RockDensityPerMille;
        public int DisusedMineCount;

        public static MapSettings Default => new MapSettings
        {
            Width = 64,
            Height = 64,
            DepthCount = 6,
            ForestDensityPerMille = 300,
            RockDensityPerMille = 120,
            DisusedMineCount = 3,
        };
    }

    /// <summary>
    /// Builds the starting terrain. Everything here runs off a single seeded stream of
    /// integers, so the same seed always produces the same town - which is what lets the
    /// determinism tests compare two generations cell by cell.
    /// </summary>
    public static class MapGenerator
    {
        public static GridMap Generate(MapSettings settings, uint seed)
        {
            var rng = new DeterministicRandom(seed);
            var map = new GridMap(settings.Width, settings.Height, settings.DepthCount);

            map.FillLayer(GridMap.SurfaceDepth, TileKind.Grass);
            for (int d = 1; d < settings.DepthCount - 1; d++) map.FillLayer(d, TileKind.Earth);
            map.FillLayer(settings.DepthCount - 1, TileKind.Bedrock);

            ScatterClusters(map, ref rng, TileKind.Forest, settings.ForestDensityPerMille, smoothPasses: 3);
            ScatterClusters(map, ref rng, TileKind.Rock, settings.RockDensityPerMille, smoothPasses: 2);
            CarveRiver(map, ref rng);
            SeedClayAlongWater(map, ref rng);
            PlaceDisusedMines(map, ref rng, settings.DisusedMineCount);
            CarveMainRoad(map, ref rng);

            return map;
        }

        /// <summary>
        /// Sprinkles a tile kind at the given density, then runs a majority-vote smoothing
        /// pass so the result reads as woods and outcrops rather than television static.
        /// </summary>
        private static void ScatterClusters(GridMap map, ref DeterministicRandom rng, TileKind kind, int densityPerMille, int smoothPasses)
        {
            var mask = new bool[map.Width * map.Height];
            for (int i = 0; i < mask.Length; i++) mask[i] = rng.Chance(densityPerMille);

            for (int pass = 0; pass < smoothPasses; pass++)
            {
                var next = new bool[mask.Length];
                for (int y = 0; y < map.Height; y++)
                for (int x = 0; x < map.Width; x++)
                {
                    int neighbours = 0;
                    for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= map.Width || ny >= map.Height) continue;
                        if (mask[ny * map.Width + nx]) neighbours++;
                    }
                    int idx = y * map.Width + x;
                    next[idx] = neighbours >= 5 || (mask[idx] && neighbours >= 3);
                }
                mask = next;
            }

            for (int y = 0; y < map.Height; y++)
            for (int x = 0; x < map.Width; x++)
            {
                if (!mask[y * map.Width + x]) continue;
                var c = new Coord(x, y);
                if (map.Get(c) == TileKind.Grass) map.Set(c, kind);
            }
        }

        /// <summary>A river wandering top to bottom. It waters the clay and floods anything dug beneath it.</summary>
        private static void CarveRiver(GridMap map, ref DeterministicRandom rng)
        {
            int x = rng.NextInt(map.Width / 4, map.Width * 3 / 4);
            for (int y = 0; y < map.Height; y++)
            {
                int halfWidth = 1 + (y % 7 == 0 ? 1 : 0);
                for (int dx = -halfWidth; dx <= halfWidth; dx++)
                {
                    var c = new Coord(x + dx, y);
                    if (!map.InBounds(c)) continue;
                    map.Set(c, TileKind.Water);

                    // Groundwater sits directly beneath the riverbed; digging into it floods the works.
                    var below = c.AtDepth(1);
                    if (map.InBounds(below)) map.Set(below, TileKind.Aquifer);
                }

                int drift = rng.NextInt(3) - 1;
                x += drift;
                if (x < 3) x = 3;
                if (x > map.Width - 4) x = map.Width - 4;
            }
        }

        private static void SeedClayAlongWater(GridMap map, ref DeterministicRandom rng)
        {
            for (int y = 0; y < map.Height; y++)
            for (int x = 0; x < map.Width; x++)
            {
                var c = new Coord(x, y);
                if (map.Get(c) != TileKind.Grass) continue;

                bool nearWater = false;
                for (int i = 0; i < Coord.Neighbours4.Length && !nearWater; i++)
                {
                    var n = c.Offset(Coord.Neighbours4[i].X, Coord.Neighbours4[i].Y);
                    nearWater = map.Get(n) == TileKind.Water;
                }
                if (nearWater && rng.Chance(400)) map.Set(c, TileKind.ClayDeposit);
            }
        }

        /// <summary>
        /// Abandoned shafts left by whoever dug here before. They are the only lawful place
        /// to dump spoil, which makes their limited capacity a real constraint on how fast
        /// the player can tunnel.
        /// </summary>
        private static void PlaceDisusedMines(GridMap map, ref DeterministicRandom rng, int count)
        {
            int placed = 0;
            for (int attempt = 0; attempt < count * 200 && placed < count; attempt++)
            {
                var c = new Coord(rng.NextInt(2, map.Width - 2), rng.NextInt(2, map.Height - 2));
                if (map.Get(c) != TileKind.Grass) continue;
                map.Set(c, TileKind.DisusedMine);

                // The shaft is already hollow underneath, and already on the empire's books.
                for (int d = 1; d <= 2 && d < map.DepthCount - 1; d++)
                {
                    var below = c.AtDepth(d);
                    if (map.Get(below) != TileKind.Earth) continue;
                    map.Set(below, TileKind.Cavity);
                    map.SetDeclared(below, true);
                }
                placed++;
            }
        }

        private static void CarveMainRoad(GridMap map, ref DeterministicRandom rng)
        {
            int y = map.Height / 2 + rng.NextInt(-3, 4);
            for (int x = 0; x < map.Width; x++)
            {
                var c = new Coord(x, y);
                if (map.Get(c) == TileKind.Water) continue; // the ford is the player's problem
                map.Set(c, TileKind.Road);
            }
        }
    }
}
