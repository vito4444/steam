using System.Collections.Generic;
using UnityEngine;

namespace Maner.Cabin
{
    /// <summary>
    /// 程序化网格构造器。本项目不使用任何手工建模资产，控制舱里的每一块钢板、
    /// 每一个旋钮都由这里的原语拼出来。工业硬表面是规则几何，正好是程序化生成最擅长的类型。
    /// </summary>
    public sealed class MeshBuilder
    {
        readonly List<Vector3> vertices = new List<Vector3>(512);
        readonly List<Vector3> normals = new List<Vector3>(512);
        readonly List<Vector2> uvs = new List<Vector2>(512);
        readonly List<int> triangles = new List<int>(1024);

        public int VertexCount => vertices.Count;
        public int TriangleCount => triangles.Count / 3;

        public void Clear()
        {
            vertices.Clear();
            normals.Clear();
            uvs.Clear();
            triangles.Clear();
        }

        public Mesh ToMesh(string name)
        {
            var mesh = new Mesh { name = name };
            if (vertices.Count > 65000)
            {
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            }
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }

        /// <summary>
        /// 四边形。顶点按逆时针给出（从法线一侧看），三角形按 0-1-2 / 0-2-3 拆分。
        /// 绕序方向是这套网格生成代码里最容易出错的一环：绕反了不会报错，
        /// 只是那个面会被背面剔除，看上去像根本没生成。
        /// </summary>
        void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal, Vector2 uvScale)
        {
            int baseIndex = vertices.Count;
            vertices.Add(a);
            vertices.Add(b);
            vertices.Add(c);
            vertices.Add(d);
            for (int i = 0; i < 4; i++)
            {
                normals.Add(normal);
            }
            uvs.Add(new Vector2(0f, 0f));
            uvs.Add(new Vector2(uvScale.x, 0f));
            uvs.Add(new Vector2(uvScale.x, uvScale.y));
            uvs.Add(new Vector2(0f, uvScale.y));

            triangles.Add(baseIndex);
            triangles.Add(baseIndex + 1);
            triangles.Add(baseIndex + 2);
            triangles.Add(baseIndex);
            triangles.Add(baseIndex + 2);
            triangles.Add(baseIndex + 3);
        }

        /// <summary>
        /// 单面矩形，UV 完整覆盖 0..1。面板正面用它，好让丝印贴图一比一贴上去。
        /// </summary>
        /// <param name="mirrorU">
        /// 水平翻转 UV。面板一律带 180 度偏航，它们的局部 +X 在世界里指向玩家视角的左边，
        /// 贴上去的图会左右反。在 UV 层面翻一次比让每一处绘制代码各自镜像干净得多。
        /// </param>
        public void AddFace(Vector3 center, float width, float height, Quaternion rotation = default, bool mirrorU = false)
        {
            if (rotation == default)
            {
                rotation = Quaternion.identity;
            }

            float hw = width * 0.5f;
            float hh = height * 0.5f;
            Vector3 normal = rotation * Vector3.forward;
            int baseIndex = vertices.Count;

            vertices.Add(center + rotation * new Vector3(-hw, -hh, 0f));
            vertices.Add(center + rotation * new Vector3(hw, -hh, 0f));
            vertices.Add(center + rotation * new Vector3(hw, hh, 0f));
            vertices.Add(center + rotation * new Vector3(-hw, hh, 0f));

            for (int i = 0; i < 4; i++)
            {
                normals.Add(normal);
            }

            float u0 = mirrorU ? 1f : 0f;
            float u1 = mirrorU ? 0f : 1f;
            uvs.Add(new Vector2(u0, 0f));
            uvs.Add(new Vector2(u1, 0f));
            uvs.Add(new Vector2(u1, 1f));
            uvs.Add(new Vector2(u0, 1f));

            triangles.Add(baseIndex);
            triangles.Add(baseIndex + 1);
            triangles.Add(baseIndex + 2);
            triangles.Add(baseIndex);
            triangles.Add(baseIndex + 2);
            triangles.Add(baseIndex + 3);
        }

