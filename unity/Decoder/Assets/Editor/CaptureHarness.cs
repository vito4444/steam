using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Decoder.Capture;
using Decoder.Rendering;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Decoder.EditorTools
{
    /// <summary>
    /// 无头截图工具。在 Xvfb + Mesa 软件渲染下由命令行驱动，
    /// 把指定场景的指定机位渲染成 PNG，供画面自检与目标参考图比对使用。
    ///
    /// 用法:
    ///   Unity -batchmode -projectPath ... -executeMethod Decoder.EditorTools.CaptureHarness.Capture \
    ///         -captureScene Assets/Scenes/Probe.unity -captureOutput artifacts/screenshots \
    ///         -captureWidth 1920 -captureHeight 1080 -captureShots probe
    ///
    /// 注意: 必须在有 X display 的环境下运行（不能加 -nographics），否则无法建立渲染上下文。
    /// </summary>
    public static class CaptureHarness
    {
        public static void Capture()
        {
            try
            {
                var scenePath = GetArg("-captureScene");
                var outputDir = GetArg("-captureOutput") ?? "artifacts/screenshots";
                var width = GetIntArg("-captureWidth", 1920);
                var height = GetIntArg("-captureHeight", 1080);
                var shotFilter = GetArg("-captureShots");

                Directory.CreateDirectory(outputDir);

                if (!string.IsNullOrEmpty(scenePath))
                {
                    Log($"打开场景 {scenePath}");
                    EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                }

                var shots = CollectShots(shotFilter);
                if (shots.Count == 0)
                {
                    Fail("场景中没有找到 CaptureShot 组件，也没有可用的主摄像机。");
                    return;
                }

                foreach (var shot in shots)
                {
                    var path = Path.Combine(outputDir, $"{shot.ShotName}.png");
                    RenderToFile(shot, width, height, path);
                    Log($"CAPTURED {path}");
                }

                Log("CAPTURE_SUCCESS");
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Fail($"截图失败: {e}");
            }
        }

        private static List<ShotRequest> CollectShots(string filter)
        {
            var result = new List<ShotRequest>();
            var wanted = string.IsNullOrEmpty(filter)
                ? null
                : new HashSet<string>(filter.Split(',').Select(s => s.Trim()));

            foreach (var marker in UnityEngine.Object.FindObjectsByType<CaptureShot>(FindObjectsSortMode.InstanceID))
            {
                if (wanted != null && !wanted.Contains(marker.shotName))
                {
                    continue;
                }

                var cam = marker.GetComponent<Camera>() ?? Camera.main;
                if (cam == null)
                {
                    continue;
                }

                result.Add(new ShotRequest(marker.shotName, cam, marker.transform));
            }

            if (result.Count == 0 && Camera.main != null)
            {
                result.Add(new ShotRequest("main", Camera.main, Camera.main.transform));
            }

            return result.OrderBy(s => s.ShotName, StringComparer.Ordinal).ToList();
        }

        private static void RenderToFile(ShotRequest shot, int width, int height, string path)
        {
            var camera = shot.Camera;
            var previousTarget = camera.targetTexture;
            var previousActive = RenderTexture.active;

            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.sRGB)
            {
                antiAliasing = 1,
            };

            var readback = new Texture2D(width, height, TextureFormat.RGB24, false, false);

            try
            {
                camera.targetTexture = rt;
                camera.Render();

                // 编辑模式下 OnRenderImage 不会被调用，后处理得手动走一遍，
                // 否则画面自检看到的成像和玩家实际看到的不是一回事。
                var post = camera.GetComponent<StationPostProcess>();
                if (post != null && post.enabled)
                {
                    // 着色器不能进构建包，所以只能在编辑器侧从资源目录直接取。
                    post.editorShader ??= AssetDatabase.LoadAssetAtPath<Shader>(
                        "Assets/Shaders/StationPost.shader");
                    var temp = RenderTexture.GetTemporary(width, height, 0,
                        RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                    post.Apply(rt, temp);
                    Graphics.Blit(temp, rt);
                    RenderTexture.ReleaseTemporary(temp);
                }

                RenderTexture.active = rt;
                readback.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                readback.Apply(false);

                File.WriteAllBytes(path, readback.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                UnityEngine.Object.DestroyImmediate(readback);
                rt.Release();
                UnityEngine.Object.DestroyImmediate(rt);
            }
        }

        private static string GetArg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (var i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == name)
                {
                    return args[i + 1];
                }
            }

            return null;
        }

        private static int GetIntArg(string name, int fallback)
        {
            var raw = GetArg(name);
            return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
                ? value
                : fallback;
        }

        private static void Log(string message)
        {
            Debug.Log($"[CaptureHarness] {message}");
            Console.WriteLine($"[CaptureHarness] {message}");
        }

        private static void Fail(string message)
        {
            Debug.LogError($"[CaptureHarness] {message}");
            Console.Error.WriteLine($"[CaptureHarness] {message}");
            EditorApplication.Exit(1);
        }

        private readonly struct ShotRequest
        {
            public ShotRequest(string shotName, Camera camera, Transform anchor)
            {
                ShotName = shotName;
                Camera = camera;
                Anchor = anchor;
            }

            public string ShotName { get; }
            public Camera Camera { get; }
            public Transform Anchor { get; }
        }
    }
}
