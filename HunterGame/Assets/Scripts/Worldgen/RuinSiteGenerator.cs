using System.Collections.Generic;
using UnityEngine;

namespace Hunter.Worldgen
{
    /// Builds the Aurum Mist ruin set piece. Layout follows the concept frame in
    /// concepts/concept-A-aurum-mist.png: a dark foreground frame, a colonnade receding
    /// into fog, and a tower silhouette holding the far plane.
    public class RuinSiteGenerator
    {
        public struct Palette
        {
            public Material Stone;
            public Material Ground;
            public Material Gold;
            public Material Silhouette;
            public Material Cloth;
            public Material Water;
            public Material Timber;
            public Material Banner;
        }

        readonly System.Random _rng;
        readonly Palette _palette;
        int _seedCounter;

        public RuinSiteGenerator(int seed, Palette palette)
        {
            _rng = new System.Random(seed);
            _palette = palette;
            _seedCounter = seed;
        }

        int NextSeed() => unchecked(_seedCounter = _seedCounter * 1103515245 + 12345);
        float Range(float a, float b) => a + (float)_rng.NextDouble() * (b - a);

        /// Handles to the objects gameplay code needs to attach behaviour to.
        public sealed class SiteHandles
        {
            public Transform Hunter;
            public Transform BellTower;
            public readonly List<Transform> Rivals = new();
            public readonly List<Transform> LootCaches = new();
            public readonly List<Transform> PatrolPoints = new();
        }

        public SiteHandles Generate(Transform root)
        {
            var handles = new SiteHandles();

            BuildGround(root);
            BuildForegroundFrame(root);
            BuildColonnade(root);
            BuildMidgroundWalls(root);
            BuildRoofBeams(root);
            BuildFarTower(root);
            BuildRubbleField(root);
            BuildPuddles(root);
            BuildProps(root);

            handles.LootCaches.Add(BuildLootCache(root, new Vector3(2.75f, 0f, 9.6f)).transform);
            handles.LootCaches.Add(BuildLootCache(root, new Vector3(-5.4f, 0f, 22.5f)).transform);
            handles.LootCaches.Add(BuildLootCache(root, new Vector3(4.9f, 0f, 33.8f)).transform);

            handles.Hunter = BuildHunter(root, new Vector3(-0.62f, 0f, 0.55f)).transform;
            handles.BellTower = BuildBellTower(root, new Vector3(-6.8f, 0f, 30f)).transform;
            BuildRivalSilhouettes(root, handles);
            BuildPatrolPoints(root, handles);

            return handles;
        }

        void BuildPatrolPoints(Transform root, SiteHandles handles)
        {
            var container = new GameObject("PatrolPoints");
            container.transform.SetParent(root, false);

            var positions = new[]
            {
                new Vector3(-4.2f, 0f, 12f), new Vector3(4.4f, 0f, 18f),
                new Vector3(-5.1f, 0f, 26f), new Vector3(3.8f, 0f, 34f),
                new Vector3(-2.4f, 0f, 42f), new Vector3(2.2f, 0f, 24f),
            };

            for (int i = 0; i < positions.Length; i++)
            {
                var point = new GameObject($"Patrol_{i}");
                point.transform.SetParent(container.transform, false);
                point.transform.position = positions[i];
                handles.PatrolPoints.Add(point.transform);
            }
        }

        /// The bell: a stone frame with a hanging bronze bell. Ringing it is the loudest
        /// commitment in a raid, so it needs to be visible from across the nave.
        GameObject BuildBellTower(Transform root, Vector3 pos)
        {
            var mb = new MeshBuilder();
            var stone = new Color(0.44f, 0.44f, 0.45f);

            for (int side = -1; side <= 1; side += 2)
            {
                mb.AddChamferedBox(pos + new Vector3(side * 1.15f, 1.75f, 0f),
                    new Vector3(0.55f, 3.5f, 0.55f), 0.06f, stone,
                    jitter: 0.03f, seed: NextSeed(), uvScale: 0.9f);
            }
            mb.AddChamferedBox(pos + new Vector3(0f, 3.65f, 0f), new Vector3(3.1f, 0.45f, 0.6f),
                0.06f, stone * 0.94f, jitter: 0.03f, seed: NextSeed(), uvScale: 0.9f);

            var tower = Emit(root, "BellTower", mb.ToMesh("BellTower"), _palette.Stone);
            tower.transform.position = Vector3.zero;

            var bell = new MeshBuilder();
            var bronze = new Color(0.95f, 0.72f, 0.30f);
            for (int r = 0; r < 6; r++)
            {
                float t0 = r / 6f, t1 = (r + 1) / 6f;
                for (int s = 0; s < 12; s++)
                {
                    float a0 = 2f * Mathf.PI * s / 12, a1 = 2f * Mathf.PI * (s + 1) / 12;
                    bell.AddQuad(BellPoint(pos, t0, a0), BellPoint(pos, t0, a1),
                        BellPoint(pos, t1, a1), BellPoint(pos, t1, a0), bronze, 1.6f);
                }
            }
            Emit(tower.transform, "BellBody", bell.ToMesh("BellBody"), _palette.Gold);

            var glowGo = new GameObject("BellGlow");
            glowGo.transform.SetParent(tower.transform, false);
            glowGo.transform.position = pos + Vector3.up * 2.6f;
            var glow = glowGo.AddComponent<Light>();
            glow.type = LightType.Point;
            glow.color = new Color(1f, 0.78f, 0.42f);
            glow.intensity = 3.2f;
            glow.range = 12f;
            glow.shadows = LightShadows.None;

            var marker = new GameObject("BellAnchor");
            marker.transform.SetParent(tower.transform, false);
            marker.transform.position = pos;

            var trigger = marker.AddComponent<SphereCollider>();
            trigger.isTrigger = true;
            trigger.radius = 2.2f;
            trigger.center = new Vector3(0f, 1.4f, 0f);
            return marker;
        }

