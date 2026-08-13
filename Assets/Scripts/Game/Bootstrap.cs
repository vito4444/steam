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

        /// <summary>
        /// Runs before anything else can log. The development console pops open over the
        /// game on the first warning, and a machine with no audio device produces an FMOD
        /// warning during engine startup, which would sit across every screenshot.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSplashScreen)]
        public static void ConfigureRuntime()
        {
            // Without this the player pauses whenever the window loses focus, which under
            // a headless X server it never gains in the first place: the simulation would
            // sit at tick zero forever and every self-test screenshot would be identical.
            Application.runInBackground = true;
            Debug.developerConsoleEnabled = false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        public static void Launch()
        {
            if (GameObject.Find(RootName) != null) return;

            var root = new GameObject(RootName);

            // Components must be configured before their Awake runs, and AddComponent on
            // an active object calls Awake immediately. Building the object graph while
            // inactive is what makes the command line arguments below take effect at all.
            root.SetActive(false);

            var runner = root.AddComponent<SimRunner>();
            runner.Seed = ReadUInt("--seed", 1);
            runner.StartingWorkers = ReadInt("--workers", 4);
            runner.StartAutomated = HasFlag("--automated");
            runner.SpeedMultiplier = ReadInt("--speed", 1);

            root.AddComponent<FactoryView>();
            root.AddComponent<PlayerController>();
            root.AddComponent<SelectionOverlay>();
            root.AddComponent<DebugControls>();
            root.AddComponent<HeadlessCapture>();

            // The HUD is skipped for capture runs that ask for the plain debug readout,
            // so screenshots can show the world without any interface over it.
            if (!HasFlag("--no-hud")) root.AddComponent<GameHud>();

            root.SetActive(true);
            Object.DontDestroyOnLoad(root);

            var camera = EnsureCamera();
            var rig = camera.GetComponent<CameraRig>();
            if (rig == null) rig = camera.gameObject.AddComponent<CameraRig>();
            rig.Bind(runner);
        }

        private static bool HasFlag(string name)
        {
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == name) return true;
            }
            return false;
        }

        private static string ReadValue(string name)
        {
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == name) return args[i + 1];
            }
            return null;
        }

        private static int ReadInt(string name, int fallback)
            => int.TryParse(ReadValue(name), out int value) ? value : fallback;

        private static uint ReadUInt(string name, uint fallback)
            => uint.TryParse(ReadValue(name), out uint value) ? value : fallback;

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
