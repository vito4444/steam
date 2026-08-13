using Maner.Controls;
using UnityEngine;

namespace Maner.Cabin
{
    /// <summary>
    /// 把一条控件定义变成一组真实的网格与游戏对象。
    /// 八种控件原型各自有独立的几何构造与运动方式，这是「操作机器而不是点菜单」的物理基础。
    /// 全部几何由代码生成，工程内没有任何建模文件。
    /// </summary>
    public static class ControlMeshFactory
    {

        /// <summary>在面板局部空间创建一个控件。面板平面为 XY，法线朝 +Z。</summary>
        public static ControlVisual Build(in ControlDefinition def, Transform panelRoot, CabinMaterials mats)
        {
            var root = new GameObject(def.Id.ToString());
            root.transform.SetParent(panelRoot, false);
            root.transform.localPosition = new Vector3(def.X, def.Y, 0f);

            var visual = root.AddComponent<ControlVisual>();
            visual.Id = def.Id;
            visual.Kind = def.Kind;

            switch (def.Kind)
            {
                case ControlKind.KnifeSwitch: BuildKnifeSwitch(def, root.transform, mats, visual); break;
                case ControlKind.ToggleLever: BuildToggleLever(def, root.transform, mats, visual); break;
                case ControlKind.RotaryKnob: BuildRotaryKnob(def, root.transform, mats, visual); break;
                case ControlKind.ValveWheel: BuildValveWheel(def, root.transform, mats, visual); break;
                case ControlKind.PushButton: BuildPushButton(def, root.transform, mats, visual); break;
                case ControlKind.Breaker: BuildBreaker(def, root.transform, mats, visual); break;
                case ControlKind.ThrottleHandle: BuildThrottleHandle(def, root.transform, mats, visual); break;
                case ControlKind.Gauge: BuildGauge(def, root.transform, mats, visual); break;
            }

            return visual;
        }

        static GameObject Piece(string name, Transform parent, Mesh mesh, Material material, bool collider = true)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = material;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            if (collider)
            {
                var mc = go.AddComponent<MeshCollider>();
                mc.sharedMesh = mesh;
            }
            return go;
        }

        // ————————————————— 闸刀 —————————————————
        // 两根绝缘柱之间架一把刀片，合闸时刀片压进下方的刀夹。
        // 必须向下拖过行程阈值才会咬合，是全场手感最重的控件。
        static void BuildKnifeSwitch(in ControlDefinition def, Transform root, CabinMaterials mats, ControlVisual visual)
        {
            float s = def.Size;
            var b = new MeshBuilder();
            b.AddBox(new Vector3(0f, 0f, 0.008f), new Vector3(s * 0.86f, s * 0.62f, 0.016f));
            b.AddCylinder(new Vector3(0f, s * 0.22f, 0.030f), s * 0.062f, s * 0.10f, 10, Quaternion.Euler(90f, 0f, 0f));
            b.AddCylinder(new Vector3(0f, -s * 0.22f, 0.030f), s * 0.062f, s * 0.10f, 10, Quaternion.Euler(90f, 0f, 0f));
            Piece("Base", root, b.ToMesh($"{def.Id}_Base"), mats.FrameSteel);

            var jaw = new MeshBuilder();
            jaw.AddBox(new Vector3(0f, -s * 0.22f, 0.052f), new Vector3(s * 0.20f, s * 0.09f, s * 0.09f));
            Piece("Jaw", root, jaw.ToMesh($"{def.Id}_Jaw"), mats.Brass);

            // 活动件的枢轴放在上接线柱处，刀片自枢轴向下延伸。
            var pivot = new GameObject("Pivot");
            pivot.transform.SetParent(root, false);
            pivot.transform.localPosition = new Vector3(0f, s * 0.22f, 0.030f);

            var blade = new MeshBuilder();
            blade.AddBox(new Vector3(0f, -s * 0.22f, 0f), new Vector3(s * 0.13f, s * 0.44f, s * 0.028f));
            blade.AddCylinder(new Vector3(0f, -s * 0.46f, 0f), s * 0.055f, s * 0.16f, 10, Quaternion.Euler(0f, 0f, 0f));
            blade.AddSphere(new Vector3(0f, -s * 0.55f, 0f), s * 0.085f, 6, 10);
            Piece("Blade", pivot.transform, blade.ToMesh($"{def.Id}_Blade"), mats.BrightSteel);

            visual.MovingPart = pivot.transform;
            visual.Movement = MovementKind.RotateLocalAxis;
            visual.LocalAxis = Vector3.right;
            visual.RangeStart = -62f;   // 断开：刀片翘起
            visual.RangeEnd = 0f;       // 合闸：刀片压入刀夹
            visual.ResponseSpeed = 6.5f;
        }