        static Vector3 BellPoint(Vector3 origin, float t, float phi)
        {
            float y = 3.35f - t * 1.05f;
            float radius = Mathf.Lerp(0.16f, 0.52f, Mathf.Pow(t, 1.4f));
            return origin + new Vector3(Mathf.Cos(phi) * radius, y, Mathf.Sin(phi) * radius);
        }

        GameObject Emit(Transform parent, string name, Mesh mesh, Material material,
            bool castShadows = true, bool collide = false)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = material;
            mr.shadowCastingMode = castShadows
                ? UnityEngine.Rendering.ShadowCastingMode.On
                : UnityEngine.Rendering.ShadowCastingMode.Off;

            // Only the surfaces a hunter can stand on or bump into get collision. Rubble
            // and roof beams stay non-solid so movement never snags on scatter detail.
            if (collide) go.AddComponent<MeshCollider>().sharedMesh = mesh;
            return go;
        }

        /// Flagstones rather than a plane. Independent slabs with height variation give the
        /// grazing lantern light something to break against.
        void BuildGround(Transform root)
        {
            var mb = new MeshBuilder();
            var baseColor = new Color(0.42f, 0.43f, 0.44f);

            // Continuous, gently undulating pavement. Earlier revisions cut the floor into
            // individual slab boxes; every joint then cast its own silhouette and the
            // result read as a tray of tiles. Joint detail now comes from the normal map.
            mb.AddSmoothGrid(
                new Vector3(-20f, 0f, -10f), 40f, 74f, 88, 156,
                (x, z) =>
                {
                    float broad = ProceduralTextures.Fbm(x * 0.045f + 11f, z * 0.045f + 3f, 3) - 0.5f;
                    float fine = ProceduralTextures.Fbm(x * 0.33f, z * 0.33f, 2) - 0.5f;
                    return broad * 0.32f + fine * 0.05f;
                },
                baseColor, uvScale: 1.05f);

            Emit(root, "Ground", mb.ToMesh("Ground"), _palette.Ground, castShadows: false, collide: true);

            // A scatter of dislodged slabs tilted out of the pavement carries the "broken
            // ground" read that a smooth sheet alone cannot.
            var slabs = new MeshBuilder();
            for (int i = 0; i < 54; i++)
            {
                float z = Range(-4f, 46f);
                float x = Range(-13f, 13f);
                if (Mathf.Abs(x) < 1.6f && z < 6f) continue;   // keep the hero's footing clear

                float w = Range(0.7f, 1.45f);
                float d = w * Range(0.7f, 1.25f);
                var center = new Vector3(x, Range(0.0f, 0.09f), z);
                var rotation = Quaternion.Euler(Range(-8f, 8f), Range(0f, 360f), Range(-8f, 8f));

                AddRotatedSlab(slabs, center, new Vector3(w, 0.15f, d), rotation,
                    baseColor * Range(0.86f, 1.08f));
            }
            Emit(root, "BrokenSlabs", slabs.ToMesh("BrokenSlabs"), _palette.Ground);
        }

        static void AddRotatedSlab(MeshBuilder mb, Vector3 center, Vector3 size, Quaternion rotation, Color color)
        {
            Vector3 h = size * 0.5f;
            var corners = new Vector3[8];
            for (int i = 0; i < 8; i++)
            {
                var local = new Vector3(
                    (i & 1) == 0 ? -h.x : h.x,
                    (i & 2) == 0 ? -h.y : h.y,
                    (i & 4) == 0 ? -h.z : h.z);
                corners[i] = center + rotation * local;
            }

            mb.AddQuad(corners[2], corners[3], corners[7], corners[6], color, 1.05f);
            mb.AddQuad(corners[0], corners[4], corners[5], corners[1], color, 1.05f);
            mb.AddQuad(corners[0], corners[1], corners[3], corners[2], color, 1.05f);
            mb.AddQuad(corners[5], corners[4], corners[6], corners[7], color, 1.05f);
            mb.AddQuad(corners[4], corners[0], corners[2], corners[6], color, 1.05f);
            mb.AddQuad(corners[1], corners[5], corners[7], corners[3], color, 1.05f);
        }

