using UnityEngine;

namespace Monster.Interaction
{
    /// <summary>Everything the booth needs from the player, as data.
    ///
    /// Input goes through an interface for two reasons that both matter now rather than
    /// later. The automated self-check has to drive the game with no keyboard attached, so
    /// there must be a source that replays a fixed sequence. And the production input
    /// stack will be Unity's Input System with rebindable controls, so keeping the call
    /// sites free of any input API means that migration touches one file.</summary>
    public interface IInputSource
    {
        /// <summary>Look delta in degrees for this frame.</summary>
        Vector2 LookDelta { get; }

        /// <summary>Pressed this frame: focus what is under the cursor, or operate it.</summary>
        bool InteractPressed { get; }

        /// <summary>Pressed this frame: step back out of a focused object.</summary>
        bool BackPressed { get; }

        void Tick(float deltaTime);
    }

    /// <summary>Mouse and keyboard through Unity's legacy input.
    ///
    /// Legacy rather than the Input System package deliberately: it needs no change to the
    /// project's active input handler, which cannot be switched cleanly from batch mode,
    /// and the vertical slice does not need rebinding. The concept's plan already
    /// schedules the Input System migration for production hardening, and this interface
    /// is what keeps that migration to one class.</summary>
    public sealed class LegacyInputSource : IInputSource
    {
        private readonly float _sensitivity;

        public LegacyInputSource(float sensitivity = 2.2f) => _sensitivity = sensitivity;

        public Vector2 LookDelta { get; private set; }
        public bool InteractPressed { get; private set; }
        public bool BackPressed { get; private set; }

        public void Tick(float deltaTime)
        {
            LookDelta = new Vector2(
                Input.GetAxisRaw("Mouse X") * _sensitivity,
                Input.GetAxisRaw("Mouse Y") * _sensitivity);

            InteractPressed = Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.E);
            BackPressed = Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.Escape);
        }
    }

    /// <summary>An input source that produces nothing, used while the self-check is
    /// positioning the camera itself.</summary>
    /// <summary>An input source driven from code.
    ///
    /// Exists so the automated self-check can go through the same path a player does.
    /// Everything before this drove the camera and the presenter directly, which is how a
    /// build shipped in which looking and clicking did nothing at all and no test noticed.
    /// </summary>
    public sealed class ScriptedInputSource : IInputSource
    {
        private bool _interact;
        private bool _back;

        public Vector2 LookDelta { get; private set; }
        public bool InteractPressed { get; private set; }
        public bool BackPressed { get; private set; }

        /// <summary>Look by this much on every frame until told otherwise.</summary>
        public Vector2 Look { get; set; }

        /// <summary>Queues a click for the next frame, the way a real button press lasts
        /// exactly one frame.</summary>
        public void Click() => _interact = true;

        public void Back() => _back = true;

        public void Tick(float deltaTime)
        {
            LookDelta = Look;

            InteractPressed = _interact;
            BackPressed = _back;
            _interact = false;
            _back = false;
        }
    }

    public sealed class NullInputSource : IInputSource
    {
        public Vector2 LookDelta => Vector2.zero;
        public bool InteractPressed => false;
        public bool BackPressed => false;
        public void Tick(float deltaTime) { }
    }
}
