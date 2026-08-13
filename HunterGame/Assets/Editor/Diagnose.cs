using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace Hunter.EditorTools
{
    /// Prints scene facts to the batch log. Faster than guessing when a render looks wrong.
    public static class Diagnose
    {
        [MenuItem("Hunter/Diagnose Scene")]
        public static void Run()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/AurumMist.unity", OpenSceneMode.Single);

            foreach (var mf in Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None)
                         .OrderBy(m => m.name))
            {
                var mesh = mf.sharedMesh;
                var mr = mf.GetComponent<MeshRenderer>();
                if (mesh == null) continue;

                var normals = mesh.normals;
                var avgNormal = Vector3.zero;
                int sampleCount = Mathf.Min(normals.Length, 500);
                for (int i = 0; i < sampleCount; i++) avgNormal += normals[i];
                if (sampleCount > 0) avgNormal /= sampleCount;

                Debug.Log($"DIAG mesh={mf.name} verts={mesh.vertexCount} tris={mesh.triangles.Length / 3} " +
                          $"bounds={mesh.bounds.size} avgNormal={avgNormal} " +
                          $"mat={(mr != null && mr.sharedMaterial != null ? mr.sharedMaterial.name : "NONE")} " +
                          $"shader={(mr != null && mr.sharedMaterial != null ? mr.sharedMaterial.shader.name : "NONE")} " +
                          $"enabled={(mr != null && mr.enabled)}");
            }

            foreach (var light in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                Debug.Log($"DIAG light={light.name} type={light.type} intensity={light.intensity} " +
                          $"range={light.range} shadows={light.shadows} color={light.color} " +
                          $"pos={light.transform.position} fwd={light.transform.forward}");
            }

            foreach (var mat in new[] { "Ground", "Stone" })
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>($"Assets/Materials/{mat}.mat");
                if (m == null) continue;
                Debug.Log($"DIAG mat={mat} baseColor={m.GetColor("_BaseColor")} " +
                          $"hasBaseMap={m.GetTexture("_BaseMap") != null} " +
                          $"tiling={m.GetTextureScale("_BaseMap")} " +
                          $"smoothness={m.GetFloat("_Smoothness")}");
            }

            Debug.Log($"DIAG ambient sky={RenderSettings.ambientSkyColor} " +
                      $"eq={RenderSettings.ambientEquatorColor} ground={RenderSettings.ambientGroundColor} " +
                      $"mode={RenderSettings.ambientMode} intensity={RenderSettings.ambientIntensity} " +
                      $"reflIntensity={RenderSettings.reflectionIntensity}");

            var probe = RenderSettings.ambientProbe;
            Debug.Log($"DIAG probe L0=({probe[0, 0]:F4},{probe[1, 0]:F4},{probe[2, 0]:F4}) " +
                      $"L1y=({probe[0, 1]:F4},{probe[1, 1]:F4},{probe[2, 1]:F4})");

            Debug.Log($"DIAG colorSpace={PlayerSettings.colorSpace} " +
                      $"sun={(RenderSettings.sun != null ? RenderSettings.sun.name : "NONE")}");

            var groundAlbedo = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Textures/GroundAlbedo.asset");
            if (groundAlbedo != null)
            {
                var px = groundAlbedo.GetPixels(0, 0, 64, 64);
                float mean = px.Average(p => (p.r + p.g + p.b) / 3f);
                float max = px.Max(p => (p.r + p.g + p.b) / 3f);
                Debug.Log($"DIAG groundAlbedo mean={mean:F4} max={max:F4} " +
                          $"format={groundAlbedo.format} sRGB={GraphicsFormatUtility.IsSRGBFormat(groundAlbedo.graphicsFormat)}");
            }

            var cam = Object.FindFirstObjectByType<Camera>();
            if (cam != null)
            {
                var extra = cam.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
                Debug.Log($"DIAG camera post={(extra != null && extra.renderPostProcessing)} " +
                          $"aa={(extra != null ? extra.antialiasing.ToString() : "n/a")} " +
                          $"volumeMask={(extra != null ? extra.volumeLayerMask.value : -1)} " +
                          $"volumeTrigger={(extra != null && extra.volumeTrigger != null ? extra.volumeTrigger.name : "null")} " +
                          $"hdr={cam.allowHDR}");
            }

            foreach (var volume in Object.FindObjectsByType<UnityEngine.Rendering.Volume>(FindObjectsSortMode.None))
            {
                var profile = volume.sharedProfile;
                Debug.Log($"DIAG volume={volume.name} global={volume.isGlobal} weight={volume.weight} " +
                          $"priority={volume.priority} layer={volume.gameObject.layer} " +
                          $"profile={(profile != null ? profile.name : "NULL")} " +
                          $"components={(profile != null ? profile.components.Count : 0)}");
            }

            Debug.Log("DIAG_DONE");
        }
    }
}
