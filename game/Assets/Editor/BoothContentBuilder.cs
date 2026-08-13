using System.Collections.Generic;
using Monster.Audio;
using Monster.Interaction;
using Monster.Presentation;
using Monster.Rules;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Monster.EditorTools
{
    /// <summary>Adds the readable and operable layer on top of the booth's geometry:
    /// printed text, colliders, hover targets and the presenter that binds a night at the
    /// checkpoint to all of it.
    ///
    /// Kept separate from <see cref="NightShiftSceneBuilder"/> because that file is about
    /// where things are in space and this one is about what they say and do.
    ///
    /// Every text object is parented to an *unscaled anchor* rather than to the prop it
    /// sits on. Props in this scene are scaled primitives -- the permit is a cube scaled to
    /// 0.300 x 0.003 x 0.400 -- and a rotated child of a non-uniformly scaled parent is
    /// sheared, because the parent's scale is applied in the parent's axes after the
    /// child's rotation has already permuted them. Compensating with an inverse scale on
    /// the child does not fix it, it just moves the error to a different axis. The first
    /// attempt did exactly that and every glyph was stretched by a factor of 133.</summary>
    public static class BoothContentBuilder
    {
        private static readonly Color InkColour = new(0.050f, 0.046f, 0.042f);
        private static readonly Color PhosphorColour = new(0.34f, 1.00f, 0.46f);
        private static readonly Color EngravedColour = new(0.88f, 0.82f, 0.60f);

        /// <summary>Dark, because these sit on pale key faces rather than on a black plate.</summary>
        private static readonly Color KeyFaceColour = new(0.13f, 0.115f, 0.095f);

        /// <summary>TextMeshPro's fontSize is not a world-space measurement. Measured on
        /// this project's typeface: at fontSize 0.015 a line renders 1.7 mm tall, so one
        /// millimetre of line height costs 0.00882 of fontSize. Every size below is written
        /// in millimetres of line height and converted through <see cref="Mm"/>, because
        /// the first pass guessed at this and produced 1.7 mm text on a 400 mm sheet.
        /// ReportTextScale re-measures on every build and fails loudly if it drifts.</summary>
        private const float FontSizePerMillimetre = 0.015f / 1.7f;

        /// <summary>Roughly half a line height, for a monospaced face.</summary>
        private const float AdvancePerLineHeight = 0.51f;

        private static float Mm(float lineHeightMillimetres) =>
            lineHeightMillimetres * FontSizePerMillimetre;

        public readonly struct Handles
        {
            public Handles(Transform stageBarrierArm, Transform stageVehicle, Transform stageSubject,
                Light[] headlights, Light[] taillights,
                Transform permitMesh, Transform permitAnchor,
                Transform manualMesh, Transform manualAnchor,
                Transform logMesh, Transform logAnchor, Transform mailMesh, Transform mailAnchor, Transform clockAnchor,
                IReadOnlyList<Transform> screenAnchors,
                IReadOnlyList<Transform> switchMeshes,
                IReadOnlyList<Transform> switchLabelAnchors,
                IReadOnlyList<Transform> intercomKeys,
                IReadOnlyList<Transform> intercomKeyLabels,
                GameObject camera)
            {
                StageBarrierArm = stageBarrierArm;
                StageVehicle = stageVehicle;
                StageSubject = stageSubject;
                Headlights = headlights;
                Taillights = taillights;
                PermitMesh = permitMesh;
                PermitAnchor = permitAnchor;
                ManualMesh = manualMesh;
                ManualAnchor = manualAnchor;
                LogMesh = logMesh;
                LogAnchor = logAnchor;
                MailMesh = mailMesh;
                MailAnchor = mailAnchor;
                ClockAnchor = clockAnchor;
                ScreenAnchors = screenAnchors;
                SwitchMeshes = switchMeshes;
                SwitchLabelAnchors = switchLabelAnchors;
                IntercomKeys = intercomKeys;
                IntercomKeyLabels = intercomKeyLabels;
                Camera = camera;
            }

            public Transform StageBarrierArm { get; }
            public Transform StageVehicle { get; }
            public Transform StageSubject { get; }
            public Light[] Headlights { get; }
            public Light[] Taillights { get; }
            public Transform PermitMesh { get; }
            public Transform PermitAnchor { get; }
            public Transform ManualMesh { get; }
            public Transform ManualAnchor { get; }
            public Transform LogMesh { get; }
            public Transform LogAnchor { get; }
            public Transform MailMesh { get; }
            public Transform MailAnchor { get; }
            public Transform ClockAnchor { get; }
            public IReadOnlyList<Transform> ScreenAnchors { get; }
            public IReadOnlyList<Transform> SwitchMeshes { get; }
            public IReadOnlyList<Transform> SwitchLabelAnchors { get; }
            public IReadOnlyList<Transform> IntercomKeys { get; }
            public IReadOnlyList<Transform> IntercomKeyLabels { get; }
            public GameObject Camera { get; }
        }

        public static void Populate(Handles handles)
        {
            var font = TypefaceSetup.Build();
            if (font == null)
            {
                Debug.LogError("[Booth] no typeface; the booth would be built with no text on it");
                return;
            }

            var permit = BuildPermitSurface(handles.PermitAnchor, font);
            var manual = BuildManualSurface(handles.ManualAnchor, font);
            var logbook = BuildLogSurface(handles.LogAnchor, font);

            var mail = BuildMailSurface(handles.MailAnchor, font);

                        // The intercom prints whole sentences rather than short readings, so its type
            // is smaller than the other two screens'.
            var intercom = BuildScreenSurface(handles.ScreenAnchors[0], font, "Intercom",
                withFooter: true, bodyMillimetres: 29f);
            var biometrics = BuildScreenSurface(handles.ScreenAnchors[1], font, "Biometrics");
            var cabin = BuildScreenSurface(handles.ScreenAnchors[2], font, "Cabin",
                withPortrait: true, portraitPair: true);

            var presenter = new GameObject("Booth").AddComponent<BoothPresenter>();
            presenter.Bind(permit, biometrics, cabin, intercom, manual, logbook, mail);

            if (handles.ClockAnchor != null)
            {
                var face = Text(handles.ClockAnchor, "Text", font, Mm(30f), PhosphorColour,
                    new Vector2(0.150f, 0.042f), Vector3.zero, TextAlignmentOptions.Center,
                    FontStyles.Bold);
                face.text = "22:00";
                presenter.BindClock(face);
            }

            var stage = presenter.gameObject.AddComponent<CheckpointStage>();
            stage.Rig(handles.StageBarrierArm, handles.StageVehicle, handles.StageSubject,
                handles.Headlights, handles.Taillights);
            presenter.BindStage(stage);

            var audio = presenter.gameObject.AddComponent<AudioSource>();
            audio.playOnAwake = false;
            presenter.gameObject.AddComponent<BoothAudio>().Bind(presenter);

            BuildSwitches(handles.SwitchMeshes, handles.SwitchLabelAnchors, font, presenter);
            BuildIntercomKeys(handles.IntercomKeys, handles.IntercomKeyLabels, font, presenter);

            // The log is signed to close a night, so it needs a body to click. The mesh,
            // not its text anchor: an anchor has no scale, and a collider sized in
            // proportion to an unscaled object comes out metres across.
            var logKey = MakeInspectable(handles.LogMesh, "log", 0.38f, new Vector3(0f, 1f, -0.32f),
                DeskInteractable.Behaviour.Operate);
            if (logKey != null)
            {
                presenter.RegisterLogControl(logKey);
            }
            MakeInspectable(handles.PermitMesh, "permit", 0.46f, new Vector3(0f, 1f, -0.34f));
            var manualKey = MakeInspectable(handles.ManualMesh, "manual", 0.42f,
                new Vector3(0f, 1f, -0.34f), DeskInteractable.Behaviour.Leaf);
            if (manualKey != null)
            {
                presenter.RegisterManualControl(manualKey);
            }
            // The tray itself, not its text anchor: the anchor has no scale, so a collider
            // sized in proportion to it would be metres across.
            var mailKey = MakeInspectable(handles.MailMesh, "mail", 0.40f, new Vector3(0f, 1f, -0.34f),
                DeskInteractable.Behaviour.Leaf);
            if (mailKey != null)
            {
                presenter.RegisterMailControl(mailKey);
            }

            handles.Camera.AddComponent<BoothCamera>();
            // No input source assigned here. IInputSource is a plain property and does not
            // serialise, so anything set at edit time is gone by the time the player runs.
            // DeskInteractor builds its own in Awake.
            var interactor = handles.Camera.AddComponent<DeskInteractor>();

            var aim = BuildAimDot();

            var interactorSerialized = new SerializedObject(interactor);
            interactorSerialized.FindProperty("aim").objectReferenceValue = aim;
            interactorSerialized.ApplyModifiedPropertiesWithoutUndo();

            var presenterSerialized = new SerializedObject(presenter);
            presenterSerialized.FindProperty("interactor").objectReferenceValue = interactor;
            presenterSerialized.FindProperty("figure").objectReferenceValue =
                handles.StageSubject != null ? handles.StageSubject.GetComponent<SubjectFigure>() : null;
            presenterSerialized.ApplyModifiedPropertiesWithoutUndo();

            var printed = permit != null && manual != null && logbook != null
                          && intercom != null && biometrics != null && cabin != null;
            Debug.Log(printed
                ? "[Booth] six printed surfaces, four switches and the presenter attached"
                : "[Booth] ERROR: not every printed surface was created");

            ReportTextScale(handles.PermitAnchor);
        }

        // ------------------------------------------------------------------- surfaces --

        private static PrintedSurface BuildPermitSurface(Transform anchor, TMP_FontAsset font)
        {
            var surface = anchor.gameObject.AddComponent<PrintedSurface>();

            var title = TextFromTop(anchor, "Title", font, Mm(17f), InkColour,
                0.286f, 0.020f, 0.180f, 0f, TextAlignmentOptions.Top, FontStyles.Bold);
            var body = TextFromTop(anchor, "Body", font, Mm(16f), InkColour,
                0.286f, 0.125f, 0.146f, 0f, TextAlignmentOptions.TopLeft);
            var portrait = TextFromTop(anchor, "Portrait", font, Mm(15f), InkColour,
                0.130f, 0.070f, 0.010f, -0.076f, TextAlignmentOptions.Top);
            var footer = TextFromTop(anchor, "Footer", font, Mm(10f), InkColour,
                0.150f, 0.060f, -0.004f, 0.062f, TextAlignmentOptions.TopLeft);

            surface.Bind(title, body, portrait, footer);
            return surface;
        }

        private static PrintedSurface BuildManualSurface(Transform anchor, TMP_FontAsset font)
        {
            var surface = anchor.gameObject.AddComponent<PrintedSurface>();

            // Deliberately small. The binder cannot be read from the seated pose and has to
            // be leaned over, which is what makes cross-referencing it cost time.
            var title = TextFromTop(anchor, "Title", font, Mm(9.5f), InkColour,
                0.264f, 0.012f, 0.166f, 0f, TextAlignmentOptions.Top, FontStyles.Bold);
            var body = TextFromTop(anchor, "Body", font, Mm(10.0f), InkColour,
                0.264f, 0.300f, 0.148f, 0f, TextAlignmentOptions.TopLeft);
            var footer = TextFromTop(anchor, "Footer", font, Mm(7.5f), InkColour,
                0.264f, 0.012f, -0.156f, 0f, TextAlignmentOptions.Top, FontStyles.Italic);

            surface.Bind(title, body, null, footer);
            return surface;
        }

        private static PrintedSurface BuildLogSurface(Transform anchor, TMP_FontAsset font)
        {
            var surface = anchor.gameObject.AddComponent<PrintedSurface>();

            var title = TextFromTop(anchor, "Title", font, Mm(14f), InkColour,
                0.214f, 0.018f, 0.146f, 0f, TextAlignmentOptions.Top, FontStyles.Bold);
            var body = TextFromTop(anchor, "Body", font, Mm(13f), InkColour,
                0.214f, 0.130f, 0.120f, 0f, TextAlignmentOptions.TopLeft);
            var footer = TextFromTop(anchor, "Footer", font, Mm(11f), InkColour,
                0.214f, 0.016f, -0.126f, 0f, TextAlignmentOptions.Top);

            surface.Bind(title, body, null, footer);
            return surface;
        }

        /// <summary>The mail tray. Set in a slightly heavier face than the duty log,
        /// because a notice from the central office is the one piece of paper on this desk
        /// that was written by somebody rather than printed by a machine.</summary>
        private static PrintedSurface BuildMailSurface(Transform anchor, TMP_FontAsset font)
        {
            var surface = anchor.gameObject.AddComponent<PrintedSurface>();

            var title = TextFromTop(anchor, "Title", font, Mm(13f), InkColour,
                0.230f, 0.018f, 0.156f, 0f, TextAlignmentOptions.Top, FontStyles.Bold);
            var body = TextFromTop(anchor, "Body", font, Mm(11f), InkColour,
                0.230f, 0.200f, 0.126f, 0f, TextAlignmentOptions.TopLeft);
            var footer = TextFromTop(anchor, "Footer", font, Mm(9.5f), InkColour,
                0.230f, 0.016f, -0.144f, 0f, TextAlignmentOptions.Top, FontStyles.Italic);

            surface.Bind(title, body, null, footer);
            return surface;
        }

        private static PrintedSurface BuildScreenSurface(Transform anchor, TMP_FontAsset font, string name,
            bool withPortrait = false, bool withFooter = false, float bodyMillimetres = 38f,
            bool portraitPair = false)
        {
            var surface = anchor.gameObject.AddComponent<PrintedSurface>();
            surface.name = name;

            var title = TextFromTop(anchor, "Title", font, Mm(24f), PhosphorColour,
                0.500f, 0.024f, 0.124f, -0.008f, TextAlignmentOptions.TopLeft, FontStyles.Bold);

            // A pair of faces is eighteen characters across where a single one is eight, so
            // it cannot sit in the column beside the fields -- it overflowed its rect and
            // printed straight through them, leaving PRESONTFILE across the middle of the
            // screen. The pair goes under the fields instead, full width.
            var body = TextFromTop(anchor, "Body", font,
                Mm(portraitPair ? 30f : bodyMillimetres), PhosphorColour,
                withPortrait && !portraitPair ? 0.320f : 0.500f,
                portraitPair ? 0.062f : 0.200f, 0.090f,
                withPortrait && !portraitPair ? -0.092f : -0.008f, TextAlignmentOptions.TopLeft);

            TextMeshPro portrait = null;
            if (withPortrait)
            {
                portrait = portraitPair
                    ? TextFromTop(anchor, "Portrait", font, Mm(26f), PhosphorColour,
                        0.500f, 0.116f, 0.014f, -0.008f, TextAlignmentOptions.TopLeft)
                    : TextFromTop(anchor, "Portrait", font, Mm(42f), PhosphorColour,
                        0.190f, 0.180f, 0.090f, 0.158f, TextAlignmentOptions.Top);
            }

            if (portrait != null && portraitPair)
            {
                // Tightened so five lines of caption and grid clear the bottom of the tube
                // without the cells having to shrink to the point of being unreadable from
                // the seat, which is the only place they are ever read from.
                portrait.lineSpacing = -12f;
            }

            TextMeshPro footer = null;
            if (withFooter)
            {
                // The intercom's voice trace: two rows of block characters under the reply,
                // drawn tight so the ramp reads as a waveform rather than as text.
                footer = TextFromTop(anchor, "Footer", font, Mm(19f), PhosphorColour,
                    0.500f, 0.060f, -0.036f, -0.008f, TextAlignmentOptions.TopLeft);
                footer.lineSpacing = -28f;
            }

            surface.Bind(title, body, portrait, footer);
            return surface;
        }

        private static void BuildSwitches(IReadOnlyList<Transform> switches, IReadOnlyList<Transform> labelAnchors,
            TMP_FontAsset font, BoothPresenter presenter)
        {
            var verdicts = new[] { Verdict.Pass, Verdict.Hold, Verdict.Refer, Verdict.Alarm };

            for (var i = 0; i < switches.Count && i < verdicts.Length; i++)
            {
                var verdict = verdicts[i];

                // The engraved label goes on the plate in front of the switch, not on the
                // switch itself, which is two centimetres across.
                if (i < labelAnchors.Count && labelAnchors[i] != null)
                {
                    Text(labelAnchors[i], "Text", font, Mm(23f), EngravedColour,
                        new Vector2(0.075f, 0.020f), Vector3.zero, TextAlignmentOptions.Center,
                        FontStyles.Bold).text = verdict.ToString().ToUpperInvariant();
                }

                var control = switches[i];
                var collider = control.gameObject.AddComponent<BoxCollider>();
                collider.size = new Vector3(1.9f, 2.6f, 1.9f);

                var interactable = control.gameObject.AddComponent<DeskInteractable>();
                interactable.Configure(DeskInteractable.Behaviour.Operate, verdict.ToString(), 0.20f,
                    new Vector3(0f, 1f, -0.4f), control.GetComponentsInChildren<Renderer>());
                presenter.RegisterSwitch(interactable);
            }
        }

        /// <summary>The aiming dot and the only screen-space element in the game.
        ///
        /// Overlay rather than in-world, because a dot painted on the glass would be at a
        /// fixed depth and the desk is not.</summary>
        private static AimDot BuildAimDot()
        {
            var canvasObject = new GameObject("AimCanvas",
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));

            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            // Above nothing in particular. There is no other UI to sort against, but a
            // default of zero puts it at the mercy of whatever URP draws last.
            canvas.sortingOrder = 100;

            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;

            var dotObject = new GameObject("AimDot", typeof(RectTransform), typeof(Image), typeof(AimDot));
            dotObject.transform.SetParent(canvasObject.transform, false);

            var rect = (RectTransform)dotObject.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(3f, 3f);

            var image = dotObject.GetComponent<Image>();
            image.sprite = RingSprite();
            image.color = new Color(0.92f, 0.88f, 0.76f, 0.22f);
            image.raycastTarget = false;

            return dotObject.GetComponent<AimDot>();
        }

        /// <summary>A ring rather than a disc: a filled dot at eleven pixels would sit on top
        /// of the small thing it is pointing at.</summary>
        private static Sprite RingSprite()
        {
            const string path = "Assets/Textures/AimRing.png";
            const int size = 64;

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var centre = (size - 1) * 0.5f;

            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var distance = Mathf.Sqrt((x - centre) * (x - centre) + (y - centre) * (y - centre));

                    // Soft-edged annulus. Antialiased by the falloff rather than by mip
                    // filtering, which at three pixels would erase it.
                    var outer = Mathf.InverseLerp(centre, centre - 5f, distance);
                    var inner = Mathf.InverseLerp(centre - 15f, centre - 11f, distance);
                    var alpha = Mathf.Clamp01(Mathf.Min(outer, inner));

                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }

            texture.Apply();
            System.IO.File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path);

            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;

            // Without this the mode defaults to None, LoadAssetAtPath returns null, and the
            // Image falls back to its built-in white square -- which is exactly what appeared
            // in the middle of the screen.
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();

            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        // -------------------------------------------------------------------- helpers --

        /// <summary>The four intercom keys. Separate from the verdict switches on purpose:
        /// one row decides a person's night and the other only asks a question, and mixing
        /// them on the same plate would invite the wrong one being thrown under time
        /// pressure.</summary>
        private static void BuildIntercomKeys(IReadOnlyList<Transform> keys,
            IReadOnlyList<Transform> labelAnchors, TMP_FontAsset font, BoothPresenter presenter)
        {
            var questions = new[] { Question.District, Question.Purpose, Question.IssuingOffice, Question.Destination };
            var captions = new[] { "DIST", "PURP", "OFFC", "DEST" };

            for (var i = 0; i < keys.Count && i < questions.Length; i++)
            {
                if (i < labelAnchors.Count && labelAnchors[i] != null)
                {
                    Text(labelAnchors[i], "Text", font, Mm(22f), KeyFaceColour,
                        new Vector2(0.058f, 0.012f), Vector3.zero, TextAlignmentOptions.Center,
                        FontStyles.Bold).text = captions[i];
                }

                var key = keys[i];
                var collider = key.gameObject.AddComponent<BoxCollider>();
                collider.size = new Vector3(1.9f, 3.0f, 1.9f);

                var interactable = key.gameObject.AddComponent<DeskInteractable>();
                interactable.Configure(DeskInteractable.Behaviour.Operate, $"Q:{questions[i]}", 0.20f,
                    new Vector3(0f, 1f, -0.4f), key.GetComponentsInChildren<Renderer>());
                presenter.RegisterSwitch(interactable);
            }
        }

        private static DeskInteractable MakeInspectable(Transform mesh, string payload, float distance,
            Vector3 offset, DeskInteractable.Behaviour mode = DeskInteractable.Behaviour.Inspect)
        {
            if (mesh.GetComponent<Collider>() == null)
            {
                var collider = mesh.gameObject.AddComponent<BoxCollider>();

                // Thickened in local Y because these are very thin scaled cubes and a
                // collider matching one exactly is almost impossible to hit with a ray.
                //
                // Only valid on a scaled mesh. Put on an unscaled anchor this is a box one
                // metre square and eight metres tall, which is what the post tray had: an
                // invisible column through the whole booth that swallowed almost every
                // click in the room. Anything without a scale of its own is refused.
                var scale = mesh.lossyScale;
                if (Mathf.Max(scale.x, scale.y, scale.z) > 0.9f)
                {
                    Debug.LogError($"[Booth] '{mesh.name}' is unscaled, so a proportional collider " +
                                   "would be metres across; give the interactable the mesh rather " +
                                   "than its anchor");
                }

                collider.size = new Vector3(1f, 8f, 1f);
            }

            var interactable = mesh.gameObject.AddComponent<DeskInteractable>();
            interactable.Configure(mode, payload, distance, offset,
                mesh.GetComponentsInChildren<Renderer>());
            return interactable;
        }

        /// <summary>Measures what a line of text actually comes out as in metres.
        ///
        /// TextMeshPro's fontSize is not a world-space measurement, and guessing at the
        /// conversion is how the first pass ended up with text a millimetre tall on a
        /// 400 mm sheet of paper. This measures the rendered bounds of a known string and
        /// fails loudly if a line of body text is not a plausible size for a document, so
        /// the mistake cannot be made silently again.</summary>
        private static void ReportTextScale(Transform permitAnchor)
        {
            var body = permitAnchor.Find("Body")?.GetComponent<TextMeshPro>();
            if (body == null)
            {
                Debug.LogError("[Booth] cannot measure text scale: the permit has no Body field");
                return;
            }

            var previous = body.text;
            body.text = "BEARER    HALDEN KASTEL";
            body.ForceMeshUpdate();

            var bounds = body.textBounds;
            var worldHeight = bounds.size.y * permitAnchor.lossyScale.y;
            var worldWidth = bounds.size.x * permitAnchor.lossyScale.x;

            body.text = previous;
            body.ForceMeshUpdate();

            Debug.Log($"[Booth] permit body line measures {worldWidth * 1000f:F1} x " +
                      $"{worldHeight * 1000f:F1} mm at fontSize {body.fontSize}");

            if (worldHeight < 0.010f || worldHeight > 0.030f)
            {
                Debug.LogError($"[Booth] permit body text is {worldHeight * 1000f:F1} mm tall, " +
                               "which is not a readable size for a document; fontSize needs recalibrating");
            }

            if (worldWidth > 0.290f)
            {
                Debug.LogError($"[Booth] permit body line is {worldWidth * 1000f:F1} mm wide and " +
                               "overruns the 300 mm sheet");
            }
        }

        /// <summary>Places a text field by the top edge it actually starts printing from,
        /// rather than by the centre of its rect. Top-aligned text in a rect positioned by
        /// its centre begins half a rect-height higher than the number suggests, which is
        /// how the permit's title ended up printed across the second line of its body.</summary>
        private static TextMeshPro TextFromTop(Transform anchor, string name, TMP_FontAsset font, float size,
            Color colour, float width, float height, float topY, float x,
            TextAlignmentOptions alignment, FontStyles style = FontStyles.Normal) =>
            Text(anchor, name, font, size, colour, new Vector2(width, height),
                new Vector3(x, topY - height * 0.5f, 0f), alignment, style);

        private static TextMeshPro Text(Transform anchor, string name, TMP_FontAsset font, float size,
            Color colour, Vector2 rect, Vector3 offset, TextAlignmentOptions alignment,
            FontStyles style = FontStyles.Normal)
        {
            var go = new GameObject(name);
            var rectTransform = go.AddComponent<RectTransform>();
            rectTransform.SetParent(anchor, false);
            rectTransform.sizeDelta = rect;
            rectTransform.localPosition = offset;
            rectTransform.localRotation = Quaternion.identity;
            rectTransform.localScale = Vector3.one;

            var text = go.AddComponent<TextMeshPro>();
            text.font = font;
            text.fontSize = size;
            text.color = colour;
            text.alignment = alignment;
            text.fontStyle = style;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;
            text.raycastTarget = false;
            text.text = string.Empty;

            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }

            return text;
        }
    }
}
