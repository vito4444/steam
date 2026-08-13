using UnityEngine;

namespace Abyssal.Visual
{
    /// <summary>
    /// 控制台元件的程序化构造。
    ///
    /// 每一种元件都由基础图元拼成，不使用任何模型资产。约束是刻意的：
    /// 一旦允许引入手工模型，元件数量就会被建模速度卡住，
    /// 而这个方案的画面说服力恰恰来自「视野里有几十个可辨识的仪表」这件事。
    ///
    /// 所有元件都建在一个「面板局部坐标系」里：面板正面朝 -Z，
    /// 面板平面是 XY，原点在面板中心，单位是米。
    /// </summary>
    public static class InstrumentFactory
    {
        const float PanelSurfaceOffset = -0.004f;

        static GameObject Primitive(PrimitiveType type, string name, Transform parent,
                                    Vector3 localPos, Vector3 localEuler, Vector3 localScale,
                                    Material material)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            var col = go.GetComponent<Collider>();
            if (col != null) Object.DestroyImmediate(col);

            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localEulerAngles = localEuler;
            go.transform.localScale = localScale;
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            return go;
        }

        /// <summary>
        /// 圆形指针仪表。返回的 <see cref="Gauge"/> 持有指针 Transform，
        /// 运行时通过 <see cref="Gauge.SetNormalized"/> 驱动。
        /// </summary>
        public static Gauge Dial(Transform panel, Vector2 uv, float diameter,
                                 ProceduralTextures.DialSpec spec)
        {
            var root = new GameObject($"Dial_{spec.Label}");
            root.transform.SetParent(panel, false);
            root.transform.localPosition = new Vector3(uv.x, uv.y, 0f);

            // 仪表壳体沉进面板里。圆柱默认沿 Y 轴，绕 X 转 90° 后圆面朝向 ±Z。
            Primitive(PrimitiveType.Cylinder, "Housing", root.transform,
                new Vector3(0f, 0f, 0.030f), new Vector3(90f, 0f, 0f),
                new Vector3(diameter * 0.96f, 0.024f, diameter * 0.96f),
                MaterialLibrary.ConsoleShell);

            // 盘面必须是真正的圆形网格。用 Quad 加透明外圈的话，
            // 那圈拿不到光的透明区在暗舱里会变成一个比面板更黑的方框。
            //
            // 材质用 Unlit 而不是 Lit：在这台没有独立显卡的机器上，
            // URP Lit 在远距离采样这些程序化贴图时会塌成平均色，
            // 主视角下整个盘面变成一个纯色圆片，刻度完全消失（特写下正常）。
            // 同一张贴图挂在 Unlit 上则任何距离都清晰。
            // 仪表可读性是这个方案的命根子，这里用光照响应换确定性。
            var faceTex = ProceduralTextures.DialFace(spec);
            var face = MeshShapes.Create("Face", root.transform, MeshShapes.Disc(72),
                MaterialLibrary.Emissive($"dialface_{spec.Label}", faceTex, Color.white, 0.82f),
                new Vector3(0f, 0f, PanelSurfaceOffset), Vector3.zero,
                new Vector3(diameter, diameter, 1f));

            // 金属压边圈。这一圈高光是仪表看起来「嵌在面板上」而不是「印在面板上」的关键。
            MeshShapes.Create("Bezel", root.transform, MeshShapes.Ring(72, 0.885f),
                MaterialLibrary.Solid("bezel", new Color(0.42f, 0.40f, 0.36f), 0.92f, 0.66f),
                new Vector3(0f, 0f, PanelSurfaceOffset - 0.0015f), Vector3.zero,
                new Vector3(diameter * 1.10f, diameter * 1.10f, 1f));

            var needle = Primitive(PrimitiveType.Quad, "Needle", root.transform,
                new Vector3(0f, 0f, PanelSurfaceOffset - 0.004f), Vector3.zero,
                new Vector3(diameter * 0.88f, diameter * 0.88f, 1f),
                MaterialLibrary.Decal("needle", ProceduralTextures.Needle(), Color.white));

            return new Gauge(root.transform, needle.transform, face.transform);
        }

