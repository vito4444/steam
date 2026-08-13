using UnityEngine;
using Worker.Core;

namespace Worker.Game
{
    /// <summary>
    /// Fixed orthographic isometric camera, framed on the whole factory.
    ///
    /// Orthographic rather than perspective on purpose. A management game is read as a
    /// diagram: the player is comparing distances and judging whether two machines line
    /// up, and perspective makes identical objects different sizes depending on where
    /// they sit. Two Point Hospital, Timberborn and Against the Storm all take the same
    /// position.
    ///
    /// The angle is a compromise. Steeper than about 55 degrees and the world flattens
    /// back into the top-down view this replaced; shallower than about 35 and tall
    /// objects start hiding the floor behind them.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class IsometricCameraRig : MonoBehaviour
    {
        [Tooltip("Downward pitch in degrees. 45 is the classic isometric angle.")]
        public float Pitch = 46f;

        [Tooltip("Rotation around the vertical axis. 45 puts tile edges on the diagonal.")]
        public float Yaw = 45f;

        [Tooltip("Extra tiles of headroom kept around the factory when framing it.")]
        public float MarginTiles = 1.5f;

        public float MinOrthographicSize = 5f;
        public float MaxOrthographicSize = 40f;
        public float ZoomSpeed = 40f;
        public float PanSpeed = 12f;

        private Camera _camera;
        private SimRunner _runner;
        private Vector3 _focus;
        private float _framedSize;

        private void Awake()
        {
            _camera = GetComponent<Camera>();
            _camera.orthographic = true;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = new Color(0.09f, 0.10f, 0.13f);
            _camera.nearClipPlane = 0.1f;
            _camera.farClipPlane = 300f;
        }

        public void Bind(SimRunner runner)
        {
            _runner = runner;
            _runner.WorldCreated += Frame;
            if (_runner.World != null) Frame(_runner.World);
        }

        private void OnDestroy()
        {
            if (_runner != null) _runner.WorldCreated -= Frame;
        }

        public void Frame(SimWorld world)
        {
            if (world == null) return;

            _focus = new Vector3(world.Map.Width * 0.5f, 0f, world.Map.Height * 0.5f);

            // A rotated rectangle projects to a diamond, not to its own diagonal. Both
            // map axes contribute to each screen axis, so the extents are (W+H) scaled by
            // the yaw, and the vertical one is additionally squashed by the pitch.
            // Using the diagonal here instead left the factory filling barely half the
            // frame.
            float yawScale = Mathf.Cos(Yaw * Mathf.Deg2Rad);
            float pitchScale = Mathf.Sin(Pitch * Mathf.Deg2Rad);
            float span = world.Map.Width + world.Map.Height;

            float screenWidth = span * yawScale + MarginTiles * 2f;
            float screenHeight = span * yawScale * pitchScale + MarginTiles * 2f;

            float aspect = _camera.aspect > 0.01f ? _camera.aspect : 16f / 9f;
            float needHorizontal = screenWidth * 0.5f / aspect;
            float needVertical = screenHeight * 0.5f;

            _framedSize = Mathf.Max(needHorizontal, needVertical);
            _camera.orthographicSize = _framedSize;

            ApplyTransform();
        }

        public void ResetFraming()
        {
            if (_runner != null) Frame(_runner.World);
        }

        private void ApplyTransform()
        {
            var rotation = Quaternion.Euler(Pitch, Yaw, 0f);
            transform.rotation = rotation;

            // Pull straight back along the view axis. Orthographic projection makes the
            // distance itself irrelevant, so it only has to clear the near plane.
            // Kept modest so it stays inside the pipeline's shadow distance; under an
            // orthographic projection the distance has no effect on framing anyway.
            transform.position = _focus - rotation * Vector3.forward * 45f;
        }

        private void Update()
        {
            HandleZoom();
            HandlePan();
        }

        private void HandleZoom()
        {
            float scroll = Input.mouseScrollDelta.y;
            if (Mathf.Approximately(scroll, 0f)) return;

            _camera.orthographicSize = Mathf.Clamp(
                _camera.orthographicSize - scroll * ZoomSpeed * Time.deltaTime,
                MinOrthographicSize, MaxOrthographicSize);
        }

        private void HandlePan()
        {
            float x = Input.GetAxisRaw("Horizontal");
            float z = Input.GetAxisRaw("Vertical");
            if (Mathf.Approximately(x, 0f) && Mathf.Approximately(z, 0f)) return;

            // Pan along the ground plane in screen-relative directions, otherwise the
            // arrow keys would move the view diagonally under a 45 degree yaw.
            var forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
            var right = Vector3.ProjectOnPlane(transform.right, Vector3.up).normalized;

            float scale = _camera.orthographicSize / Mathf.Max(1f, _framedSize);
            _focus += (right * x + forward * z) * (PanSpeed * scale * Time.deltaTime);

            ApplyTransform();
        }

        /// <summary>Projects a screen point onto the ground plane and returns the tile under it.</summary>
        public GridPos ScreenToTile(Vector3 screenPoint)
        {
            var ray = _camera.ScreenPointToRay(screenPoint);
            var ground = new Plane(Vector3.up, new Vector3(0f, 0.2f, 0f));

            if (!ground.Raycast(ray, out float distance)) return GridPos.Invalid;

            var hit = ray.GetPoint(distance);
            return new GridPos(Mathf.FloorToInt(hit.x), Mathf.FloorToInt(hit.z));
        }
    }
}
