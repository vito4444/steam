using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Worker.Core;

namespace Worker.Game
{
    /// <summary>
    /// The in-game interface: status strip, build bar, and an inspector for whatever is
    /// selected.
    ///
    /// The inspector is where the game's argument lives. Selecting a worker shows a
    /// person with a name, a wage, stamina and morale, and a "Lay off" button that
    /// states its severance cost and its morale cost to everyone who stays. Selecting a
    /// bench shows a machine that does the same job without any of that.
    /// </summary>
    [RequireComponent(typeof(SimRunner))]
    [RequireComponent(typeof(PlayerController))]
    public sealed class GameHud : MonoBehaviour
    {
        private const float TopBarHeight = 64f;
        private const float BuildBarHeight = 104f;
        private const float InspectorWidth = 340f;

        private SimRunner _runner;
        private PlayerController _player;

        private Text _dayText;
        private Text _cashText;
        private Text _staffText;
        private Text _shippedText;
        private Text _speedText;
        private Text _hintText;

        private RectTransform _inspector;
        private Text _inspectorTitle;
        private Text _inspectorBody;
        private Button _actionButton;
        private Text _actionLabel;
        private Image _barA;
        private Image _barB;
        private Text _barLabels;

        private readonly List<Button> _buildButtons = new List<Button>();
        private readonly List<Image> _buildButtonBackgrounds = new List<Image>();

        private void Awake()
        {
            _runner = GetComponent<SimRunner>();
            _player = GetComponent<PlayerController>();

            EnsureEventSystem();
            Build();
        }

        private void Update()
        {
            // Clicks that land on a panel must not also place a building underneath it.
            _player.PointerOverUi = EventSystem.current != null
                                    && EventSystem.current.IsPointerOverGameObject();

            RefreshStatus();
            RefreshBuildBar();
            RefreshInspector();
        }

        private static void EnsureEventSystem()
        {
            if (EventSystem.current != null) return;

            var holder = new GameObject("EventSystem");
            holder.AddComponent<EventSystem>();
            holder.AddComponent<StandaloneInputModule>();
        }

        // ------------------------------------------------------------------ build

        private void Build()
        {
            var canvas = UiKit.CreateCanvas("Hud", transform);
            BuildTopBar(canvas.transform);
            BuildBuildBar(canvas.transform);
            BuildInspector(canvas.transform);
        }

        private void BuildTopBar(Transform parent)
        {
            var bar = UiKit.CreatePanel("TopBar", parent, Palette.PanelBackground,
                new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -TopBarHeight), Vector2.zero);

            float x = 24f;
            _dayText = AddStat(bar, "Day", ref x, 90f);
            _cashText = AddStat(bar, "Cash", ref x, 170f);
            _staffText = AddStat(bar, "Staff", ref x, 90f);
            _shippedText = AddStat(bar, "Shipped", ref x, 120f);
            _speedText = AddStat(bar, "Speed", ref x, 110f);

            var pause = UiKit.CreateButton("Pause", bar, "Pause", 16, Palette.BuildingBody, Palette.TextPrimary);
            pause.GetComponent<RectTransform>().PlaceTopLeft(x, 16f, 90f, 32f);
            pause.onClick.AddListener(() => _runner.SpeedMultiplier = _runner.SpeedMultiplier > 0 ? 0 : 1);
            x += 98f;

            var faster = UiKit.CreateButton("Faster", bar, "Speed +", 16, Palette.BuildingBody, Palette.TextPrimary);
            faster.GetComponent<RectTransform>().PlaceTopLeft(x, 16f, 90f, 32f);
            faster.onClick.AddListener(() =>
            {
                _runner.SpeedMultiplier = _runner.SpeedMultiplier >= 8 ? 1 : Mathf.Max(1, _runner.SpeedMultiplier * 2);
            });
        }

        private Text AddStat(RectTransform parent, string label, ref float x, float width)
        {
            var caption = UiKit.CreateText(label + "Caption", parent, label.ToUpperInvariant(), 12, Palette.TextSecondary);
            caption.GetComponent<RectTransform>().PlaceTopLeft(x, 10f, width, 16f);

            var value = UiKit.CreateText(label + "Value", parent, "-", 22, Palette.TextPrimary);
            value.GetComponent<RectTransform>().PlaceTopLeft(x, 26f, width, 28f);

            x += width;
            return value;
        }

