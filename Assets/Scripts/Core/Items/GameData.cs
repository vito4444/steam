using System;
using System.Collections.Generic;

namespace Worker.Core
{
    public sealed class ItemDef
    {
        public readonly ItemId Id;
        public readonly string DisplayKey;
        public readonly int StackSize;
        /// <summary>Reference value in cents, used for order pricing and scrap value.</summary>
        public readonly int BaseValue;

        public ItemDef(ItemId id, string displayKey, int stackSize, int baseValue)
        {
            Id = id;
            DisplayKey = displayKey;
            StackSize = stackSize;
            BaseValue = baseValue;
        }
    }

    /// <summary>
    /// Static content tables. Kept in code rather than ScriptableObjects so that
    /// <see cref="Worker.Core"/> stays engine-free and unit-testable, and so that
    /// content changes are reviewable as plain diffs.
    /// </summary>
    public static class GameData
    {
        private static readonly ItemDef[] Items =
        {
            new ItemDef(ItemId.None,        "item.none",        0,    0),
            new ItemDef(ItemId.Log,         "item.log",         20,  120),
            new ItemDef(ItemId.Plank,       "item.plank",       50,   60),
            new ItemDef(ItemId.ChairLeg,    "item.chair_leg",   50,   45),
            new ItemDef(ItemId.Seat,        "item.seat",        20,  180),
            new ItemDef(ItemId.WoodChair,   "item.wood_chair",  10,  900),
            new ItemDef(ItemId.Fabric,      "item.fabric",      40,  150),
            new ItemDef(ItemId.MetalRod,    "item.metal_rod",   40,  200),
            new ItemDef(ItemId.OfficeChair, "item.office_chair", 8, 2200),
            new ItemDef(ItemId.Bookshelf,   "item.bookshelf",    6, 1600)
        };

        private static readonly Recipe[] Recipes =
        {
            new Recipe(
                RecipeId.SawLogs, "recipe.saw_logs",
                new[] { new ItemStack(ItemId.Log, 1) },
                new ItemStack(ItemId.Plank, 3),
                40, BuildingKind.Sawbench),

            new Recipe(
                RecipeId.TurnLegs, "recipe.turn_legs",
                new[] { new ItemStack(ItemId.Plank, 1) },
                new ItemStack(ItemId.ChairLeg, 2),
                30, BuildingKind.Lathe),

            new Recipe(
                RecipeId.CutSeat, "recipe.cut_seat",
                new[] { new ItemStack(ItemId.Plank, 2) },
                new ItemStack(ItemId.Seat, 1),
                50, BuildingKind.Sawbench),

            new Recipe(
                RecipeId.AssembleWoodChair, "recipe.assemble_wood_chair",
                new[] { new ItemStack(ItemId.ChairLeg, 4), new ItemStack(ItemId.Seat, 1) },
                new ItemStack(ItemId.WoodChair, 1),
                80, BuildingKind.AssemblyBench)
        };

        private static readonly Dictionary<ItemId, ItemDef> ItemLookup = BuildItemLookup();
        private static readonly Dictionary<RecipeId, Recipe> RecipeLookup = BuildRecipeLookup();

        private static Dictionary<ItemId, ItemDef> BuildItemLookup()
        {
            var map = new Dictionary<ItemId, ItemDef>(Items.Length);
            foreach (var def in Items) map[def.Id] = def;
            return map;
        }

        private static Dictionary<RecipeId, Recipe> BuildRecipeLookup()
        {
            var map = new Dictionary<RecipeId, Recipe>(Recipes.Length);
            foreach (var recipe in Recipes) map[recipe.Id] = recipe;
            return map;
        }

        public static ItemDef Item(ItemId id)
        {
            if (ItemLookup.TryGetValue(id, out var def)) return def;
            throw new ArgumentOutOfRangeException(nameof(id), "No ItemDef registered for " + id);
        }

        public static Recipe Recipe(RecipeId id)
        {
            if (RecipeLookup.TryGetValue(id, out var recipe)) return recipe;
            throw new ArgumentOutOfRangeException(nameof(id), "No Recipe registered for " + id);
        }

        public static IReadOnlyList<Recipe> AllRecipes => Recipes;
        public static IReadOnlyList<ItemDef> AllItems => Items;

        /// <summary>Recipes this station kind is able to run, in declaration order.</summary>
        public static List<Recipe> RecipesFor(BuildingKind station)
        {
            var result = new List<Recipe>();
            foreach (var recipe in Recipes)
            {
                if (recipe.Station == station) result.Add(recipe);
            }
            return result;
        }
    }
}
