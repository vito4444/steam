using UnityEngine;
using UnityEngine.InputSystem;
using Overclock.Core;

namespace Overclock
{
    /// <summary>
    /// 玩家操作：把鼠标指向的世界位置换算成格子坐标，放置和拆除元件，切换手上的元件，平移和缩放镜头。
    ///
    /// 硅层是一个平面，所以不需要物理射线检测——直接把鼠标射线和 y=0 平面求交，
    /// 再除以格子边长就能拿到坐标。省掉了给几百个格子挂 Collider 的开销。
    /// </summary>
    public sealed class PlayerController
    {
        readonly SiliconLayer _layer;
        readonly LayerView _view;
        readonly Camera _camera;

        /// <summary>当前手上拿的元件在调色板里的下标。</summary>
        public int SelectedSlot { get; private set; }

        /// <summary>鼠标指向的格子，越界时为 (-1,-1)。</summary>
        public Vector2Int HoveredCell { get; private set; } = new Vector2Int(-1, -1);

        /// <summary>本帧是否真的改动了布局。改了就要立刻重算流量，不能等定时器。</summary>
        public bool LayoutChanged { get; private set; }

        // 镜头。俯角固定，玩家只能平移、旋转和缩放——
        // 自由飞行的镜头会让网格的空间关系变得难以读取。
        float _yaw;
        float _pitch = 42f;
        float _distance = 18f;
        Vector3 _focus;

        const float MinDistance = 7f;
        const float MaxDistance = 34f;
        const float PanSpeed = 9f;

        public PlayerController(SiliconLayer layer, LayerView view, Camera camera)
        {
            _layer = layer;
            _view = view;
            _camera = camera;
            ApplyCamera();
        }

        public ComponentKind SelectedKind => GameHud.Palette[
            Mathf.Clamp(SelectedSlot, 0, GameHud.Palette.Length - 1)];

        public void Tick(float deltaTime)
        {
            LayoutChanged = false;

            ReadSelection();
            ReadCamera(deltaTime);
            UpdateHover();
            ReadPlacement();

            _view.SetHighlight(HoveredCell, SelectedKind, CanPlaceHere());
        }

        void ReadSelection()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            var keys = new[]
            {
                Key.Digit1, Key.Digit2, Key.Digit3,
                Key.Digit4, Key.Digit5, Key.Digit6,
            };

            for (int i = 0; i < keys.Length && i < GameHud.Palette.Length; i++)
            {
                if (keyboard[keys[i]].wasPressedThisFrame) SelectedSlot = i;
            }

            // 滚轮切换元件是可选路径，有些玩家更习惯它而不是数字键。
            var mouse = Mouse.current;
            if (mouse != null && keyboard.leftShiftKey.isPressed)
            {
                float scroll = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > 0.01f)
                {
                    int dir = scroll > 0f ? 1 : -1;
                    SelectedSlot = (SelectedSlot + dir + GameHud.Palette.Length) % GameHud.Palette.Length;
                }
            }
        }

        void ReadCamera(float deltaTime)
        {
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;

            if (keyboard != null)
            {
                var pan = Vector3.zero;
                if (keyboard.wKey.isPressed) pan.z += 1f;
                if (keyboard.sKey.isPressed) pan.z -= 1f;
                if (keyboard.aKey.isPressed) pan.x -= 1f;
                if (keyboard.dKey.isPressed) pan.x += 1f;

                if (pan.sqrMagnitude > 0.01f)
                {
                    // 平移方向要跟着镜头朝向走，否则玩家转过视角后 WASD 就反了。
                    var flat = Quaternion.Euler(0f, _yaw, 0f);
                    _focus += flat * pan.normalized * PanSpeed * deltaTime;
                    _focus.x = Mathf.Clamp(_focus.x, -_layer.Width * 0.5f, _layer.Width * 0.5f);
                    _focus.z = Mathf.Clamp(_focus.z, -_layer.Height * 0.5f, _layer.Height * 0.5f);
                }

                if (keyboard.qKey.isPressed) _yaw -= 60f * deltaTime;
                if (keyboard.eKey.isPressed) _yaw += 60f * deltaTime;
            }

            if (mouse != null && (keyboard == null || !keyboard.leftShiftKey.isPressed))
            {
                float scroll = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > 0.01f)
                    _distance = Mathf.Clamp(_distance - scroll * 0.012f, MinDistance, MaxDistance);
            }

            ApplyCamera();
        }

        void ApplyCamera()
        {
            if (_camera == null) return;

            var rot = Quaternion.Euler(_pitch, _yaw, 0f);
            _camera.transform.rotation = rot;
            _camera.transform.position = _focus - rot * Vector3.forward * _distance;
        }

        /// <summary>
        /// 鼠标射线和硅层平面求交。层平面是 y=0 的 XZ 平面。
        /// </summary>
        void UpdateHover()
        {
            var mouse = Mouse.current;
            if (mouse == null || _camera == null)
            {
                HoveredCell = new Vector2Int(-1, -1);
                return;
            }

            var screen = mouse.position.ReadValue();
            var ray = _camera.ScreenPointToRay(screen);

            if (Mathf.Abs(ray.direction.y) < 1e-5f)
            {
                HoveredCell = new Vector2Int(-1, -1);
                return;
            }

            float t = -ray.origin.y / ray.direction.y;
            if (t <= 0f)
            {
                HoveredCell = new Vector2Int(-1, -1);
                return;
            }

            var hit = ray.origin + ray.direction * t;

            int x = Mathf.RoundToInt(hit.x / LayerView.CellSize + (_layer.Width - 1) * 0.5f);
            int y = Mathf.RoundToInt(hit.z / LayerView.CellSize + (_layer.Height - 1) * 0.5f);

            HoveredCell = _layer.InBounds(x, y) ? new Vector2Int(x, y) : new Vector2Int(-1, -1);
        }

        bool CanPlaceHere()
        {
            if (HoveredCell.x < 0) return false;
            if (_layer.IsBurned(HoveredCell.x, HoveredCell.y)) return false;

            var existing = _layer.CellAt(HoveredCell.x, HoveredCell.y);
            if (existing == ComponentKind.Source || existing == ComponentKind.Sink) return false;
            if (existing == ComponentKind.DeadCell) return false;

            int refund = existing == ComponentKind.Empty
                ? 0
                : ComponentLibrary.Of(existing).Cost / 2;
            return _layer.Budget + refund >= ComponentLibrary.Of(SelectedKind).Cost;
        }

        void ReadPlacement()
        {
            var mouse = Mouse.current;
            if (mouse == null || HoveredCell.x < 0) return;

            // 按住拖拽可以连续铺设。一格一格点着铺一条二十格的线太折磨人了。
            if (mouse.leftButton.isPressed)
            {
                if (_layer.Place(HoveredCell.x, HoveredCell.y, SelectedKind))
                    LayoutChanged = true;
            }
            else if (mouse.rightButton.isPressed)
            {
                if (_layer.Remove(HoveredCell.x, HoveredCell.y))
                    LayoutChanged = true;
            }
        }

        /// <summary>换层时重置镜头和选择。</summary>
        public void ResetForNewLayer()
        {
            _focus = Vector3.zero;
            _distance = 18f;
            _yaw = 0f;
        }
    }
}