        /// The foreground frame is the biggest single upgrade over the M0 probe: dark
        /// masses at the edges create depth and stop the shot reading as a flat corridor.
        void BuildForegroundFrame(Transform root)
        {
            var mb = new MeshBuilder();
            var stone = new Color(0.36f, 0.36f, 0.37f);

            // Uneven course heights. Equal-height courses read as a bookshelf, which is
            // exactly how the first revision of this frame looked.
            float leftY = 0.4f;
            for (int i = 0; i < 8 && leftY < 7.6f; i++)
            {
                float courseHeight = Range(0.55f, 1.35f);
                float depth = Range(1.25f, 1.85f) * Mathf.Clamp01(1f - i * 0.05f);
                mb.AddChamferedBox(
                    new Vector3(-4.75f + Range(-0.18f, 0.18f), leftY + courseHeight * 0.5f, 2.2f + Range(-0.22f, 0.22f)),
                    new Vector3(Range(1.3f, 2.0f), courseHeight, depth),
                    0.09f, stone * Range(0.86f, 1.06f), jitter: 0.11f, seed: NextSeed(), uvScale: 0.6f);
                leftY += courseHeight - Range(0.02f, 0.12f);
            }

            // Right pier, taller and deeper so the frame is asymmetric.
            float rightY = 0.4f;
            for (int i = 0; i < 10 && rightY < 9.4f; i++)
            {
                float courseHeight = Range(0.6f, 1.45f);
                mb.AddChamferedBox(
                    new Vector3(5.15f + Range(-0.16f, 0.16f), rightY + courseHeight * 0.5f, 3.4f + Range(-0.2f, 0.2f)),
                    new Vector3(Range(1.5f, 2.2f), courseHeight, Range(1.35f, 2.0f)),
                    0.09f, stone * Range(0.84f, 1.04f), jitter: 0.11f, seed: NextSeed(), uvScale: 0.6f);
                rightY += courseHeight - Range(0.02f, 0.14f);
            }

            // Collapsed arch spanning overhead: only the left haunch survives, so the top
            // of the frame is closed on one side and open on the other.
            mb.AddArch(new Vector3(0.1f, 6.4f, 3.0f), 4.3f, 0.85f, 2.1f, 13,
                stone * 0.92f, collapseFrom: 0.62f);

            Emit(root, "ForegroundFrame", mb.ToMesh("ForegroundFrame"), _palette.Stone, collide: true);
        }

        void BuildColonnade(Transform root)
        {
            var mb = new MeshBuilder();
            var stone = new Color(0.44f, 0.44f, 0.45f);

            for (int i = 0; i < 9; i++)
            {
                float z = 7.5f + i * 5.4f;
                for (int side = -1; side <= 1; side += 2)
                {
                    // Every few columns is a stump instead of a full shaft, so the
                    // colonnade reads as ruined rather than as a repeating asset.
                    bool broken = _rng.NextDouble() < 0.34;
                    float height = broken ? Range(1.6f, 3.4f) : Range(6.4f, 7.6f);
                    float x = side * (4.6f + Range(-0.25f, 0.25f));
                    var pos = new Vector3(x, 0f, z + Range(-0.5f, 0.5f));
                    var tint = stone * Range(0.82f, 1.12f);

                    mb.AddChamferedBox(pos + Vector3.up * 0.26f, new Vector3(1.85f, 0.52f, 1.85f),
                        0.07f, tint * 0.95f, jitter: 0.03f, seed: NextSeed(), uvScale: 0.6f);

                    mb.AddFlutedColumn(pos + Vector3.up * 0.5f, height, 0.62f, 9, tint);

                    if (!broken)
                    {
                        mb.AddChamferedBox(pos + Vector3.up * (0.5f + height + 0.24f),
                            new Vector3(1.65f, 0.48f, 1.65f),
                            0.06f, tint * 1.05f, jitter: 0.03f, seed: NextSeed(), uvScale: 0.6f);

                        // Broken lintel stub cantilevering inward hints at a lost roof.
                        if (_rng.NextDouble() < 0.55)
                        {
                            mb.AddChamferedBox(
                                pos + new Vector3(-side * Range(0.7f, 1.5f), 0.5f + height + 0.72f, 0f),
                                new Vector3(Range(1.4f, 2.6f), 0.62f, 1.1f),
                                0.06f, tint * 0.9f, jitter: 0.05f, seed: NextSeed(), uvScale: 0.6f);
                        }
                    }
                    else
                    {
                        // Toppled drum lying beside its own stump.
                        mb.AddRock(pos + new Vector3(Range(-1.4f, 1.4f), 0.42f, Range(-1.2f, 1.2f)),
                            Range(0.55f, 0.85f), NextSeed(), tint * 0.88f, flatten: 0.75f);
                    }
                }
            }

            Emit(root, "Colonnade", mb.ToMesh("Colonnade"), _palette.Stone, collide: true);
        }

        /// Low walls between the columns at staggered depths. These are what create the
        /// four-to-five distinct fog layers the concept frame has and the probe lacked.
        void BuildMidgroundWalls(Transform root)
        {
            var mb = new MeshBuilder();
            var stone = new Color(0.40f, 0.40f, 0.41f);
            float[] depths = { 13f, 19.5f, 27f, 35f, 44f };

            foreach (float z in depths)
            {
                float gapCenter = Range(-2.2f, 2.2f);
                float gapWidth = Range(2.6f, 4.2f);

                for (float x = -11f; x < 11f; x += 1.25f)
                {
                    if (Mathf.Abs(x - gapCenter) < gapWidth * 0.5f) continue;

                    int courses = Mathf.Max(1, Mathf.RoundToInt(Range(2f, 6f)));
                    for (int c = 0; c < courses; c++)
                    {
                        mb.AddChamferedBox(
                            new Vector3(x + Range(-0.07f, 0.07f), 0.34f + c * 0.62f, z + Range(-0.25f, 0.25f)),
                            new Vector3(1.2f, 0.58f, Range(0.85f, 1.15f)),
                            0.05f, stone * Range(0.76f, 1.1f), jitter: 0.05f, seed: NextSeed(), uvScale: 0.65f);
                    }
                }
            }

            Emit(root, "MidgroundWalls", mb.ToMesh("MidgroundWalls"), _palette.Stone, collide: true);
        }

