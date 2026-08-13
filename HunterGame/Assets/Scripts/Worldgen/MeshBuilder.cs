using System.Collections.Generic;
using UnityEngine;

namespace Hunter.Worldgen
{
    /// Accumulates geometry for procedurally generated props. Everything is welded into
    /// a single mesh per prop so the ruins stay cheap to draw despite the detail count.
    public class MeshBuilder
    {
        readonly List<Vector3> _vertices = new();
        readonly List<Vector3> _normals = new();
        readonly List<Vector2> _uv = new();
        readonly List<Color> _colors = new();
        readonly List<int> _triangles = new();

        public int VertexCount => _vertices.Count;

        public void AddTriangle(Vector3 a, Vector3 b, Vector3 c, Color color, float uvScale = 1f)
        {
            var normal = Vector3.Cross(b - a, c - a).normalized;
            int baseIndex = _vertices.Count;

            AddVertex(a, normal, PlanarUv(a, normal, uvScale), color);
            AddVertex(b, normal, PlanarUv(b, normal, uvScale), color);
            AddVertex(c, normal, PlanarUv(c, normal, uvScale), color);

            _triangles.Add(baseIndex);
            _triangles.Add(baseIndex + 1);
            _triangles.Add(baseIndex + 2);
        }

        public void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color color, float uvScale = 1f)
        {
            AddTriangle(a, b, c, color, uvScale);
            AddTriangle(a, c, d, color, uvScale);
        }

        void AddVertex(Vector3 position, Vector3 normal, Vector2 uv, Color color)
        {
            _vertices.Add(position);
            _normals.Add(normal);
            _uv.Add(uv);
            _colors.Add(color);
        }

        /// Triplanar-style UV picked from the dominant normal axis. Keeps texel density
        /// uniform across procedurally sized blocks without authoring UVs per shape.
        static Vector2 PlanarUv(Vector3 p, Vector3 n, float scale)
        {
            var a = new Vector3(Mathf.Abs(n.x), Mathf.Abs(n.y), Mathf.Abs(n.z));
            if (a.x >= a.y && a.x >= a.z) return new Vector2(p.z, p.y) * scale;
            if (a.y >= a.z) return new Vector2(p.x, p.z) * scale;
            return new Vector2(p.x, p.y) * scale;
        }

