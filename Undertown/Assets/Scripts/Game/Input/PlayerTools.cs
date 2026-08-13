using Undertown.Core.Buildings;

namespace Undertown.Game.InputHandling
{
    public enum ToolMode
    {
        Inspect,
        Place,
        Excavate,
        Demolish,
    }

    /// <summary>
    /// What the mouse currently does. Kept separate from the controller so the HUD can read
    /// and set it without either side knowing how the other is built.
    /// </summary>
    public sealed class PlayerTools
    {
        public ToolMode Mode { get; private set; } = ToolMode.Inspect;
        public BuildingKind Selected { get; private set; } = BuildingKind.None;

        public void SelectBuilding(BuildingKind kind)
        {
            Selected = kind;
            Mode = kind == BuildingKind.None ? ToolMode.Inspect : ToolMode.Place;
        }

        public void SelectExcavate()
        {
            Selected = BuildingKind.None;
            Mode = ToolMode.Excavate;
        }

        public void SelectDemolish()
        {
            Selected = BuildingKind.None;
            Mode = ToolMode.Demolish;
        }

        public void Cancel()
        {
            Selected = BuildingKind.None;
            Mode = ToolMode.Inspect;
        }

        public string Describe()
        {
            switch (Mode)
            {
                case ToolMode.Place:
                    var def = BuildingCatalog.Get(Selected);
                    return def != null ? $"PLACING  {def.Name}" : "PLACING";
                case ToolMode.Excavate: return "EXCAVATE  ·  drag to mark earth for digging";
                case ToolMode.Demolish: return "DEMOLISH  ·  half the materials come back";
                default: return null;
            }
        }
    }
}