        /// Far tower anchoring the vanishing point, matching the concept frame's skyline.
        /// Surviving roof beams spanning the colonnade. They are the reason the key light
        /// breaks into discrete shafts instead of washing the whole nave evenly; without
        /// an occluder overhead there is nothing for a steep light to cut against.
        /// Narrative props. The concept frame is full of evidence that people were here
        /// and left in a hurry; bare architecture reads as a level, not as a place.
        void BuildProps(Transform root)
        {
            var timber = new MeshBuilder();
            var banners = new MeshBuilder();
            var wood = new Color(0.34f, 0.24f, 0.15f);
            var cloth = new Color(0.52f, 0.16f, 0.11f);

            // Scaffolds abandoned mid-repair, leaning against the colonnade.
            for (int i = 0; i < 7; i++)
            {
                float z = Range(9f, 44f);
                int side = _rng.NextDouble() < 0.5 ? -1 : 1;
                float x = side * Range(3.4f, 6.2f);
                float height = Range(2.2f, 4.1f);

                for (int leg = 0; leg < 2; leg++)
                {
                    float lx = x + (leg == 0 ? -0.5f : 0.5f);
                    timber.AddChamferedBox(new Vector3(lx, height * 0.5f, z),
                        new Vector3(0.19f, height, 0.19f), 0.028f, wood * Range(0.8f, 1.15f),
                        jitter: 0.03f, seed: NextSeed(), uvScale: 2.4f);
                }
                for (int rung = 1; rung <= 3; rung++)
                {
                    float ry = height * rung / 4f;
                    timber.AddChamferedBox(new Vector3(x, ry, z), new Vector3(1.3f, 0.15f, 0.16f),
                        0.025f, wood * Range(0.75f, 1.1f), jitter: 0.02f, seed: NextSeed(), uvScale: 2.4f);
                }
                AddRotatedSlab(timber,
                    new Vector3(x, height * 0.5f, z + 0.16f),
                    new Vector3(Mathf.Sqrt(1f + height * height) * 0.98f, 0.11f, 0.13f),
                    Quaternion.Euler(0f, 0f, Mathf.Atan2(height, 1f) * Mathf.Rad2Deg),
                    wood * Range(0.72f, 0.95f));

                // A plank that has slipped and now hangs at an angle.
                if (_rng.NextDouble() < 0.6)
                {
                    AddRotatedSlab(timber, new Vector3(x + side * 0.7f, height * Range(0.4f, 0.8f), z + Range(-0.4f, 0.4f)),
                        new Vector3(Range(1.4f, 2.4f), 0.08f, 0.28f),
                        Quaternion.Euler(Range(-25f, 25f), Range(0f, 180f), Range(15f, 55f)), wood * 0.9f);
                }
            }

            // Poles with hanging banners, the marks of whoever claimed this ruin first.
            for (int i = 0; i < 6; i++)
            {
                float z = Range(12f, 42f);
                int side = _rng.NextDouble() < 0.5 ? -1 : 1;
                float x = side * Range(2.6f, 5.4f);
                float poleHeight = Range(2.8f, 4.4f);

                timber.AddChamferedBox(new Vector3(x, poleHeight * 0.5f, z),
                    new Vector3(0.17f, poleHeight, 0.17f), 0.025f, wood * 0.85f,
                    jitter: 0.02f, seed: NextSeed(), uvScale: 3f);

                // Cloth hangs as a tapered sheet with a torn, uneven hem.
                float width = Range(1.0f, 1.6f);
                float drop = Range(1.6f, 2.8f);
                float top = poleHeight - 0.15f;
                const int cols = 5, rows = 6;

                for (int r = 0; r < rows; r++)
                {
                    for (int c = 0; c < cols; c++)
                    {
                        float u0 = (float)c / cols, u1 = (float)(c + 1) / cols;
                        float v0 = (float)r / rows, v1 = (float)(r + 1) / rows;
                        banners.AddQuad(
                            BannerPoint(x, top, z, side, width, drop, u0, v0),
                            BannerPoint(x, top, z, side, width, drop, u1, v0),
                            BannerPoint(x, top, z, side, width, drop, u1, v1),
                            BannerPoint(x, top, z, side, width, drop, u0, v1),
                            cloth * Range(0.75f, 1.1f), 1.4f);
                    }
                }
            }

            // Coils of rope and dropped tools near the caches.
            for (int i = 0; i < 12; i++)
            {
                float z = Range(6f, 40f);
                float x = Range(-8f, 8f);
                timber.AddRock(new Vector3(x, 0.07f, z), Range(0.12f, 0.26f), NextSeed(),
                    wood * Range(0.7f, 1f), flatten: 0.32f);
            }

            Emit(root, "Timberwork", timber.ToMesh("Timberwork"), _palette.Timber);
            Emit(root, "Banners", banners.ToMesh("Banners", recalculateNormals: true), _palette.Banner,
                castShadows: true);
        }