        // ————————————————— 拨杆 —————————————————
        static void BuildToggleLever(in ControlDefinition def, Transform root, CabinMaterials mats, ControlVisual visual)
        {
            float s = def.Size;
            var b = new MeshBuilder();
            b.AddCylinder(new Vector3(0f, 0f, 0.010f), s * 0.52f, 0.020f, 14, Quaternion.Euler(90f, 0f, 0f));
            b.AddTorus(new Vector3(0f, 0f, 0.018f), s * 0.44f, s * 0.045f, 16, 6, Quaternion.Euler(90f, 0f, 0f));
            Piece("Bezel", root, b.ToMesh($"{def.Id}_Bezel"), mats.FrameSteel);

            var pivot = new GameObject("Pivot");
            pivot.transform.SetParent(root, false);
            pivot.transform.localPosition = new Vector3(0f, 0f, 0.020f);

            var lever = new MeshBuilder();
            lever.AddCylinder(new Vector3(0f, s * 0.42f, 0f), s * 0.075f, s * 0.84f, 10);
            lever.AddSphere(new Vector3(0f, s * 0.86f, 0f), s * 0.155f, 7, 11);
            Piece("Lever", pivot.transform, lever.ToMesh($"{def.Id}_Lever"), mats.Bakelite);

            bool threeWay = def.Detents >= 3;
            visual.MovingPart = pivot.transform;
            visual.Movement = MovementKind.RotateLocalAxis;
            visual.LocalAxis = Vector3.right;
            visual.RangeStart = threeWay ? -26f : -20f;
            visual.RangeEnd = threeWay ? 26f : 20f;
            visual.ResponseSpeed = 8f;
        }

        // ————————————————— 旋钮 —————————————————
        static void BuildRotaryKnob(in ControlDefinition def, Transform root, CabinMaterials mats, ControlVisual visual)
        {
            float s = def.Size;
            var b = new MeshBuilder();
            b.AddCylinder(new Vector3(0f, 0f, 0.006f), s * 0.62f, 0.012f, 18, Quaternion.Euler(90f, 0f, 0f));
            Piece("Plate", root, b.ToMesh($"{def.Id}_Plate"), mats.FrameSteel);

            var pivot = new GameObject("Pivot");
            pivot.transform.SetParent(root, false);
            pivot.transform.localPosition = new Vector3(0f, 0f, 0.012f);

            var knob = new MeshBuilder();
            knob.AddCylinder(new Vector3(0f, 0f, s * 0.17f), s * 0.46f, s * 0.34f, 20, Quaternion.Euler(90f, 0f, 0f), true, 0.82f);
            // 滚花：一圈小凸起，让旋转在视觉上可读。
            for (int i = 0; i < 12; i++)
            {
                float a = i / 12f * Mathf.PI * 2f;
                knob.AddBox(
                    new Vector3(Mathf.Cos(a) * s * 0.44f, Mathf.Sin(a) * s * 0.44f, s * 0.17f),
                    new Vector3(s * 0.055f, s * 0.055f, s * 0.30f),
                    Quaternion.Euler(0f, 0f, a * Mathf.Rad2Deg));
            }
            Piece("Knob", pivot.transform, knob.ToMesh($"{def.Id}_Knob"), mats.Bakelite);

            var mark = new MeshBuilder();
            mark.AddBox(new Vector3(0f, s * 0.26f, s * 0.345f), new Vector3(s * 0.05f, s * 0.24f, s * 0.02f));
            Piece("Index", pivot.transform, mark.ToMesh($"{def.Id}_Index"), mats.Brass, false);

            visual.MovingPart = pivot.transform;
            visual.Movement = MovementKind.RotateLocalAxis;
            visual.LocalAxis = Vector3.forward;
            visual.RangeStart = 138f;
            visual.RangeEnd = -138f;
            visual.ResponseSpeed = 7f;
        }

