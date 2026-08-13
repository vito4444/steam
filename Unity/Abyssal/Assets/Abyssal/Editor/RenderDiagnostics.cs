using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Abyssal.EditorTools
{
    /// <summary>
    /// 无 GPU 环境下渲染链路的诊断。
    ///
    /// 软件渲染出全黑图的原因可能有很多层：图形设备没起来、渲染管线没挂上、
    /// 着色器没找到、场景里没有可见对象、相机朝向错了。
    /// 这个工具把每一层都单独打出来，避免靠猜。
    /// </summary>
    public static class RenderDiagnostics
    {
        [MenuItem("Abyssal/Diagnose Rendering")]
        public static void Run()
        {
            Log("--- 图形设备 ---");
            Log($"graphicsDeviceType   = {SystemInfo.graphicsDeviceType}");
            Log($"graphicsDeviceName   = {SystemInfo.graphicsDeviceName}");
            Log($"graphicsShaderLevel  = {SystemInfo.graphicsShaderLevel}");
            Log($"supportsComputeShader= {SystemInfo.supportsComputeShaders}");
            Log($"maxTextureSize       = {SystemInfo.maxTextureSize}");
            Log($"renderTexture RGBAHalf = {SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf)}");

            Log("--- 渲染管线 ---");
            Log($"defaultRenderPipeline = {NameOf(GraphicsSettings.defaultRenderPipeline)}");
            Log($"currentRenderPipeline = {NameOf(GraphicsSettings.currentRenderPipeline)}");
            Log($"QualitySettings.renderPipeline = {NameOf(QualitySettings.renderPipeline)}");
            Log($"RenderPipelineManager.currentPipeline = {RenderPipelineManager.currentPipeline?.GetType().Name ?? "null"}");

            Log("--- 着色器 ---");
            foreach (var name in new[]
                     {
                         "Universal Render Pipeline/Lit",
                         "Universal Render Pipeline/Unlit",
                         "Standard",
                     })
            {
                var s = Shader.Find(name);
                Log($"{name,-40} = {(s == null ? "缺失" : $"存在, supported={s.isSupported}")}");
            }

            Log("--- 场景 ---");
            var renderers = Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None);
            Log($"MeshRenderer 数量 = {renderers.Length}");
            Log($"Light 数量        = {Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Length}");

            int nullMat = renderers.Count(r => r.sharedMaterial == null);
            int badShader = renderers.Count(r => r.sharedMaterial != null &&
                                                 (r.sharedMaterial.shader == null ||
                                                  !r.sharedMaterial.shader.isSupported));
            Log($"材质为空的 renderer = {nullMat}");
            Log($"着色器不受支持的 renderer = {badShader}");

            var cam = Object.FindFirstObjectByType<Camera>();
            if (cam == null)
            {
                Log("场景里没有相机");
                return;
            }

            Log($"相机位置 = {cam.transform.position}, 朝向 = {cam.transform.eulerAngles}");
            Log($"FOV = {cam.fieldOfView}, near = {cam.nearClipPlane}, far = {cam.farClipPlane}");
            Log($"clearFlags = {cam.clearFlags}, background = {cam.backgroundColor}");
            Log($"cullingMask = {cam.cullingMask}, allowHDR = {cam.allowHDR}");

            // 数一数相机视锥里有多少个 renderer，排除「相机对着空气」这种可能。
            var planes = GeometryUtility.CalculateFrustumPlanes(cam);
            int visible = renderers.Count(r => GeometryUtility.TestPlanesAABB(planes, r.bounds));
            Log($"视锥内的 renderer = {visible}");

            Log("--- 逐档渲染测试 ---");
            TestRender(cam, "A-当前设置", null);
            TestRender(cam, "B-纯色背景无后处理", c =>
            {
                c.clearFlags = CameraClearFlags.SolidColor;
                c.backgroundColor = new Color(0.4f, 0.1f, 0.1f);
                var data = c.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
                if (data != null) data.renderPostProcessing = false;
            });
            TestRender(cam, "C-非HDR目标", null, RenderTextureFormat.ARGB32);
        }

        static void TestRender(Camera cam, string label, System.Action<Camera> tweak,
                               RenderTextureFormat format = RenderTextureFormat.DefaultHDR)
        {
            tweak?.Invoke(cam);

            const int w = 320, h = 180;
            var rt = new RenderTexture(w, h, 24, format);
            cam.targetTexture = rt;
            cam.Render();

            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            cam.targetTexture = null;

            var px = tex.GetPixels32();
            long sum = 0;
            int maxV = 0, nonBlack = 0;
            foreach (var p in px)
            {
                int v = p.r + p.g + p.b;
                sum += v;
                if (v > maxV) maxV = v;
                if (v > 6) nonBlack++;
            }

            Log($"{label,-24} 平均亮度={sum / (double)px.Length / 3.0:F2} " +
                $"最亮={maxV / 3.0:F0} 非黑像素={nonBlack * 100.0 / px.Length:F1}%");

            Object.DestroyImmediate(tex);
            rt.Release();
            Object.DestroyImmediate(rt);
        }

        /// <summary>
        /// 检查具体某几个物体的材质和网格。
        /// 用于定位「有光照但贴图不显示」这类只看截图判断不了的问题。
        /// </summary>
        [MenuItem("Abyssal/Diagnose Materials")]
        public static void DiagnoseMaterials()
        {
            foreach (var target in new[] { "Face", "Bezel", "Glass", "Housing" })
            {
                var go = Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None)
                    .FirstOrDefault(r => r.gameObject.name == target);
                if (go == null) { Log($"找不到名为 {target} 的 renderer"); continue; }
                DumpRenderer(target, go);
            }

            // 面板本体：父物体叫 SlantPanel，其下的 Face。
            var panel = Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None)
                .FirstOrDefault(r => r.transform.parent != null &&
                                     r.transform.parent.name == "SlantPanel" &&
                                     r.gameObject.name == "Face");
            if (panel != null) DumpRenderer("SlantPanel/Face", panel);
        }

        static void DumpRenderer(string label, MeshRenderer r)
        {
            var m = r.sharedMaterial;
            Log($"=== {label} ===");
            if (m == null) { Log("  材质为空"); return; }

            Log($"  material   = {m.name}");
            Log($"  shader     = {m.shader.name} (supported={m.shader.isSupported})");
            Log($"  _BaseColor = {(m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor").ToString() : "无此属性")}");

            foreach (var prop in new[] { "_BaseMap", "_MainTex", "_EmissionMap" })
            {
                if (!m.HasProperty(prop)) { Log($"  {prop,-12} = 无此属性"); continue; }
                var t = m.GetTexture(prop) as Texture2D;
                if (t == null) { Log($"  {prop,-12} = null"); continue; }

                string sample = "不可读";
                try
                {
                    var c = t.GetPixel(t.width / 2, t.height / 4);
                    sample = $"({c.r:F2},{c.g:F2},{c.b:F2},{c.a:F2})";
                }
                catch { /* 贴图不可读时跳过 */ }

                Log($"  {prop,-12} = {t.name} {t.width}x{t.height} {t.format} " +
                    $"mips={t.mipmapCount} 采样={sample}");
            }

            if (m.HasProperty("_BaseMap_ST"))
                Log($"  _BaseMap_ST = {m.GetVector("_BaseMap_ST")}");
            Log($"  keywords   = [{string.Join(", ", m.shaderKeywords)}]");

            var mf = r.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null)
            {
                var mesh = mf.sharedMesh;
                var uv = mesh.uv;
                Log($"  mesh       = {mesh.name} verts={mesh.vertexCount} tris={mesh.triangles.Length / 3} uvCount={uv.Length}");
                if (uv.Length >= 3)
                    Log($"  uv[0..2]   = {uv[0]}, {uv[1]}, {uv[2]}");
            }
        }

        static string NameOf(Object o) => o == null ? "null" : $"{o.name} ({o.GetType().Name})";

        static void Log(string message) => Debug.Log($"DIAG| {message}");
    }
}