        /// Banner sheet with a sag along its length and a ragged lower edge.
        static Vector3 BannerPoint(float x, float top, float z, int side, float width, float drop,
            float u, float v)
        {
            float sag = Mathf.Sin(u * Mathf.PI) * 0.09f;
            float tear = v > 0.82f ? ProceduralTextures.Fbm(u * 6f, v * 6f, 2) * 0.28f : 0f;
            return new Vector3(
                x + (u - 0.5f) * width + Mathf.Sin(v * 2.4f) * 0.07f,
                top - v * drop + sag - tear,
                z + side * 0.06f);
        }

        /// Standing water in the pavement's low spots.
        ///
        /// This is the answer to the foreground reading as a featureless dark mass. In a
        /// backlit scene a horizontal surface receives almost no diffuse light, so no
        /// amount of extra lamps recovers detail there. What the concept frame actually
        /// shows in its foreground is specular: the wet floor mirroring the bright mist.
        /// Puddles give that reflection something to happen on.
        void BuildPuddles(Transform root)
        {
            var mb = new MeshBuilder();
            int placed = 0;

            // The hero framing looks down the first few metres of the nave, and that is
            // precisely the region that has no diffuse light. Guarantee water there rather
            // than leaving it to where the noise happens to dip.
            var guaranteed = new[]
            {
                new Vector2(-1.9f, 2.6f), new Vector2(1.7f, 4.4f), new Vector2(-0.6f, 6.8f),
                new Vector2(2.6f, 7.9f), new Vector2(-2.8f, 9.4f),
            };

            for (int attempt = 0; attempt < 260 && placed < 38; attempt++)
            {
                float x, z, radius;

                if (attempt < guaranteed.Length)
                {
                    x = guaranteed[attempt].x;
                    z = guaranteed[attempt].y;
                    radius = Range(0.9f, 1.8f);
                }
                else
                {
                    x = Range(-14f, 14f);
                    z = Range(-6f, 46f);
                    radius = Range(0.55f, 2.3f);

                    float dip = GroundHeight(x, z);
                    // Elsewhere, only settle where the pavement actually dips, otherwise
                    // the water reads as puddles floating on a flat floor.
                    if (dip > -0.035f) continue;
                    if (radius > 1.4f && dip > -0.08f) continue;   // big pools need real basins
                }

                float height = GroundHeight(x, z);

                const int segments = 14;
                var centre = new Vector3(x, height + 0.028f, z);
                var previous = Vector3.zero;

                for (int s = 0; s <= segments; s++)
                {
                    float phi = 2f * Mathf.PI * s / segments;
                    // Irregular outline; a perfect circle of water looks placed, not pooled.
                    float wobble = 0.72f + ProceduralTextures.Fbm(
                        Mathf.Cos(phi) * 1.7f + x, Mathf.Sin(phi) * 1.7f + z, 3) * 0.62f;
                    var point = centre + new Vector3(Mathf.Cos(phi), 0f, Mathf.Sin(phi)) * radius * wobble;

                    if (s > 0) mb.AddTriangle(centre, point, previous, new Color(0.05f, 0.055f, 0.06f), 0.7f);
                    previous = point;
                }
                placed++;
            }

            if (placed == 0) return;
            Emit(root, "Puddles", mb.ToMesh("Puddles"), _palette.Water, castShadows: false);
        }

        /// Must match the height function used by BuildGround exactly, or the water will
        /// float above the pavement or sink into it.
        static float GroundHeight(float x, float z)
        {
            float broad = ProceduralTextures.Fbm(x * 0.045f + 11f, z * 0.045f + 3f, 3) - 0.5f;
            float fine = ProceduralTextures.Fbm(x * 0.33f, z * 0.33f, 2) - 0.5f;
            return broad * 0.32f + fine * 0.05f;
        }

        void BuildRoofBeams(Transform root)
        {
            var mb = new MeshBuilder();
            var stone = new Color(0.42f, 0.42f, 0.43f);

            for (int i = 0; i < 12; i++)
            {
                float z = 8.5f + i * 3.7f + Range(-0.5f, 0.5f);
                float y = Range(7.2f, 8.4f);

                // Roughly half the beams are gone, and the survivors are partial spans.
                if (_rng.NextDouble() < 0.42) continue;

                float span = Range(0.45f, 1f);
                float centerX = Range(-2.2f, 2.2f);
                float length = 11f * span;

                mb.AddChamferedBox(new Vector3(centerX, y, z),
                    new Vector3(length, Range(0.55f, 0.85f), Range(0.6f, 0.95f)),
                    0.07f, stone * Range(0.82f, 1.06f), jitter: 0.07f, seed: NextSeed(), uvScale: 0.6f);

                // Occasional cross joist adds a second, finer shadow frequency.
                if (_rng.NextDouble() < 0.45)
                {
                    mb.AddChamferedBox(
                        new Vector3(centerX + Range(-3f, 3f), y + Range(0.5f, 0.9f), z + Range(-0.8f, 0.8f)),
                        new Vector3(Range(0.4f, 0.6f), 0.42f, Range(2.5f, 4.5f)),
                        0.05f, stone * Range(0.8f, 1f), jitter: 0.05f, seed: NextSeed(), uvScale: 0.7f);
                }
            }

            Emit(root, "RoofBeams", mb.ToMesh("RoofBeams"), _palette.Stone);
        }