        // ————————————————— 阀轮 —————————————————
        // 全场最耗时的控件：从全关转到全开要转好几圈，
        // 这让「临时把风门开大」永远不是一个免费的决定。
        static void BuildValveWheel(in ControlDefinition def, Transform root, CabinMaterials mats, ControlVisual visual)
        {
            float s = def.Size;
            var b = new MeshBuilder();
            b.AddCylinder(new Vector3(0f, 0f, 0.012f), s * 0.20f, 0.024f, 14, Quaternion.Euler(90f, 0f, 0f));
            b.AddBox(new Vector3(0f, 0f, 0.006f), new Vector3(s * 0.66f, s * 0.30f, 0.012f));
            Piece("Boss", root, b.ToMesh($"{def.Id}_Boss"), mats.FrameSteel);

            var pivot = new GameObject("Pivot");
            pivot.transform.SetParent(root, false);
            pivot.transform.localPosition = new Vector3(0f, 0f, 0.030f);

            var wheel = new MeshBuilder();
            wheel.AddTorus(Vector3.zero, s * 0.46f, s * 0.058f, 22, 7, Quaternion.Euler(90f, 0f, 0f));
            wheel.AddCylinder(Vector3.zero, s * 0.12f, s * 0.11f, 12, Quaternion.Euler(90f, 0f, 0f));
            for (int i = 0; i < 3; i++)
            {
                float a = i / 3f * Mathf.PI * 2f;
                wheel.AddBox(
                    new Vector3(Mathf.Cos(a) * s * 0.24f, Mathf.Sin(a) * s * 0.24f, 0f),
                    new Vector3(s * 0.055f, s * 0.46f, s * 0.045f),
                    Quaternion.Euler(0f, 0f, a * Mathf.Rad2Deg + 90f));
            }
            Piece("Wheel", pivot.transform, wheel.ToMesh($"{def.Id}_Wheel"), mats.RedBakelite);

            visual.MovingPart = pivot.transform;
            visual.Movement = MovementKind.RotateLocalAxis;
            visual.LocalAxis = Vector3.forward;
            visual.RangeStart = 0f;
            visual.RangeEnd = -def.TurnsToFull * 360f;
            visual.ResponseSpeed = 2.2f;
        }

        // ————————————————— 按钮 —————————————————
        static void BuildPushButton(in ControlDefinition def, Transform root, CabinMaterials mats, ControlVisual visual)
        {
            float s = def.Size;
            var b = new MeshBuilder();
            b.AddCylinder(new Vector3(0f, 0f, 0.008f), s * 0.68f, 0.016f, 16, Quaternion.Euler(90f, 0f, 0f));
            b.AddTorus(new Vector3(0f, 0f, 0.016f), s * 0.60f, s * 0.075f, 18, 6, Quaternion.Euler(90f, 0f, 0f));
            Piece("Ring", root, b.ToMesh($"{def.Id}_Ring"), mats.FrameSteel);

            var capHolder = new GameObject("Cap");
            capHolder.transform.SetParent(root, false);
            capHolder.transform.localPosition = new Vector3(0f, 0f, 0.016f);

            var cap = new MeshBuilder();
            cap.AddCylinder(new Vector3(0f, 0f, s * 0.11f), s * 0.48f, s * 0.22f, 16, Quaternion.Euler(90f, 0f, 0f), true, 0.92f);
            Piece("CapMesh", capHolder.transform, cap.ToMesh($"{def.Id}_Cap"), mats.RedBakelite);

            visual.MovingPart = capHolder.transform;
            visual.Movement = MovementKind.TranslateLocalAxis;
            visual.LocalAxis = Vector3.forward;
            visual.RangeStart = 0f;
            visual.RangeEnd = -s * 0.13f;
            visual.ResponseSpeed = 22f;
        }