        /// Convex hull-free box with chamfered edges. Chamfers are the single cheapest way
        /// to stop procedural stone from reading as plastic: they give the lighting a
        /// gradient to run along instead of a hard 90 degree switch.
        public void AddChamferedBox(Vector3 center, Vector3 size, float chamfer, Color color,
            float jitter = 0f, int seed = 0, float uvScale = 1f)
        {
            var rng = new System.Random(seed);
            Vector3 h = size * 0.5f;
            chamfer = Mathf.Min(chamfer, Mathf.Min(h.x, Mathf.Min(h.y, h.z)) * 0.9f);

            // Eight corner clusters, each pulled inward by the chamfer on every axis.
            var corners = new Vector3[8];
            for (int i = 0; i < 8; i++)
            {
                float sx = (i & 1) == 0 ? -1f : 1f;
                float sy = (i & 2) == 0 ? -1f : 1f;
                float sz = (i & 4) == 0 ? -1f : 1f;
                var offset = new Vector3(sx * (h.x - chamfer), sy * (h.y - chamfer), sz * (h.z - chamfer));
                if (jitter > 0f)
                {
                    offset += new Vector3(
                        (float)(rng.NextDouble() - 0.5) * jitter,
                        (float)(rng.NextDouble() - 0.5) * jitter,
                        (float)(rng.NextDouble() - 0.5) * jitter);
                }
                corners[i] = center + offset;
            }

            Vector3 ex = Vector3.right * chamfer;
            Vector3 ey = Vector3.up * chamfer;
            Vector3 ez = Vector3.forward * chamfer;

            // Six faces, each inset by the chamfer along its own axis.
            AddQuad(corners[0] - ez, corners[1] - ez, corners[3] - ez, corners[2] - ez, color, uvScale);
            AddQuad(corners[6] + ez, corners[7] + ez, corners[5] + ez, corners[4] + ez, color, uvScale);
            AddQuad(corners[4] - ex, corners[6] - ex, corners[2] - ex, corners[0] - ex, color, uvScale);
            AddQuad(corners[1] + ex, corners[3] + ex, corners[7] + ex, corners[5] + ex, color, uvScale);
            AddQuad(corners[2] + ey, corners[3] + ey, corners[7] + ey, corners[6] + ey, color, uvScale);
            AddQuad(corners[0] - ey, corners[4] - ey, corners[5] - ey, corners[1] - ey, color, uvScale);

            // Twelve edge bevels stitching the inset faces together.
            AddQuad(corners[0] - ez, corners[0] - ey, corners[1] - ey, corners[1] - ez, color, uvScale);
            AddQuad(corners[2] - ez, corners[3] - ez, corners[3] + ey, corners[2] + ey, color, uvScale);
            AddQuad(corners[4] - ey, corners[4] + ez, corners[5] + ez, corners[5] - ey, color, uvScale);
            AddQuad(corners[6] + ey, corners[7] + ey, corners[7] + ez, corners[6] + ez, color, uvScale);
            AddQuad(corners[0] - ez, corners[2] - ez, corners[2] - ex, corners[0] - ex, color, uvScale);
            AddQuad(corners[1] - ez, corners[1] + ex, corners[3] + ex, corners[3] - ez, color, uvScale);
            AddQuad(corners[4] - ex, corners[6] - ex, corners[6] + ez, corners[4] + ez, color, uvScale);
            AddQuad(corners[5] + ex, corners[5] + ez, corners[7] + ez, corners[7] + ex, color, uvScale);
            AddQuad(corners[0] - ex, corners[0] - ey, corners[4] - ey, corners[4] - ex, color, uvScale);
            AddQuad(corners[1] - ey, corners[1] + ex, corners[5] + ex, corners[5] - ey, color, uvScale);
            AddQuad(corners[2] - ex, corners[2] + ey, corners[6] + ey, corners[6] - ex, color, uvScale);
            AddQuad(corners[3] + ey, corners[3] + ex, corners[7] + ex, corners[7] + ey, color, uvScale);

            // Corner patches.
            AddTriangle(corners[0] - ex, corners[0] - ey, corners[0] - ez, color, uvScale);
            AddTriangle(corners[1] - ez, corners[1] - ey, corners[1] + ex, color, uvScale);
            AddTriangle(corners[2] - ez, corners[2] + ey, corners[2] - ex, color, uvScale);
            AddTriangle(corners[3] - ez, corners[3] + ex, corners[3] + ey, color, uvScale);
            AddTriangle(corners[4] - ex, corners[4] + ez, corners[4] - ey, color, uvScale);
            AddTriangle(corners[5] - ey, corners[5] + ez, corners[5] + ex, color, uvScale);
            AddTriangle(corners[6] - ex, corners[6] + ey, corners[6] + ez, color, uvScale);
            AddTriangle(corners[7] + ey, corners[7] + ex, corners[7] + ez, color, uvScale);
        }

        /// Irregular convex chunk used for rubble. Radial jitter on a subdivided sphere
        /// gives shard-like silhouettes that read as broken masonry rather than pebbles.
        public void AddRock(Vector3 center, float radius, int seed, Color color, float flatten = 0.6f)
        {
            var rng = new System.Random(seed);
            const int rings = 5;
            const int segments = 7;

            var grid = new Vector3[rings + 1, segments];
            for (int r = 0; r <= rings; r++)
            {
                float theta = Mathf.PI * r / rings;
                for (int s = 0; s < segments; s++)
                {
                    float phi = 2f * Mathf.PI * s / segments;
                    float jitter = 0.55f + (float)rng.NextDouble() * 0.75f;
                    var dir = new Vector3(
                        Mathf.Sin(theta) * Mathf.Cos(phi),
                        Mathf.Cos(theta) * flatten,
                        Mathf.Sin(theta) * Mathf.Sin(phi));
                    grid[r, s] = center + dir * radius * jitter;
                }
            }

            for (int r = 0; r < rings; r++)
            {
                for (int s = 0; s < segments; s++)
                {
                    int s2 = (s + 1) % segments;
                    AddQuad(grid[r, s], grid[r, s2], grid[r + 1, s2], grid[r + 1, s], color, 0.6f);
                }
            }
        }

        /// Fluted column shaft. The flutes matter more than polygon count: vertical
        /// grooves catch the lantern and turn a flat cylinder into readable architecture.
        public void AddFlutedColumn(Vector3 basePos, float height, float radius, int flutes,
            Color color, float taper = 0.88f, int rings = 6)
        {
            int segments = flutes * 3;
            var previous = new Vector3[segments];
            var current = new Vector3[segments];

            for (int r = 0; r <= rings; r++)
            {
                float t = (float)r / rings;
                float y = basePos.y + height * t;
                float rr = radius * Mathf.Lerp(1f, taper, t);

                for (int s = 0; s < segments; s++)
                {
                    float phi = 2f * Mathf.PI * s / segments;
                    // Every third vertex dips inward, carving the flute channel.
                    float depth = (s % 3 == 1) ? 0.82f : 1f;
                    current[s] = new Vector3(
                        basePos.x + Mathf.Cos(phi) * rr * depth,
                        y,
                        basePos.z + Mathf.Sin(phi) * rr * depth);
                }

                if (r > 0)
                {
                    for (int s = 0; s < segments; s++)
                    {
                        int s2 = (s + 1) % segments;
                        AddQuad(previous[s], previous[s2], current[s2], current[s], color, 0.85f);
                    }
                }

                System.Array.Copy(current, previous, segments);
            }
        }