        void BuildFarTower(Transform root)
        {
            var mb = new MeshBuilder();
            var stone = new Color(0.38f, 0.38f, 0.40f);
            var basePos = new Vector3(1.2f, 0f, 62f);

            for (int i = 0; i < 20; i++)
            {
                float y = 0.6f + i * 1.15f;
                float shrink = 1f - i * 0.021f;
                // Upper courses erode away on one side, breaking the silhouette.
                float erosion = i > 13 ? (i - 13) * 0.22f : 0f;
                mb.AddChamferedBox(
                    basePos + new Vector3(erosion * 0.5f, y, 0f),
                    new Vector3(5.6f * shrink - erosion, 1.1f, 5.6f * shrink - erosion),
                    0.12f, stone * Range(0.86f, 1.06f), jitter: 0.09f, seed: NextSeed(), uvScale: 0.4f);
            }

            mb.AddArch(basePos + new Vector3(0f, 4.2f, -2.9f), 1.9f, 0.6f, 1.4f, 9, stone * 0.8f);
            Emit(root, "FarTower", mb.ToMesh("FarTower"), _palette.Stone);
        }

        void BuildRubbleField(Transform root)
        {
            var mb = new MeshBuilder();
            var stone = new Color(0.41f, 0.41f, 0.42f);

            for (int i = 0; i < 150; i++)
            {
                float z = Range(2.5f, 52f);
                float spread = Mathf.Lerp(6f, 13f, Mathf.InverseLerp(0f, 52f, z));
                float x = Range(-spread, spread);

                // Debris banks against the colonnade instead of carpeting the floor, which
                // keeps the walking lane clear and the composition readable.
                if (Mathf.Abs(Mathf.Abs(x) - 4.6f) > 2.4f && _rng.NextDouble() < 0.78) continue;

                float r = Range(0.07f, 0.26f);
                mb.AddRock(new Vector3(x, r * 0.5f, z), r, NextSeed(), stone * Range(0.82f, 1.08f));
            }

            // A few large fallen blocks give the eye a scale reference in the midground.
            for (int i = 0; i < 10; i++)
            {
                float z = Range(8f, 40f);
                float x = Range(-8f, 8f);
                if (Mathf.Abs(x) < 3.0f) x += Mathf.Sign(x == 0 ? 1f : x) * 3.2f;

                var block = new Vector3(Range(1.1f, 2.0f), Range(0.55f, 1.0f), Range(1.1f, 2.2f));
                mb.AddChamferedBox(new Vector3(x, block.y * 0.42f, z), block,
                    0.08f, stone * Range(0.82f, 1.05f), jitter: 0.07f, seed: NextSeed(), uvScale: 0.55f);
            }

            Emit(root, "Rubble", mb.ToMesh("Rubble"), _palette.Stone);
        }

        /// Gold is the only saturated colour in the palette, so it doubles as the loot
        /// read: anything glowing warm is worth walking toward.
        GameObject BuildLootCache(Transform root, Vector3 pos)
        {
            var mb = new MeshBuilder();
            var gold = new Color(1.0f, 0.74f, 0.26f);

            mb.AddChamferedBox(pos + new Vector3(0f, 0.42f, 0f), new Vector3(1.35f, 0.84f, 0.95f),
                0.07f, gold * 0.85f, jitter: 0.02f, seed: NextSeed(), uvScale: 0.9f);
            mb.AddChamferedBox(pos + new Vector3(0f, 0.92f, 0f), new Vector3(1.42f, 0.22f, 1.02f),
                0.06f, gold, jitter: 0.015f, seed: NextSeed(), uvScale: 0.9f);

            for (int i = 0; i < 16; i++)
            {
                mb.AddRock(pos + new Vector3(Range(-1.3f, 1.3f), Range(0.04f, 0.16f), Range(-1.0f, 1.0f)),
                    Range(0.055f, 0.14f), NextSeed(), gold * Range(0.8f, 1.2f), flatten: 0.5f);
            }

            var go = Emit(root, "LootCache", mb.ToMesh("LootCache"), _palette.Gold);

            var glow = new GameObject("LootGlow");
            glow.transform.SetParent(go.transform, false);
            glow.transform.localPosition = pos + Vector3.up * 0.85f;
            var light = glow.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.70f, 0.30f);
            light.intensity = 4.5f;
            light.range = 9f;
            light.shadows = LightShadows.None;

            var anchor = new GameObject("CacheAnchor");
            anchor.transform.SetParent(go.transform, false);
            anchor.transform.position = pos;

