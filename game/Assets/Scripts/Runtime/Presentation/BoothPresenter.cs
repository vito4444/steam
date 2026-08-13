using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
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
        [SerializeField] private PrintedSurface intercom;
        [SerializeField] private PrintedSurface manual;
        [SerializeField] private PrintedSurface logbook;
        [SerializeField] private PrintedSurface mailTray;
        [SerializeField] private TMPro.TMP_Text clock;

        // Serialised, not subscribed at edit time. C# event subscriptions do not survive
        // serialisation, so wiring the switches when the scene was generated left every
        // control dead in the build with nothing to indicate why.
        [SerializeField] private List<DeskInteractable> switches = new();
        [SerializeField] private List<DeskInteractable> mailControls = new();
        [SerializeField] private List<DeskInteractable> manualControls = new();

        [SerializeField] private CheckpointStage stage;

        private Campaign _campaign;
        private ShiftDirector _director;
        private Coroutine _vehicleCycle;
        private Coroutine _pendingReply;
        private IReadOnlyList<Notice> _mail = Array.Empty<Notice>();
        private int _mailPage;
        private int _manualPage;

        private const int ManualCriteriaPerPage = 4;
        private const int ManualLabelWidth = 9;

        /// <summary>Characters per line, sized to the page rather than guessed. The binder
        /// is 272 mm across and the body is set in 10 mm DejaVu Sans Mono, whose advance is
        /// 0.602 em, so 42 characters is 253 mm and fits with a margin. A first pass assumed
        /// an advance of 0.51 and ran every line off the right edge of the paper.</summary>
        private const int ManualLineWidth = 33;

        public ShiftDirector Director => _director;

        public event Action<Decision> DecisionMade;
        public event Action<Question> QuestionAsked;
        public event Action<Reply> ReplyReceived;
        public event Action<NightlyStatement> ShiftEnded;

        public void Configure(int seed, int shift)
        {
            campaignSeed = seed;
            shiftIndex = shift;
        }

        public void Bind(PrintedSurface permitSurface, PrintedSurface biometricsSurface,
            PrintedSurface cabinSurface, PrintedSurface intercomSurface,
            PrintedSurface manualSurface, PrintedSurface logbookSurface,
            PrintedSurface mailSurface)
        {
            permit = permitSurface;
            biometrics = biometricsSurface;
            cabin = cabinSurface;
            intercom = intercomSurface;
            manual = manualSurface;
            logbook = logbookSurface;
            mailTray = mailSurface;
        }

        public void BindClock(TMPro.TMP_Text face) => clock = face;

        /// <summary>Puts a question through the glass. The reply lands after however long
        /// this particular bearer takes to start answering, which is the whole point: the
        /// pause is felt in real time as well as printed, and asking costs time the player
        /// is short of.</summary>
        public bool Ask(Question question)
        {
            if (_director == null || _director.IsFinished || _pendingReply != null)
            {
                return false;
            }

            // The clock is the whole reason asking is a decision rather than a habit.
            if (!_director.Spend(ShiftDirector.MinutesPerQuestion))
            {
                return false;
            }

            _pendingReply = StartCoroutine(AwaitReply(question));
            return true;
        }

        private IEnumerator AwaitReply(Question question)
        {
            var subject = _director.Current.Attributes;
            var reply = Interrogation.Ask(subject, question);

            RefreshClock();
            Show(intercom, DocumentBuilder.IntercomWaiting(question));
            QuestionAsked?.Invoke(question);

            yield return new WaitForSeconds(Mathf.Max(0.05f, reply.DelaySeconds));

            Show(intercom, DocumentBuilder.IntercomReply(reply));
            ReplyReceived?.Invoke(reply);
            _pendingReply = null;
        }

        public void RegisterSwitch(DeskInteractable control) => switches.Add(control);

        public void RegisterMailControl(DeskInteractable control) => mailControls.Add(control);

        public void RegisterManualControl(DeskInteractable control) => manualControls.Add(control);

        public void BindStage(CheckpointStage checkpointStage) => stage = checkpointStage;

        public CheckpointStage Stage => stage;

        private void OnEnable()
        {
            foreach (var control in switches.Where(c => c != null))
            {
                control.Operated += OnSwitchThrown;
            }

            foreach (var control in mailControls.Where(c => c != null))
            {
                control.Operated += OnMailTurned;
            }

            foreach (var control in manualControls.Where(c => c != null))
            {
                control.Operated += OnManualTurned;
            }
        }

        private void OnMailTurned(DeskInteractable _) => LeafThroughMail();

        private void OnManualTurned(DeskInteractable _) => LeafThroughManual();

        private void OnDisable()
        {
            foreach (var control in switches.Where(c => c != null))
            {
                control.Operated -= OnSwitchThrown;
            }

            foreach (var control in mailControls.Where(c => c != null))
            {
                control.Operated -= OnMailTurned;
            }

            foreach (var control in manualControls.Where(c => c != null))
            {
                control.Operated -= OnManualTurned;
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

            _campaign.Record(verdict);
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
                var statement = _campaign.EndShift(_director);
                Save();
                ShowStatement(statement);
                ShowMail(statement.Mail);
                ShiftEnded?.Invoke(statement);
                return true;
            }

            RefreshClock();
            RefreshDesk();
            return true;
        }

        private void RefreshClock()
        {
            if (clock == null || _director == null)
            {
                return;
            }

            clock.text = _director.TimeOfDay;
        }

        private void Start()
        {
            BeginShift(shiftIndex);
        }

        [Tooltip("Write the campaign to disk at the end of every night.")]
        [SerializeField] private bool saveProgress = true;

        public Campaign Campaign => _campaign;

        /// <summary>Picks up where the player left off, or starts a new run if there is
        /// nothing on disk. Returns the night that is now open.</summary>
        public int ResumeOrBegin()
        {
            var save = saveProgress ? CampaignStore.Read() : null;

            if (save == null)
            {
                BeginShift(0);
                return 0;
            }

            ShiftDirector open;

            try
            {
                _campaign = Campaign.Restore(save, out open);
            }
            catch (InvalidOperationException exception)
            {
                Debug.LogWarning($"[Booth] starting a new run: {exception.Message}");
                BeginShift(0);
                return 0;
            }

            if (_campaign.IsOver)
            {
                BeginShift(0);
                return 0;
            }

            _director = open ?? _campaign.BeginShift();
            shiftIndex = _campaign.ShiftIndex;

            if (stage != null)
            {
                stage.SetPhase(CheckpointStage.Phase.AtTheWindow, immediate: true);
            }

            OpenShift();
            return shiftIndex;
        }

        private void Save()
        {
            if (!saveProgress || _campaign == null)
            {
                return;
            }

            try
            {
                CampaignStore.Write(_campaign.ToSave());
            }
            catch (Exception exception)
            {
                // A failed write must not take the shift down with it. The player keeps
                // playing; they just lose the resume.
                Debug.LogError($"[Booth] the campaign could not be saved: {exception.Message}");
            }
        }

        public void BeginShift(int shift)
        {
            shiftIndex = shift;

            // A fresh campaign jumped straight to the requested night. Campaign.BeginShift
            // is sequential by design, so without the skip this silently opened night one
            // whatever it was asked for -- which went unnoticed until the self-check tried
            // to photograph a binder that had amendments in it.
            _campaign = new Campaign(campaignSeed);
            _campaign.SkipToShift(shift);
            _director = _campaign.BeginShift();

            if (_vehicleCycle != null)
            {
                StopCoroutine(_vehicleCycle);
                _vehicleCycle = null;
            }

            if (stage != null)
            {
                stage.SetPhase(CheckpointStage.Phase.AtTheWindow, immediate: true);
            }

            OpenShift();
        }

        /// <summary>Moves on to the next night of the same run, so consequences queued by
        /// earlier nights actually arrive. BeginShift starts a fresh campaign; this
        /// continues one.</summary>
        public bool NextShift()
        {
            if (_campaign == null || _campaign.IsOver)
            {
                return false;
            }

            shiftIndex = _campaign.ShiftIndex;
            _director = _campaign.BeginShift();

            if (_vehicleCycle != null)
            {
                StopCoroutine(_vehicleCycle);
                _vehicleCycle = null;
            }

            if (stage != null)
            {
                stage.SetPhase(CheckpointStage.Phase.AtTheWindow, immediate: true);
            }

            OpenShift();
            return true;
        }

        private void OpenShift()
        {
            RefreshClock();
            RefreshDesk();
            RefreshManual();
            RefreshLogbook(null);
            ShowMail(_campaign.MailFor(shiftIndex));
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
            // Verdict switches and intercom keys are the same kind of control; the payload
            // says which. A question key is prefixed so a typo cannot silently become a
            // verdict.
            if (control.Payload != null && control.Payload.StartsWith("Q:", StringComparison.Ordinal))
            {
                if (Enum.TryParse<Question>(control.Payload[2..], true, out var question))
                {
                    Ask(question);
                }
                else
                {
                    Debug.LogError($"[Booth] intercom key '{control.name}' has an unknown question " +
                                   $"'{control.Payload}'");
                }

                return;
            }

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

            // A new vehicle means a new voice; anything the last one said is gone.
            if (_pendingReply != null)
            {
                StopCoroutine(_pendingReply);
                _pendingReply = null;
            }

            Show(permit, _director.Permit);
            Show(biometrics, _director.Biometrics);
            Show(cabin, _director.Cabin);
            Show(intercom, DocumentBuilder.IntercomIdle());
        }

        /// <summary>The binder shows every page ever issued, current and superseded, in the
        /// order they arrived. Working out which page is in force is the player's job, so
        /// nothing here marks the superseded ones.</summary>
        /// <summary>Five criteria to a page, printed in full.
        ///
        /// They used to be one line each, truncated at forty-six characters, which meant a
        /// player could not read the rule they were about to be judged against. That is not
        /// a legibility complaint, it is unfair: the whole game is deciding whether a
        /// subject matches a written criterion.</summary>
        private void RefreshManual()
        {
            if (manual == null || _director == null)
            {
                return;
            }

            var pages = _director.Manual.AllPages.ToList();
            var total = Math.Max(1, (pages.Count + ManualCriteriaPerPage - 1) / ManualCriteriaPerPage);
            _manualPage = Math.Clamp(_manualPage, 0, total - 1);

            var fields = new List<DocumentField>();

            foreach (var page in pages.Skip(_manualPage * ManualCriteriaPerPage).Take(ManualCriteriaPerPage))
            {
                var lines = Wrap(page.PrintedText, ManualLineWidth);

                // The night the page arrived goes in the label column under its number,
                // where the continuation lines leave it empty anyway. Without it a player
                // holding two revisions of one criterion cannot tell which the office
                // grades against, which is unfair rather than difficult.
                var stamps = new[]
                {
                    $"{page.Id}/{page.Revision}",
                    $"NIGHT {_director.Manual.ArrivalOf(page) + 1}",
                };

                for (var i = 0; i < Math.Max(lines.Count, stamps.Length); i++)
                {
                    fields.Add(new DocumentField(
                        i < stamps.Length ? stamps[i] : string.Empty,
                        i < lines.Count ? lines[i] : string.Empty));
                }

                fields.Add(new DocumentField(string.Empty, string.Empty));
            }

            Show(manual, new DocumentContent(
                $"STANDING ORDERS - NIGHT {_director.ShiftIndex + 1} - PAGE {_manualPage + 1}/{total}",
                fields,
                "AMENDMENTS SUPERSEDE. RETAIN ALL PAGES.",
                null,
                ManualLabelWidth));
        }

        /// <summary>Turns to the next page of the binder, wrapping. Wired to clicking the
        /// manual while already leaning over it.</summary>
        public void LeafThroughManual()
        {
            _manualPage++;
            RefreshManual();
        }

        /// <summary>Breaks a criterion across lines at word boundaries. TextMeshPro would
        /// wrap this itself, but then the wrapped lines would start under the criterion
        /// number instead of alongside it, and a page of rules has to stay a list.</summary>
        private static List<string> Wrap(string text, int width)
        {
            var lines = new List<string>();
            var line = new StringBuilder();

            foreach (var word in (text ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.Length > 0 && line.Length + 1 + word.Length > width)
                {
                    lines.Add(line.ToString());
                    line.Clear();
                }

                if (line.Length > 0)
                {
                    line.Append(' ');
                }

                line.Append(word);
            }

            lines.Add(line.ToString());
            return lines;
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

        /// <summary>The morning report, which deliberately does not say how many decisions
        /// were right. The wage is flat per vehicle so accuracy cannot be read out of the
        /// money either; errors come back days later as deductions that never say which
        /// decision they are for. A test guards that this stays true.</summary>
        private void ShowStatement(NightlyStatement statement)
        {
            var fields = new List<DocumentField>
            {
                new("PROCESSED", statement.Processed.ToString(CultureInfo.InvariantCulture)),
                new("QUOTA", statement.Quota.ToString(CultureInfo.InvariantCulture)),
                new("WAGE", $"{statement.Wage} CR"),
                new("WITHHELD", statement.Deductions == 0 ? "0 CR" : $"-{statement.Deductions} CR"),
                new("NET", $"{statement.Net} CR"),
                new("BALANCE", $"{statement.Credits} CR"),
            };

            Show(logbook, new DocumentContent($"MORNING REPORT - NIGHT {statement.ShiftIndex + 1}", fields,
                statement.QuotaMet ? "QUOTA MET" : "QUOTA NOT MET"));

            permit?.Clear();
            biometrics?.Clear();
            cabin?.Clear();
            intercom?.Clear();
        }

        /// <summary>Whatever the office sent. The topmost notice is the one on the tray;
        /// the rest are under it, which is why only one is legible at a time.</summary>
        private void ShowMail(IReadOnlyList<Notice> mail)
        {
            _mail = mail ?? Array.Empty<Notice>();
            _mailPage = 0;
            RefreshMail();
        }

        /// <summary>Turns to the next item in the tray, wrapping. Wired to clicking the post
        /// while already leaning over it.</summary>
        public void LeafThroughMail()
        {
            if (_mail.Count > 1)
            {
                _mailPage = (_mailPage + 1) % _mail.Count;
                RefreshMail();
            }
        }

        private void RefreshMail()
        {
            if (mailTray == null)
            {
                return;
            }

            if (_mail.Count == 0)
            {
                Show(mailTray, new DocumentContent("DISTRICT POST", Array.Empty<DocumentField>(),
                    "NOTHING TODAY"));
                return;
            }

            var notice = _mail[_mailPage];
            var fields = notice.Lines.Select(line => new DocumentField(string.Empty, line)).ToList();
            var footer = _mail.Count > 1
                ? $"ITEM {_mailPage + 1} OF {_mail.Count}"
                : "FILED";

            Show(mailTray, new DocumentContent(notice.Heading, fields, footer, null, 0));
        }

        private static void Show(PrintedSurface surface, DocumentContent content) => surface?.Show(content);

        private static string Shorten(string text, int limit) =>
            text.Length <= limit ? text : text[..(limit - 1)] + "\u2026";
    }
}
