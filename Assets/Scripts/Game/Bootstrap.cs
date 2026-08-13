using UnityEngine;

namespace Worker.Game
{
    /// <summary>
    /// Builds the entire runtime scene from code.
    ///
    /// Nothing has to be wired up in a .unity file: the scene can be completely empty
    /// and the game still starts. That is worth the small amount of setup code, because
    /// a scene asset is an opaque YAML blob that cannot be reviewed in a diff, cannot be
    /// merged sensibly, and silently breaks when a component is renamed. Everything the
    /// game needs to exist is therefore declared here, in source.
    /// </summary>
    public static class Bootstrap
    {
        private const string RootName = "[worker]";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        public static void Launch()
        {
            if (GameObject.Find(RootName) != null) return;

            var root = new GameObject(RootName);
            Object.DontDestroyOnLoad(root);

            var runner = root.AddComponent<SimRunner>();
            runner.Seed = 1;
            runner.StartingWorkers = 4;
            runner.StartAutomated = false;

            root.AddComponent<FactoryView>();

            var camera = EnsureCamera();
            var rig = camera.GetComponent<CameraRig>();
            if (rig == null) rig = camera.gameObject.AddComponent<CameraRig>();
            rig.Bind(runner);

            root.AddComponent<DebugControls>();
        }

        private static Camera EnsureCamera()
        {
            var existing = Camera.main;
            if (existing != null) return existing;

            var holder = new GameObject("Main Camera");
            holder.tag = "MainCamera";
            var camera = holder.AddComponent<Camera>();
            camera.orthographic = true;
            return camera;
        }
    }
}
