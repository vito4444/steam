using System;
using System.Collections.Generic;

namespace Worker.Core
{
    /// <summary>
    /// A placed building. Footprint grows from <see cref="Origin"/> towards +X/+Y after
    /// rotation is applied, so <see cref="Width"/>/<see cref="Height"/> are the rotated extents.
    /// </summary>
    public sealed class BuildingInstance
    {
        public readonly int Id;
        public readonly BuildingKind Kind;
        public readonly GridPos Origin;
        public readonly Direction Facing;

        public readonly Inventory Input;
        public readonly Inventory Output;

        /// <summary>Recipe this station is set to run. <see cref="RecipeId.None"/> means idle.</summary>
        public RecipeId ActiveRecipe;

        /// <summary>Accumulated labour in work-units; a recipe completes at <see cref="Recipe.WorkTicks"/> * 100.</summary>
        public int WorkProgress;

        /// <summary>Worker currently assigned to operate this station, or 0.</summary>
        public int OperatorWorkerId;

        public BuildingInstance(int id, BuildingKind kind, GridPos origin, Direction facing)
        {
            var def = BuildingData.Get(kind);
            Id = id;
            Kind = kind;
            Origin = origin;
            Facing = facing;
            Input = new Inventory(Math.Max(1, def.InputSlots));
            Output = new Inventory(Math.Max(1, def.OutputSlots));
            ActiveRecipe = RecipeId.None;
        }

        public BuildingDef Def => BuildingData.Get(Kind);

        public int Width
        {
            get
            {
                var def = Def;
                return (Facing == Direction.East || Facing == Direction.West) ? def.Height : def.Width;
            }
        }

        public int Height
        {
            get
            {
                var def = Def;
                return (Facing == Direction.East || Facing == Direction.West) ? def.Width : def.Height;
            }
        }

        public bool HasInputSlots => Def.InputSlots > 0;
        public bool HasOutputSlots => Def.OutputSlots > 0;

        public IEnumerable<GridPos> Footprint()
        {
            int w = Width, h = Height;
            for (int dy = 0; dy < h; dy++)
            {
                for (int dx = 0; dx < w; dx++)
                {
                    yield return new GridPos(Origin.X + dx, Origin.Y + dy);
                }
            }
        }

        public bool Covers(GridPos pos)
        {
            return pos.X >= Origin.X && pos.X < Origin.X + Width
                && pos.Y >= Origin.Y && pos.Y < Origin.Y + Height;
        }

        /// <summary>
        /// Tiles orthogonally adjacent to the footprint, in a stable clockwise order
        /// starting from the south-west. Workers interact with the building from these.
        /// </summary>
        public List<GridPos> AdjacentTiles()
        {
            int w = Width, h = Height;
            var tiles = new List<GridPos>((w + h) * 2);

            for (int dx = 0; dx < w; dx++) tiles.Add(new GridPos(Origin.X + dx, Origin.Y - 1));
            for (int dy = 0; dy < h; dy++) tiles.Add(new GridPos(Origin.X + w, Origin.Y + dy));
            for (int dx = w - 1; dx >= 0; dx--) tiles.Add(new GridPos(Origin.X + dx, Origin.Y + h));
            for (int dy = h - 1; dy >= 0; dy--) tiles.Add(new GridPos(Origin.X - 1, Origin.Y + dy));

            return tiles;
        }

        /// <summary>Centre of the footprint in tile units scaled by 100, for presentation and distance sorting.</summary>
        public GridPos CenterTile()
            => new GridPos(Origin.X + (Width - 1) / 2, Origin.Y + (Height - 1) / 2);

        public Recipe CurrentRecipe()
            => ActiveRecipe == RecipeId.None ? null : GameData.Recipe(ActiveRecipe);

        /// <summary>True when inputs are satisfied and the output buffer has room for the result.</summary>
        public bool CanStartWork()
        {
            var recipe = CurrentRecipe();
            if (recipe == null) return false;
            if (!Input.ContainsAll(recipe.Inputs)) return false;
            return Output.SpaceFor(recipe.Output.Item) >= recipe.Output.Count;
        }

        public override string ToString() => Kind + "#" + Id + "@" + Origin;
    }
}
