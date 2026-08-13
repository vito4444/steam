using System;

namespace Worker.Core
{
    public enum RecipeId : ushort
    {
        None = 0,
        SawLogs = 1,
        TurnLegs = 2,
        CutSeat = 3,
        AssembleWoodChair = 4
    }

    /// <summary>
    /// A production step. <see cref="WorkTicks"/> is the labour required at 100% worker
    /// speed; an operator with a different speed rating consumes it faster or slower.
    /// </summary>
    public sealed class Recipe
    {
        public readonly RecipeId Id;
        public readonly string DisplayKey;
        public readonly ItemStack[] Inputs;
        public readonly ItemStack Output;
        public readonly int WorkTicks;
        public readonly BuildingKind Station;

        public Recipe(RecipeId id, string displayKey, ItemStack[] inputs, ItemStack output, int workTicks, BuildingKind station)
        {
            if (inputs == null || inputs.Length == 0) throw new ArgumentException("Recipe needs at least one input", nameof(inputs));
            if (output.IsEmpty) throw new ArgumentException("Recipe needs an output", nameof(output));
            if (workTicks <= 0) throw new ArgumentOutOfRangeException(nameof(workTicks));

            Id = id;
            DisplayKey = displayKey;
            Inputs = inputs;
            Output = output;
            WorkTicks = workTicks;
            Station = station;
        }
    }
}