            var trigger = anchor.AddComponent<SphereCollider>();
            trigger.isTrigger = true;
            trigger.radius = 1.1f;
            trigger.center = new Vector3(0f, 0.6f, 0f);
            return anchor;
        }

        /// Hooded figure built from a tapered cloak and a cowl. The concept frame reads the
        /// player as a silhouette, so the shape language matters far more than the topology.
        GameObject BuildHunter(Transform root, Vector3 pos)
        {
            var mb = new MeshBuilder();
            var cloth = new Color(0.16f, 0.16f, 0.18f);

            const int segments = 14;
            const int rings = 9;
            var previous = new Vector3[segments];
            var current = new Vector3[segments];

            for (int r = 0; r <= rings; r++)
            {
                float t = (float)r / rings;
                float y = t * 1.62f;
                // Wide at the hem, pinched at the shoulders: the classic cloak silhouette.
                float radius = Mathf.Lerp(0.46f, 0.20f, Mathf.SmoothStep(0f, 1f, Mathf.Pow(t, 0.72f)));
                radius *= 1f + Mathf.Sin(t * 9f) * 0.045f;

                for (int s = 0; s < segments; s++)
                {
                    float phi = 2f * Mathf.PI * s / segments;
                    // Fabric folds: a low-frequency ripple around the circumference.
                    float fold = 1f + Mathf.Sin(phi * 5f + t * 2.2f) * 0.09f * (1f - t * 0.55f);
                    current[s] = pos + new Vector3(
                        Mathf.Cos(phi) * radius * fold,
                        y,
                        Mathf.Sin(phi) * radius * fold * 0.82f);
                }

                if (r > 0)
                {
                    for (int s = 0; s < segments; s++)
                    {
                        int s2 = (s + 1) % segments;
                        mb.AddQuad(previous[s], previous[s2], current[s2], current[s], cloth, 1.4f);
                    }
                }
                System.Array.Copy(current, previous, segments);
            }

            // Cowl: a squashed dome pushed forward over where the face would be.
            var head = pos + new Vector3(0f, 1.63f, 0.04f);
            for (int r = 0; r < 6; r++)
            {
                float t0 = (float)r / 6, t1 = (float)(r + 1) / 6;
                for (int s = 0; s < segments; s++)
                {
                    float p0 = 2f * Mathf.PI * s / segments;
                    float p1 = 2f * Mathf.PI * (s + 1) / segments;
                    mb.AddQuad(Cowl(head, t0, p0), Cowl(head, t0, p1), Cowl(head, t1, p1), Cowl(head, t1, p0),
                        cloth * 0.9f, 1.6f);
                }
            }

            // Extended arm carrying the lantern, angled forward and to the right.
            var shoulder = pos + new Vector3(0.19f, 1.32f, 0.06f);
            var hand = pos + new Vector3(0.46f, 1.16f, 0.62f);
            AddLimb(mb, shoulder, hand, 0.085f, cloth * 0.95f);

            // Silhouette furniture. At this camera distance the hunter is the largest
            // shape in frame, and a plain cloak cone reads as a traffic cone.
            var scabbardTop = pos + new Vector3(-0.34f, 1.16f, -0.14f);
            var scabbardTip = pos + new Vector3(0.30f, 0.30f, -0.46f);
            AddLimb(mb, scabbardTop, scabbardTip, 0.055f, cloth * 0.8f);
            AddLimb(mb, scabbardTop, scabbardTop + new Vector3(-0.13f, 0.19f, -0.05f), 0.032f, cloth * 1.25f);

            // Shoulder mantle: a short flared cape over the upper cloak.
            const int mantleSegments = 14;
            for (int r = 0; r < 4; r++)
            {
                float t0 = r / 4f, t1 = (r + 1) / 4f;
                for (int s = 0; s < mantleSegments; s++)
                {
                    float a0 = 2f * Mathf.PI * s / mantleSegments;
                    float a1 = 2f * Mathf.PI * (s + 1) / mantleSegments;
                    mb.AddQuad(Mantle(pos, t0, a0), Mantle(pos, t0, a1), Mantle(pos, t1, a1), Mantle(pos, t1, a0),
                        cloth * 0.86f, 1.5f);
                }
            }

            // Satchel on the left hip: the loot the hunter is risking.
            mb.AddChamferedBox(pos + new Vector3(-0.31f, 0.86f, -0.10f),
                new Vector3(0.30f, 0.34f, 0.22f), 0.05f, cloth * 1.1f,
                jitter: 0.02f, seed: NextSeed(), uvScale: 2.4f);

            var hunterGo = Emit(root, "Hunter", mb.ToMesh("Hunter", recalculateNormals: true), _palette.Cloth);

            var lanternRoot = new GameObject("Lantern");
            lanternRoot.transform.SetParent(root, false);
            lanternRoot.transform.position = hand + new Vector3(0.02f, -0.20f, 0.05f);

            var lb = new MeshBuilder();
            var brass = new Color(0.92f, 0.68f, 0.32f);
            lb.AddChamferedBox(Vector3.zero, new Vector3(0.17f, 0.24f, 0.17f), 0.035f, brass,
                jitter: 0f, seed: NextSeed(), uvScale: 2f);
            lb.AddChamferedBox(new Vector3(0f, 0.15f, 0f), new Vector3(0.12f, 0.07f, 0.12f), 0.02f,
                brass * 0.8f, seed: NextSeed(), uvScale: 2f);
            Emit(lanternRoot.transform, "LanternBody", lb.ToMesh("Lantern"), _palette.Gold);

            var lightGo = new GameObject("LanternLight");
            lightGo.transform.SetParent(lanternRoot.transform, false);
            var lantern = lightGo.AddComponent<Light>();
            lantern.type = LightType.Point;
            lantern.color = new Color(1f, 0.74f, 0.40f);
            lantern.intensity = 32f;
            lantern.range = 34f;
            // Point-light shadows for the lantern produced heavy self-shadowing acne on
            // the pavement at the shadowmap resolution this scene can afford.
            lantern.shadows = LightShadows.None;

            var anchor = new GameObject("HunterAnchor");
            anchor.transform.SetParent(root, false);
            anchor.transform.position = pos;
            hunterGo.transform.SetParent(anchor.transform, true);
            lanternRoot.transform.SetParent(anchor.transform, true);
            return anchor;
        }

        static Vector3 Mantle(Vector3 origin, float t, float phi)
        {
            float y = 1.44f - t * 0.42f;
            float radius = Mathf.Lerp(0.235f, 0.40f, Mathf.Pow(t, 0.8f));
            float fold = 1f + Mathf.Sin(phi * 6f) * 0.07f;
            return origin + new Vector3(
                Mathf.Cos(phi) * radius * fold,
                y - Mathf.Abs(Mathf.Sin(phi * 3f)) * t * 0.09f,
                Mathf.Sin(phi) * radius * fold * 0.85f);
        }

        static Vector3 Cowl(Vector3 center, float t, float phi)
        {
            float theta = t * Mathf.PI * 0.62f;
            return center + new Vector3(
                Mathf.Sin(theta) * Mathf.Cos(phi) * 0.245f,
                Mathf.Cos(theta) * 0.235f,
                Mathf.Sin(theta) * Mathf.Sin(phi) * 0.245f + Mathf.Sin(theta) * 0.055f);
        }

        static void AddLimb(MeshBuilder mb, Vector3 from, Vector3 to, float radius, Color color)
        {
            var axis = (to - from).normalized;
            var up = Mathf.Abs(Vector3.Dot(axis, Vector3.up)) > 0.9f ? Vector3.forward : Vector3.up;
            var right = Vector3.Cross(axis, up).normalized;
            up = Vector3.Cross(right, axis).normalized;

            const int sides = 8;
            for (int s = 0; s < sides; s++)
            {
                float a0 = 2f * Mathf.PI * s / sides;
                float a1 = 2f * Mathf.PI * (s + 1) / sides;
                Vector3 o0 = (right * Mathf.Cos(a0) + up * Mathf.Sin(a0)) * radius;
                Vector3 o1 = (right * Mathf.Cos(a1) + up * Mathf.Sin(a1)) * radius;
                mb.AddQuad(from + o0, from + o1, to + o1 * 0.75f, to + o0 * 0.75f, color, 2f);
            }
        }

        /// Rival gold-hunters staged at increasing depth. Their only job in this frame is
        /// to prove the fog resolves figures as silhouettes before it resolves detail.
        void BuildRivalSilhouettes(Transform root, SiteHandles handles)
        {
            var positions = new[]
            {
                new Vector3(-2.9f, 0f, 16.5f),
                new Vector3(-3.9f, 0f, 21.0f),
                new Vector3(3.3f, 0f, 29.0f),
            };

            var dark = new Color(0.07f, 0.07f, 0.08f);

            for (int index = 0; index < positions.Length; index++)
            {
                var p = positions[index];
                var mb = new MeshBuilder();
                const int segments = 10;
                const int rings = 7;
                var previous = new Vector3[segments];
                var current = new Vector3[segments];

                for (int r = 0; r <= rings; r++)
                {
                    float t = (float)r / rings;
                    float radius = Mathf.Lerp(0.42f, 0.19f, Mathf.Pow(t, 0.75f));
                    for (int s = 0; s < segments; s++)
                    {
                        float phi = 2f * Mathf.PI * s / segments;
                        current[s] = p + new Vector3(Mathf.Cos(phi) * radius, t * 1.66f, Mathf.Sin(phi) * radius * 0.8f);
                    }
                    if (r > 0)
                    {
                        for (int s = 0; s < segments; s++)
                        {
                            int s2 = (s + 1) % segments;
                            mb.AddQuad(previous[s], previous[s2], current[s2], current[s], dark, 1.2f);
                        }
                    }
                    System.Array.Copy(current, previous, segments);
                }

                var head = p + new Vector3(0f, 1.66f, 0.03f);
                for (int r = 0; r < 4; r++)
                {
                    float t0 = (float)r / 4, t1 = (float)(r + 1) / 4;
                    for (int s = 0; s < segments; s++)
                    {
                        float p0 = 2f * Mathf.PI * s / segments;
                        float p1 = 2f * Mathf.PI * (s + 1) / segments;
                        mb.AddQuad(Cowl(head, t0, p0), Cowl(head, t0, p1), Cowl(head, t1, p1), Cowl(head, t1, p0),
                            dark, 1.4f);
                    }
                }

                var rival = Emit(root, $"RivalHunter_{index}",
                    mb.ToMesh($"RivalHunter_{index}", recalculateNormals: true), _palette.Silhouette);

                var anchor = new GameObject($"RivalAnchor_{index}");
                anchor.transform.SetParent(root, false);
                anchor.transform.position = p;
                rival.transform.SetParent(anchor.transform, true);
                handles.Rivals.Add(anchor.transform);
            }
        }
    }
}