        // ————————————————— 断路器 —————————————————
        static void BuildBreaker(in ControlDefinition def, Transform root, CabinMaterials mats, ControlVisual visual)
        {
            float s = def.Size;
            var b = new MeshBuilder();
            b.AddBox(new Vector3(0f, 0f, s * 0.16f), new Vector3(s * 0.72f, s * 1.30f, s * 0.30f));
            b.AddBox(new Vector3(0f, 0f, s * 0.31f), new Vector3(s * 0.46f, s * 0.60f, s * 0.06f));
            Piece("Body", root, b.ToMesh($"{def.Id}_Body"), mats.Bakelite);

            var pivot = new GameObject("Pivot");
            pivot.transform.SetParent(root, false);
            pivot.transform.localPosition = new Vector3(0f, 0f, s * 0.31f);

            var toggle = new MeshBuilder();
            toggle.AddBox(new Vector3(0f, s * 0.16f, s * 0.05f), new Vector3(s * 0.24f, s * 0.38f, s * 0.14f));
            Piece("Toggle", pivot.transform, toggle.ToMesh($"{def.Id}_Toggle"), mats.BrightSteel);

            var lamp = new MeshBuilder();
            lamp.AddCylinder(new Vector3(0f, -s * 0.50f, s * 0.32f), s * 0.11f, s * 0.05f, 12, Quaternion.Euler(90f, 0f, 0f));
            var lampGo = Piece("Lamp", root, lamp.ToMesh($"{def.Id}_Lamp"), mats.LampOff, false);
            visual.LampRenderer = lampGo.GetComponent<Renderer>();

            visual.MovingPart = pivot.transform;
            visual.Movement = MovementKind.RotateLocalAxis;
            visual.LocalAxis = Vector3.right;
            visual.RangeStart = 26f;
            visual.RangeEnd = -26f;
            visual.ResponseSpeed = 12f;
        }

        // ————————————————— 调速手柄 —————————————————
        // 唯一需要精细控制的控件：沿弧形槽推拉，行程越长越好把握速度。
        static void BuildThrottleHandle(in ControlDefinition def, Transform root, CabinMaterials mats, ControlVisual visual)
        {
            float s = def.Size;
            var b = new MeshBuilder();
            b.AddBox(new Vector3(0f, 0f, 0.008f), new Vector3(s * 0.34f, s * 1.06f, 0.016f));
            // 弧形导槽由一串小块沿弧线拼出。
            for (int i = 0; i <= 14; i++)
            {
                float t = i / 14f;
                float a = Mathf.Lerp(-48f, 48f, t) * Mathf.Deg2Rad;
                float r = s * 0.46f;
                b.AddBox(
                    new Vector3(0f, Mathf.Cos(a) * r - r * 0.06f, Mathf.Sin(a) * r * 0.32f + 0.020f),
                    new Vector3(s * 0.26f, s * 0.07f, s * 0.05f),
                    Quaternion.Euler(a * Mathf.Rad2Deg * 0.4f, 0f, 0f));
            }
            b.AddCylinder(new Vector3(0f, -s * 0.48f, 0.030f), s * 0.10f, s * 0.14f, 12, Quaternion.Euler(90f, 0f, 0f));
            Piece("Track", root, b.ToMesh($"{def.Id}_Track"), mats.FrameSteel);

            var pivot = new GameObject("Pivot");
            pivot.transform.SetParent(root, false);
            pivot.transform.localPosition = new Vector3(0f, -s * 0.48f, 0.030f);

            var handle = new MeshBuilder();
            handle.AddCylinder(new Vector3(0f, s * 0.42f, 0f), s * 0.062f, s * 0.84f, 10);
            handle.AddCylinder(new Vector3(0f, s * 0.86f, 0f), s * 0.13f, s * 0.20f, 12, Quaternion.Euler(0f, 0f, 90f));
            handle.AddSphere(new Vector3(0f, s * 0.86f, 0f), s * 0.145f, 7, 11);
            Piece("Handle", pivot.transform, handle.ToMesh($"{def.Id}_Handle"), mats.Bakelite);

            visual.MovingPart = pivot.transform;
            visual.Movement = MovementKind.RotateLocalAxis;
            visual.LocalAxis = Vector3.right;
            visual.RangeStart = 44f;
            visual.RangeEnd = -44f;
            visual.ResponseSpeed = 5.5f;
        }