        private void BuildBuildBar(Transform parent)
        {
            var bar = UiKit.CreatePanel("BuildBar", parent, Palette.PanelBackground,
                new Vector2(0f, 0f), new Vector2(1f, 0f),
                Vector2.zero, new Vector2(0f, BuildBarHeight));

            float x = 24f;
            const float buttonWidth = 104f;

            for (int i = 0; i < PlayerController.Buildable.Length; i++)
            {
                var kind = PlayerController.Buildable[i];
                var def = BuildingData.Get(kind);

                var button = UiKit.CreateButton("Build" + kind, bar, null, 14,
                    Palette.BuildingBody, Palette.TextPrimary);
                button.GetComponent<RectTransform>().PlaceTopLeft(x, 12f, buttonWidth, 80f);

                var swatch = UiKit.CreateImage("Swatch", button.transform, null, Palette.ForBuilding(kind));
                swatch.GetComponent<RectTransform>().PlaceTopLeft(8f, 8f, 24f, 24f);

                var name = UiKit.CreateText("Name", button.transform, UiKit.BuildingLabel(kind), 14, Palette.TextPrimary);
                name.GetComponent<RectTransform>().PlaceTopLeft(38f, 10f, buttonWidth - 42f, 20f);

                var cost = UiKit.CreateText("Cost", button.transform,
                    def.Cost > 0 ? UiKit.FormatMoney(def.Cost) : "free", 13, Palette.TextSecondary);
                cost.GetComponent<RectTransform>().PlaceTopLeft(8f, 38f, buttonWidth - 16f, 18f);

                var hotkey = UiKit.CreateText("Hotkey", button.transform, (i + 1).ToString(), 12, Palette.TextSecondary);
                hotkey.GetComponent<RectTransform>().PlaceTopLeft(8f, 58f, 40f, 16f);

                var captured = kind;
                button.onClick.AddListener(() => _player.SetSelectedKind(captured));

                _buildButtons.Add(button);
                _buildButtonBackgrounds.Add(button.GetComponent<Image>());

                x += buttonWidth + 8f;
            }

            x += 16f;

            var rotate = UiKit.CreateButton("Rotate", bar, "Rotate  R", 15, Palette.BuildingBody, Palette.TextPrimary);
            rotate.GetComponent<RectTransform>().PlaceTopLeft(x, 12f, 110f, 36f);
            rotate.onClick.AddListener(() => _player.RotatePlacement());

            var demolish = UiKit.CreateButton("Demolish", bar, "Demolish  X", 15,
                Palette.PlacementInvalid, Palette.TextPrimary);
            demolish.GetComponent<RectTransform>().PlaceTopLeft(x, 56f, 110f, 36f);
            demolish.onClick.AddListener(() => _player.SetMode(
                _player.Mode == InteractionMode.Demolish ? InteractionMode.Select : InteractionMode.Demolish));

            x += 126f;

            var hire = UiKit.CreateButton("Hire", bar, "Hire worker  $120", 15,
                Palette.AccentAssembly, Palette.PanelBackground);
            hire.GetComponent<RectTransform>().PlaceTopLeft(x, 12f, 180f, 36f);
            hire.onClick.AddListener(() => _player.HireWorker());

            _hintText = UiKit.CreateText("Hint", bar,
                "Left click to place, right click to cancel, click anything to inspect it",
                14, Palette.TextSecondary);
            _hintText.GetComponent<RectTransform>().PlaceTopLeft(x, 58f, 520f, 20f);
        }

