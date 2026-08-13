using UnityEngine;
using Worker.Core;

namespace Worker.Game
{
    /// <summary>
    /// Orthographic top-down camera framed on the whole shop floor.
    ///
    /// The default view shows the entire factory at once. That is a design decision, not
    /// a convenience: the game is about reading a whole system, and a camera that hides
    /// half the floor would turn layout problems into navigation problems. Panning and
    /// zooming exist for inspection, and the framing can always be reset.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class CameraRig : MonoBehaviour
    {
        [Tooltip("Tiles of empty space kept around the factory when framing it.")]
        public float MarginTiles = 1.5f;

        public float MinOrthographicSize = 4f;
        public float MaxOrthographicSize = 24f;
        public float ZoomSpeed = 6f;
        public float PanSpeed = 14f;

        private Camera _camera;
        private SimRunner _runner;
        private Vector3 _framedCenter;
        private float _framedSize;

        private void Awake()
        {
            _camera = GetComponent<Camera>();
            _camera.orthographic = true;
            _camera.backgroundColor = Palette.Yard.Darken(35).ToUnity();
            _camera.clearFlags = CameraClearFlags.SolidColor;
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

        /// <summary>Fits the whole map in view, accounting for the current aspect ratio.</summary>
        public void Frame(SimWorld world)
        {
            if (world == null) return;

            float width = world.Map.Width + MarginTiles * 2f;
            float height = world.Map.Height + MarginTiles * 2f;

            float aspect = _camera.aspect > 0.01f ? _camera.aspect : 16f / 9f;
            float sizeForHeight = height * 0.5f;
            float sizeForWidth = width * 0.5f / aspect;

            _framedSize = Mathf.Max(sizeForHeight, sizeForWidth);
            _framedCenter = new Vector3(world.Map.Width * 0.5f, world.Map.Height * 0.5f, -10f);

            _camera.orthographicSize = _framedSize;
            transform.position = _framedCenter;
        }

        public void ResetFraming()
        {
            if (_runner != null) Frame(_runner.World);
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

            float size = _camera.orthographicSize - scroll * ZoomSpeed * Time.deltaTime * 10f;
            _camera.orthographicSize = Mathf.Clamp(size, MinOrthographicSize, MaxOrthographicSize);
        }

        private void HandlePan()
        {
            float x = Input.GetAxisRaw("Horizontal");
            float y = Input.GetAxisRaw("Vertical");
            if (Mathf.Approximately(x, 0f) && Mathf.Approximately(y, 0f)) return;

            // Pan speed scales with zoom so the view moves at a consistent apparent rate.
            float scale = _camera.orthographicSize / Mathf.Max(1f, _framedSize);
            var delta = new Vector3(x, y, 0f) * (PanSpeed * scale * Time.deltaTime);
            transform.position += delta;
        }
    }
}
