using System;

namespace Hunter.Gameplay.Items
{
    /// The single decision function behind "is this worth it". The player HUD reads it to
    /// sort a container's contents, and rival hunter AI reads the same function to decide
    /// what to grab, what to swap out and when to run for the exit.
    ///
    /// Keeping both on one implementation is a design requirement, not a convenience: the
    /// AI is the replacement for human opponents, and it is only believable if it wants
    /// the same things a player wants for the same reasons.
    public static class LootValuation
    {
        /// Value per kilogram. The core currency of every carry decision.
        public static float ValueDensity(ItemInstance item)
        {
            if (item == null) return 0f;
            // Weightless items (quest tokens) are always worth taking.
            if (item.Weight <= 1e-4f) return float.MaxValue / 4f;
            return item.Value / item.Weight;
        }

        /// Context a hunter uses to judge a find.
        public readonly struct Context
        {
            /// 0 at insertion, 1 when the extraction window closes.
            public float RunProgress { get; }

            /// Straight-line distance to the nearest known extraction point, in metres.
            public float DistanceToExtraction { get; }

            /// 0 = safe, 1 = actively being hunted. Raises the bar for greedy picks.
            public float Threat { get; }

            public Context(float runProgress, float distanceToExtraction, float threat)
            {
                RunProgress = Math.Clamp(runProgress, 0f, 1f);
                DistanceToExtraction = Math.Max(0f, distanceToExtraction);
                Threat = Math.Clamp(threat, 0f, 1f);
            }

            public static Context FreshRun => new(0f, 60f, 0f);
        }

        /// Density a find must beat to be worth the pickup, given the situation. Late in a
        /// run, far from an exit, or while being chased, only genuinely good loot is worth
        /// the seconds it costs.
        public static float RequiredDensity(in Context context, float baselineDensity = 4f)
        {
            float lateness = 1f + context.RunProgress * context.RunProgress * 2.4f;
            float distance = 1f + Math.Min(context.DistanceToExtraction / 120f, 1f) * 0.8f;
            float danger = 1f + context.Threat * 1.6f;
            return baselineDensity * lateness * distance * danger;
        }

        public enum Decision
        {
            Take,
            SwapForWorseItem,
            Ignore,
        }

        public readonly struct Appraisal
        {
            public Decision Decision { get; }
            public ItemInstance ItemToDrop { get; }
            public float Density { get; }
            public float Threshold { get; }

            public Appraisal(Decision decision, ItemInstance itemToDrop, float density, float threshold)
            {
                Decision = decision;
                ItemToDrop = itemToDrop;
                Density = density;
                Threshold = threshold;
            }
        }

        /// Decides what a hunter does when standing over a find.
        public static Appraisal Appraise(ItemInstance candidate, Inventory inventory, in Context context,
            float baselineDensity = 4f)
        {
            if (candidate == null || inventory == null)
                return new Appraisal(Decision.Ignore, null, 0f, 0f);

            float density = ValueDensity(candidate);
            float threshold = RequiredDensity(context, baselineDensity);

            if (density < threshold)
                return new Appraisal(Decision.Ignore, null, density, threshold);

            if (inventory.CanFit(candidate))
                return new Appraisal(Decision.Take, null, density, threshold);

            // Bag is full or too heavy. Dropping is only worth the time if the thing being
            // left behind is clearly worse, otherwise hunters would thrash on near-ties.
            var worst = inventory.WorstByDensity();
            if (worst == null)
                return new Appraisal(Decision.Ignore, null, density, threshold);

            bool freesEnoughWeight = inventory.TotalWeight - worst.Weight + candidate.Weight
                                     <= inventory.WeightLimit + 1e-4f;
            bool clearlyBetter = density > ValueDensity(worst) * 1.25f;

            if (freesEnoughWeight && clearlyBetter)
                return new Appraisal(Decision.SwapForWorseItem, worst, density, threshold);

            return new Appraisal(Decision.Ignore, null, density, threshold);
        }

        /// How badly a hunter wants to leave, in 0..1. Crossing ExtractUrgencyThreshold is
        /// what turns a looting AI into a fleeing one, and it is what the HUD uses to start
        /// nagging the player.
        public const float ExtractUrgencyThreshold = 0.62f;

        public static float ExtractionUrgency(Inventory inventory, in Context context)
        {
            if (inventory == null) return 0f;

            // Carrying a fortune is itself a reason to leave.
            float haul = Math.Min(inventory.TotalValue / 1200f, 1f);
            float load = Math.Min(inventory.LoadRatio, 1f);
            float clock = context.RunProgress;
            float danger = context.Threat;

            float urgency = haul * 0.3f + load * 0.16f + clock * 0.34f + danger * 0.42f;
            return Math.Clamp(urgency, 0f, 1f);
        }

        public static bool ShouldHeadForExtraction(Inventory inventory, in Context context)
            => ExtractionUrgency(inventory, context) >= ExtractUrgencyThreshold;
    }
}
