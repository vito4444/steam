using System.Collections.Generic;
using UnityEngine;
using Abyssal.Visual;
using Overclock.Core;

namespace Overclock
{
    /// <summary>
    /// 把一层硅片画出来。
    ///
    /// 每个格子由一块基底和一个元件体组成，元件之间还有连接段。
    /// 所有颜色都通过 MaterialPropertyBlock 逐帧更新，共用同一个材质，
    /// 这样几百个格子也只占很少的 draw call。
    ///
    /// 视觉规则很简单，但要严格遵守：亮度代表流量，色相代表温度。
    /// 玩家不需要读任何数字就能看出「这条线很忙」和「这条线快烧了」的区别。
    /// </summary>
    public sealed class LayerView
    {
        public Transform Root { get; private set; }

        /// <summary>一个格子在世界空间的边长。</summary>
        public const float CellSize = 1.0f;

        SiliconLayer _layer;

        readonly List<MeshRenderer> _substrate = new List<MeshRenderer>();
        readonly List<MeshRenderer> _bodies = new List<MeshRenderer>();
        readonly List<Transform> _bodyTransforms = new List<Transform>();
        readonly List<MeshRenderer> _links = new List<MeshRenderer>();
        readonly List<(int a, int b)> _linkCells = new List<(int, int)>();

        MaterialPropertyBlock _block;
        Material _substrateMaterial;
        Material _emissiveMaterial;

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        public void Build(SiliconLayer layer, Transform parent)
        {
            _layer = layer;
            _block = new MaterialPropertyBlock();

            Root = new GameObject("SiliconLayer").transform;
            Root.SetParent(parent, false);

            _substrateMaterial = MaterialLibrary.Emissive(
                "oc_substrate", null, Color.white, 1f);
            _emissiveMaterial = MaterialLibrary.Emissive(
                "oc_emissive", null, Color.white, 1f);

            BuildSubstrate();
            BuildBodies();
            BuildLinks();
            Refresh();
        }

        /// <summary>格子中心在本层局部空间的位置。层平面是 XZ，Y 是高度。</summary>
        public Vector3 CellCenter(int x, int y)
            => new Vector3((x - (_layer.Width - 1) * 0.5f) * CellSize, 0f,
                           (y - (_layer.Height - 1) * 0.5f) * CellSize);

        void BuildSubstrate()
        {
            var group = new GameObject("Substrate").transform;
            group.SetParent(Root, false);

            for (int y = 0; y < _layer.Height; y++)
            for (int x = 0; x < _layer.Width; x++)
            {
                // 格子之间留一道缝，这道缝就是网格线，不用额外画。
                var tile = Make(group, $"Cell_{x}_{y}", PrimitiveType.Cube,
                    CellCenter(x, y) + new Vector3(0f, -0.06f, 0f),
                    new Vector3(CellSize * 0.94f, 0.10f, CellSize * 0.94f),
                    _substrateMaterial);
                _substrate.Add(tile.GetComponent<MeshRenderer>());
            }
        }

        void BuildBodies()
        {
            var group = new GameObject("Components").transform;
            group.SetParent(Root, false);

            for (int y = 0; y < _layer.Height; y++)
            for (int x = 0; x < _layer.Width; x++)
            {
                var go = new GameObject($"Body_{x}_{y}");
                go.transform.SetParent(group, false);
                go.transform.localPosition = CellCenter(x, y);

                var mesh = Make(go.transform, "Mesh", PrimitiveType.Cube,
                    Vector3.zero, Vector3.one * 0.4f, _emissiveMaterial);

                _bodies.Add(mesh.GetComponent<MeshRenderer>());
                _bodyTransforms.Add(mesh.transform);
            }
        }

        /// <summary>
        /// 相邻两个导数据格子之间的连接段。
        /// 它们是画面里最重要的元素：一整张发光的网就是靠这些短线连出来的。
        /// </summary>
        void BuildLinks()
        {
            var group = new GameObject("Links").transform;
            group.SetParent(Root, false);

            for (int y = 0; y < _layer.Height; y++)
            for (int x = 0; x < _layer.Width; x++)
            {
                foreach (var (dx, dy) in new[] { (1, 0), (0, 1) })
                {
                    int nx = x + dx, ny = y + dy;
                    if (!_layer.InBounds(nx, ny)) continue;

                    var a = CellCenter(x, y);
                    var b = CellCenter(nx, ny);

                    var link = Make(group, $"Link_{x}_{y}_{dx}{dy}", PrimitiveType.Cube,
                        (a + b) * 0.5f,
                        dx > 0 ? new Vector3(CellSize * 0.62f, 0.055f, 0.11f)
                               : new Vector3(0.11f, 0.055f, CellSize * 0.62f),
                        _emissiveMaterial);

                    _links.Add(link.GetComponent<MeshRenderer>());
                    _linkCells.Add((_layer.Index(x, y), _layer.Index(nx, ny)));
                }
            }
        }

