using System.Collections.Generic;
using UnityEngine;

namespace Worker.Game
{
    /// <summary>
    /// Generates the geometry the isometric view is built from.
    ///
    /// The important one is <see cref="BeveledBox"/>. Unity's primitive cube has hard
    /// 90 degree edges, and a hard edge under a single directional light produces an
    /// abrupt jump from lit to unlit with nothing in between. Real low-poly art always
    /// chamfers: the narrow bevel face catches the key light at a grazing angle and
    /// draws a bright line along every edge, which is most of what separates a crafted
    /// looking object from an untextured box. It costs about ninety vertices per object
    /// and changes the image more than any amount of extra detail geometry.
    /// </summary>
    public static class ProceduralMesh
    {
        private static readonly Dictionary<int, Mesh> Cache = new Dictionary<int, Mesh>();

        public static void ClearCache()
        {
            foreach (var pair in Cache)
            {
                if (pair.Value != null) Object.Destroy(pair.Value);
            }
            Cache.Clear();
        }

        /// <summary>
        /// A unit cube with chamfered edges and corners. Bevel is expressed as a
        /// fraction of the smallest half-extent so the same value looks consistent on
        /// objects of different sizes.
        /// </summary>
        public static Mesh BeveledBox(float bevel = 0.06f)
        {
            int key = Mathf.RoundToInt(bevel * 10000f);
            if (Cache.TryGetValue(key, out var cached) && cached != null) return cached;

            var mesh = BuildBeveledBox(bevel);
            mesh.hideFlags = HideFlags.HideAndDontSave;
            Cache[key] = mesh;
            return mesh;
        }

        private static Mesh BuildBeveledBox(float bevel)
        {
            const float h = 0.5f;
            float inner = h - bevel;

            var vertices = new List<Vector3>(128);
            var normals = new List<Vector3>(128);
            var uvs = new List<Vector2>(128);
            var triangles = new List<int>(192);

            // Six main faces, each inset by the bevel on both of its in-plane axes.
            for (int axis = 0; axis < 3; axis++)
            {
                for (int sign = -1; sign <= 1; sign += 2)
                {
                    var normal = AxisVector(axis) * sign;
                    int u = (axis + 1) % 3;
                    int v = (axis + 2) % 3;

                    var corners = new Vector3[4];
                    for (int i = 0; i < 4; i++)
                    {
                        float su = (i == 0 || i == 3) ? -1f : 1f;
                        float sv = (i < 2) ? -1f : 1f;

                        var point = Vector3.zero;
                        point[axis] = h * sign;
                        point[u] = inner * su;
                        point[v] = inner * sv;
                        corners[i] = point;
                    }

                    // Winding has to flip with the sign so every face points outward.
                    if (sign < 0) System.Array.Reverse(corners);
                    AddQuad(vertices, normals, uvs, triangles, corners, normal, axis);
                }
            }

            // Twelve edge chamfers. Each is a quad spanning two adjacent main faces.
            for (int axis = 0; axis < 3; axis++)
            {
                int u = (axis + 1) % 3;
                int v = (axis + 2) % 3;

                for (int su = -1; su <= 1; su += 2)
                {
                    for (int sv = -1; sv <= 1; sv += 2)
                    {
                        var a0 = Vector3.zero;
                        a0[axis] = -inner; a0[u] = h * su; a0[v] = inner * sv;
                        var a1 = Vector3.zero;
                        a1[axis] = inner; a1[u] = h * su; a1[v] = inner * sv;
                        var b1 = Vector3.zero;
                        b1[axis] = inner; b1[u] = inner * su; b1[v] = h * sv;
                        var b0 = Vector3.zero;
                        b0[axis] = -inner; b0[u] = inner * su; b0[v] = h * sv;

                        var normal = (AxisVector(u) * su + AxisVector(v) * sv).normalized;
                        var quad = new[] { a0, a1, b1, b0 };

                        if (su * sv < 0) System.Array.Reverse(quad);
                        AddQuad(vertices, normals, uvs, triangles, quad, normal, axis);
                    }
                }
            }

            // Eight corner triangles closing the gaps where three chamfers meet.
            for (int sx = -1; sx <= 1; sx += 2)
            {
                for (int sy = -1; sy <= 1; sy += 2)
                {
                    for (int sz = -1; sz <= 1; sz += 2)
                    {
                        var px = new Vector3(h * sx, inner * sy, inner * sz);
                        var py = new Vector3(inner * sx, h * sy, inner * sz);
                        var pz = new Vector3(inner * sx, inner * sy, h * sz);

                        var normal = new Vector3(sx, sy, sz).normalized;
                        var tri = new[] { px, py, pz };

                        if (sx * sy * sz < 0) System.Array.Reverse(tri);
                        AddTriangle(vertices, normals, uvs, triangles, tri, normal);
                    }
                }
            }

            var mesh = new Mesh { name = "BeveledBox" };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }

