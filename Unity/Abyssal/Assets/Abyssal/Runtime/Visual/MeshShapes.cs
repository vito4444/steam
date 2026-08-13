using System.Collections.Generic;
using UnityEngine;

namespace Abyssal.Visual
{
    /// <summary>
    /// 内置图元不够用时补的几个程序化网格。
    ///
    /// 最重要的是 <see cref="Disc"/>：仪表盘必须是真正的圆形网格。
    /// 用 Quad 贴一张带透明外圈的圆形贴图看似等价，实际上会在暗环境里
    /// 露出一圈方形黑边——透明区域拿不到任何光照，在几乎全黑的舱内
    /// 反而比周围的面板更黑，每个仪表都会被一个黑方块框住。
    /// </summary>
    public static class MeshShapes
    {
        static readonly Dictionary<int, Mesh> DiscCache = new Dictionary<int, Mesh>();
        static readonly Dictionary<int, Mesh> RingCache = new Dictionary<int, Mesh>();

        /// <summary>
        /// 半径 0.5 的圆盘，位于 XY 平面，法线朝 -Z（与内置 Quad 一致）。
        /// UV 把圆内接到 [0,1]² 的贴图上。
        /// </summary>
        public static Mesh Disc(int segments = 64)
        {
            if (DiscCache.TryGetValue(segments, out var cached) && cached != null) return cached;

            var mesh = new Mesh { name = $"Disc{segments}" };
            var verts = new Vector3[segments + 1];
            var uvs = new Vector2[segments + 1];
            var normals = new Vector3[segments + 1];
            var tris = new int[segments * 3];

            verts[0] = Vector3.zero;
            uvs[0] = new Vector2(0.5f, 0.5f);
            normals[0] = Vector3.back;

            for (int i = 0; i < segments; i++)
            {
                float a = i / (float)segments * Mathf.PI * 2f;
                float c = Mathf.Cos(a), s = Mathf.Sin(a);
                verts[i + 1] = new Vector3(c * 0.5f, s * 0.5f, 0f);
                uvs[i + 1] = new Vector2(0.5f + c * 0.5f, 0.5f + s * 0.5f);
                normals[i + 1] = Vector3.back;

                // 顶点是按角度递增排的，从 -Z 侧看过去这个顺序是逆时针，
                // 而 Unity 判定正面用的是屏幕空间顺时针，所以后两个索引必须交换。
                // 写反了不会报任何错，表盘会被背面剔除掉，画面上只剩下指针在空中漂。
                tris[i * 3 + 0] = 0;
                tris[i * 3 + 1] = (i + 1) % segments + 1;
                tris[i * 3 + 2] = i + 1;
            }

            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.normals = normals;
            mesh.triangles = tris;
            mesh.RecalculateBounds();

            DiscCache[segments] = mesh;
            return mesh;
        }

        /// <summary>
        /// 外径 0.5、内径由 <paramref name="innerRatio"/> 决定的圆环，同样在 XY 平面朝 -Z。
        /// 用来做仪表外圈的金属压边。
        /// </summary>
        public static Mesh Ring(int segments = 64, float innerRatio = 0.86f)
        {
            int key = segments * 1000 + Mathf.RoundToInt(innerRatio * 100f);
            if (RingCache.TryGetValue(key, out var cached) && cached != null) return cached;

            var mesh = new Mesh { name = $"Ring{segments}_{innerRatio:F2}" };
            var verts = new Vector3[segments * 2];
            var uvs = new Vector2[segments * 2];
            var normals = new Vector3[segments * 2];
            var tris = new int[segments * 6];

            for (int i = 0; i < segments; i++)
            {
                float a = i / (float)segments * Mathf.PI * 2f;
                float c = Mathf.Cos(a), s = Mathf.Sin(a);

                verts[i * 2 + 0] = new Vector3(c * 0.5f, s * 0.5f, 0f);
                verts[i * 2 + 1] = new Vector3(c * 0.5f * innerRatio, s * 0.5f * innerRatio, 0f);
                uvs[i * 2 + 0] = new Vector2(i / (float)segments, 1f);
                uvs[i * 2 + 1] = new Vector2(i / (float)segments, 0f);
                normals[i * 2 + 0] = Vector3.back;
                normals[i * 2 + 1] = Vector3.back;

                int n = (i + 1) % segments;
                tris[i * 6 + 0] = i * 2;
                tris[i * 6 + 1] = i * 2 + 1;
                tris[i * 6 + 2] = n * 2;
                tris[i * 6 + 3] = n * 2;
                tris[i * 6 + 4] = i * 2 + 1;
                tris[i * 6 + 5] = n * 2 + 1;
            }

            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.normals = normals;
            mesh.triangles = tris;
            mesh.RecalculateBounds();

            RingCache[key] = mesh;
            return mesh;
        }

        public static void ClearCache()
        {
            foreach (var m in DiscCache.Values) if (m != null) Object.DestroyImmediate(m);
            foreach (var m in RingCache.Values) if (m != null) Object.DestroyImmediate(m);
            DiscCache.Clear();
            RingCache.Clear();
        }

        /// <summary>用给定网格建一个渲染对象。</summary>
        public static GameObject Create(string name, Transform parent, Mesh mesh, Material material,
                                        Vector3 localPos, Vector3 localEuler, Vector3 localScale)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localEulerAngles = localEuler;
            go.transform.localScale = localScale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            return go;
        }
    }
}
