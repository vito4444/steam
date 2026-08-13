using UnityEngine;
using Worker.Core;

namespace Worker.Game
{
    /// <summary>
    /// Keyboard controls and an on-screen readout for development builds.
    ///
    /// This is scaffolding, not the shipping UI: it exists so the game is inspectable
    /// the moment it runs, before any real interface has been built. It is compiled out
    /// of release builds.
    /// </summary>
    [RequireComponent(typeof(SimRunner))]
    public sealed class DebugControls : MonoBehaviour
    {
        private SimRunner _runner;
        private GUIStyle _style;

        /// <summary>
        /// Set by --hud on the command line. The readout is not gated on a development
        /// build, because the self-test captures release players: a development build
        /// shows Unity's own console over the game on the first warning, and a machine
        /// with no audio device warns during engine startup.
        /// </summary>
        private bool _showHud;

        private void Awake()
        {
            _runner = GetComponent<SimRunner>();

            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--hud") _showHud = true;
            }
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Space))
            {
                _runner.SpeedMultiplier = _runner.SpeedMultiplier > 0 ? 0 : 1;
            }

            if (Input.GetKeyDown(KeyCode.Alpha1)) _runner.SpeedMultiplier = 1;
            if (Input.GetKeyDown(KeyCode.Alpha2)) _runner.SpeedMultiplier = 3;
            if (Input.GetKeyDown(KeyCode.Alpha3)) _runner.SpeedMultiplier = 8;

            if (Input.GetKeyDown(KeyCode.R))
            {
                _runner.CreateWorld();
            }

            if (Input.GetKeyDown(KeyCode.T))
            {
                _runner.StartAutomated = !_runner.StartAutomated;
                _runner.CreateWorld();
            }
        }

        private void OnGUI()
        {
            if (!_showHud) return;

            var world = _runner.World;
            if (world == null) return;

            _style ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 15,
                normal = { textColor = Palette.TextPrimary.ToUnity() }
            };

            const float width = 340f;
            GUILayout.BeginArea(new Rect(12f, 12f, width, 260f));

            GUILayout.Label("day " + (world.Tick / SimConfig.TicksPerDay + 1)
                            + "   tick " + world.Tick
                            + "   speed " + _runner.SpeedMultiplier + "x", _style);
            GUILayout.Label("cash    $" + (world.Ledger.Balance / 100), _style);
            GUILayout.Label("staff   " + world.Workers.Count, _style);
            GUILayout.Label("shipped " + world.TotalUnitsShipped
                            + "   crafts " + world.TotalCraftsCompleted, _style);

            int idle = 0;
            for (int i = 0; i < world.Workers.Count; i++)
            {
                if (world.Workers[i].IsIdle) idle++;
            }
            GUILayout.Label("idle    " + idle + "/" + world.Workers.Count, _style);

            GUILayout.Space(8f);
            GUILayout.Label("space pause   1/2/3 speed   r restart   t layout", _style);

            GUILayout.EndArea();
        }
    }
}
