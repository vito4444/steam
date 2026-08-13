using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Monster.Interaction;
using Monster.Rules;
using Monster.Shift;
using UnityEngine;

namespace Monster.Presentation
{
    /// <summary>Connects one night at the checkpoint to the objects in the booth.
    ///
    /// The rules, the queue and the economy all live in <see cref="ShiftDirector"/>, which
    /// knows nothing about Unity. This class is the only place the two meet, so the game
    /// can be played out entirely in a test and this file stays thin enough to read.</summary>
    public sealed class BoothPresenter : MonoBehaviour
    {
        [SerializeField] private int campaignSeed = 20260813;
        [SerializeField] private int shiftIndex;

        [Header("Printed surfaces")]
        [SerializeField] private PrintedSurface permit;
        [SerializeField] private PrintedSurface biometrics;
        [SerializeField] private PrintedSurface cabin;
        [SerializeField] private PrintedSurface underside;
        [SerializeField] private PrintedSurface manual;
        [SerializeField] private PrintedSurface logbook;

        // Serialised, not subscribed at edit time. C# event subscriptions do not survive
        // serialisation, so wiring the switches when the scene was generated left every
        // control dead in the build with nothing to indicate why.
        [SerializeField] private List<DeskInteractable> switches = new();

        [SerializeField] private CheckpointStage stage;

        private ShiftDirector _director;
        private Coroutine _vehicleCycle;

        public ShiftDirector Director => _director;

        public event Action<Decision> DecisionMade;
        public event Action<ShiftReport> ShiftEnded;

        public void Configure(int seed, int shift)
        {
            campaignSeed = seed;
            shiftIndex = shift;
        }

        public void Bind(PrintedSurface permitSurface, PrintedSurface biometricsSurface,
            PrintedSurface cabinSurface, PrintedSurface undersideSurface,
            PrintedSurface manualSurface, PrintedSurface logbookSurface)
        {
            permit = permitSurface;
            biometrics = biometricsSurface;
            cabin = cabinSurface;
            underside = undersideSurface;
            manual = manualSurface;
            logbook = logbookSurface;
        }

        public void RegisterSwitch(DeskInteractable control) => switches.Add(control);

        public void BindStage(CheckpointStage checkpointStage) => stage = checkpointStage;

        public CheckpointStage Stage => stage;

        private void OnEnable()
        {
            foreach (var control in switches.Where(c => c != null))
            {
                control.Operated += OnSwitchThrown;
            }
        }

        private void OnDisable()
        {
            foreach (var control in switches.Where(c => c != null))
            {
                control.Operated -= OnSwitchThrown;
            }
        }

        /// <summary>Throws a switch directly, without a mouse. The automated self-check
        /// plays a whole shift through this, which is the only way the decision loop gets
        /// exercised end to end in a real build on this machine.</summary>
        public bool Submit(Verdict verdict)
        {
            if (_director == null || _director.IsFinished)
            {
                return false;
            }

            var decision = _director.Decide(verdict);
            DecisionMade?.Invoke(decision);
            RefreshLogbook(decision);

            // The paperwork changes at once and the vehicle cycle plays out alongside it.
            // Gating the decision on the animation would make the loop untestable and would
            // punish a player who reads faster than a boom gate lifts.
            if (stage != null && isActiveAndEnabled)
            {
                if (_vehicleCycle != null)
                {
                    StopCoroutine(_vehicleCycle);
                }

                _vehicleCycle = StartCoroutine(CycleVehicle(verdict));
            }

            if (_director.IsFinished)
            {
                var report = _director.BuildReport();
                ShowReport(report);
                ShiftEnded?.Invoke(report);
                return true;
            }

            RefreshDesk();
            return true;
        }

        private void Start()
        {
            BeginShift(shiftIndex);
        }

        public void BeginShift(int shift)
        {
            shiftIndex = shift;
            _director = new ShiftDirector(campaignSeed, shift);

            if (_vehicleCycle != null)
            {
                StopCoroutine(_vehicleCycle);
                _vehicleCycle = null;
            }

            if (stage != null)
            {
                stage.SetPhase(CheckpointStage.Phase.AtTheWindow, immediate: true);
            }

            RefreshDesk();
            RefreshManual();
            RefreshLogbook(null);
        }