        /// Semicircular arch assembled from individual wedge stones, optionally collapsed
        /// past a given angle so the ruin silhouette is broken rather than intact.
        public void AddArch(Vector3 center, float radius, float thickness, float depth,
            int stones, Color color, float collapseFrom = 1f)
        {
            int kept = Mathf.Max(1, Mathf.RoundToInt(stones * Mathf.Clamp01(collapseFrom)));
            for (int i = 0; i < stones; i++)
            {
                bool leftSide = i < stones / 2;
                int fromLeft = leftSide ? i : stones - 1 - i;
                if (fromLeft >= kept) continue;

                float a0 = Mathf.PI * i / stones;
                float a1 = Mathf.PI * (i + 1) / stones;
                float gap = 0.012f;
                a0 += gap;
                a1 -= gap;

                Vector3 In0 = center + new Vector3(-Mathf.Cos(a0), Mathf.Sin(a0), 0f) * radius;
                Vector3 In1 = center + new Vector3(-Mathf.Cos(a1), Mathf.Sin(a1), 0f) * radius;
                Vector3 Out0 = center + new Vector3(-Mathf.Cos(a0), Mathf.Sin(a0), 0f) * (radius + thickness);
                Vector3 Out1 = center + new Vector3(-Mathf.Cos(a1), Mathf.Sin(a1), 0f) * (radius + thickness);

                Vector3 dz = Vector3.forward * depth * 0.5f;

                AddQuad(In0 - dz, In1 - dz, Out1 - dz, Out0 - dz, color, 0.9f);
                AddQuad(Out0 + dz, Out1 + dz, In1 + dz, In0 + dz, color, 0.9f);
                AddQuad(In0 - dz, In0 + dz, In1 + dz, In1 - dz, color, 0.9f);
                AddQuad(Out1 - dz, Out1 + dz, Out0 + dz, Out0 - dz, color, 0.9f);
                AddQuad(In0 - dz, Out0 - dz, Out0 + dz, In0 + dz, color, 0.9f);
                AddQuad(Out1 - dz, In1 - dz, In1 + dz, Out1 + dz, color, 0.9f);
            }
        }

        /// Welded, smooth-shaded height grid. Used for the ruin floor: slicing the floor
        /// into individual slab boxes made every joint cast its own silhouette and the
        /// result read as a tray of tiles rather than as ground.
        public void AddSmoothGrid(Vector3 origin, float width, float depth, int cols, int rows,
            System.Func<float, float, float> height, Color color, float uvScale = 1f)
        {
            int baseIndex = _vertices.Count;
            float dx = width / cols;
            float dz = depth / rows;

            for (int r = 0; r <= rows; r++)
            {
                for (int c = 0; c <= cols; c++)
                {
                    float x = origin.x + c * dx;
                    float z = origin.z + r * dz;
                    float y = origin.y + height(x, z);

                    // Central differences on the height field give continuous normals
                    // across the whole sheet.
                    float hL = height(x - dx, z), hR = height(x + dx, z);
                    float hD = height(x, z - dz), hU = height(x, z + dz);
                    var normal = new Vector3(hL - hR, 2f * dx, hD - hU).normalized;

                    _vertices.Add(new Vector3(x, y, z));
                    _normals.Add(normal);
                    _uv.Add(new Vector2(x, z) * uvScale);
                    _colors.Add(color);
                }
            }

            int stride = cols + 1;
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    int i0 = baseIndex + r * stride + c;
                    int i1 = i0 + 1;
                    int i2 = i0 + stride;
                    int i3 = i2 + 1;

                    _triangles.Add(i0); _triangles.Add(i2); _triangles.Add(i1);
                    _triangles.Add(i1); _triangles.Add(i2); _triangles.Add(i3);
                }
            }
        }

        public Mesh ToMesh(string name, bool recalculateNormals = false)
        {
            var mesh = new Mesh { name = name };
            if (_vertices.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

            mesh.SetVertices(_vertices);
            mesh.SetTriangles(_triangles, 0);
            mesh.SetUVs(0, _uv);
            mesh.SetColors(_colors);

            if (recalculateNormals) mesh.RecalculateNormals();
            else mesh.SetNormals(_normals);

            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
