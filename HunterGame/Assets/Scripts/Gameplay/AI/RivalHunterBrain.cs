using System;
using Hunter.Gameplay.Items;
using UnityEngine;

namespace Hunter.Gameplay.AI
{
    public enum HunterGoal
    {
        /// Working a loot route of its own.
        Scavenge,
        /// Racing the player to a container both can see.
        ContestLoot,
        /// Moving on a heard bell to catch whoever rang it.
        Ambush,
        /// Committed to a fight in progress.
        Fight,
        /// Hurt and carrying something worth keeping.
        Flee,
        /// Bag is good enough; heading for the exit.
        Extract,
    }

    /// What a rival hunter can perceive this frame. Everything here is information a
    /// player would also have access to: sight, sound, and their own bag. The AI gets no
    /// privileged knowledge, because the moment it does, encounters stop feeling fair.
    public readonly struct Perception
    {
        public float Health01 { get; }
        public bool CanSeeRival { get; }
        public float DistanceToRival { get; }
        /// Estimated worth of what the other hunter is carrying, from visible loot glow.
        public int RivalVisibleHaul { get; }
        public bool HeardBell { get; }
        public float DistanceToBell { get; }
        public bool LootInReach { get; }
        public ItemInstance VisibleLoot { get; }
        public float DistanceToExtraction { get; }
        public bool SovereignNearby { get; }

        public Perception(float health01, bool canSeeRival, float distanceToRival, int rivalVisibleHaul,
            bool heardBell, float distanceToBell, bool lootInReach, ItemInstance visibleLoot,
            float distanceToExtraction, bool sovereignNearby)
        {
            Health01 = Mathf.Clamp01(health01);
            CanSeeRival = canSeeRival;
            DistanceToRival = Mathf.Max(0f, distanceToRival);
            RivalVisibleHaul = Mathf.Max(0, rivalVisibleHaul);
            HeardBell = heardBell;
            DistanceToBell = Mathf.Max(0f, distanceToBell);
            LootInReach = lootInReach;
            VisibleLoot = visibleLoot;
            DistanceToExtraction = Mathf.Max(0f, distanceToExtraction);
            SovereignNearby = sovereignNearby;
        }
    }

    public readonly struct Intent
    {
        public HunterGoal Goal { get; }
        /// Why this goal won, for debugging and for the encounter log.
        public string Reason { get; }
        public LootValuation.Appraisal LootCall { get; }

        public Intent(HunterGoal goal, string reason, LootValuation.Appraisal lootCall = default)
        {
            Goal = goal;
            Reason = reason;
            LootCall = lootCall;
        }
    }

    /// Decision layer for a rival gold-hunter, kept free of Unity lifecycle so its
    /// behaviour can be asserted in tests rather than eyeballed in play.
    ///
    /// The design contract from docs/01-game-concepts.md A.2: rivals run the same bag,
    /// the same valuation rules and the same fear of dying that a player does. They are
    /// the replacement for PvP, so their decisions have to be legible to a player who
    /// watches them for ten seconds.
    public sealed class RivalHunterBrain
    {
        public sealed class Profile
        {
            /// Below this health, a rival stops trading blows.
            public float FleeHealth = 0.35f;

            /// Won't start a fight over a haul smaller than this.
            public int MinHaulWorthFighting = 180;

            /// How far it will travel to answer a bell.
            public float BellResponseRange = 90f;

            /// 0 = pure looter, 1 = pure aggressor. Varies per spawn so a lobby of rivals
            /// does not behave like one organism.
            public float Aggression = 0.5f;

            /// Engagement envelope for committing to melee.
            public float EngageRange = 18f;
        }

        readonly Profile _profile;

        public RivalHunterBrain(Inventory inventory, Profile profile = null)
        {
            Inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
            _profile = profile ?? new Profile();
        }

        public Inventory Inventory { get; }
        public HunterGoal LastGoal { get; private set; } = HunterGoal.Scavenge;

        public Intent Decide(in Perception perception, in LootValuation.Context context)
        {
            // Survival first. A wounded hunter carrying anything at all runs; that is what
            // makes killing a loaded rival feel like a real steal rather than a chore.
            bool wounded = perception.Health01 <= _profile.FleeHealth;
            if (wounded && Inventory.TotalValue > 0)
                return Commit(HunterGoal.Flee, "wounded while carrying");

            if (perception.SovereignNearby)
                return Commit(HunterGoal.Flee, "sovereign closing");

            if (wounded)
                return Commit(HunterGoal.Flee, "wounded");

            // Bag is good enough, clock is late, or things have got dangerous.
            if (LootValuation.ShouldHeadForExtraction(Inventory, context))
                return Commit(HunterGoal.Extract, "haul and clock say leave");

            // The bell is the loudest thing in the ruin and it advertises a full bag.
            if (perception.HeardBell && perception.DistanceToBell <= _profile.BellResponseRange)
            {
                float greed = _profile.Aggression + Mathf.Clamp01(1f - Inventory.LoadRatio) * 0.3f;
                if (greed >= 0.45f)
                    return Commit(HunterGoal.Ambush, "answering the bell");
            }

            if (perception.CanSeeRival && perception.DistanceToRival <= _profile.EngageRange)
            {
                bool worthIt = perception.RivalVisibleHaul >= _profile.MinHaulWorthFighting;
                bool feelingBrave = perception.Health01 > 0.7f && _profile.Aggression >= 0.4f;

                if (worthIt || feelingBrave)
                    return Commit(HunterGoal.Fight, worthIt ? "target is loaded" : "opportunistic");
            }

            if (perception.LootInReach && perception.VisibleLoot != null)
            {
                var call = LootValuation.Appraise(perception.VisibleLoot, Inventory, context);
                if (call.Decision != LootValuation.Decision.Ignore)
                {
                    // Contested loot beats uncontested loot: a rival that visibly races the
                    // player for the same chest is the single most readable AI behaviour.
                    var goal = perception.CanSeeRival ? HunterGoal.ContestLoot : HunterGoal.Scavenge;
                    return Commit(goal, call.Decision == LootValuation.Decision.Take
                        ? "worth picking up"
                        : "worth swapping in", call);
                }
            }

            return Commit(HunterGoal.Scavenge, "looking for better");
        }

        Intent Commit(HunterGoal goal, string reason, LootValuation.Appraisal call = default)
        {
            LastGoal = goal;
            return new Intent(goal, reason, call);
        }

        /// Applies a decision that involves the bag, so world code does not have to
        /// re-derive what the brain already worked out.
        public bool ExecuteLootCall(in LootValuation.Appraisal call, ItemInstance candidate)
        {
            switch (call.Decision)
            {
                case LootValuation.Decision.Take:
                    return Inventory.TryAdd(candidate) == AddResult.Added;

                case LootValuation.Decision.SwapForWorseItem:
                    if (call.ItemToDrop == null) return false;
                    if (!Inventory.Remove(call.ItemToDrop)) return false;
                    if (Inventory.TryAdd(candidate) == AddResult.Added) return true;
                    // Never silently lose the dropped item if the swap fails.
                    Inventory.TryAdd(call.ItemToDrop);
                    return false;

                default:
                    return false;
            }
        }
    }
}
