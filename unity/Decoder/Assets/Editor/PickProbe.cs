using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Decoder.EditorTools
{
    /// <summary>
    /// 从截图上的像素位置反查是哪个物体。
    ///
    /// 场景全是代码生成的，几百个物体没有可视化编辑器可点。看到画面上有个穿帮的形状时，
    /// 靠坐标心算去猜是哪一句 AddCylinder 又慢又容易猜错，直接投射一条射线问引擎最快。
    /// 用 -pickUv u,v 传归一化屏幕坐标（左下角为原点）。
    /// </summary>
    public static class PickProbe
    {
        public static void Pick()
        {
            var args = System.Environment.GetCommandLineArgs();
            var spots = "0.5,0.5";
            var scene = "Assets/Scenes/Station.unity";
            var cameraName = "probe_front";

            for (var i = 0; i < args.Length - 1; i++)
            {
                switch (args[i])
                {
                    case "-pickUv":
                        spots = args[i + 1];
                        break;
                    case "-pickScene":
                        scene = args[i + 1];
                        break;
                    case "-pickCamera":
                        cameraName = args[i + 1];
                        break;
                }
            }

            EditorSceneManager.OpenScene(scene, OpenSceneMode.Single);

            var camera = Object.FindObjectsByType<Camera>(FindObjectsSortMode.None)
                .FirstOrDefault(c => c.name == cameraName);
            if (camera == null)
            {
                Debug.LogError($"[PickProbe] 找不到相机 {cameraName}");
                EditorApplication.Exit(1);
                return;
            }

            // 场景里的物体没有 Collider，射线打不到。临时给所有 MeshFilter 挂上
            // MeshCollider，问完就整场景丢弃，不落盘。
            var meshes = Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None);
            foreach (var mesh in meshes)
            {
                if (mesh.sharedMesh != null && mesh.GetComponent<Collider>() == null)
                {
                    mesh.gameObject.AddComponent<MeshCollider>();
                }
            }

            Physics.SyncTransforms();

            foreach (var spot in spots.Split(';'))
            {
                var parts = spot.Split(',');
                var uv = new Vector2(float.Parse(parts[0]), float.Parse(parts[1]));
                var ray = camera.ViewportPointToRay(new Vector3(uv.x, uv.y, 0f));
                var hits = Physics.RaycastAll(ray, 40f).OrderBy(h => h.distance).ToArray();

                Debug.Log($"[PickProbe] 相机 {cameraName} 位于 {camera.transform.position}，视口 {uv}，命中 {hits.Length} 个");
                foreach (var hit in hits.Take(3))
                {
                    var path = hit.collider.name;
                    var parent = hit.collider.transform.parent;
                    while (parent != null)
                    {
                        path = parent.name + "/" + path;
                        parent = parent.parent;
                    }

                    Debug.Log($"[PickProbe]   {hit.distance:F2}m  {path}  世界坐标 {hit.point}");
                }
            }

            EditorApplication.Exit(0);
        }

        /// <summary>
        /// 打印所有灯与几个关键物体的世界坐标。各个 Build 方法都用局部坐标写死数值，
        /// 而它们的根节点带着自己的变换，光靠读代码算出来的世界坐标是错的。
        /// </summary>
        public static void DumpLights()
        {
            var args = System.Environment.GetCommandLineArgs();
            var scene = "Assets/Scenes/Station.unity";
            for (var i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-pickScene")
                {
                    scene = args[i + 1];
                }
            }

            EditorSceneManager.OpenScene(scene, OpenSceneMode.Single);

            foreach (var light in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                var t = light.transform;
                Debug.Log($"[Dump] 灯 {light.name} {light.type} 位于 {t.position} 朝 {t.forward} " +
                          $"强度 {light.intensity} 范围 {light.range} 锥角 {light.spotAngle}");
            }

            foreach (var name in new[] { "DeskTop", "LampShade", "LampBulbGlow", "CrtScreen", "Panel_0" })
            {
                var go = GameObject.Find(name);
                if (go != null)
                {
                    Debug.Log($"[Dump] 物体 {name} 位于 {go.transform.position} 尺寸 {go.transform.lossyScale}");
                }
            }

            EditorApplication.Exit(0);
        }
    }
}