        private void BuildInspector(Transform parent)
        {
            _inspector = UiKit.CreatePanel("Inspector", parent, Palette.PanelBackground,
                new Vector2(1f, 0f), new Vector2(1f, 1f),
                new Vector2(-InspectorWidth, BuildBarHeight), new Vector2(0f, -TopBarHeight));

            _inspectorTitle = UiKit.CreateText("Title", _inspector, "", 20, Palette.TextPrimary);
            _inspectorTitle.GetComponent<RectTransform>().PlaceTopLeft(20f, 20f, InspectorWidth - 40f, 28f);

            _inspectorBody = UiKit.CreateText("Body", _inspector, "", 15, Palette.TextSecondary);
            _inspectorBody.GetComponent<RectTransform>().PlaceTopLeft(20f, 56f, InspectorWidth - 40f, 220f);
            _inspectorBody.verticalOverflow = VerticalWrapMode.Overflow;
            _inspectorBody.horizontalOverflow = HorizontalWrapMode.Wrap;

            _barLabels = UiKit.CreateText("BarLabels", _inspector, "", 12, Palette.TextSecondary);
            _barLabels.GetComponent<RectTransform>().PlaceTopLeft(20f, 268f, InspectorWidth - 40f, 16f);

            _barA = UiKit.CreateBar("BarA", _inspector, Palette.ProgressFill);
            _barA.transform.parent.GetComponent<RectTransform>().PlaceTopLeft(20f, 288f, InspectorWidth - 40f, 10f);

            _barB = UiKit.CreateBar("BarB", _inspector, Palette.AccentBreakRoom);
            _barB.transform.parent.GetComponent<RectTransform>().PlaceTopLeft(20f, 306f, InspectorWidth - 40f, 10f);

            _actionButton = UiKit.CreateButton("Action", _inspector, "", 15,
                Palette.BuildingBody, Palette.TextPrimary);
            _actionButton.GetComponent<RectTransform>().PlaceTopLeft(20f, 336f, InspectorWidth - 40f, 40f);
            _actionLabel = _actionButton.GetComponentInChildren<Text>();
            _actionButton.onClick.AddListener(OnActionClicked);
        }

        // ---------------------------------------------------------------- refresh

        private void RefreshStatus()
        {
            var world = _runner.World;
            if (world == null) return;

            _dayText.text = (world.Tick / SimConfig.TicksPerDay + 1).ToString();
            _cashText.text = UiKit.FormatMoney(world.Ledger.Balance);
            _cashText.color = (world.Ledger.Balance < 0 ? Palette.PlacementInvalid : Palette.TextPrimary).ToUnity();
            _staffText.text = world.Workers.Count.ToString();
            _shippedText.text = world.TotalUnitsShipped.ToString();
            _speedText.text = _runner.SpeedMultiplier <= 0 ? "paused" : _runner.SpeedMultiplier + "x";
        }

        private void RefreshBuildBar()
        {
            for (int i = 0; i < _buildButtonBackgrounds.Count; i++)
            {
                bool active = _player.Mode == InteractionMode.Build
                              && _player.SelectedKind == PlayerController.Buildable[i];

                _buildButtonBackgrounds[i].color = (active ? Palette.AccentIntake : Palette.BuildingBody).ToUnity();
            }

            _hintText.text = _player.Mode switch
            {
                InteractionMode.Build => "Placing " + UiKit.BuildingLabel(_player.SelectedKind)
                                                    + " facing " + _player.PlacementFacing
                                                    + "   -   R to rotate, right click to stop",
                InteractionMode.Demolish => "Demolish mode: click a building to remove it. Intake and dispatch are protected.",
                _ => "Left click to place, right click to cancel, click anything to inspect it"
            };
        }

        private void RefreshInspector()
        {
            var worker = _player.SelectedWorker;
            var building = _player.SelectedBuilding;

            if (worker != null)
            {
                ShowWorker(worker);
                return;
            }

            if (building != null)
            {
                ShowBuilding(building);
                return;
            }

            _inspectorTitle.text = "Nothing selected";
            _inspectorBody.text = "Click a worker or a machine to see what it is doing.\n\n"
                                  + "Your people are the expensive part. Every belt you lay is a trip "
                                  + "somebody no longer has to walk.";
            SetBars(false, 0f, 0f, "");
            SetAction(false, "");
        }

