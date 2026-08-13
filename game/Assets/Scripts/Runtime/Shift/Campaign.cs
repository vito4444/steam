using System;
using System.Collections.Generic;
using System.Linq;
using Monster.Rules;

namespace Monster.Shift
{
    /// <summary>A whole run of nights at the checkpoint.
    ///
    /// This exists because the design pillar of the concept — that the game usually does
    /// not tell you whether you were right — cannot be honoured one shift at a time. If the
    /// morning report grades every decision, the player knows immediately and there is
    /// nothing to be uncertain about.
    ///
    /// So the wage is flat per vehicle processed, and errors come back later as notices of
    /// deduction that never say which decision they are for. The player finds out they were
    /// wrong four nights on, in money, without being told what they got wrong.
    ///
    /// No Unity types, so a thirty-night campaign runs in a test in milliseconds.</summary>
    public sealed class Campaign
    {
        public const int WagePerVehicle = 6;
        public const int QuotaShortfallPenalty = 8;
        public const int TotalShifts = 30;

        private readonly List<Notice> _pending = new();
        private readonly List<ShiftReport> _completed = new();
        private readonly List<Notice> _delivered = new();
        private readonly List<List<Verdict>> _history = new();
        private List<Verdict> _tonight = new();

        public Campaign(int seed) => Seed = seed;

        public int Seed { get; }
        public int ShiftIndex { get; private set; }
        public int Credits { get; private set; }

        public IReadOnlyList<ShiftReport> Completed => _completed;

        /// <summary>Every notice the player has actually seen, in the order it arrived.</summary>
        public IReadOnlyList<Notice> Delivered => _delivered;

        public bool IsOver => ShiftIndex >= TotalShifts;

        public ShiftDirector BeginShift()
        {
            if (IsOver)
            {
                throw new InvalidOperationException("the campaign is over");
            }

            _tonight = new List<Verdict>();
            return new ShiftDirector(Seed, ShiftIndex);
        }

        /// <summary>Records a verdict for the save file. The director owns the decision
        /// itself; this only remembers what was chosen, because that plus the seed is
        /// enough to rebuild the entire campaign.</summary>
        public void Record(Verdict verdict) => _tonight.Add(verdict);

        /// <summary>The mail waiting on the mat at the start of tonight's shift.</summary>
        public IReadOnlyList<Notice> MailFor(int shiftIndex)
        {
            var mail = _pending.Where(n => n.ArrivesOnShift == shiftIndex).ToList();
            mail.AddRange(ConsequenceWriter.RoutineFor(shiftIndex, Seed));
            return mail;
        }

        /// <summary>Closes tonight out: delivers the mail, pays the wage less whatever the
        /// office has decided to withhold, and queues what tonight will produce.</summary>
        public NightlyStatement EndShift(ShiftDirector director)
        {
            if (director == null)
            {
                throw new ArgumentNullException(nameof(director));
            }

            var report = director.BuildReport();
            _completed.Add(report);

            var mail = MailFor(ShiftIndex);
            _delivered.AddRange(mail);

            var deductions = mail.Sum(n => n.Deduction);
            var shortfall = Math.Max(0, director.Quota - report.Processed) * QuotaShortfallPenalty;
            var wage = report.Processed * WagePerVehicle;
            var net = Math.Max(0, wage - deductions - shortfall);

            Credits += net;

            foreach (var decision in report.Decisions)
            {
                _pending.AddRange(ConsequenceWriter.For(decision, ShiftIndex));
            }

            _history.Add(_tonight);
            _tonight = new List<Verdict>();
            ShiftIndex++;

            return new NightlyStatement(report.ShiftIndex, report.Processed, director.Quota,
                wage, deductions, shortfall, net, Credits, mail);
        }

        // --------------------------------------------------------------- saving --

        public CampaignSave ToSave()
        {
            var save = new CampaignSave { seed = Seed };

            foreach (var night in _history)
            {
                save.nights.Add(new CampaignSave.Night
                {
                    verdicts = night.Select(v => (int)v).ToList(),
                });
            }

            // A night in progress is written too, so quitting mid-shift does not throw the
            // evening away.
            if (_tonight.Count > 0)
            {
                save.nights.Add(new CampaignSave.Night
                {
                    verdicts = _tonight.Select(v => (int)v).ToList(),
                });
            }

            return save;
        }

        /// <summary>Rebuilds a campaign by replaying every recorded verdict. The returned
        /// director is the partly played night, or null if the save landed on a boundary.
        /// </summary>
        public static Campaign Restore(CampaignSave save, out ShiftDirector inProgress)
        {
            if (save == null)
            {
                throw new ArgumentNullException(nameof(save));
            }

            if (!save.IsConsistent(out var problem))
            {
                throw new InvalidOperationException($"this save cannot be resumed: {problem}");
            }

            var campaign = new Campaign(save.seed);
            inProgress = null;

            foreach (var night in save.nights)
            {
                var director = campaign.BeginShift();

                foreach (var verdict in night.verdicts)
                {
                    campaign.Record((Verdict)verdict);
                    director.Decide((Verdict)verdict);
                }

                if (!director.IsFinished)
                {
                    // Stop here: this night is still being played, so it must not be closed
                    // out or its consequences would be queued twice.
                    inProgress = director;
                    return campaign;
                }

                campaign.EndShift(director);
            }

            return campaign;
        }

        /// <summary>What the player would have earned had they been perfect. Not shown to
        /// them; used by tests to assert that care pays better than carelessness.</summary>
        public static int PlayOut(int seed, Func<ShiftDirector, Verdict> policy)
        {
            var campaign = new Campaign(seed);

            while (!campaign.IsOver)
            {
                var director = campaign.BeginShift();
                while (!director.IsFinished)
                {
                    var verdict = policy(director);
                    campaign.Record(verdict);
                    director.Decide(verdict);
                }

                campaign.EndShift(director);
            }

            return campaign.Credits;
        }
    }

    /// <summary>What the morning report actually prints.
    ///
    /// Note what is absent: how many decisions were correct. The office does not tell you,
    /// and the wage is flat per vehicle so it cannot be reverse-engineered from the money
    /// either. Errors surface as deductions days later, attributed to a night rather than
    /// to a decision.</summary>
    public sealed class NightlyStatement
    {
        public NightlyStatement(int shiftIndex, int processed, int quota, int wage, int deductions,
            int shortfall, int net, int credits, IReadOnlyList<Notice> mail)
        {
            ShiftIndex = shiftIndex;
            Processed = processed;
            Quota = quota;
            Wage = wage;
            Deductions = deductions;
            Shortfall = shortfall;
            Net = net;
            Credits = credits;
            Mail = mail;
        }

        public int ShiftIndex { get; }
        public int Processed { get; }
        public int Quota { get; }
        public int Wage { get; }
        public int Deductions { get; }
        public int Shortfall { get; }
        public int Net { get; }
        public int Credits { get; }
        public IReadOnlyList<Notice> Mail { get; }

        public bool QuotaMet => Processed >= Quota;
    }
}