        /// <summary>可旋转的旋钮。指示槽朝向反映当前设定值。</summary>
        public static Knob RotaryKnob(Transform panel, Vector2 uv, float diameter, string label)
        {
            var root = new GameObject($"Knob_{label}");
            root.transform.SetParent(panel, false);
            root.transform.localPosition = new Vector3(uv.x, uv.y, 0f);

            Primitive(PrimitiveType.Cylinder, "Collar", root.transform,
                new Vector3(0f, 0f, -0.004f), new Vector3(90f, 0f, 0f),
                new Vector3(diameter * 1.22f, 0.004f, diameter * 1.22f),
                MaterialLibrary.DarkPlastic);

            var body = Primitive(PrimitiveType.Cylinder, "Body", root.transform,
                new Vector3(0f, 0f, -0.016f), new Vector3(90f, 0f, 0f),
                new Vector3(diameter, 0.014f, diameter),
                MaterialLibrary.BrassKnob);

            // 指示槽。玩家靠它读出旋钮转到了哪个位置。
            Primitive(PrimitiveType.Cube, "Indicator", body.transform,
                new Vector3(0f, -0.55f, 0.62f), Vector3.zero,
                new Vector3(0.10f, 1.15f, 0.10f),
                MaterialLibrary.DarkPlastic);

            AddNameplate(root.transform, label, new Vector3(0f, -diameter * 0.92f, PanelSurfaceOffset),
                         diameter * 1.9f);

            return new Knob(root.transform, body.transform);
        }

        /// <summary>两态或三态拨杆。</summary>
        public static Toggle Lever(Transform panel, Vector2 uv, float height, string label)
        {
            var root = new GameObject($"Lever_{label}");
            root.transform.SetParent(panel, false);
            root.transform.localPosition = new Vector3(uv.x, uv.y, 0f);

            Primitive(PrimitiveType.Cube, "Base", root.transform,
                new Vector3(0f, 0f, -0.006f), Vector3.zero,
                new Vector3(height * 0.42f, height * 0.30f, 0.012f),
                MaterialLibrary.DarkPlastic);

            var pivot = new GameObject("Pivot");
            pivot.transform.SetParent(root.transform, false);
            pivot.transform.localPosition = new Vector3(0f, 0f, -0.010f);

            Primitive(PrimitiveType.Cylinder, "Shaft", pivot.transform,
                new Vector3(0f, height * 0.34f, 0f), Vector3.zero,
                new Vector3(height * 0.10f, height * 0.34f, height * 0.10f),
                MaterialLibrary.PipeSteel);

            Primitive(PrimitiveType.Sphere, "Tip", pivot.transform,
                new Vector3(0f, height * 0.68f, 0f), Vector3.zero,
                Vector3.one * (height * 0.20f),
                MaterialLibrary.RustAccent);

            AddNameplate(root.transform, label, new Vector3(0f, -height * 0.34f, PanelSurfaceOffset),
                         height * 1.5f);

            return new Toggle(root.transform, pivot.transform);
        }

        /// <summary>带背光的按钮。灯亮灭由 <see cref="IndicatorLamp"/> 控制。</summary>
        public static IndicatorLamp Button(Transform panel, Vector2 uv, float diameter,
                                           Color litColor, string label = null)
        {
            var root = new GameObject($"Button_{label ?? "unnamed"}");
            root.transform.SetParent(panel, false);
            root.transform.localPosition = new Vector3(uv.x, uv.y, 0f);

            Primitive(PrimitiveType.Cylinder, "Housing", root.transform,
                new Vector3(0f, 0f, -0.002f), new Vector3(90f, 0f, 0f),
                new Vector3(diameter * 1.30f, 0.006f, diameter * 1.30f),
                MaterialLibrary.DarkPlastic);

            var cap = Primitive(PrimitiveType.Cylinder, "Cap", root.transform,
                new Vector3(0f, 0f, -0.009f), new Vector3(90f, 0f, 0f),
                new Vector3(diameter, 0.005f, diameter),
                MaterialLibrary.Emissive($"lamp_{ColorKey(litColor)}", null, litColor, 2.2f));

            if (!string.IsNullOrEmpty(label))
                AddNameplate(root.transform, label,
                             new Vector3(0f, -diameter * 1.05f, PanelSurfaceOffset), diameter * 2.1f);

            return new IndicatorLamp(root.transform, cap.GetComponent<MeshRenderer>(), litColor);
        }

        /// <summary>CRT 屏幕。</summary>
        public static Transform Screen(Transform panel, Vector2 uv, Vector2 size, string caption, int seed)
        {
            var root = new GameObject($"Screen_{caption}");
            root.transform.SetParent(panel, false);
            root.transform.localPosition = new Vector3(uv.x, uv.y, 0f);

            Primitive(PrimitiveType.Cube, "Bezel", root.transform,
                new Vector3(0f, 0f, 0.006f), Vector3.zero,
                new Vector3(size.x * 1.12f, size.y * 1.16f, 0.020f),
                MaterialLibrary.DarkPlastic);

            Primitive(PrimitiveType.Quad, "Glass", root.transform,
                new Vector3(0f, 0f, PanelSurfaceOffset - 0.001f), Vector3.zero,
                new Vector3(size.x, size.y, 1f),
                MaterialLibrary.Emissive($"crt_{caption}",
                    ProceduralTextures.CrtScreen(caption, seed), Color.white, 1.35f));

            return root.transform;
        }