        /// <summary>轴对齐长方体。center 为几何中心，size 为全尺寸。</summary>
        public void AddBox(Vector3 center, Vector3 size, Quaternion rotation = default, float uvScale = 1f)
        {
            if (rotation == default)
            {
                rotation = Quaternion.identity;
            }

            Vector3 h = size * 0.5f;
            Vector3 P(float x, float y, float z) => center + rotation * new Vector3(x * h.x, y * h.y, z * h.z);
            Vector3 N(Vector3 n) => rotation * n;

            AddQuad(P(-1, -1, 1), P(1, -1, 1), P(1, 1, 1), P(-1, 1, 1), N(Vector3.forward), new Vector2(size.x * uvScale, size.y * uvScale));
            AddQuad(P(1, -1, -1), P(-1, -1, -1), P(-1, 1, -1), P(1, 1, -1), N(Vector3.back), new Vector2(size.x * uvScale, size.y * uvScale));
            AddQuad(P(1, -1, 1), P(1, -1, -1), P(1, 1, -1), P(1, 1, 1), N(Vector3.right), new Vector2(size.z * uvScale, size.y * uvScale));
            AddQuad(P(-1, -1, -1), P(-1, -1, 1), P(-1, 1, 1), P(-1, 1, -1), N(Vector3.left), new Vector2(size.z * uvScale, size.y * uvScale));
            AddQuad(P(-1, 1, 1), P(1, 1, 1), P(1, 1, -1), P(-1, 1, -1), N(Vector3.up), new Vector2(size.x * uvScale, size.z * uvScale));
            AddQuad(P(-1, -1, -1), P(1, -1, -1), P(1, -1, 1), P(-1, -1, 1), N(Vector3.down), new Vector2(size.x * uvScale, size.z * uvScale));
        }

        /// <summary>沿指定轴的圆柱，带端盖。</summary>
        public void AddCylinder(Vector3 center, float radius, float height, int segments = 20,
            Quaternion rotation = default, bool caps = true, float topRadiusScale = 1f)
        {
            if (rotation == default)
            {
                rotation = Quaternion.identity;
            }

            float half = height * 0.5f;
            float topRadius = radius * topRadiusScale;
            int ringStart = vertices.Count;

            for (int i = 0; i <= segments; i++)
            {
                float t = (float)i / segments;
                float a = t * Mathf.PI * 2f;
                float cos = Mathf.Cos(a);
                float sin = Mathf.Sin(a);

                Vector3 nLocal = new Vector3(cos, 0f, sin);
                Vector3 n = rotation * nLocal;

                vertices.Add(center + rotation * new Vector3(cos * radius, -half, sin * radius));
                normals.Add(n);
                uvs.Add(new Vector2(t, 0f));

                vertices.Add(center + rotation * new Vector3(cos * topRadius, half, sin * topRadius));
                normals.Add(n);
                uvs.Add(new Vector2(t, 1f));
            }

            for (int i = 0; i < segments; i++)
            {
                int b = ringStart + i * 2;
                triangles.Add(b);
                triangles.Add(b + 1);
                triangles.Add(b + 2);
                triangles.Add(b + 1);
                triangles.Add(b + 3);
                triangles.Add(b + 2);
            }

            if (!caps)
            {
                return;
            }

            AddDisc(center + rotation * new Vector3(0f, half, 0f), topRadius, segments, rotation, rotation * Vector3.up, true);
            AddDisc(center + rotation * new Vector3(0f, -half, 0f), radius, segments, rotation, rotation * Vector3.down, false);
        }

        /// <summary>
        /// 圆盘。flip 控制三角形绕序：传 true 时圆面朝向 rotation 的局部 +Z 一侧，
        /// 也就是通常「朝向观察者」的那一面。传错的后果不是法线错，而是整个圆面被
        /// 背面剔除掉，看上去像是它压根没被创建。
        /// </summary>
        public void AddDisc(Vector3 center, float radius, int segments, Quaternion rotation, Vector3 normal, bool flip)
        {
            int centerIndex = vertices.Count;
            vertices.Add(center);
            normals.Add(normal);
            uvs.Add(new Vector2(0.5f, 0.5f));

            for (int i = 0; i <= segments; i++)
            {
                float t = (float)i / segments;
                float a = t * Mathf.PI * 2f;
                float cos = Mathf.Cos(a);
                float sin = Mathf.Sin(a);
                vertices.Add(center + rotation * new Vector3(cos * radius, 0f, sin * radius));
                normals.Add(normal);
                uvs.Add(new Vector2(0.5f + cos * 0.5f, 0.5f + sin * 0.5f));
            }

            for (int i = 0; i < segments; i++)
            {
                int a = centerIndex + 1 + i;
                int b = centerIndex + 2 + i;
                if (flip)
                {
                    triangles.Add(centerIndex);
                    triangles.Add(b);
                    triangles.Add(a);
                }
                else
                {
                    triangles.Add(centerIndex);
                    triangles.Add(a);
                    triangles.Add(b);
                }
            }
        }