        private static void AddQuad(List<Vector3> vertices, List<Vector3> normals, List<Vector2> uvs,
            List<int> triangles, Vector3[] corners, Vector3 normal, int axis)
        {
            int start = vertices.Count;

            for (int i = 0; i < 4; i++)
            {
                vertices.Add(corners[i]);
                normals.Add(normal);
                uvs.Add(PlanarUv(corners[i], axis));
            }

            triangles.Add(start);
            triangles.Add(start + 1);
            triangles.Add(start + 2);
            triangles.Add(start);
            triangles.Add(start + 2);
            triangles.Add(start + 3);
        }

        private static void AddTriangle(List<Vector3> vertices, List<Vector3> normals, List<Vector2> uvs,
            List<int> triangles, Vector3[] corners, Vector3 normal)
        {
            int start = vertices.Count;

            for (int i = 0; i < 3; i++)
            {
                vertices.Add(corners[i]);
                normals.Add(normal);
                uvs.Add(PlanarUv(corners[i], 1));
            }

            triangles.Add(start);
            triangles.Add(start + 1);
            triangles.Add(start + 2);
        }

        /// <summary>
        /// Planar projection along the face's dominant axis. Good enough for tiling
        /// surface textures and far cheaper than a real unwrap; every face is a plane.
        /// </summary>
        private static Vector2 PlanarUv(Vector3 point, int axis)
        {
            switch (axis)
            {
                case 0: return new Vector2(point.z + 0.5f, point.y + 0.5f);
                case 1: return new Vector2(point.x + 0.5f, point.z + 0.5f);
                default: return new Vector2(point.x + 0.5f, point.y + 0.5f);
            }
        }

        private static Vector3 AxisVector(int axis)
        {
            switch (axis)
            {
                case 0: return Vector3.right;
                case 1: return Vector3.up;
                default: return Vector3.forward;
            }
        }

        /// <summary>
        /// A cylinder with chamfered rims, for drums, rollers and blades. The primitive
        /// cylinder has the same hard-edge problem as the cube.
        /// </summary>
        public static Mesh BeveledCylinder(int sides = 16, float bevel = 0.06f)
        {
            int key = 1_000_000 + sides * 1000 + Mathf.RoundToInt(bevel * 1000f);
            if (Cache.TryGetValue(key, out var cached) && cached != null) return cached;

            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();

            const float h = 0.5f;
            float innerH = h - bevel;
            float outerR = 0.5f;
            float innerR = 0.5f - bevel;

            for (int i = 0; i < sides; i++)
            {
                float a0 = i * Mathf.PI * 2f / sides;
                float a1 = (i + 1) * Mathf.PI * 2f / sides;

                var d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0));
                var d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));

                // Side wall.
                AddQuad(vertices, normals, uvs, triangles, new[]
                {
                    d0 * outerR + Vector3.down * innerH,
                    d1 * outerR + Vector3.down * innerH,
                    d1 * outerR + Vector3.up * innerH,
                    d0 * outerR + Vector3.up * innerH
                }, (d0 + d1).normalized, 2);

                // Top and bottom rim chamfers.
                AddQuad(vertices, normals, uvs, triangles, new[]
                {
                    d0 * outerR + Vector3.up * innerH,
                    d1 * outerR + Vector3.up * innerH,
                    d1 * innerR + Vector3.up * h,
                    d0 * innerR + Vector3.up * h
                }, (d0 + d1 + Vector3.up * 2f).normalized, 2);

                AddQuad(vertices, normals, uvs, triangles, new[]
                {
                    d0 * innerR + Vector3.down * h,
                    d1 * innerR + Vector3.down * h,
                    d1 * outerR + Vector3.down * innerH,
                    d0 * outerR + Vector3.down * innerH
                }, (d0 + d1 + Vector3.down * 2f).normalized, 2);

                // Caps.
                AddTriangle(vertices, normals, uvs, triangles, new[]
                {
                    Vector3.up * h,
                    d0 * innerR + Vector3.up * h,
                    d1 * innerR + Vector3.up * h
                }, Vector3.up);

                AddTriangle(vertices, normals, uvs, triangles, new[]
                {
                    Vector3.down * h,
                    d1 * innerR + Vector3.down * h,
                    d0 * innerR + Vector3.down * h
                }, Vector3.down);
            }

            var mesh = new Mesh { name = "BeveledCylinder" };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            mesh.hideFlags = HideFlags.HideAndDontSave;

            Cache[key] = mesh;
            return mesh;
        }
    }
}
