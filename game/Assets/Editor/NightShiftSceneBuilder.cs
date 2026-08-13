using System.Collections.Generic;
using System.IO;
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
        private const string TexturesFolder = "Assets/Textures";

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
        private const float DeskMinZ = -0.16f;
        private const float DeskMaxZ = 1.62f;

        // Window aperture in the +Z wall.
        private const float WindowMinX = -1.42f;
        private const float WindowMaxX = 1.42f;
        private const float WindowMinY = 1.26f;
        private const float WindowMaxY = 2.08f;

        /// <summary>Shared by the fog, the camera's clear colour and the far backdrop, so
        /// that the horizon has no visible seam.</summary>
        private static readonly Color FogColor = new(0.150f, 0.160f, 0.188f);

        private static readonly Dictionary<string, Material> Materials = new();

        // Collected while the geometry is built and handed to BoothContentBuilder, which
        // needs to know which cube is the permit and which cylinder is the ALARM switch.
        // Text is parented to these unscaled anchors rather than to the scaled props it
        // appears on, because a rotated child of a non-uniformly scaled parent is sheared.
        private static readonly List<Transform> ScreenAnchors = new();
        private static readonly List<Transform> Switches = new();
        private static readonly List<Transform> SwitchLabelAnchors = new();
        private static readonly List<Transform> IntercomKeys = new();
        private static readonly List<Transform> IntercomKeyLabels = new();
        private static Transform _permitPaper;
        private static Transform _permitAnchor;
        private static Transform _manualPages;
        private static Transform _manualAnchor;
        private static Transform _logAnchor;
        private static Transform _mailMesh;
        private static Transform _mailAnchor;
        private static Transform _clockAnchor;
        private static Vector3 _lampOrigin;
        private static Vector3 _lampTarget;
        private static Transform _barrierArm;
        private static Transform _vehicleRoot;
        private static Transform _subjectRoot;
        private static readonly List<Light> Headlights = new();
        private static readonly List<Light> Taillights = new();

        [MenuItem("MONSTER/Build Night Shift Booth Scene")]
        public static void Build()
        {
            MonsterSetup.ConfigureRenderPipeline();
            MonsterSetup.EnsureFolder(ScenesFolder);
            MonsterSetup.EnsureFolder(MaterialsFolder);
            Materials.Clear();
            ScreenAnchors.Clear();
            Switches.Clear();
            SwitchLabelAnchors.Clear();
            IntercomKeys.Clear();
            IntercomKeyLabels.Clear();
            _permitPaper = null;
            _permitAnchor = null;
            _manualPages = null;
            _manualAnchor = null;
            _logAnchor = null;
            _mailMesh = null;
            _mailAnchor = null;
            _clockAnchor = null;
            _lampOrigin = Vector3.zero;
            _lampTarget = Vector3.zero;
            _barrierArm = null;
            _vehicleRoot = null;
            _subjectRoot = null;
            Headlights.Clear();
            Taillights.Clear();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            BuildEnvironmentSettings();
            var booth = new GameObject("Booth").transform;
            BuildShell(booth);
            BuildDesk(booth);
            BuildDeskEquipment(booth);
            BuildLamp(booth);

            var outside = new GameObject("Outside").transform;
            BuildOutside(outside);

            BoothAtmosphere.Build(booth, _lampOrigin, _lampTarget, ScreenAnchors.ToArray());

            var camera = BuildCamera();
            BoothContentBuilder.Populate(new BoothContentBuilder.Handles(
                _barrierArm, _vehicleRoot, _subjectRoot, Headlights.ToArray(), Taillights.ToArray(),
                _permitPaper, _permitAnchor,
                _manualPages, _manualAnchor,
                _logAnchor, _mailMesh, _mailAnchor, _clockAnchor,
                ScreenAnchors, Switches, SwitchLabelAnchors,
                IntercomKeys, IntercomKeyLabels,
                camera.gameObject));
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
            RenderSettings.ambientSkyColor = new Color(0.052f, 0.060f, 0.082f);
            RenderSettings.ambientEquatorColor = new Color(0.036f, 0.041f, 0.052f);
            RenderSettings.ambientGroundColor = new Color(0.022f, 0.024f, 0.029f);
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
            RenderSettings.customReflectionTexture = null;

            // Fog is the atmosphere and it is nearly free. Exponential-squared keeps the
            // booth interior almost clear while burying the road beyond ten metres.
            // The fog is also the backdrop the subject is read against. A silhouette is
            // only a silhouette if what is behind it is brighter than it is, so the fog is
            // deliberately lifted well above the figure's albedo. The first pass had it at
            // 0.105 and the subject vanished into the night.
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = FogColor;
            RenderSettings.fogDensity = 0.098f;

            RenderSettings.skybox = null;

            // A dim cold key from above stands in for moonlight through cloud. It is the
            // only shadow-casting light outside; everything else is practical.
            var moon = new GameObject("Moonlight").AddComponent<Light>();
            moon.type = LightType.Directional;
            moon.color = new Color(0.55f, 0.66f, 0.90f);
            moon.intensity = 0.38f;
            moon.shadows = LightShadows.Hard;
            moon.shadowStrength = 0.75f;
            moon.transform.rotation = Quaternion.Euler(38f, 200f, 0f);
        }

        // ------------------------------------------------------------------------ shell --

        private static void BuildShell(Transform parent)
        {
            // The first grunge pass used heavy vertical streaking and it read as wood grain
            // rather than as a dirty concrete wall. Higher frequency, lower contrast and
            // much less streaking gives blotchy damp staining instead.
            var wallGrunge = Grunge("Grunge_Wall", 512, 11.0f, 1.15f, 0.16f, 7301);
            var floorGrunge = Grunge("Grunge_Floor", 512, 9.0f, 1.30f, 0.0f, 5512);

            var concrete = Mat("Concrete", new Color(0.300f, 0.294f, 0.272f), 0.06f, 0f, null, wallGrunge, 1.4f);
            var concreteDark = Mat("ConcreteDark", new Color(0.160f, 0.163f, 0.166f), 0.04f, 0f, null,
                floorGrunge, 2.6f);

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

            // Grimy glass. One transparent quad, and it does more for "you are inside
            // looking out" than any amount of extra geometry outside would.
            var glass = TransparentMat("WindowGlass", new Color(0.52f, 0.55f, 0.58f, 0.16f), 0.88f,
                Grunge("Grunge_Glass", 512, 6.0f, 1.9f, 0.65f, 3390), 1f);
            Box("Glass", parent, new Vector3(0f, (WindowMinY + WindowMaxY) * 0.5f, RoomMaxZ + 0.02f),
                new Vector3(WindowMaxX - WindowMinX, WindowMaxY - WindowMinY, 0.008f), glass);

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
            var deskGrunge = Grunge("Grunge_Desk", 512, 7.0f, 1.40f, 0.20f, 9184);
            var deskMat = Mat("DeskSteel", new Color(0.215f, 0.210f, 0.200f), 0.34f, 0.55f, null,
                deskGrunge, 2.2f);
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
            var crtShell = Mat("CRTShell", new Color(0.215f, 0.205f, 0.170f), 0.22f);

            // Unlit, because a phosphor screen emits and does not reflect. As a Lit
            // material the desk lamp fell across the nearest monitor and washed its face
            // from green to pale yellow, which no CRT has ever done.
            var crtScreen = UnlitMat("CRTScreen", new Color(0.030f, 0.150f, 0.064f));
            var brass = Mat("Brass", new Color(0.44f, 0.34f, 0.15f), 0.38f, 0.70f);
            var paper = Mat("Paper", new Color(0.660f, 0.636f, 0.552f), 0.05f, 0f, null,
                Grunge("Grunge_Paper", 256, 3.0f, 0.55f, 0.0f, 2231), 1f);
            var darkPlastic = Mat("DarkPlastic", new Color(0.055f, 0.055f, 0.062f), 0.28f);
            var enamel = Mat("Enamel", new Color(0.700f, 0.690f, 0.650f), 0.52f);
            var wood = Mat("Wood", new Color(0.220f, 0.135f, 0.075f), 0.18f);

            var monitors = new GameObject("Monitors").transform;
            monitors.SetParent(parent, false);

            // A tight arc of three wide, shallow monitors rather than three deep boxes.
            // The concept art reads as one continuous band of screens across the desk, and
            // the chunky separated CRTs of the previous pass did not.
            var placements = new[]
            {
                (x: -0.70f, yaw: 22f),
                (x: 0.02f, yaw: 0f),
                (x: 0.74f, yaw: -22f),
            };

            for (var i = 0; i < placements.Length; i++)
            {
                var (x, yaw) = placements[i];
                var pivot = new GameObject($"CRT_{i}").transform;
                pivot.SetParent(monitors, false);
                pivot.SetPositionAndRotation(new Vector3(x, DeskTopY, 1.40f), Quaternion.Euler(0f, yaw, 0f));

                Box("Shell", pivot, new Vector3(0f, 0.200f, 0f), new Vector3(0.62f, 0.40f, 0.26f), crtShell);
                Box("Hood", pivot, new Vector3(0f, 0.395f, -0.07f), new Vector3(0.64f, 0.026f, 0.15f), crtShell);
                Box("Bezel", pivot, new Vector3(0f, 0.205f, -0.132f), new Vector3(0.560f, 0.310f, 0.010f), darkPlastic);
                Box("Screen", pivot, new Vector3(0f, 0.205f, -0.140f),
                    new Vector3(0.520f, 0.280f, 0.008f), crtScreen);
                ScreenAnchors.Add(Anchor($"ScreenText_{i}", pivot,
                    new Vector3(0f, 0.205f, -0.150f), Quaternion.identity));


                var glow = new GameObject("Glow").AddComponent<Light>();
                glow.transform.SetParent(pivot, false);
                glow.transform.localPosition = new Vector3(0f, 0.17f, -0.26f);
                glow.type = LightType.Point;
                glow.color = new Color(0.34f, 0.92f, 0.46f);
                glow.intensity = 0.13f;
                glow.range = 0.85f;
                glow.shadows = LightShadows.None;
            }

            var ink = Mat("Ink", new Color(0.085f, 0.080f, 0.075f), 0.05f);

            // The form under the lamp, centre foreground. In the concept art this is the
            // brightest object in the frame and the thing the eye lands on first, so it is
            // ruled with printed lines rather than left as a blank rectangle.
            var forms = new GameObject("Paperwork").transform;
            forms.SetParent(parent, false);
            var mainForm = new GameObject("Form_Main").transform;
            mainForm.SetParent(forms, false);
            mainForm.SetPositionAndRotation(new Vector3(0.06f, DeskTopY + 0.004f, 0.52f),
                Quaternion.Euler(0f, -6f, 0f));
            _permitPaper = Box("Sheet", mainForm, Vector3.zero,
                new Vector3(0.300f, 0.003f, 0.400f), paper).transform;
            _permitAnchor = Anchor("PermitText", mainForm, new Vector3(0f, 0.003f, 0f),
                Quaternion.Euler(90f, 0f, 0f));

            // The mail tray, front left. Whatever the office sent tonight lands here.
            _mailMesh = Box("MailTray", forms, new Vector3(-0.54f, DeskTopY + 0.002f, 0.40f),
                new Vector3(0.246f, 0.004f, 0.336f),
                Mat("PaperMail", new Color(0.560f, 0.540f, 0.470f), 0.05f, 0f, null,
                    Grunge("Grunge_Mail", 256, 3.4f, 0.60f, 0.0f, 5150), 1f),
                new Vector3(0f, -9f, 0f)).transform;
            _mailAnchor = Anchor("MailText", forms, new Vector3(-0.54f, DeskTopY + 0.006f, 0.40f),
                Quaternion.Euler(0f, -9f, 0f) * Quaternion.Euler(90f, 0f, 0f));
            Box("Form_Stack_B", forms, new Vector3(-0.30f, DeskTopY + 0.012f, 0.73f),
                new Vector3(0.225f, 0.005f, 0.315f), paper, new Vector3(0f, 6f, 0f));
            _logAnchor = Anchor("LogText", forms, new Vector3(-0.30f, DeskTopY + 0.016f, 0.73f),
                Quaternion.Euler(0f, 6f, 0f) * Quaternion.Euler(90f, 0f, 0f));

            Cylinder("Pencil", forms, new Vector3(0.28f, DeskTopY + 0.008f, 0.42f),
                new Vector3(0.011f, 0.088f, 0.011f), Mat("Pencil", new Color(0.42f, 0.30f, 0.07f), 0.30f),
                new Vector3(90f, 24f, 0f));

            // The manual: a ring binder lying open, which is the object the whole game is
            // actually about.
            var binder = new GameObject("Manual").transform;
            binder.SetParent(parent, false);
            binder.SetPositionAndRotation(new Vector3(0.86f, DeskTopY, 0.88f), Quaternion.Euler(0f, -22f, 0f));
            Box("Cover", binder, new Vector3(0f, 0.012f, 0f), new Vector3(0.290f, 0.024f, 0.360f), darkPlastic);
            _manualPages = Box("Pages", binder, new Vector3(0f, 0.029f, 0f),
                new Vector3(0.272f, 0.016f, 0.344f), paper).transform;
            _manualAnchor = Anchor("ManualText", binder, new Vector3(0f, 0.038f, 0f),
                Quaternion.Euler(90f, 0f, 0f));

            // Classification panel: the dial and switches that decide whether a subject
            // passes. Moved to the centre, directly under the monitors, where the concept
            // art puts it.
            var panel = new GameObject("ClassificationPanel").transform;
            panel.SetParent(parent, false);
            panel.SetPositionAndRotation(new Vector3(-0.10f, DeskTopY + 0.030f, 1.02f),
                Quaternion.Euler(-20f, 0f, 0f));
            Box("Plate", panel, Vector3.zero, new Vector3(0.560f, 0.028f, 0.210f), darkPlastic);
            Cylinder("DialFace", panel, new Vector3(-0.135f, 0.020f, 0f), new Vector3(0.150f, 0.006f, 0.150f), brass);
            Cylinder("DialHub", panel, new Vector3(-0.135f, 0.032f, 0f), new Vector3(0.048f, 0.014f, 0.048f), darkPlastic);
            for (var i = 0; i < 4; i++)
            {
                Switches.Add(Cylinder($"Switch_{i}", panel, new Vector3(0.020f + i * 0.075f, 0.030f, 0.010f),
                    new Vector3(0.032f, 0.028f, 0.032f), brass).transform);
                Box($"Label_{i}", panel, new Vector3(0.020f + i * 0.075f, 0.016f, -0.070f),
                    new Vector3(0.058f, 0.003f, 0.026f), darkPlastic);
                SwitchLabelAnchors.Add(Anchor($"LabelText_{i}", panel,
                    new Vector3(0.020f + i * 0.075f, 0.019f, -0.070f),
                    Quaternion.Euler(90f, 0f, 0f)));
            }

            // The shift clock, mounted on the desk beside the switches. It is the only
            // thing in the booth that tells the player they are running out of anything.
            var clock = new GameObject("ShiftClock").transform;
            clock.SetParent(parent, false);
            clock.SetPositionAndRotation(new Vector3(0.36f, DeskTopY + 0.030f, 0.72f),
                Quaternion.Euler(-24f, -8f, 0f));
            Box("Case", clock, Vector3.zero, new Vector3(0.190f, 0.060f, 0.096f), darkPlastic);
            Box("Face", clock, new Vector3(0f, 0.006f, -0.049f), new Vector3(0.150f, 0.044f, 0.006f),
                Mat("ClockFace", new Color(0.020f, 0.030f, 0.024f), 0.5f, 0f,
                    new Color(0.020f, 0.070f, 0.034f)));
            _clockAnchor = Anchor("ClockText", clock, new Vector3(0f, 0.006f, -0.054f),
                Quaternion.identity);

            // Intercom keypad. Deliberately across the desk from the verdict switches:
            // one row asks a question and the other ends someone's night.
            var keypad = new GameObject("IntercomKeypad").transform;
            keypad.SetParent(parent, false);
            keypad.SetPositionAndRotation(new Vector3(-0.78f, DeskTopY + 0.026f, 0.80f),
                Quaternion.Euler(-18f, 12f, 0f));
            Box("Plate", keypad, Vector3.zero, new Vector3(0.320f, 0.024f, 0.130f), darkPlastic);
            Box("Grille", keypad, new Vector3(0f, 0.014f, 0.044f), new Vector3(0.250f, 0.004f, 0.030f), brass);
            for (var i = 0; i < 4; i++)
            {
                var x = -0.114f + i * 0.076f;
                IntercomKeys.Add(Box($"Key_{i}", keypad, new Vector3(x, 0.024f, -0.014f),
                    new Vector3(0.054f, 0.022f, 0.038f), brass).transform);
                IntercomKeyLabels.Add(Anchor($"KeyLabel_{i}", keypad,
                    new Vector3(x, 0.014f, -0.052f), Quaternion.Euler(90f, 0f, 0f)));
            }

            // Stamp and ink pad.
            Box("InkPad", parent, new Vector3(0.62f, DeskTopY + 0.010f, 0.24f),
                new Vector3(0.115f, 0.020f, 0.090f), darkPlastic);
            Box("Stamp_Head", parent, new Vector3(0.78f, DeskTopY + 0.026f, 0.26f),
                new Vector3(0.068f, 0.052f, 0.068f), wood);
            Cylinder("Stamp_Handle", parent, new Vector3(0.78f, DeskTopY + 0.078f, 0.26f),
                new Vector3(0.030f, 0.028f, 0.030f), wood);

            // Telephone, front left. It is a large silhouette in the concept art and it
            // anchors the bottom-left corner of the composition, which was empty before.
            var phone = new GameObject("Telephone").transform;
            phone.SetParent(parent, false);
            phone.SetPositionAndRotation(new Vector3(-0.94f, DeskTopY, 0.24f), Quaternion.Euler(0f, 26f, 0f));
            Box("Base", phone, new Vector3(0f, 0.040f, 0f), new Vector3(0.250f, 0.080f, 0.290f), darkPlastic);
            Cylinder("Dial", phone, new Vector3(0f, 0.083f, -0.070f), new Vector3(0.135f, 0.005f, 0.135f), brass);
            Box("Cradle_L", phone, new Vector3(-0.088f, 0.098f, 0.075f), new Vector3(0.055f, 0.038f, 0.070f), darkPlastic);
            Box("Cradle_R", phone, new Vector3(0.088f, 0.098f, 0.075f), new Vector3(0.055f, 0.038f, 0.070f), darkPlastic);
            Box("Handset", phone, new Vector3(0f, 0.128f, 0.075f), new Vector3(0.265f, 0.062f, 0.082f), darkPlastic);

            // Coffee. Present in the concept art and it is the object that says a person
            // has been sitting here for hours.
            Cylinder("Mug", parent, new Vector3(1.14f, DeskTopY + 0.052f, 0.22f),
                new Vector3(0.096f, 0.052f, 0.096f), enamel);
            Cylinder("Mug_Coffee", parent, new Vector3(1.14f, DeskTopY + 0.096f, 0.22f),
                new Vector3(0.082f, 0.004f, 0.082f),
                Mat("Coffee", new Color(0.055f, 0.030f, 0.018f), 0.72f));
            Box("Mug_Handle", parent, new Vector3(1.205f, DeskTopY + 0.052f, 0.22f),
                new Vector3(0.036f, 0.048f, 0.014f), enamel);

            BuildWallNotes(parent, Mat("PaperWall", new Color(0.430f, 0.412f, 0.352f), 0.05f, 0f, null,
                Grunge("Grunge_Note", 256, 4.0f, 0.70f, 0.0f, 6612), 1f), ink);
        }

        /// <summary>Notices, amendments and torn-off scraps pinned to the walls around the
        /// window. Bare walls read as an unfinished blockout; this is the cheapest way to
        /// say that somebody has worked in this room for a long time, and it doubles as
        /// diegetic space for the manual amendments the game is built around.</summary>
        private static void BuildWallNotes(Transform parent, Material paper, Material ink)
        {
            var notes = new GameObject("WallNotes").transform;
            notes.SetParent(parent, false);

            var random = new System.Random(88);

            void Note(Vector3 position, Vector2 size, Vector3 euler, int rules)
            {
                var note = new GameObject("Note").transform;
                note.SetParent(notes, false);
                note.SetPositionAndRotation(position, Quaternion.Euler(euler));
                Box("Sheet", note, Vector3.zero, new Vector3(size.x, size.y, 0.002f), paper);
                for (var i = 0; i < rules; i++)
                {
                    Box($"Rule_{i}", note,
                        new Vector3(0f, size.y * 0.34f - i * (size.y * 0.68f / Mathf.Max(1, rules - 1)), -0.0016f),
                        new Vector3(size.x * 0.78f, 0.0035f, 0.002f), ink);
                }
            }

            // On the front wall, flanking the window.
            Note(new Vector3(-1.56f, 1.60f, RoomMaxZ - 0.005f), new Vector2(0.135f, 0.185f),
                new Vector3(0f, 0f, -3f), 5);
            Note(new Vector3(1.56f, 1.68f, RoomMaxZ - 0.005f), new Vector2(0.125f, 0.170f),
                new Vector3(0f, 0f, 4f), 4);
            Note(new Vector3(1.54f, 1.36f, RoomMaxZ - 0.005f), new Vector2(0.105f, 0.085f),
                new Vector3(0f, 0f, -6f), 2);

            // On the side walls, angled away from the camera.
            for (var i = 0; i < 4; i++)
            {
                var y = 1.32f + (float)random.NextDouble() * 0.62f;
                var z = 0.75f + i * 0.24f;
                Note(new Vector3(RoomMinX + 0.008f, y, z), new Vector2(0.145f, 0.19f),
                    new Vector3(0f, 90f, (float)random.NextDouble() * 8f - 4f), 4);
            }

            for (var i = 0; i < 3; i++)
            {
                var y = 1.40f + (float)random.NextDouble() * 0.52f;
                var z = 0.85f + i * 0.28f;
                Note(new Vector3(RoomMaxX - 0.008f, y, z), new Vector2(0.135f, 0.175f),
                    new Vector3(0f, -90f, (float)random.NextDouble() * 8f - 4f), 3);
            }
        }

        private static void BuildLamp(Transform parent)
        {
            var lampMat = Mat("LampEnamel", new Color(0.400f, 0.120f, 0.080f), 0.45f, 0.2f);
            var lampInner = Mat("LampInner", new Color(0.02f, 0.02f, 0.02f), 0.1f, 0f,
                new Color(1.00f, 0.72f, 0.38f) * 2.6f);

            // Far enough in from the wall to stay in frame, and tall enough that the shade
            // clears the CRTs. The first pass put it at the very edge of the view where it
            // read as an orange smear rather than as the source of the light.
            var origin = new Vector3(-1.30f, DeskTopY, 1.06f);
            var lamp = new GameObject("DeskLamp").transform;
            lamp.SetParent(parent, false);
            lamp.localPosition = origin;

            Cylinder("Base", lamp, new Vector3(0f, 0.014f, 0f), new Vector3(0.160f, 0.014f, 0.160f), lampMat);
            NoShadowCast(Cylinder("Stem", lamp, new Vector3(0.055f, 0.210f, 0.015f),
                new Vector3(0.024f, 0.205f, 0.024f), lampMat, new Vector3(-4f, 0f, -15f)));
            NoShadowCast(Cylinder("Shade", lamp, new Vector3(0.165f, 0.415f, 0.045f),
                new Vector3(0.220f, 0.085f, 0.220f), lampMat, new Vector3(30f, 0f, 26f)));
            NoShadowCast(Cylinder("Bulb", lamp, new Vector3(0.170f, 0.372f, 0.048f),
                new Vector3(0.145f, 0.007f, 0.145f), lampInner, new Vector3(30f, 0f, 26f)));

            // The key light, aimed explicitly at the paperwork rather than at an angle
            // guessed in Euler degrees. This is the pool of warm light the whole shot is
            // built around, and the first pass missed the desk entirely.
            var keyPosition = origin + new Vector3(0.170f, 0.360f, 0.048f);
            var keyTarget = new Vector3(0.16f, DeskTopY, 0.52f);
            _lampOrigin = keyPosition;
            _lampTarget = keyTarget;
            var light = new GameObject("Key").AddComponent<Light>();
            light.transform.SetParent(lamp, true);
            light.transform.SetPositionAndRotation(keyPosition,
                Quaternion.LookRotation(keyTarget - keyPosition, Vector3.up));
            light.type = LightType.Spot;
            light.color = new Color(1.00f, 0.735f, 0.455f);
            light.intensity = 16f;
            light.range = 4.5f;
            light.spotAngle = 104f;
            light.innerSpotAngle = 26f;
            light.shadows = LightShadows.Hard;
            light.shadowStrength = 0.80f;

            // A dim, high, wide source over the whole desk.
            //
            // Without it the lamp had to do everything, and a point source cannot: at 1/d²
            // the binder two metres away needed an intensity that blew the intercom keypad
            // seventy centimetres away into a solid gold slab brighter than any of the
            // paperwork. Splitting the job lets the lamp stay a lamp.
            var fill = new GameObject("DeskFill").AddComponent<Light>();
            fill.transform.SetParent(lamp.parent, false);
            fill.transform.SetPositionAndRotation(new Vector3(0.30f, 2.05f, 0.55f),
                Quaternion.Euler(90f, 0f, 0f));
            fill.type = LightType.Spot;
            fill.color = new Color(1.00f, 0.83f, 0.62f);
            fill.intensity = 5.2f;
            fill.range = 4.2f;
            fill.spotAngle = 120f;
            fill.innerSpotAngle = 40f;
            fill.shadows = LightShadows.None;

            // A weak unshadowed bounce so the wall behind the lamp is not pure black.
            var bounce = new GameObject("Bounce").AddComponent<Light>();
            bounce.transform.SetParent(lamp, false);
            bounce.transform.localPosition = new Vector3(0.10f, 0.24f, 0.05f);
            bounce.type = LightType.Point;
            bounce.color = new Color(1.00f, 0.66f, 0.38f);
            bounce.intensity = 1.6f;
            bounce.range = 2.9f;
            bounce.shadows = LightShadows.None;
        }

        // ---------------------------------------------------------------------- outside --

        /// <summary>The furniture of a road: kerbs, delineator posts, a chicane and a sign.
        ///
        /// This is what turns the view through the window from a barrier floating in grey
        /// into somewhere. The reflector tips matter most: they are the only things out
        /// there that catch the vehicle's headlamps, so as it comes down the road they light
        /// up in sequence and give the fog a depth the fog itself cannot.</summary>
        private static void BuildRoadside(Transform parent, Material treeline)
        {
            var roadside = new GameObject("Roadside").transform;
            roadside.SetParent(parent, false);

            var kerb = Mat("Kerb", new Color(0.115f, 0.112f, 0.105f), 0.16f, 0f, null,
                Grunge("Grunge_Kerb", 256, 5.0f, 0.9f, 0f, 8123), 6f);
            var postMaterial = Mat("MarkerPost", new Color(0.300f, 0.295f, 0.270f), 0.18f);
            var concrete = Mat("Chicane", new Color(0.190f, 0.185f, 0.170f), 0.14f, 0f, null,
                Grunge("Grunge_Chicane", 256, 4.2f, 1.1f, 0f, 3311), 2.4f);

            // Emissive rather than merely light-coloured: a retroreflector returns light to
            // its source, which no material in a rasteriser does, so it is faked.
            var reflectorAmber = Mat("ReflectorAmber", new Color(0.20f, 0.09f, 0.01f), 0.5f, 0f,
                new Color(1.00f, 0.42f, 0.05f) * 2.6f);
            var reflectorWhite = Mat("ReflectorWhite", new Color(0.18f, 0.18f, 0.17f), 0.5f, 0f,
                new Color(0.90f, 0.88f, 0.78f) * 2.0f);

            foreach (var side in new[] { -1f, 1f })
            {
                Box($"Kerb_{side:F0}", roadside, new Vector3(side * 4.3f, 0.07f, 11f),
                    new Vector3(0.34f, 0.14f, 22f), kerb);
            }

            for (var i = 0; i < 6; i++)
            {
                var z = 5.6f + i * 2.15f;
                foreach (var side in new[] { -1f, 1f })
                {
                    var post = new GameObject($"Marker_{i}_{side:F0}").transform;
                    post.SetParent(roadside, false);
                    post.localPosition = new Vector3(side * 4.32f, 0f, z);
                    Box("Post", post, new Vector3(0f, 0.76f, 0f), new Vector3(0.09f, 1.52f, 0.06f),
                        postMaterial);
                    Box("Reflector", post, new Vector3(side * -0.035f, 1.28f, 0f),
                        new Vector3(0.03f, 0.13f, 0.05f), side < 0f ? reflectorWhite : reflectorAmber);
                    BoothAtmosphere.Glare($"MarkerGlare_{i}_{side:F0}", post,
                        new Vector3(side * -0.06f, 1.28f, 0f), 0.42f,
                        side < 0f ? new Color(0.60f, 0.58f, 0.50f) : new Color(0.72f, 0.30f, 0.05f));
                }
            }

            // A chicane. Vehicles have to slow and weave, which is why one stops here at all.
            var chicane = new[] { (-1.6f, 12.4f), (1.9f, 10.2f), (-1.9f, 8.0f) };
            for (var i = 0; i < chicane.Length; i++)
            {
                var (x, z) = chicane[i];
                Box($"Block_{i}", roadside, new Vector3(x, 0.34f, z), new Vector3(1.5f, 0.68f, 0.44f),
                    concrete);
                Box($"BlockStripe_{i}", roadside, new Vector3(x, 0.60f, z - 0.23f),
                    new Vector3(1.2f, 0.10f, 0.02f), reflectorWhite);

                // A wand on each block, because the block itself sits under the sill line.
                Box($"BlockWand_{i}", roadside, new Vector3(x + 0.6f, 1.06f, z),
                    new Vector3(0.05f, 0.76f, 0.05f), postMaterial);
                Box($"BlockWandTip_{i}", roadside, new Vector3(x + 0.6f, 1.38f, z - 0.03f),
                    new Vector3(0.07f, 0.14f, 0.04f), reflectorAmber);
            }

            // A sign on the near side, angled at the driver. Unreadable at this distance and
            // in this fog, which is the point: it is a shape you recognise, not information.
            var signPost = new GameObject("Sign").transform;
            signPost.SetParent(roadside, false);
            signPost.localPosition = new Vector3(-3.5f, 0f, 6.4f);
            signPost.localRotation = Quaternion.Euler(0f, 24f, 0f);
            Box("Mast", signPost, new Vector3(0f, 1.05f, 0f), new Vector3(0.08f, 2.10f, 0.08f), postMaterial);
            Box("Board", signPost, new Vector3(0f, 2.06f, 0f), new Vector3(0.92f, 0.66f, 0.05f),
                Mat("SignFace", new Color(0.34f, 0.31f, 0.24f), 0.22f, 0f,
                    new Color(0.10f, 0.09f, 0.07f)));
            Box("Border", signPost, new Vector3(0f, 2.06f, -0.03f), new Vector3(0.80f, 0.54f, 0.02f),
                Mat("SignBorder", new Color(0.42f, 0.10f, 0.08f), 0.22f));

            BuildDistantLights(roadside);

            // A fence running back along the left, cut off by the fog rather than ending.
            for (var i = 0; i < 9; i++)
            {
                var z = 5.2f + i * 1.55f;
                Box($"FencePost_{i}", roadside, new Vector3(-5.4f, 0.72f, z),
                    new Vector3(0.10f, 1.44f, 0.10f), treeline);
            }

            foreach (var y in new[] { 0.52f, 1.02f, 1.38f })
            {
                Box($"FenceWire_{y:F2}", roadside, new Vector3(-5.4f, y, 11.4f),
                    new Vector3(0.03f, 0.03f, 13.8f), treeline);
            }
        }

        /// <summary>Lights receding into the fog.
        ///
        /// Dark objects do not give fog depth. Past ten metres or so everything unlit
        /// converges on the fog's own colour and simply disappears, which is why a treeline
        /// at twenty metres was invisible no matter how large it was. Light is the only
        /// thing that survives the distance, so the depth out there is built from lamps: a
        /// beacon at ten metres, a work light at fifteen, lit windows at eighteen.</summary>
        private static void BuildDistantLights(Transform parent)
        {
            var mast = Mat("DistantMast", new Color(0.10f, 0.10f, 0.09f), 0.2f);

            // Amber beacon, the kind that sits on top of a barrier housing.
            var beacon = new GameObject("Beacon").transform;
            beacon.SetParent(parent, false);
            beacon.localPosition = new Vector3(3.55f, 0f, 9.8f);
            Box("Mast", beacon, new Vector3(0f, 1.30f, 0f), new Vector3(0.09f, 2.60f, 0.09f), mast);
            Box("Lamp", beacon, new Vector3(0f, 2.68f, 0f), new Vector3(0.20f, 0.22f, 0.20f),
                Mat("BeaconLamp", new Color(0.30f, 0.12f, 0.01f), 0.4f, 0f,
                    new Color(1.00f, 0.46f, 0.06f) * 4.5f));
            BoothAtmosphere.Glare("BeaconGlare", beacon, new Vector3(0f, 2.68f, -0.14f), 1.9f,
                new Color(1.05f, 0.44f, 0.07f));

            // A work light further back, pointed away, so all that reaches the booth is the
            // halo and the pool it throws on the fog.
            var work = new GameObject("WorkLight").transform;
            work.SetParent(parent, false);
            work.localPosition = new Vector3(-6.2f, 0f, 14.6f);
            Box("Mast", work, new Vector3(0f, 1.85f, 0f), new Vector3(0.12f, 3.70f, 0.12f), mast);
            BoothAtmosphere.Glare("WorkGlare", work, new Vector3(0f, 3.60f, -0.20f), 4.2f,
                new Color(0.62f, 0.58f, 0.46f));

            // A hut with two lit windows. At eighteen metres the building is fog and the
            // windows are all that is left of it, which is exactly the read.
            var hut = new GameObject("Hut").transform;
            hut.SetParent(parent, false);
            hut.localPosition = new Vector3(7.4f, 0f, 17.5f);
            Box("Shell", hut, new Vector3(0f, 1.55f, 0f), new Vector3(4.6f, 3.10f, 3.4f),
                Mat("HutShell", new Color(0.045f, 0.045f, 0.048f), 0.10f));
            foreach (var offset in new[] { -1.05f, 1.05f })
            {
                Box($"Window_{offset:F1}", hut, new Vector3(offset, 1.85f, -1.72f),
                    new Vector3(0.86f, 0.62f, 0.05f),
                    Mat("HutWindow", new Color(0.24f, 0.20f, 0.12f), 0.3f, 0f,
                        new Color(1.00f, 0.80f, 0.48f) * 2.4f));
                BoothAtmosphere.Glare($"HutGlare_{offset:F1}", hut,
                    new Vector3(offset, 1.85f, -1.80f), 2.1f, new Color(0.78f, 0.60f, 0.34f));
            }
        }

        private static void BuildOutside(Transform parent)
        {
            var asphaltGrunge = Grunge("Grunge_Asphalt", 512, 11.0f, 1.25f, 0.0f, 4407);
            var asphalt = Mat("Asphalt", new Color(0.075f, 0.075f, 0.080f), 0.22f, 0f, null,
                asphaltGrunge, 9f);
            var paint = Mat("RoadPaint", new Color(0.44f, 0.42f, 0.36f), 0.10f);
            var barrierRed = Mat("BarrierRed", new Color(0.400f, 0.070f, 0.055f), 0.20f);
            var barrierWhite = Mat("BarrierWhite", new Color(0.560f, 0.545f, 0.505f), 0.20f);
            var figure = Mat("Figure", new Color(0.010f, 0.010f, 0.013f), 0.08f);
            var tail = Mat("Taillight", new Color(0.08f, 0.005f, 0.005f), 0.4f, 0f,
                new Color(1.00f, 0.09f, 0.06f) * 7.0f);

            Box("Road", parent, new Vector3(0f, -0.20f, 16f), new Vector3(24f, 0.4f, 34f), asphalt);

            // A wall of unlit fog-coloured geometry far down the road. Without it the sky
            // above the road is the camera's clear colour, the fog has nothing to blend
            // into, and anything standing in front of it reads as black on black.
            var haze = Mat("Haze", new Color(0.02f, 0.02f, 0.025f), 0.0f, 0f, FogColor * 1.35f);
            Box("Backdrop", parent, new Vector3(0f, 8f, 30f), new Vector3(60f, 22f, 0.4f), haze);

            // A treeline, deep enough in the fog that it is just darker patches. Without it
            // the view through the window is a flat grey rectangle.
            //
            // Pulled in from seventeen to twenty-three metres, where the fog left 6 percent
            // of the geometry showing and the whole treeline was invisible. Everything that
            // is meant to read sits between five and sixteen metres now, which is the band
            // this fog density actually passes light through.
            var treeline = Mat("Treeline", new Color(0.020f, 0.024f, 0.026f), 0.05f);
            var random = new System.Random(41);
            for (var i = 0; i < 16; i++)
            {
                var x = -15f + i * 1.95f + (float)random.NextDouble() * 1.1f;
                var height = 3.8f + (float)random.NextDouble() * 4.2f;
                var z = 12.5f + (float)random.NextDouble() * 5.5f;
                Box($"Tree_{i}", parent, new Vector3(x, height * 0.5f, z),
                    new Vector3(0.9f + (float)random.NextDouble() * 0.7f, height, 0.9f), treeline);
            }

            BuildRoadside(parent, treeline);

            var floodPole = new GameObject("CheckpointFlood").transform;
            floodPole.SetParent(parent, false);
            floodPole.localPosition = new Vector3(-2.55f, 0f, 4.10f);
            Box("Mast", floodPole, new Vector3(0f, 1.90f, 0f), new Vector3(0.11f, 3.80f, 0.11f),
                Mat("FloodMast", new Color(0.13f, 0.13f, 0.12f), 0.30f, 0.6f));
            Box("Head", floodPole, new Vector3(0.28f, 3.72f, 0f), new Vector3(0.44f, 0.20f, 0.30f),
                Mat("FloodHead", new Color(0.16f, 0.15f, 0.12f), 0.35f, 0.5f));
            Box("Lens", floodPole, new Vector3(0.44f, 3.66f, 0f), new Vector3(0.10f, 0.16f, 0.26f),
                Mat("FloodLens", new Color(0.05f, 0.05f, 0.04f), 0.6f, 0f,
                    new Color(1.00f, 0.86f, 0.62f) * 2.2f));

            var flood = new GameObject("FloodLight").AddComponent<Light>();
            flood.transform.SetParent(floodPole, true);
            var floodPosition = floodPole.position + new Vector3(0.46f, 3.64f, 0f);
            flood.transform.SetPositionAndRotation(floodPosition,
                Quaternion.LookRotation(new Vector3(0.7f, 0.2f, 6.2f) - floodPosition, Vector3.up));
            flood.type = LightType.Spot;
            flood.color = new Color(1.00f, 0.88f, 0.68f);
            flood.intensity = 26f;
            flood.range = 16f;
            flood.spotAngle = 78f;
            flood.innerSpotAngle = 18f;
            flood.shadows = LightShadows.None;

            foreach (var (x, z) in new[] { (-3.9f, 11.5f), (4.4f, 19.0f) })
            {
                Box($"Pole_{x:F1}", parent, new Vector3(x, 3.1f, z), new Vector3(0.16f, 6.2f, 0.16f), treeline);
                Box($"PoleArm_{x:F1}", parent, new Vector3(x, 5.7f, z), new Vector3(1.5f, 0.10f, 0.10f), treeline);
            }
            for (var i = 0; i < 7; i++)
            {
                Box($"RoadLine_{i}", parent, new Vector3(-0.55f, 0.002f, 6.5f + i * 3.2f),
                    new Vector3(0.14f, 0.01f, 1.5f), paint);
            }

            // Barrier arm across the road, striped.
            var barrier = new GameObject("Barrier").transform;
            barrier.SetParent(parent, false);
            // Raised and pushed back from the first pass, where the window sill hid the
            // whole arm and only the housing showed.
            barrier.localPosition = new Vector3(0f, 0f, 4.60f);
            Cylinder("Post", barrier, new Vector3(-2.05f, 0.62f, 0f), new Vector3(0.14f, 0.62f, 0.14f),
                Mat("BarrierPost", new Color(0.16f, 0.16f, 0.15f), 0.30f, 0.6f));
            Box("Housing", barrier, new Vector3(-2.05f, 1.28f, 0f), new Vector3(0.26f, 0.26f, 0.24f),
                Mat("BarrierHousing", new Color(0.34f, 0.30f, 0.10f), 0.35f, 0.4f));
            // The arm hangs off a pivot at the post. Eight loose segments could not lift.
            _barrierArm = Anchor("ArmPivot", barrier, new Vector3(-2.05f, 1.22f, 0f), Quaternion.identity);
            for (var i = 0; i < 8; i++)
            {
                Box($"Arm_{i}", _barrierArm, new Vector3(0.43f + i * 0.62f, 0f, 0f),
                    new Vector3(0.62f, 0.09f, 0.09f), i % 2 == 0 ? barrierWhite : barrierRed);
            }
            Box("ArmTip", _barrierArm, new Vector3(5.10f, 0f, 0f),
                new Vector3(0.10f, 0.14f, 0.14f), barrierRed);

            // The subject. Never clearly seen: it stands in fog, backlit by taillights, and
            // its proportions are wrong rather than its features being monstrous. Arms are
            // long, the head sits high, and it does not move.
            var subject = new GameObject("Subject").transform;
            subject.SetParent(parent, false);
            _subjectRoot = subject;
            subject.SetPositionAndRotation(new Vector3(0.52f, 0f, 4.95f), Quaternion.Euler(0f, 184f, 0f));
            Box("Legs", subject, new Vector3(0f, 0.46f, 0f), new Vector3(0.28f, 0.92f, 0.21f), figure);
            Box("Torso", subject, new Vector3(0f, 1.31f, 0f), new Vector3(0.44f, 0.80f, 0.25f), figure);
            Box("Neck", subject, new Vector3(0f, 1.82f, 0f), new Vector3(0.09f, 0.24f, 0.09f), figure);
            Box("Head", subject, new Vector3(0f, 2.06f, 0f), new Vector3(0.18f, 0.25f, 0.19f), figure);
            Box("Arm_L", subject, new Vector3(-0.29f, 1.10f, 0.02f), new Vector3(0.10f, 1.24f, 0.12f), figure,
                new Vector3(0f, 0f, 3f));
            Box("Arm_R", subject, new Vector3(0.29f, 1.10f, 0.02f), new Vector3(0.10f, 1.24f, 0.12f), figure,
                new Vector3(0f, 0f, -3f));

            // The vehicle it stepped out of, reduced to two taillights and a dark mass.
            var vehicle = new GameObject("Vehicle").transform;
            vehicle.SetParent(parent, false);
            _vehicleRoot = vehicle;
            vehicle.localPosition = new Vector3(-0.34f, 0f, 8.20f);
            Box("Body", vehicle, new Vector3(0f, 0.85f, 0f), new Vector3(2.05f, 1.55f, 4.60f),
                Mat("VehicleBody", new Color(0.030f, 0.032f, 0.036f), 0.30f, 0.4f));
            foreach (var side in new[] { -0.78f, 0.78f })
            {
                Box($"Tail_{side:F1}", vehicle, new Vector3(side, 0.92f, 2.31f),
                    new Vector3(0.34f, 0.16f, 0.04f), tail);
                BoothAtmosphere.Glare($"TailGlare_{side:F1}", vehicle,
                    new Vector3(side, 1.00f, 2.36f), 2.4f, new Color(0.90f, 0.11f, 0.07f));
                var lamp = new GameObject($"TailLight_{side:F1}").AddComponent<Light>();
                lamp.transform.SetParent(vehicle, false);
                lamp.transform.localPosition = new Vector3(side, 0.92f, 2.55f);
                lamp.type = LightType.Point;
                lamp.color = new Color(1.00f, 0.13f, 0.08f);
                lamp.intensity = 9f;
                lamp.range = 11f;
                lamp.shadows = LightShadows.None;
                Taillights.Add(lamp);
            }

            // Headlamps point back down the road at the booth. Aimed slightly down so they
            // pool on the asphalt rather than shining straight into the window, which at
            // this distance would wash out the whole shot.
            var headlampGlass = Mat("Headlamp", new Color(0.10f, 0.10f, 0.09f), 0.6f, 0f,
                new Color(1.00f, 0.94f, 0.78f) * 4.0f);
            foreach (var side in new[] { -0.74f, 0.74f })
            {
                Box($"Headlamp_{side:F1}", vehicle, new Vector3(side, 0.86f, -2.32f),
                    new Vector3(0.30f, 0.18f, 0.05f), headlampGlass);
                BoothAtmosphere.Glare($"HeadGlare_{side:F1}", vehicle,
                    new Vector3(side, 1.05f, -2.38f), 5.0f, new Color(2.10f, 1.85f, 1.45f));
                BoothAtmosphere.Glare($"HeadCore_{side:F1}", vehicle,
                    new Vector3(side, 0.86f, -2.40f), 1.1f, new Color(3.40f, 3.10f, 2.60f));

                var beam = new GameObject($"HeadLight_{side:F1}").AddComponent<Light>();
                beam.transform.SetParent(vehicle, false);
                beam.transform.localPosition = new Vector3(side, 0.86f, -2.45f);
                beam.transform.localRotation = Quaternion.Euler(9f, 180f, 0f);
                beam.type = LightType.Spot;
                beam.color = new Color(1.00f, 0.93f, 0.80f);
                beam.intensity = 16f;
                beam.range = 22f;
                beam.spotAngle = 62f;
                beam.innerSpotAngle = 20f;
                beam.shadows = LightShadows.None;
                Headlights.Add(beam);
            }
        }

        // ----------------------------------------------------------------------- camera --

        private static Camera BuildCamera()
        {
            var go = new GameObject("PlayerCamera");
            go.tag = "MainCamera";
            // Pulled back and pitched down slightly from the first pass so more of the desk
            // is in frame. The shot has to show the paperwork, not just the monitors.
            go.transform.SetPositionAndRotation(new Vector3(-0.03f, 1.44f, -0.76f), Quaternion.Euler(13f, 0f, 0f));

            var camera = go.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = FogColor;
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

            // Four fixed poses over three different subjects. Each is captured from the
            // home pose so the images stay comparable run to run, and each shows a
            // different part of the loop, so a regression in the paperwork, the monitors
            // or the manual all get caught rather than only whatever the idle shot happens
            // to include.
            // 2 = AtTheWindow, 1 = Approaching, 3 = Admitted, matching CheckpointStage.Phase.
            var poses = new (string name, Transform lookAt, int subject, bool leanIn, int stage, int ask, int night)[]
            {
                ("booth_idle", null, 0, false, 2, -1, -1),
                ("permit", _permitPaper, 2, true, 2, -1, -1),
                ("monitors", ScreenAnchors.Count > 1 ? ScreenAnchors[1] : null, 2, false, 2, -1, -1),
                ("manual", _manualPages, 5, true, 2, -1, -1),
                ("approach", null, 3, false, 1, -1, -1),
                ("admitted", null, 3, false, 3, -1, -1),
                ("intercom", ScreenAnchors.Count > 0 ? ScreenAnchors[0] : null, 4, false, 2, 0, -1),
                ("mail", _mailAnchor, 1, true, 2, -1, -1),

                // Night 22: late enough that the binder holds amendments, which is the
                // only state in which supersession can be photographed at all.
                ("manual_amended", _manualPages, 5, true, 2, -1, 21),
                ("manual_page2", _manualPages, 5, true, 2, -1, -1),
            };

            var serialized = new SerializedObject(runner);
            var list = serialized.FindProperty("checkpoints");
            list.arraySize = poses.Length;

            for (var i = 0; i < poses.Length; i++)
            {
                var entry = list.GetArrayElementAtIndex(i);
                entry.FindPropertyRelative("name").stringValue = poses[i].name;
                entry.FindPropertyRelative("settleSeconds").floatValue = 0.6f;
                entry.FindPropertyRelative("camera").objectReferenceValue = camera;
                entry.FindPropertyRelative("lookAt").objectReferenceValue = poses[i].lookAt;
                entry.FindPropertyRelative("subjectIndex").intValue = poses[i].subject;
                entry.FindPropertyRelative("leanIn").boolValue = poses[i].leanIn;
                entry.FindPropertyRelative("stagePhase").intValue = poses[i].stage;
                entry.FindPropertyRelative("askQuestion").intValue = poses[i].ask;
                entry.FindPropertyRelative("turnPage").boolValue = poses[i].name.EndsWith("_page2");
                entry.FindPropertyRelative("night").intValue = poses[i].night;
            }

            serialized.FindProperty("reportAnchor").objectReferenceValue = _logAnchor;
            serialized.FindProperty("mailAnchor").objectReferenceValue = _mailAnchor;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void RegisterInBuildSettings()
        {
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }

        // ---------------------------------------------------------------------- helpers --

        /// <summary>Takes a light fixture's own housing out of the shadow pass.
        ///
        /// This is not cosmetic. The desk lamp's spot light sits inside its shade, so the
        /// moment additional-light shadows are active the shade occludes the cone and the
        /// desk falls dark. That is precisely the regression the screenshot check caught
        /// after the render pipeline asset was regenerated from scratch.</summary>
        private static GameObject NoShadowCast(GameObject go)
        {
            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }

            return go;
        }

        /// <summary>An empty, unit-scaled child used as a parent for text. Everything in
        /// this scene is a scaled primitive, and text parented to one of those is sheared
        /// by the parent's non-uniform scale once it is rotated to lie on the surface.</summary>
        private static Transform Anchor(string name, Transform parent, Vector3 localPosition,
            Quaternion localRotation)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = localRotation;
            go.transform.localScale = Vector3.one;
            return go.transform;
        }

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

        /// <summary>Generates a tiling grunge map and caches it as a project asset.
        ///
        /// Flat untextured colour is what made the first two renders read as an untextured
        /// blockout rather than as a room. A single multi-octave noise map multiplied into
        /// the albedo fixes most of that for almost nothing: it costs one texture fetch and
        /// no authoring time, and it is generated from a seed so it is reproducible.</summary>
        private static Texture2D Grunge(string name, int size, float frequency, float contrast,
            float streaks, int seed)
        {
            var path = $"{TexturesFolder}/{name}.png";
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null)
            {
                return existing;
            }

            MonsterSetup.EnsureFolder(TexturesFolder);

            var random = new System.Random(seed);
            var offsetX = (float)random.NextDouble() * 1000f;
            var offsetY = (float)random.NextDouble() * 1000f;

            var texture = new Texture2D(size, size, TextureFormat.RGB24, true);
            var pixels = new Color32[size * size];

            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var u = (float)x / size;
                    var v = (float)y / size;

                    // Four octaves of Perlin. Sampling on a torus would be the correct way
                    // to make it seamless; at these tiling rates the visible seam is well
                    // below the noise floor of the grain pass, so it is not worth the cost.
                    var value = 0f;
                    var amplitude = 0.5f;
                    var f = frequency;
                    for (var octave = 0; octave < 4; octave++)
                    {
                        value += amplitude * Mathf.PerlinNoise(offsetX + u * f, offsetY + v * f);
                        amplitude *= 0.5f;
                        f *= 2.07f;
                    }

                    // Vertical streaking, which is what dirt on a wall actually looks like.
                    if (streaks > 0f)
                    {
                        var streak = Mathf.PerlinNoise(offsetX + u * frequency * 3.1f, offsetY + v * 0.6f);
                        value = Mathf.Lerp(value, value * streak * 1.6f, streaks);
                    }

                    value = Mathf.Clamp01(0.5f + (value - 0.5f) * contrast);
                    var level = (byte)(Mathf.Lerp(0.45f, 1.0f, value) * 255f);
                    pixels[y * size + x] = new Color32(level, level, level, 255);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();

            File.WriteAllBytes(Path.Combine(Directory.GetCurrentDirectory(), path), texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);

            if (AssetImporter.GetAtPath(path) is TextureImporter importer)
            {
                importer.textureType = TextureImporterType.Default;
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.filterMode = FilterMode.Bilinear;
                importer.mipmapEnabled = true;
                importer.sRGBTexture = true;
                importer.maxTextureSize = size;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>URP's Lit shader needs six properties and a keyword set consistently
        /// for transparency to work; setting only _Surface leaves the material opaque in a
        /// build even though it looks right in the editor.</summary>
        private static Material TransparentMat(string name, Color baseColor, float smoothness,
            Texture2D albedo = null, float tiling = 1f)
        {
            var material = Mat(name, baseColor, smoothness, 0f, null, albedo, tiling);
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_AlphaClip", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHATEST_ON");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>A material that ignores room lighting. For anything that is a source
        /// rather than a surface.</summary>
        private static Material UnlitMat(string name, Color baseColor)
        {
            if (Materials.TryGetValue(name, out var cached))
            {
                return cached;
            }

            var path = $"{MaterialsFolder}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            var shader = Shader.Find("Universal Render Pipeline/Unlit");

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
            EditorUtility.SetDirty(material);
            Materials[name] = material;
            return material;
        }

        private static Material Mat(string name, Color baseColor, float smoothness,
            float metallic = 0f, Color? emission = null, Texture2D albedo = null, float tiling = 1f)
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

            if (albedo != null)
            {
                material.SetTexture("_BaseMap", albedo);
                material.SetTextureScale("_BaseMap", Vector2.one * tiling);
            }
            else
            {
                material.SetTexture("_BaseMap", null);
            }

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