        // ————————————————— 表盘 —————————————————
        static void BuildGauge(in ControlDefinition def, Transform root, CabinMaterials mats, ControlVisual visual)
        {
            float s = def.Size;
            float r = s * 0.5f;

            var bezel = new MeshBuilder();
            bezel.AddCylinder(new Vector3(0f, 0f, 0.010f), r * 1.06f, 0.020f, 26, Quaternion.Euler(90f, 0f, 0f));
            bezel.AddTorus(new Vector3(0f, 0f, 0.022f), r * 0.99f, s * 0.045f, 24, 7, Quaternion.Euler(90f, 0f, 0f));
            Piece("Bezel", root, bezel.ToMesh($"{def.Id}_Bezel"), mats.Brass);

            var face = new MeshBuilder();
            face.AddDisc(new Vector3(0f, 0f, 0.022f), r * 0.94f, 30, Quaternion.Euler(90f, 0f, 0f), Vector3.forward, true);
            var faceGo = Piece("Face", root, face.ToMesh($"{def.Id}_Face"), mats.GaugeFace, false);

            var faceMaterial = new Material(mats.GaugeFace) { name = $"M_Face_{def.Id}" };
            // URP Lit 的主贴图属性是 _BaseMap，不是内置管线的 _MainTex，
            // 走 Material.mainTexture 会静默失效，表盘会变成一片空白。
            // 表盘是背光式的：材质走 Unlit，贴图直接决定亮度。
            // 这些发亮的表面是整个画面里少数几处明确亮点，概念图的影调正靠它们撑起来。
            var faceTexture = GaugeTextureFactory.Create(def.Id);
            faceMaterial.SetTexture("_BaseMap", faceTexture);
            CabinMaterials.EnsureUnitTiling(faceMaterial);
            faceMaterial.SetColor("_BaseColor", new Color(0.60f, 0.52f, 0.40f));
            if (faceMaterial.HasProperty("_Color"))
            {
                faceMaterial.SetColor("_Color", new Color(0.60f, 0.52f, 0.40f));
            }
            faceGo.GetComponent<Renderer>().sharedMaterial = faceMaterial;


            var pivot = new GameObject("Needle");
            pivot.transform.SetParent(root, false);
            pivot.transform.localPosition = new Vector3(0f, 0f, 0.030f);

            var needle = new MeshBuilder();
            needle.AddBox(new Vector3(0f, r * 0.34f, 0f), new Vector3(s * 0.028f, r * 0.78f, s * 0.014f));
            needle.AddBox(new Vector3(0f, -r * 0.10f, 0f), new Vector3(s * 0.045f, r * 0.22f, s * 0.014f));
            needle.AddCylinder(Vector3.zero, s * 0.045f, s * 0.03f, 10, Quaternion.Euler(90f, 0f, 0f));
            Piece("NeedleMesh", pivot.transform, needle.ToMesh($"{def.Id}_Needle"), mats.Needle, false);

            // 这里原本有一片玻璃罩。用代码把 URP Lit 切成透明模式需要同时改
            // _Surface、混合因子、ZWrite、renderQueue 与关键字，任何一处不到位都会
            // 静默退化成不透明——实测就是那片玻璃把整个表盘面严实地盖住了，
            // 表现为「表盘只有一圈框和一根针，刻度全没了」。
            // 玻璃对可读性没有贡献，直接去掉；需要反光时改用外框高光表达。

            visual.MovingPart = pivot.transform;
            visual.Movement = MovementKind.RotateLocalAxis;
            visual.LocalAxis = Vector3.forward;
            visual.RangeStart = 132f;
            visual.RangeEnd = -132f;
            visual.ResponseSpeed = 3.5f;
        }
    }
}