        /// <summary>
        /// 按当前仿真状态刷新所有颜色和形体。
        /// 每帧调用一次，开销主要在 MaterialPropertyBlock 的写入上。
        /// </summary>
        public void Refresh()
        {
            int w = _layer.Width;

            for (int y = 0; y < _layer.Height; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                var kind = _layer.CellAt(x, y);
                float stress = (float)_layer.ThermalStressAt(x, y);
                float load = (float)_layer.UtilizationAt(x, y);

                // 基底：被下面的热量烤出颜色。
                SetColor(_substrate[i], _layer.IsBurned(x, y)
                    ? OverclockPalette.Burned
                    : OverclockPalette.SubstrateByHeat(stress));

                // 元件体：形状代表种类，亮度代表负载，色相代表温度。
                var (scale, color, glow) = BodyStyle(kind, load, stress, _layer.IsBurned(x, y));
                _bodyTransforms[i].localScale = scale;
                _bodyTransforms[i].gameObject.SetActive(scale.sqrMagnitude > 1e-6f);
                SetColor(_bodies[i], color * glow);
            }

            for (int i = 0; i < _links.Count; i++)
            {
                var (a, b) = _linkCells[i];
                int ax = a % w, ay = a / w;
                int bx = b % w, by = b / w;

                bool live = ComponentLibrary.CarriesData(_layer.CellAt(ax, ay))
                            && ComponentLibrary.CarriesData(_layer.CellAt(bx, by))
                            && !_layer.IsBurned(ax, ay) && !_layer.IsBurned(bx, by);

                _links[i].gameObject.SetActive(live);
                if (!live) continue;

                float load = Mathf.Min((float)_layer.UtilizationAt(ax, ay),
                                       (float)_layer.UtilizationAt(bx, by));
                float stress = Mathf.Max((float)_layer.ThermalStressAt(ax, ay),
                                         (float)_layer.ThermalStressAt(bx, by));

                SetColor(_links[i], OverclockPalette.ByThermalStress(stress) * (0.45f + load * 2.4f));
            }
        }

        /// <summary>
        /// 每种元件的形体和发光。
        /// 形状必须在俯视下一眼可辨——玩家是靠轮廓而不是颜色来认元件的，
        /// 颜色已经被征用去表达温度了。
        /// </summary>
        static (Vector3 scale, Color color, float glow) BodyStyle(
            ComponentKind kind, float load, float stress, bool burned)
        {
            if (burned)
                return (new Vector3(0.34f, 0.10f, 0.34f), OverclockPalette.Burned, 1.6f);

            switch (kind)
            {
                case ComponentKind.Empty:
                    return (Vector3.zero, Color.black, 0f);

                case ComponentKind.DeadCell:
                    return (new Vector3(0.72f, 0.16f, 0.72f), OverclockPalette.DeadCell, 1.0f);

                case ComponentKind.Source:
                    // 最高最亮，一眼能看出流量从哪来。
                    return (new Vector3(0.46f, 0.62f, 0.46f), OverclockPalette.Source,
                            1.4f + load * 1.8f);

                case ComponentKind.Sink:
                    return (new Vector3(0.52f, 0.44f, 0.52f), OverclockPalette.Sink,
                            1.4f + load * 1.8f);

                case ComponentKind.Trace:
                    // 最扁最矮，铺满一片时读起来像电路板上的走线。
                    return (new Vector3(0.30f, 0.07f, 0.30f),
                            OverclockPalette.ByThermalStress(stress), 0.9f + load * 2.2f);

                case ComponentKind.Buffer:
                    return (new Vector3(0.44f, 0.20f, 0.44f),
                            OverclockPalette.ByThermalStress(stress), 0.8f + load * 2.4f);

                case ComponentKind.Bus:
                    // 最粗，也最容易变红。
                    return (new Vector3(0.60f, 0.30f, 0.60f),
                            OverclockPalette.ByThermalStress(stress), 0.8f + load * 2.6f);

                case ComponentKind.Splitter:
                    return (new Vector3(0.52f, 0.14f, 0.24f),
                            OverclockPalette.ByThermalStress(stress), 0.9f + load * 2.2f);

                case ComponentKind.Compressor:
                    return (new Vector3(0.34f, 0.42f, 0.34f),
                            OverclockPalette.ByThermalStress(stress), 0.9f + load * 2.6f);

                case ComponentKind.HeatSink:
                    // 唯一不参与数据的元件，用冷色和高瘦的形体把它区分出来。
                    return (new Vector3(0.30f, 0.46f, 0.66f), OverclockPalette.Coolant,
                            0.6f + (1f - stress) * 0.8f);

                default:
                    return (Vector3.zero, Color.black, 0f);
            }
        }

        void SetColor(MeshRenderer renderer, Color color)
        {
            renderer.GetPropertyBlock(_block);
            _block.SetColor(BaseColorId, color);
            renderer.SetPropertyBlock(_block);
        }

        static GameObject Make(Transform parent, string name, PrimitiveType type,
                               Vector3 localPos, Vector3 localScale, Material material)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            var col = go.GetComponent<Collider>();
            if (col != null) Object.DestroyImmediate(col);

            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = localScale;
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            return go;
        }
    }
}