        /// <summary>丝印标签牌。</summary>
        public static void AddNameplate(Transform parent, string text, Vector3 localPos, float width)
        {
            if (string.IsNullOrEmpty(text)) return;
            Primitive(PrimitiveType.Quad, $"Plate_{text}", parent,
                localPos, Vector3.zero, new Vector3(width, width * 0.25f, 1f),
                MaterialLibrary.Decal($"plate_{text}", ProceduralTextures.Nameplate(text), Color.white));
        }

        static string ColorKey(Color c) => $"{c.r:F2}_{c.g:F2}_{c.b:F2}";
    }

    /// <summary>指针仪表的运行时句柄。</summary>
    public sealed class Gauge
    {
        public readonly Transform Root;
        readonly Transform _needle;
        readonly Transform _face;
        float _displayed;

        public Gauge(Transform root, Transform needle, Transform face)
        {
            Root = root;
            _needle = needle;
            _face = face;
        }

        public Transform Face => _face;

        /// <summary>直接把指针打到位置，不做平滑。用于场景初始化。</summary>
        public void SetNormalized(float t)
        {
            _displayed = Mathf.Clamp01(t);
            Apply();
        }

        /// <summary>
        /// 带阻尼地跟踪目标值。真实仪表的指针有惯性，
        /// 瞬间跳变会让整块面板显得很廉价。
        /// </summary>
        public void TrackNormalized(float target, float deltaTime, float responseTime = 0.35f)
        {
            target = Mathf.Clamp01(target);
            float k = responseTime <= 0f ? 1f : 1f - Mathf.Exp(-deltaTime / responseTime);
            _displayed = Mathf.Lerp(_displayed, target, k);
            Apply();
        }

        void Apply()
        {
            // 贴图里 0 刻度在 225°、满量程在 -45°，而指针 Quad 默认指向 +Y（即 90°）。
            float angle = ProceduralTextures.DialAngle(_displayed) - 90f;
            _needle.localEulerAngles = new Vector3(0f, 0f, angle);
        }
    }

    /// <summary>旋钮的运行时句柄。</summary>
    public sealed class Knob
    {
        public readonly Transform Root;
        readonly Transform _body;

        public Knob(Transform root, Transform body)
        {
            Root = root;
            _body = body;
        }

        /// <summary>把旋钮转到 0–1 对应的位置，行程和仪表盘一致，都是 270°。</summary>
        public void SetNormalized(float t)
        {
            // 旋钮体绕自身轴（旋转后的局部 Y）转，所以角度写在 Y 上。
            _body.localEulerAngles = new Vector3(90f, ProceduralTextures.DialSweep * Mathf.Clamp01(t), 0f);
        }
    }

    /// <summary>拨杆的运行时句柄。</summary>
    public sealed class Toggle
    {
        public readonly Transform Root;
        readonly Transform _pivot;

        public Toggle(Transform root, Transform pivot)
        {
            Root = root;
            _pivot = pivot;
        }

        /// <summary>-1 向下、0 居中、+1 向上。</summary>
        public void SetPosition(float position)
        {
            _pivot.localEulerAngles = new Vector3(-Mathf.Clamp(position, -1f, 1f) * 32f, 0f, 0f);
        }
    }

    /// <summary>指示灯的运行时句柄。</summary>
    public sealed class IndicatorLamp
    {
        public readonly Transform Root;
        readonly MeshRenderer _renderer;
        readonly Color _litColor;
        MaterialPropertyBlock _block;

        public IndicatorLamp(Transform root, MeshRenderer renderer, Color litColor)
        {
            Root = root;
            _renderer = renderer;
            _litColor = litColor;
        }

        /// <summary>0 为熄灭，1 为全亮。中间值用于呼吸和闪烁。</summary>
        public void SetIntensity(float intensity)
        {
            _block ??= new MaterialPropertyBlock();
            _renderer.GetPropertyBlock(_block);
            _block.SetColor("_BaseColor", _litColor * Mathf.Max(0.06f, intensity * 2.6f));
            _renderer.SetPropertyBlock(_block);
        }
    }
}