        /// <summary>Puts a specific vehicle at the window without deciding anything on the
        /// way there. The self-check uses this so its screenshots are of a known subject.</summary>
        public void ShowSubject(int queuePosition)
        {
            _director.SkipTo(queuePosition);
            RefreshDesk();
        }

        private IEnumerator CycleVehicle(Verdict verdict)
        {
            stage.SetPhase(CheckpointStage.PhaseFor(verdict));
            while (!stage.PhaseComplete)
            {
                yield return null;
            }

            if (_director == null || _director.IsFinished)
            {
                stage.SetPhase(CheckpointStage.Phase.Clear);
                _vehicleCycle = null;
                yield break;
            }

            stage.SetPhase(CheckpointStage.Phase.Approaching);
            while (!stage.PhaseComplete)
            {
                yield return null;
            }

            stage.SetPhase(CheckpointStage.Phase.AtTheWindow);
            _vehicleCycle = null;
        }

        private void OnSwitchThrown(DeskInteractable control)
        {
            if (!Enum.TryParse<Verdict>(control.Payload, true, out var verdict))
            {
                Debug.LogError($"[Booth] switch '{control.name}' has an unrecognised payload '{control.Payload}'");
                return;
            }

            Submit(verdict);
        }

        private void RefreshDesk()
        {
            if (_director == null)
            {
                return;
            }

            Show(permit, _director.Permit);
            Show(biometrics, _director.Biometrics);
            Show(cabin, _director.Cabin);
            Show(underside, _director.Underside);
        }

        /// <summary>The binder shows every page ever issued, current and superseded, in the
        /// order they arrived. Working out which page is in force is the player's job, so
        /// nothing here marks the superseded ones.</summary>
        private void RefreshManual()
        {
            if (manual == null || _director == null)
            {
                return;
            }

            var fields = _director.Manual.AllPages
                .Select(p => new DocumentField($"{p.Id}/{p.Revision}", Shorten(p.PrintedText, 46)))
                .ToList();

            Show(manual, new DocumentContent(
                $"CHECKPOINT 14 - STANDING ORDERS - NIGHT {_director.ShiftIndex + 1}",
                fields,
                "AMENDMENTS SUPERSEDE. RETAIN ALL PAGES."));
        }

        private void RefreshLogbook(Decision? last)
        {
            if (logbook == null || _director == null)
            {
                return;
            }

            var fields = new List<DocumentField>
            {
                new("NIGHT", (_director.ShiftIndex + 1).ToString(CultureInfo.InvariantCulture)),
                new("QUOTA", $"{Math.Min(_director.Position, _director.Quota)} / {_director.Quota}"),
                new("SEEN", _director.Position.ToString(CultureInfo.InvariantCulture)),
            };

            if (last.HasValue)
            {
                fields.Add(new DocumentField("LAST", last.Value.Chosen.ToString().ToUpperInvariant()));
            }

            Show(logbook, new DocumentContent("DUTY LOG", fields));
        }

        private void ShowReport(ShiftReport report)
        {
            var fields = new List<DocumentField>
            {
                new("PROCESSED", report.Processed.ToString(CultureInfo.InvariantCulture)),
                new("CORRECT", $"{report.Correct} / {report.Processed}"),
                new("GROSS", $"{report.GrossPay} CR"),
                new("PENALTY", report.Penalty == 0 ? "0 CR" : $"-{report.Penalty} CR"),
                new("NET", $"{report.NetPay} CR"),
            };

            Show(logbook, new DocumentContent($"MORNING REPORT - NIGHT {report.ShiftIndex + 1}", fields,
                report.QuotaMet ? "QUOTA MET" : "QUOTA NOT MET"));

            permit?.Clear();
            biometrics?.Clear();
            cabin?.Clear();
            underside?.Clear();
        }

        private static void Show(PrintedSurface surface, DocumentContent content) => surface?.Show(content);

        private static string Shorten(string text, int limit) =>
            text.Length <= limit ? text : text[..(limit - 1)] + "\u2026";
    }
}
