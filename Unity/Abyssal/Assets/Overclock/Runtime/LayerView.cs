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

        // 鼠标悬停的格子高亮框。它是玩家和这块板子之间唯一的触点，
        // 必须在任何背景亮度下都能看清，所以用纯白和纯红两个极端色。
        readonly List<MeshRenderer> _highlightEdges = new List<MeshRenderer>();
        Transform _highlight;

        MaterialPropertyBlock _block;
        Material _substrateMaterial;
        Material _emissiveMaterial;

        // 沿线奔跑的数据包。这是整个画面「活着」的来源——
        // 一张不流动的电路图和一张流动的电路图，在观感上是电路板和活物的区别。
        readonly List<Transform> _packets = new List<Transform>();
        readonly List<MeshRenderer> _packetRenderers = new List<MeshRenderer>();
        readonly List<int> _packetLink = new List<int>();
        readonly List<float> _packetPhase = new List<float>();

        const int PacketsPerLink = 2;

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
            BuildPackets();
            BuildHighlight();
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
        /// 悬停高亮框。用四条细边而不是一整块半透明色块：
        /// 色块会盖住格子里的元件，而玩家正需要看清自己要覆盖掉什么。
        /// </summary>
        void BuildHighlight()
        {
            _highlight = new GameObject("Highlight").transform;
            _highlight.SetParent(Root, false);

            const float thickness = 0.055f;
            const float span = CellSize * 0.98f;

            (Vector3 pos, Vector3 scale)[] edges =
            {
                (new Vector3(0f, 0f, span * 0.5f), new Vector3(span, thickness, thickness)),
                (new Vector3(0f, 0f, -span * 0.5f), new Vector3(span, thickness, thickness)),
                (new Vector3(span * 0.5f, 0f, 0f), new Vector3(thickness, thickness, span)),
                (new Vector3(-span * 0.5f, 0f, 0f), new Vector3(thickness, thickness, span)),
            };

            foreach (var (pos, scale) in edges)
            {
                var edge = Make(_highlight, "Edge", PrimitiveType.Cube,
                    pos + new Vector3(0f, 0.22f, 0f), scale, _emissiveMaterial);
                _highlightEdges.Add(edge.GetComponent<MeshRenderer>());
            }

            _highlight.gameObject.SetActive(false);
        }

        /// <summary>
        /// 更新悬停高亮。<paramref name="canPlace"/> 为假时框变红，
        /// 玩家在按下鼠标之前就知道这一下放不下去。
        /// </summary>
        public void SetHighlight(Vector2Int cell, ComponentKind kind, bool canPlace)
        {
            if (_highlight == null) return;

            if (cell.x < 0 || !_layer.InBounds(cell.x, cell.y))
            {
                _highlight.gameObject.SetActive(false);
                return;
            }

            _highlight.gameObject.SetActive(true);
            _highlight.localPosition = CellCenter(cell.x, cell.y);

            var color = canPlace
                ? OverclockPalette.Selection * 2.4f
                : OverclockPalette.Critical * 2.8f;

            foreach (var edge in _highlightEdges) SetColor(edge, color);
        }

        /// <summary>
        /// 每条连接段上预生成固定数量的数据包。用对象池而不是动态增删，
        /// 是因为格子数固定、连接数也固定，池的规模完全可预测。
        /// </summary>
        void BuildPackets()
        {
            var group = new GameObject("Packets").transform;
            group.SetParent(Root, false);

            for (int link = 0; link < _links.Count; link++)
            {
                for (int p = 0; p < PacketsPerLink; p++)
                {
                    var packet = Make(group, $"Packet_{link}_{p}", PrimitiveType.Cube,
                        Vector3.zero, new Vector3(0.145f, 0.10f, 0.145f), _emissiveMaterial);

                    _packets.Add(packet.transform);
                    _packetRenderers.Add(packet.GetComponent<MeshRenderer>());
                    _packetLink.Add(link);
                    // 相位错开，否则同一条线上的包会叠在一起像一个大方块。
                    _packetPhase.Add(p / (float)PacketsPerLink);
                }
            }
        }

        /// <summary>
        /// 推进数据包。<paramref name="time"/> 是累计时间，
        /// 包沿连接段循环移动，速度正比于这条线的负载率。
        ///
        /// 这一步和仿真完全解耦：包只是按流量密度画出来的视觉表现，
        /// 不参与任何计算。这是自动化游戏能做到大规模而不掉帧的关键技巧。
        /// </summary>
        public void TickPackets(float time)
        {
            int w = _layer.Width;

            for (int i = 0; i < _packets.Count; i++)
            {
                int link = _packetLink[i];
                var (a, b) = _linkCells[link];

                int ax = a % w, ay = a / w;
                int bx = b % w, by = b / w;

                bool live = _links[link].gameObject.activeSelf;
                float load = live
                    ? Mathf.Min((float)_layer.UtilizationAt(ax, ay), (float)_layer.UtilizationAt(bx, by))
                    : 0f;

                // 负载太低就不画包。稀疏的线上偶尔飘过一个点，
                // 比每条线都塞满包更能读出「哪里忙哪里闲」。
                if (!live || load < 0.06f)
                {
                    _packets[i].gameObject.SetActive(false);
                    continue;
                }

                _packets[i].gameObject.SetActive(true);

                float speed = 0.45f + load * 1.9f;
                float t = Mathf.Repeat(time * speed + _packetPhase[i], 1f);

                var from = CellCenter(ax, ay);
                var to = CellCenter(bx, by);
                _packets[i].localPosition = Vector3.Lerp(from, to, t) + new Vector3(0f, 0.10f, 0f);

                float stress = Mathf.Max((float)_layer.ThermalStressAt(ax, ay),
                                         (float)_layer.ThermalStressAt(bx, by));

                // 包在两端附近淡出，避免它突兀地出现和消失。
                float fade = Mathf.Sin(t * Mathf.PI);

                // 线路一热就变橙红，数据包如果跟着变色就会融进背景里。
                // 越热越把包推向白色，让它始终能从线上读出来。
                var packetColor = Color.Lerp(OverclockPalette.Data, Color.white,
                                             Mathf.Clamp01(stress * 0.85f));
                SetColor(_packetRenderers[i], packetColor * (1.05f + load * 1.35f) * fade);
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
