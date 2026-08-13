using System.Collections.Generic;
using Monster.SelfCheck;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Monster.EditorTools
{
    /// <summary>
    /// Generates the Concept E ("Night Shift") checkpoint booth from primitives.
    ///
    /// The scene is built in code rather than authored by hand for three reasons: it is
    /// reviewable in a diff, it is reproducible from a clean clone with no binary assets,
    /// and it can be regenerated deterministically, which is what makes run-to-run
    /// screenshot comparison meaningful.
    ///
    /// Layout, in metres, with the seated player at the origin looking down +Z:
    ///   interior    X [-1.70, 1.70]   Y [0.00, 2.45]   Z [-1.50, 1.70]
    ///   desk top    Y 0.76,           Z [ 0.62, 1.62]
    ///   window      X [-1.15, 1.15]   Y [1.20, 1.85]   in the +Z wall
    ///   eye         (0.00, 1.24, -0.05), pitched 4 degrees down
    /// </summary>
    public static class NightShiftSceneBuilder
    {
        private const string ScenesFolder = "Assets/Scenes";
        private const string ScenePath = ScenesFolder + "/NightShift_Booth.unity";
        private const string MaterialsFolder = "Assets/Materials";

        // Interior shell.
        private const float RoomMinX = -1.70f;
        private const float RoomMaxX = 1.70f;
        private const float RoomMinZ = -1.50f;
        private const float RoomMaxZ = 1.70f;
        private const float RoomHeight = 2.45f;
        private const float WallThickness = 0.15f;

        // Desk.
        private const float DeskTopY = 0.76f;
        private const float DeskThickness = 0.07f;
        private const float DeskMinZ = 0.62f;
        private const float DeskMaxZ = 1.62f;

        // Window aperture in the +Z wall.
        private const float WindowMinX = -1.15f;
        private const float WindowMaxX = 1.15f;
        private const float WindowMinY = 1.20f;
        private const float WindowMaxY = 1.85f;

        private static readonly Dictionary<string, Material> Materials = new();

        [MenuItem("MONSTER/Build Night Shift Booth Scene")]
        public static void Build()
        {
            MonsterSetup.ConfigureRenderPipeline();
            MonsterSetup.EnsureFolder(ScenesFolder);
            MonsterSetup.EnsureFolder(MaterialsFolder);
            Materials.Clear();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            BuildEnvironmentSettings();
            var booth = new GameObject("Booth").transform;
            BuildShell(booth);
            BuildDesk(booth);
            BuildDeskEquipment(booth);
            BuildLamp(booth);

            var outside = new GameObject("Outside").transform;
            BuildOutside(outside);

            var camera = BuildCamera();
            BuildSelfCheck(camera);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            RegisterInBuildSettings();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[NightShift] scene generated: {ScenePath}");
        }

        // ------------------------------------------------------------------ environment --

        private static void BuildEnvironmentSettings()
        {
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.035f, 0.042f, 0.060f);
            RenderSettings.ambientEquatorColor = new Color(0.022f, 0.026f, 0.035f);
            RenderSettings.ambientGroundColor = new Color(0.012f, 0.013f, 0.016f);
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
            RenderSettings.customReflectionTexture = null;

            // Fog is the atmosphere and it is nearly free. Exponential-squared keeps the
            // booth interior almost clear while burying the road beyond ten metres.
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(0.055f, 0.060f, 0.072f);
            RenderSettings.fogDensity = 0.10f;

            RenderSettings.skybox = null;

            // A very dim cold key from above stands in for moonlight through cloud. It is
            // the only shadow-casting light outside; everything else is practical.
            var moon = new GameObject("Moonlight").AddComponent<Light>();
            moon.type = LightType.Directional;
            moon.color = new Color(0.55f, 0.66f, 0.90f);
            moon.intensity = 0.16f;
            moon.shadows = LightShadows.Hard;
            moon.shadowStrength = 0.75f;
            moon.transform.rotation = Quaternion.Euler(38f, 200f, 0f);
        }

        // ------------------------------------------------------------------------ shell --

        private static void BuildShell(Transform parent)
        {
            var concrete = Mat("Concrete", new Color(0.235f, 0.232f, 0.215f), 0.06f);
            var concreteDark = Mat("ConcreteDark", new Color(0.125f, 0.128f, 0.130f), 0.04f);

            var width = RoomMaxX - RoomMinX;
            var depth = RoomMaxZ - RoomMinZ;
            var midX = (RoomMinX + RoomMaxX) * 0.5f;
            var midZ = (RoomMinZ + RoomMaxZ) * 0.5f;

            Box("Floor", parent, new Vector3(midX, -WallThickness * 0.5f, midZ),
                new Vector3(width, WallThickness, depth), concreteDark);
            Box("Ceiling", parent, new Vector3(midX, RoomHeight + WallThickness * 0.5f, midZ),
                new Vector3(width, WallThickness, depth), concreteDark);
            Box("Wall_Back", parent, new Vector3(midX, RoomHeight * 0.5f, RoomMinZ - WallThickness * 0.5f),
                new Vector3(width, RoomHeight, WallThickness), concrete);
            Box("Wall_Left", parent, new Vector3(RoomMinX - WallThickness * 0.5f, RoomHeight * 0.5f, midZ),
                new Vector3(WallThickness, RoomHeight, depth), concrete);
            Box("Wall_Right", parent, new Vector3(RoomMaxX + WallThickness * 0.5f, RoomHeight * 0.5f, midZ),
                new Vector3(WallThickness, RoomHeight, depth), concrete);

            // The front wall is four pieces around the window aperture. Cutting a hole in a
            // primitive is not possible, and four boxes cost less than a custom mesh.
            var frontZ = RoomMaxZ + WallThickness * 0.5f;
            Box("Wall_Front_Sill", parent, new Vector3(midX, WindowMinY * 0.5f, frontZ),
                new Vector3(width, WindowMinY, WallThickness), concrete);
            Box("Wall_Front_Header", parent,
                new Vector3(midX, (WindowMaxY + RoomHeight) * 0.5f, frontZ),
                new Vector3(width, RoomHeight - WindowMaxY, WallThickness), concrete);
            Box("Wall_Front_JambL", parent,
                new Vector3((RoomMinX + WindowMinX) * 0.5f, (WindowMinY + WindowMaxY) * 0.5f, frontZ),
                new Vector3(WindowMinX - RoomMinX, WindowMaxY - WindowMinY, WallThickness), concrete);
            Box("Wall_Front_JambR", parent,
                new Vector3((WindowMaxX + RoomMaxX) * 0.5f, (WindowMinY + WindowMaxY) * 0.5f, frontZ),
                new Vector3(RoomMaxX - WindowMaxX, WindowMaxY - WindowMinY, WallThickness), concrete);

            // A shallow steel frame around the aperture reads as "this is a window" rather
            // than "this wall is missing a piece".
            var steel = Mat("Steel", new Color(0.155f, 0.158f, 0.160f), 0.42f, 0.6f);
            const float frameDepth = 0.05f;
            var frameZ = RoomMaxZ - 0.01f;
            Box("Frame_Bottom", parent, new Vector3(0f, WindowMinY, frameZ),
                new Vector3(WindowMaxX - WindowMinX + 0.10f, 0.04f, frameDepth), steel);
            Box("Frame_Top", parent, new Vector3(0f, WindowMaxY, frameZ),
                new Vector3(WindowMaxX - WindowMinX + 0.10f, 0.04f, frameDepth), steel);
            Box("Frame_Left", parent, new Vector3(WindowMinX, (WindowMinY + WindowMaxY) * 0.5f, frameZ),
                new Vector3(0.04f, WindowMaxY - WindowMinY, frameDepth), steel);
            Box("Frame_Right", parent, new Vector3(WindowMaxX, (WindowMinY + WindowMaxY) * 0.5f, frameZ),
                new Vector3(0.04f, WindowMaxY - WindowMinY, frameDepth), steel);
            Box("Frame_Mullion", parent, new Vector3(0f, (WindowMinY + WindowMaxY) * 0.5f, frameZ),
                new Vector3(0.03f, WindowMaxY - WindowMinY, frameDepth), steel);

            // Pipework along the ceiling. Cheap, and it fills the upper third of the frame
            // which would otherwise be an empty grey band.
            var pipe = Mat("Pipe", new Color(0.19f, 0.17f, 0.14f), 0.30f, 0.5f);
            for (var i = 0; i < 3; i++)
            {
                var x = -1.05f + i * 0.42f;
                Cylinder($"Pipe_{i}", parent, new Vector3(x, RoomHeight - 0.16f, midZ),
                    new Vector3(0.05f, depth * 0.5f, 0.05f), pipe, new Vector3(90f, 0f, 0f));
            }
        }

        // ------------------------------------------------------------------------- desk --

        private static void BuildDesk(Transform parent)
        {
            var deskMat = Mat("DeskSteel", new Color(0.175f, 0.172f, 0.165f), 0.34f, 0.55f);
            var deskWidth = RoomMaxX - RoomMinX;
            var deskDepth = DeskMaxZ - DeskMinZ;
            var deskZ = (DeskMinZ + DeskMaxZ) * 0.5f;

            Box("Desk_Top", parent, new Vector3(0f, DeskTopY - DeskThickness * 0.5f, deskZ),
                new Vector3(deskWidth, DeskThickness, deskDepth), deskMat);
            Box("Desk_Apron", parent, new Vector3(0f, DeskTopY - 0.16f, DeskMinZ + 0.03f),
                new Vector3(deskWidth, 0.18f, 0.05f), deskMat);

            foreach (var x in new[] { RoomMinX + 0.25f, RoomMaxX - 0.25f })
            {
                Box($"Desk_Leg_{x:F2}", parent, new Vector3(x, (DeskTopY - DeskThickness) * 0.5f, deskZ + 0.1f),
                    new Vector3(0.06f, DeskTopY - DeskThickness, 0.06f), deskMat);
            }
        }

        private static void BuildDeskEquipment(Transform parent)
        {
            var crtShell = Mat("CRTShell", new Color(0.255f, 0.245f, 0.205f), 0.22f);
            var crtScreen = Mat("CRTScreen", new Color(0.015f, 0.030f, 0.020f), 0.55f, 0f,
                new Color(0.075f, 0.520f, 0.185f) * 1.7f);
            var brass = Mat("Brass", new Color(0.62f, 0.47f, 0.19f), 0.66f, 0.85f);
            var paper = Mat("Paper", new Color(0.760f, 0.735f, 0.640f), 0.05f);
            var darkPlastic = Mat("DarkPlastic", new Color(0.055f, 0.055f, 0.062f), 0.28f);
            var enamel = Mat("Enamel", new Color(0.700f, 0.690f, 0.650f), 0.52f);
            var wood = Mat("Wood", new Color(0.220f, 0.135f, 0.075f), 0.18f);

            var monitors = new GameObject("Monitors").transform;
            monitors.SetParent(parent, false);

            // Three CRTs across the back of the desk, the outer two angled inward so the
            // player can read all three without turning their head.
            var placements = new[]
            {
                (x: -0.80f, yaw: 17f),
                (x: 0.00f, yaw: 0f),
                (x: 0.80f, yaw: -17f),
            };

            for (var i = 0; i < placements.Length; i++)
            {
                var (x, yaw) = placements[i];
                var pivot = new GameObject($"CRT_{i}").transform;
                pivot.SetParent(monitors, false);
                pivot.SetPositionAndRotation(new Vector3(x, DeskTopY, 1.40f), Quaternion.Euler(0f, yaw, 0f));

                Box("Shell", pivot, new Vector3(0f, 0.175f, 0f), new Vector3(0.40f, 0.35f, 0.38f), crtShell);
                Box("Hood", pivot, new Vector3(0f, 0.345f, -0.10f), new Vector3(0.42f, 0.03f, 0.20f), crtShell);
                Box("Screen", pivot, new Vector3(0f, 0.185f, -0.196f), new Vector3(0.320f, 0.250f, 0.012f), crtScreen);

                // Scanlines, faked with three darker bars over the screen. Effectively free
                // and it stops the screen reading as a flat green rectangle.
                for (var line = 0; line < 3; line++)
                {
                    Box($"Scanline_{line}", pivot,
                        new Vector3(0f, 0.120f + line * 0.065f, -0.203f),
                        new Vector3(0.320f, 0.010f, 0.004f), darkPlastic);
                }

                var glow = new GameObject("Glow").AddComponent<Light>();
                glow.transform.SetParent(pivot, false);
                glow.transform.localPosition = new Vector3(0f, 0.19f, -0.32f);
                glow.type = LightType.Point;
                glow.color = new Color(0.30f, 0.95f, 0.42f);
                glow.intensity = 0.55f;
                glow.range = 1.25f;
                glow.shadows = LightShadows.None;
            }

            // Paperwork, deliberately scattered rather than aligned.
            var forms = new GameObject("Paperwork").transform;
            forms.SetParent(parent, false);
            var sheets = new[]
            {
                (pos: new Vector3(0.10f, DeskTopY + 0.002f, 0.86f), yaw: -7f),
                (pos: new Vector3(0.16f, DeskTopY + 0.005f, 0.90f), yaw: 4f),
                (pos: new Vector3(-0.06f, DeskTopY + 0.008f, 0.84f), yaw: 12f),
            };
            for (var i = 0; i < sheets.Length; i++)
            {
                Box($"Form_{i}", forms, sheets[i].pos, new Vector3(0.210f, 0.003f, 0.297f), paper,
                    new Vector3(0f, sheets[i].yaw, 0f));
            }

            // The manual: a ring binder lying open, which is the object the whole game is
            // actually about.
            var binder = new GameObject("Manual").transform;
            binder.SetParent(parent, false);
            binder.SetPositionAndRotation(new Vector3(0.98f, DeskTopY, 0.82f), Quaternion.Euler(0f, -14f, 0f));
            Box("Cover", binder, new Vector3(0f, 0.012f, 0f), new Vector3(0.250f, 0.024f, 0.320f), darkPlastic);
            Box("Pages", binder, new Vector3(0f, 0.028f, 0f), new Vector3(0.235f, 0.014f, 0.305f), paper);

            // Classification panel: the switches that decide whether a subject passes.
            var panel = new GameObject("ClassificationPanel").transform;
            panel.SetParent(parent, false);
            panel.SetPositionAndRotation(new Vector3(-0.74f, DeskTopY + 0.035f, 0.90f),
                Quaternion.Euler(-24f, 0f, 0f));
            Box("Plate", panel, Vector3.zero, new Vector3(0.360f, 0.030f, 0.170f), darkPlastic);
            for (var i = 0; i < 4; i++)
            {
                Cylinder($"Switch_{i}", panel, new Vector3(-0.126f + i * 0.084f, 0.030f, 0.014f),
                    new Vector3(0.030f, 0.026f, 0.030f), brass);
            }

            // Stamp and ink pad.
            Box("InkPad", parent, new Vector3(-0.36f, DeskTopY + 0.010f, 0.74f),
                new Vector3(0.105f, 0.020f, 0.080f), darkPlastic);
            Box("Stamp_Head", parent, new Vector3(-0.20f, DeskTopY + 0.024f, 0.74f),
                new Vector3(0.062f, 0.048f, 0.062f), wood);
            Cylinder("Stamp_Handle", parent, new Vector3(-0.20f, DeskTopY + 0.072f, 0.74f),
                new Vector3(0.028f, 0.026f, 0.028f), wood);

            // Telephone.
            var phone = new GameObject("Telephone").transform;
            phone.SetParent(parent, false);
            phone.SetPositionAndRotation(new Vector3(1.30f, DeskTopY, 1.14f), Quaternion.Euler(0f, -28f, 0f));
            Box("Base", phone, new Vector3(0f, 0.035f, 0f), new Vector3(0.200f, 0.070f, 0.240f), darkPlastic);
            Box("Handset", phone, new Vector3(0f, 0.095f, 0.010f), new Vector3(0.215f, 0.055f, 0.075f), darkPlastic);
            Cylinder("Dial", phone, new Vector3(0f, 0.072f, -0.070f), new Vector3(0.105f, 0.004f, 0.105f), brass);

            // Coffee. Present in the concept art and it is the object that says a person
            // has been sitting here for hours.
            Cylinder("Mug", parent, new Vector3(0.56f, DeskTopY + 0.048f, 0.72f),
                new Vector3(0.082f, 0.048f, 0.082f), enamel);
            Cylinder("Mug_Coffee", parent, new Vector3(0.56f, DeskTopY + 0.088f, 0.72f),
                new Vector3(0.070f, 0.004f, 0.070f),
                Mat("Coffee", new Color(0.055f, 0.030f, 0.018f), 0.72f));
        }

        private static void BuildLamp(Transform parent)
        {
            var lampMat = Mat("LampEnamel", new Color(0.330f, 0.095f, 0.065f), 0.45f, 0.2f);
            var lampInner = Mat("LampInner", new Color(0.02f, 0.02f, 0.02f), 0.1f, 0f,
                new Color(1.00f, 0.72f, 0.38f) * 2.6f);

            var lamp = new GameObject("DeskLamp").transform;
            lamp.SetParent(parent, false);
            lamp.localPosition = new Vector3(-1.30f, DeskTopY, 0.95f);

            Cylinder("Base", lamp, new Vector3(0f, 0.012f, 0f), new Vector3(0.150f, 0.012f, 0.150f), lampMat);
            Cylinder("Stem", lamp, new Vector3(0.02f, 0.155f, 0.02f), new Vector3(0.022f, 0.150f, 0.022f), lampMat,
                new Vector3(-12f, 0f, -8f));
            Cylinder("Shade", lamp, new Vector3(0.10f, 0.330f, 0.10f), new Vector3(0.190f, 0.075f, 0.190f), lampMat,
                new Vector3(28f, 0f, 22f));
            Cylinder("Bulb", lamp, new Vector3(0.10f, 0.290f, 0.10f), new Vector3(0.120f, 0.008f, 0.120f), lampInner,
                new Vector3(28f, 0f, 22f));

            // The key light. Everything else in the booth is fill.
            var light = new GameObject("Key").AddComponent<Light>();
            light.transform.SetParent(lamp, false);
            light.transform.localPosition = new Vector3(0.10f, 0.285f, 0.10f);
            light.transform.rotation = Quaternion.Euler(58f, 34f, 0f);
            light.type = LightType.Spot;
            light.color = new Color(1.00f, 0.755f, 0.480f);
            light.intensity = 9.5f;
            light.range = 4.2f;
            light.spotAngle = 96f;
            light.innerSpotAngle = 34f;
            light.shadows = LightShadows.Hard;
            light.shadowStrength = 0.85f;

            // A weak unshadowed bounce so the walls behind the lamp are not pure black.
            var bounce = new GameObject("Bounce").AddComponent<Light>();
            bounce.transform.SetParent(lamp, false);
            bounce.transform.localPosition = new Vector3(0.05f, 0.18f, 0.05f);
            bounce.type = LightType.Point;
            bounce.color = new Color(1.00f, 0.70f, 0.42f);
            bounce.intensity = 1.1f;
            bounce.range = 2.6f;
            bounce.shadows = LightShadows.None;
        }

        // ---------------------------------------------------------------------- outside --

        private static void BuildOutside(Transform parent)
        {
            var asphalt = Mat("Asphalt", new Color(0.058f, 0.058f, 0.062f), 0.22f);
            var paint = Mat("RoadPaint", new Color(0.44f, 0.42f, 0.36f), 0.10f);
            var barrierRed = Mat("BarrierRed", new Color(0.400f, 0.070f, 0.055f), 0.20f);
            var barrierWhite = Mat("BarrierWhite", new Color(0.560f, 0.545f, 0.505f), 0.20f);
            var figure = Mat("Figure", new Color(0.014f, 0.014f, 0.018f), 0.08f);
            var tail = Mat("Taillight", new Color(0.08f, 0.005f, 0.005f), 0.4f, 0f,
                new Color(1.00f, 0.09f, 0.06f) * 5.0f);

            Box("Road", parent, new Vector3(0f, -0.20f, 16f), new Vector3(24f, 0.4f, 34f), asphalt);
            for (var i = 0; i < 7; i++)
            {
                Box($"RoadLine_{i}", parent, new Vector3(-0.55f, 0.002f, 6.5f + i * 3.2f),
                    new Vector3(0.14f, 0.01f, 1.5f), paint);
            }

            // Barrier arm across the road, striped.
            var barrier = new GameObject("Barrier").transform;
            barrier.SetParent(parent, false);
            barrier.localPosition = new Vector3(0f, 0f, 4.30f);
            Cylinder("Post", barrier, new Vector3(-2.05f, 0.50f, 0f), new Vector3(0.14f, 0.50f, 0.14f),
                Mat("BarrierPost", new Color(0.16f, 0.16f, 0.15f), 0.30f, 0.6f));
            Box("Housing", barrier, new Vector3(-2.05f, 1.02f, 0f), new Vector3(0.26f, 0.22f, 0.24f),
                Mat("BarrierHousing", new Color(0.34f, 0.30f, 0.10f), 0.35f, 0.4f));
            for (var i = 0; i < 8; i++)
            {
                Box($"Arm_{i}", barrier, new Vector3(-1.62f + i * 0.62f, 0.98f, 0f),
                    new Vector3(0.62f, 0.09f, 0.09f), i % 2 == 0 ? barrierWhite : barrierRed);
            }

            // The subject. Never clearly seen: it stands in fog, backlit by taillights, and
            // its proportions are wrong rather than its features being monstrous. Arms are
            // long, the head sits high, and it does not move.
            var subject = new GameObject("Subject").transform;
            subject.SetParent(parent, false);
            subject.SetPositionAndRotation(new Vector3(0.62f, 0f, 6.15f), Quaternion.Euler(0f, 184f, 0f));
            Box("Legs", subject, new Vector3(0f, 0.44f, 0f), new Vector3(0.30f, 0.88f, 0.22f), figure);
            Box("Torso", subject, new Vector3(0f, 1.24f, 0f), new Vector3(0.46f, 0.76f, 0.26f), figure);
            Box("Neck", subject, new Vector3(0f, 1.72f, 0f), new Vector3(0.10f, 0.22f, 0.10f), figure);
            Box("Head", subject, new Vector3(0f, 1.95f, 0f), new Vector3(0.19f, 0.25f, 0.20f), figure);
            Box("Arm_L", subject, new Vector3(-0.30f, 1.06f, 0.02f), new Vector3(0.11f, 1.10f, 0.13f), figure,
                new Vector3(0f, 0f, 4f));
            Box("Arm_R", subject, new Vector3(0.30f, 1.06f, 0.02f), new Vector3(0.11f, 1.10f, 0.13f), figure,
                new Vector3(0f, 0f, -4f));

            // The vehicle it stepped out of, reduced to two taillights and a dark mass.
            var vehicle = new GameObject("Vehicle").transform;
            vehicle.SetParent(parent, false);
            vehicle.localPosition = new Vector3(-0.30f, 0f, 9.40f);
            Box("Body", vehicle, new Vector3(0f, 0.85f, 0f), new Vector3(2.05f, 1.55f, 4.60f),
                Mat("VehicleBody", new Color(0.030f, 0.032f, 0.036f), 0.30f, 0.4f));
            foreach (var side in new[] { -0.78f, 0.78f })
            {
                Box($"Tail_{side:F1}", vehicle, new Vector3(side, 0.92f, -2.31f),
                    new Vector3(0.34f, 0.16f, 0.04f), tail);
                var lamp = new GameObject($"TailLight_{side:F1}").AddComponent<Light>();
                lamp.transform.SetParent(vehicle, false);
                lamp.transform.localPosition = new Vector3(side, 0.92f, -2.55f);
                lamp.type = LightType.Point;
                lamp.color = new Color(1.00f, 0.13f, 0.08f);
                lamp.intensity = 3.4f;
                lamp.range = 6.5f;
                lamp.shadows = LightShadows.None;
            }
        }

        // ----------------------------------------------------------------------- camera --

        private static Camera BuildCamera()
        {
            var go = new GameObject("PlayerCamera");
            go.tag = "MainCamera";
            go.transform.SetPositionAndRotation(new Vector3(0f, 1.24f, -0.05f), Quaternion.Euler(4f, 0f, 0f));

            var camera = go.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.030f, 0.034f, 0.042f);
            camera.fieldOfView = 60f;
            camera.nearClipPlane = 0.03f;
            camera.farClipPlane = 60f;
            camera.allowHDR = true;
            camera.allowMSAA = false;

            var urp = go.AddComponent<UniversalAdditionalCameraData>();
            urp.renderPostProcessing = true;
            urp.antialiasing = AntialiasingMode.FastApproximateAntialiasing;
            urp.renderShadows = true;

            go.AddComponent<AudioListener>();

            var volumeGo = new GameObject("PostProcessVolume");
            var volume = volumeGo.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 0f;
            volume.sharedProfile = MonsterSetup.CreatePostProcessProfile();

            return camera;
        }

        private static void BuildSelfCheck(Camera camera)
        {
            var go = new GameObject("SelfCheck");
            var runner = go.AddComponent<SelfCheckRunner>();

            // The camera is fixed in this scene, so a single checkpoint is enough for now.
            // Concept E's later checkpoints (document inspect, camera feed, morning report)
            // attach here as they are built.
            var serialized = new SerializedObject(runner);
            var list = serialized.FindProperty("checkpoints");
            list.arraySize = 1;
            var entry = list.GetArrayElementAtIndex(0);
            entry.FindPropertyRelative("name").stringValue = "booth_idle";
            entry.FindPropertyRelative("settleSeconds").floatValue = 0.75f;
            entry.FindPropertyRelative("camera").objectReferenceValue = camera;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void RegisterInBuildSettings()
        {
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }

        // ---------------------------------------------------------------------- helpers --

        private static GameObject Box(string name, Transform parent, Vector3 position, Vector3 size,
            Material material, Vector3? euler = null) =>
            Primitive(PrimitiveType.Cube, name, parent, position, size, material, euler);

        private static GameObject Cylinder(string name, Transform parent, Vector3 position, Vector3 size,
            Material material, Vector3? euler = null) =>
            Primitive(PrimitiveType.Cylinder, name, parent, position, size, material, euler);

        private static GameObject Primitive(PrimitiveType type, string name, Transform parent,
            Vector3 position, Vector3 size, Material material, Vector3? euler)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            if (parent != null)
            {
                go.transform.SetParent(parent, false);
            }

            go.transform.localPosition = position;
            go.transform.localScale = size;
            if (euler.HasValue)
            {
                go.transform.localRotation = Quaternion.Euler(euler.Value);
            }

            go.GetComponent<MeshRenderer>().sharedMaterial = material;

            // Nothing in this scene needs collision yet, and colliders on a few hundred
            // primitives are pure cost.
            var collider = go.GetComponent<Collider>();
            if (collider != null)
            {
                Object.DestroyImmediate(collider);
            }

            return go;
        }

        private static Material Mat(string name, Color baseColor, float smoothness,
            float metallic = 0f, Color? emission = null)
        {
            if (Materials.TryGetValue(name, out var cached))
            {
                return cached;
            }

            var path = $"{MaterialsFolder}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                Debug.LogError("[NightShift] URP Lit shader not found; is the render pipeline configured?");
                shader = Shader.Find("Standard");
            }

            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            else
            {
                material.shader = shader;
            }

            material.SetColor("_BaseColor", baseColor);
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_Metallic", metallic);

            if (emission.HasValue)
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", emission.Value);
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            else
            {
                material.DisableKeyword("_EMISSION");
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }

            EditorUtility.SetDirty(material);
            Materials[name] = material;
            return material;
        }
    }
}