        /// <summary>圆环。阀轮的轮圈、表盘的外框都用它。</summary>
        public void AddTorus(Vector3 center, float majorRadius, float minorRadius,
            int majorSegments = 24, int minorSegments = 8, Quaternion rotation = default)
        {
            if (rotation == default)
            {
                rotation = Quaternion.identity;
            }

            int start = vertices.Count;
            for (int i = 0; i <= majorSegments; i++)
            {
                float u = (float)i / majorSegments;
                float ua = u * Mathf.PI * 2f;
                Vector3 ringCenter = new Vector3(Mathf.Cos(ua) * majorRadius, 0f, Mathf.Sin(ua) * majorRadius);
                Vector3 outward = new Vector3(Mathf.Cos(ua), 0f, Mathf.Sin(ua));

                for (int j = 0; j <= minorSegments; j++)
                {
                    float v = (float)j / minorSegments;
                    float va = v * Mathf.PI * 2f;
                    Vector3 nLocal = outward * Mathf.Cos(va) + Vector3.up * Mathf.Sin(va);
                    vertices.Add(center + rotation * (ringCenter + nLocal * minorRadius));
                    normals.Add(rotation * nLocal);
                    uvs.Add(new Vector2(u, v));
                }
            }

            int stride = minorSegments + 1;
            for (int i = 0; i < majorSegments; i++)
            {
                for (int j = 0; j < minorSegments; j++)
                {
                    int a = start + i * stride + j;
                    int b = a + stride;
                    triangles.Add(a);
                    triangles.Add(b);
                    triangles.Add(a + 1);
                    triangles.Add(a + 1);
                    triangles.Add(b);
                    triangles.Add(b + 1);
                }
            }
        }

        /// <summary>沿一串点挤出的管道。用于走线与管路。</summary>
        public void AddTube(IReadOnlyList<Vector3> path, float radius, int segments = 8)
        {
            if (path.Count < 2)
            {
                return;
            }

            int start = vertices.Count;
            for (int p = 0; p < path.Count; p++)
            {
                Vector3 forward = p == 0 ? (path[1] - path[0]) :
                    p == path.Count - 1 ? (path[p] - path[p - 1]) :
                    (path[p + 1] - path[p - 1]);
                forward = forward.sqrMagnitude < 1e-8f ? Vector3.forward : forward.normalized;
                Vector3 up = Mathf.Abs(Vector3.Dot(forward, Vector3.up)) > 0.95f ? Vector3.right : Vector3.up;
                Vector3 right = Vector3.Cross(up, forward).normalized;
                up = Vector3.Cross(forward, right);

                for (int i = 0; i <= segments; i++)
                {
                    float t = (float)i / segments;
                    float a = t * Mathf.PI * 2f;
                    Vector3 n = right * Mathf.Cos(a) + up * Mathf.Sin(a);
                    vertices.Add(path[p] + n * radius);
                    normals.Add(n);
                    uvs.Add(new Vector2(t, p));
                }
            }

            int stride = segments + 1;
            for (int p = 0; p < path.Count - 1; p++)
            {
                for (int i = 0; i < segments; i++)
                {
                    int a = start + p * stride + i;
                    int b = a + stride;
                    triangles.Add(a);
                    triangles.Add(b);
                    triangles.Add(a + 1);
                    triangles.Add(a + 1);
                    triangles.Add(b);
                    triangles.Add(b + 1);
                }
            }
        }

        /// <summary>低多边形球体，用于拨杆球头与灯泡。</summary>
        public void AddSphere(Vector3 center, float radius, int rings = 8, int segments = 12)
        {
            int start = vertices.Count;
            for (int r = 0; r <= rings; r++)
            {
                float v = (float)r / rings;
                float phi = v * Mathf.PI;
                float y = Mathf.Cos(phi);
                float ringRadius = Mathf.Sin(phi);

                for (int s = 0; s <= segments; s++)
                {
                    float u = (float)s / segments;
                    float theta = u * Mathf.PI * 2f;
                    Vector3 n = new Vector3(Mathf.Cos(theta) * ringRadius, y, Mathf.Sin(theta) * ringRadius);
                    vertices.Add(center + n * radius);
                    normals.Add(n);
                    uvs.Add(new Vector2(u, 1f - v));
                }
            }

            int stride = segments + 1;
            for (int r = 0; r < rings; r++)
            {
                for (int s = 0; s < segments; s++)
                {
                    int a = start + r * stride + s;
                    int b = a + stride;
                    triangles.Add(a);
                    triangles.Add(b);
                    triangles.Add(a + 1);
                    triangles.Add(a + 1);
                    triangles.Add(b);
                    triangles.Add(b + 1);
                }
            }
        }

        /// <summary>面板上的螺丝点阵。数量不多但极大提升工业质感。</summary>
        public void AddScrewGrid(Vector3 origin, Vector3 right, Vector3 up, Vector3 normal,
            float width, float height, int countX, int countY, float radius)
        {
            var rot = Quaternion.LookRotation(normal, up) * Quaternion.Euler(90f, 0f, 0f);
            for (int x = 0; x < countX; x++)
            {
                for (int y = 0; y < countY; y++)
                {
                    float fx = countX == 1 ? 0.5f : (float)x / (countX - 1);
                    float fy = countY == 1 ? 0.5f : (float)y / (countY - 1);
                    Vector3 p = origin + right * (fx * width) + up * (fy * height);
                    AddCylinder(p + normal * radius * 0.3f, radius, radius * 0.6f, 6, rot);
                }
            }
        }
    }
}
