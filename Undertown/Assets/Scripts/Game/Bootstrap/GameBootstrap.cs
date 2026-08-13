using UnityEngine;
using UnityEngine.Tilemaps;
using Undertown.Core.World;
using Undertown.Game.Presentation;

namespace Undertown.Game.Bootstrap
{
    /// <summary>
    /// Builds the whole runtime scene in code. The project is developed on a headless
    /// machine, so nothing may depend on objects dragged into a scene by hand: if it cannot
    /// be constructed from a script it cannot be built, tested or screenshotted here.
    /// </summary>
    public sealed class GameBootstrap : MonoBehaviour
    {
        [SerializeField] private uint _seed = 20260813;

        private GridMap _map;
        private WorldRenderer _renderer;
        private Camera _camera;

        public GridMap Map => _map;
        public WorldRenderer Renderer => _renderer;

        private void Awake()
        {
            var settings = MapSettings.Default;
            _map = MapGenerator.Generate(settings, _seed);

            _renderer = BuildRenderer();
            _renderer.Bind(_map);

            _camera = BuildCamera(settings);

            Debug.Log($"[SMOKE] boot ok seed={_seed} map={settings.Width}x{settings.Height}x{settings.DepthCount} " +
                      $"fingerprint={_map.Fingerprint():X8}");
        }

        private static WorldRenderer BuildRenderer()
        {
            var gridObject = new GameObject("Grid");
            var grid = gridObject.AddComponent<Grid>();
            grid.cellSize = new Vector3(1f, 1f, 0f);

            var ghost = CreateTilemapLayer(gridObject.transform, "Tilemap_Ghost", sortingOrder: 0);
            var primary = CreateTilemapLayer(gridObject.transform, "Tilemap_Primary", sortingOrder: 10);

            var renderer = gridObject.AddComponent<WorldRenderer>();
            renderer.Configure(primary, ghost);
            return renderer;
        }

        private static Tilemap CreateTilemapLayer(Transform parent, string name, int sortingOrder)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, worldPositionStays: false);
            var tilemap = go.AddComponent<Tilemap>();
            var tilemapRenderer = go.AddComponent<TilemapRenderer>();
            tilemapRenderer.sortingOrder = sortingOrder;
            return tilemap;
        }

        private static Camera BuildCamera(MapSettings settings)
        {
            var go = new GameObject("MainCamera");
            go.tag = "MainCamera";
            var cam = go.AddComponent<Camera>();
            cam.orthographic = true;

            // Frame a readable slice of town rather than the whole map: at 18 units of
            // half-height a 1920x1080 window shows roughly 64 by 36 tiles.
            cam.orthographicSize = 18f;
            cam.backgroundColor = new Color32(0x14, 0x12, 0x10, 0xFF);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.transform.position = new Vector3(settings.Width / 2f, settings.Height / 2f, -10f);
            return cam;
        }
    }
}
