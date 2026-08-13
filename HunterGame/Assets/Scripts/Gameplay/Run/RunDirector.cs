using System;
using System.Collections.Generic;
using Hunter.Gameplay.Items;

namespace Hunter.Gameplay.Run
{
    public enum RunPhase
    {
        Insertion,
        Scavenging,
        /// The bell has been rung and a portal is open on a countdown.
        ExtractionWindow,
        Extracted,
        Died,
        TimedOut,
    }

    public enum BellResult
    {
        Rung,
        AlreadyRinging,
        NotAllowedYet,
        RunOver,
    }

    /// Drives one raid. Deliberately a plain C# class with an explicit Tick so the whole
    /// loop is testable without entering play mode, which matters because this machine
    /// owns the run's stakes: what is banked, what is lost, and when.
    public sealed class RunDirector
    {
        public sealed class Settings
        {
            /// Hard ceiling on a raid. Concept doc A.2 targets 15-20 minutes.
            public float RunDurationSeconds = 1080f;

            /// When the unkillable Mist Sovereign starts hunting. Borrowed structurally
            /// from DRG: Rogue Core's timer pressure, see market research 3.4.
            public float SovereignSpawnSeconds = 720f;

            /// Portal lifetime after the bell. Short enough that ringing is a commitment.
            public float ExtractionWindowSeconds = 15f;

            /// Ringing immediately is not allowed; it would let a hunter bank the starting
            /// kit without ever entering the ruin.
            public float EarliestBellSeconds = 45f;

            /// The bell is loud. Concept doc A.2: it summons everything.
            public float BellThreatSpike = 0.55f;
        }

        readonly Settings _settings;

        public RunDirector(Inventory inventory, Settings settings = null)
        {
            Inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
            _settings = settings ?? new Settings();
            Phase = RunPhase.Insertion;
        }

        public Inventory Inventory { get; }
        public RunPhase Phase { get; private set; }
        public float ElapsedSeconds { get; private set; }
        public float PortalSecondsRemaining { get; private set; }
        public bool SovereignActive { get; private set; }

        /// 0..1 ambient danger. Rises with the clock, spikes on the bell, and is what the
        /// valuation context feeds on.
        public float Threat { get; private set; }

        /// Banked only on a successful extraction. Dying zeroes it, which is the entire
        /// risk structure of the mode.
        public int BankedValue { get; private set; }

        public IReadOnlyList<ItemInstance> BankedItems => _bankedItems;
        readonly List<ItemInstance> _bankedItems = new();

        public event Action<RunPhase> PhaseChanged;
        public event Action SovereignAwakened;
        public event Action<IReadOnlyList<ItemInstance>> LootDropped;

        public float RunProgress => Math.Clamp(ElapsedSeconds / _settings.RunDurationSeconds, 0f, 1f);

        public bool IsOver => Phase is RunPhase.Extracted or RunPhase.Died or RunPhase.TimedOut;

        public void Begin()
        {
            if (Phase != RunPhase.Insertion) return;
            SetPhase(RunPhase.Scavenging);
        }

        public void Tick(float deltaSeconds)
        {
            if (deltaSeconds < 0f) throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
            if (IsOver || Phase == RunPhase.Insertion) return;

            ElapsedSeconds += deltaSeconds;

            if (!SovereignActive && ElapsedSeconds >= _settings.SovereignSpawnSeconds)
            {
                SovereignActive = true;
                SovereignAwakened?.Invoke();
            }

            // Baseline danger climbs with the clock; the sovereign roughly doubles it.
            float clockThreat = RunProgress * 0.5f + (SovereignActive ? 0.3f : 0f);
            Threat = Math.Clamp(Math.Max(Threat * DecayPerSecond(deltaSeconds), clockThreat), 0f, 1f);

            if (Phase == RunPhase.ExtractionWindow)
            {
                PortalSecondsRemaining -= deltaSeconds;
                if (PortalSecondsRemaining <= 0f)
                {
                    // Missing your own portal drops you back into the raid rather than
                    // ending it: the loss is the time and the noise, not the run.
                    PortalSecondsRemaining = 0f;
                    SetPhase(RunPhase.Scavenging);
                }
            }

            if (ElapsedSeconds >= _settings.RunDurationSeconds)
            {
                // Timing out is a total loss, same as dying. The clock is an opponent.
                DropEverything();
                SetPhase(RunPhase.TimedOut);
            }
        }

        static float DecayPerSecond(float delta) => (float)Math.Pow(0.94d, delta);

        public void AddThreat(float amount)
        {
            Threat = Math.Clamp(Threat + amount, 0f, 1f);
        }

        public BellResult RingBell()
        {
            if (IsOver) return BellResult.RunOver;
            if (Phase == RunPhase.ExtractionWindow) return BellResult.AlreadyRinging;
            if (Phase != RunPhase.Scavenging) return BellResult.NotAllowedYet;
            if (ElapsedSeconds < _settings.EarliestBellSeconds) return BellResult.NotAllowedYet;

            PortalSecondsRemaining = _settings.ExtractionWindowSeconds;
            AddThreat(_settings.BellThreatSpike);
            SetPhase(RunPhase.ExtractionWindow);
            return BellResult.Rung;
        }

        /// Stepping through an open portal. Only valid while the window is up.
        public bool EnterPortal()
        {
            if (Phase != RunPhase.ExtractionWindow) return false;

            _bankedItems.AddRange(Inventory.Items);
            BankedValue = Inventory.TotalValue;
            Inventory.DropAll();
            SetPhase(RunPhase.Extracted);
            return true;
        }

        public void Kill()
        {
            if (IsOver) return;
            DropEverything();
            SetPhase(RunPhase.Died);
        }

        void DropEverything()
        {
            var dropped = Inventory.DropAll();
            BankedValue = 0;
            _bankedItems.Clear();
            if (dropped.Count > 0) LootDropped?.Invoke(dropped);
        }

        public LootValuation.Context BuildValuationContext(float distanceToExtraction)
            => new(RunProgress, distanceToExtraction, Threat);

        void SetPhase(RunPhase phase)
        {
            if (Phase == phase) return;
            Phase = phase;
            PhaseChanged?.Invoke(phase);
        }
    }
}
