// 无 GPU 环境下的 Unity 能力探针。
//
// 放到 Unity 工程的 Assets/Editor/ 下，用 -executeMethod 从命令行调用。
// 这套脚本用于验证一台新机器是否具备项目 CODER 需要的三项能力：
// 程序化建场景、软件渲染截图、Windows 交叉编译。
//
// 截图（注意：不能加 -nographics，否则 Camera.Render 得到全黑）：
//   export LIBGL_ALWAYS_SOFTWARE=1 GALLIUM_DRIVER=llvmpipe
//   xvfb-run -a -s "-screen 0 1920x1080x24" $UNITY \
//     -batchmode -quit -projectPath <项目> \
//     -executeMethod ProbeBuild.Capture -logFile <日志>
//
// 构建（可以加 -nographics 加速）：
//   xvfb-run -a -s "-screen 0 1920x1080x24" $UNITY \
//     -batchmode -nographics -quit -projectPath <项目> \
//     -executeMethod ProbeBuild.BuildWindows -logFile <日志>

using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class ProbeBuild
{
    const string SceneDir = "Assets/Scenes";
    const string ScenePath = SceneDir + "/Probe.unity";

    static string OutRoot => System.Environment.GetEnvironmentVariable("PROBE_OUT") ?? "/tmp/probe";

    [MenuItem("Probe/Make Scene")]
    public static void MakeScene()
    {
        Directory.CreateDirectory(SceneDir);
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var camGo = new GameObject("MainCamera") { tag = "MainCamera" };
        var cam = camGo.AddComponent<Camera>();
        cam.transform.SetPositionAndRotation(new Vector3(4.5f, 3.2f, -6.0f), Quaternion.Euler(22f, -32f, 0f));
        cam.fieldOfView = 35f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.02f, 0.03f, 0.05f);

        var light = new GameObject("KeyLight").AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.35f;
        light.color = new Color(1.0f, 0.94f, 0.85f);
        light.transform.rotation = Quaternion.Euler(48f, -34f, 0f);

        var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "Floor";
        floor.transform.localScale = Vector3.one * 2f;

        for (int x = 0; x < 5; x++)
        {
            for (int z = 0; z < 5; z++)
            {
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.name = $"Node_{x}_{z}";
                float h = 0.3f + Mathf.PerlinNoise(x * 0.6f, z * 0.6f) * 1.6f;
                cube.transform.position = new Vector3(x * 1.4f, h * 0.5f, z * 1.4f);
                cube.transform.localScale = new Vector3(0.9f, h, 0.9f);

                var mat = new Material(Shader.Find("Standard"));
                float t = (x + z) / 8f;
                mat.color = Color.Lerp(new Color(0.05f, 0.08f, 0.10f), new Color(0.08f, 0.05f, 0.03f), t);
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", Color.Lerp(
                    new Color(0.0f, 0.75f, 0.95f) * 2.2f,
                    new Color(1.0f, 0.42f, 0.08f) * 2.2f, t));
                cube.GetComponent<Renderer>().sharedMaterial = mat;
            }
        }

        EditorSceneManager.SaveScene(scene, ScenePath);
        Debug.Log($"PROBE: scene saved to {ScenePath}");
    }

    [MenuItem("Probe/Capture")]
    public static void Capture()
    {
        MakeScene();
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        var cam = Object.FindFirstObjectByType<Camera>();
        if (cam == null)
        {
            Debug.LogError("PROBE: no camera in scene");
            EditorApplication.Exit(2);
            return;
        }

        const int w = 1280, h = 720;
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 2 };
        cam.targetTexture = rt;
        cam.Render();

        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        tex.Apply();
        RenderTexture.active = null;
        cam.targetTexture = null;

        var outDir = Path.Combine(OutRoot, "out");
        Directory.CreateDirectory(outDir);
        var outPath = Path.Combine(outDir, "editor-render.png");
        File.WriteAllBytes(outPath, tex.EncodeToPNG());
        Debug.Log($"PROBE: wrote {outPath} ({new FileInfo(outPath).Length} bytes)");
    }

    [MenuItem("Probe/Build Windows")]
    public static void BuildWindows() =>
        Build(BuildTarget.StandaloneWindows64, Path.Combine(OutRoot, "build/win64/TechProbe.exe"));

    [MenuItem("Probe/Build Linux")]
    public static void BuildLinux() =>
        Build(BuildTarget.StandaloneLinux64, Path.Combine(OutRoot, "build/linux64/TechProbe.x86_64"));

    static void Build(BuildTarget target, string location)
    {
        MakeScene();
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = location,
            target = target,
            targetGroup = BuildTargetGroup.Standalone,
            options = BuildOptions.None,
        });

        var s = report.summary;
        Debug.Log($"PROBE: {target} build result={s.result} size={s.totalSize} errors={s.totalErrors}");
        if (s.result != BuildResult.Succeeded)
            EditorApplication.Exit(3);
    }
}