        private void ShowWorker(WorkerUnit worker)
        {
            _inspectorTitle.text = worker.Name;

            string carrying = worker.Carried.IsEmpty
                ? "empty handed"
                : "carrying " + worker.Carried.Count + " " + UiKit.ItemLabel(worker.Carried.Item).ToLowerInvariant();

            _inspectorBody.text =
                UiKit.TaskLabel(worker) + ", " + carrying + ".\n\n"
                + "Wage      " + UiKit.FormatMoney(worker.DailyWage) + " per day\n"
                + "Pace      " + worker.EffectiveRate() + "% of standard\n"
                + "Worked    " + (worker.LifetimeWorkTicks / SimConfig.TicksPerSecond) + " seconds of labour\n";

            SetBars(true,
                worker.Stamina / (float)WorkerUnit.MaxStamina,
                worker.Morale / (float)WorkerUnit.MaxMorale,
                "Stamina and morale");

            SetAction(true, "Lay off  -  " + UiKit.FormatMoney(worker.DailyWage * 7) + " severance");
        }

        private void ShowBuilding(BuildingInstance building)
        {
            _inspectorTitle.text = UiKit.BuildingLabel(building.Kind);

            var recipe = building.CurrentRecipe();
            var body = new System.Text.StringBuilder();

            if (building.Def.IsStation)
            {
                body.Append("Making: ").Append(UiKit.RecipeLabel(building.ActiveRecipe)).Append('\n');
                if (recipe != null)
                {
                    body.Append("Needs:  ");
                    for (int i = 0; i < recipe.Inputs.Length; i++)
                    {
                        if (i > 0) body.Append(", ");
                        body.Append(recipe.Inputs[i].Count).Append(' ')
                            .Append(UiKit.ItemLabel(recipe.Inputs[i].Item).ToLowerInvariant());
                    }
                    body.Append('\n');
                    body.Append("Makes:  ").Append(recipe.Output.Count).Append(' ')
                        .Append(UiKit.ItemLabel(recipe.Output.Item).ToLowerInvariant()).Append('\n');
                }

                body.Append("Operator: ")
                    .Append(building.OperatorWorkerId == 0 ? "nobody" : "worker #" + building.OperatorWorkerId)
                    .Append('\n');
            }

            AppendInventory(body, "In", building.Input);
            AppendInventory(body, "Out", building.Output);

            if (building.IsConveyor)
            {
                body.Append("Facing ").Append(building.Facing).Append('\n');
                body.Append("Carrying ").Append(building.Conveyor.Count).Append(" of ")
                    .Append(ConveyorState.Capacity).Append('\n');
            }

            _inspectorBody.text = body.ToString();

            if (building.Def.IsStation && recipe != null)
            {
                SetBars(true, Mathf.Clamp01(building.WorkProgress / (float)(recipe.WorkTicks * 100)), 0f, "Progress");
            }
            else
            {
                SetBars(false, 0f, 0f, "");
            }

            bool canCycle = building.Def.IsStation && GameData.RecipesFor(building.Kind).Count > 1;
            SetAction(canCycle, "Switch recipe");
        }

        private static void AppendInventory(System.Text.StringBuilder body, string label, Inventory inventory)
        {
            if (inventory.IsEmpty) return;

            body.Append(label).Append(":     ");
            bool first = true;
            for (int i = 0; i < inventory.SlotCount; i++)
            {
                var stack = inventory[i];
                if (stack.IsEmpty) continue;
                if (!first) body.Append(", ");
                body.Append(stack.Count).Append(' ').Append(UiKit.ItemLabel(stack.Item).ToLowerInvariant());
                first = false;
            }
            body.Append('\n');
        }

        private void SetBars(bool visible, float a, float b, string label)
        {
            _barA.transform.parent.gameObject.SetActive(visible);
            _barB.transform.parent.gameObject.SetActive(visible && b > 0f);
            _barLabels.text = visible ? label : "";

            if (!visible) return;

            UiKit.SetBarFill(_barA, a);
            if (b > 0f) UiKit.SetBarFill(_barB, b);
        }

        private void SetAction(bool visible, string label)
        {
            _actionButton.gameObject.SetActive(visible);
            if (visible) _actionLabel.text = label;
        }

        private void OnActionClicked()
        {
            if (_player.SelectedWorker != null)
            {
                _player.LayOffSelectedWorker();
                return;
            }

            if (_player.SelectedBuilding != null)
            {
                _player.CycleRecipe(_player.SelectedBuilding);
            }
        }
    }
}
