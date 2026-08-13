using System;
using System.Collections.Generic;
using Worker.Core;

namespace Worker.Preview
{
    public sealed class RenderOptions
    {
        public int CanvasWidth = 1600;
        public int CanvasHeight = 900;
        public int TilePixels = 44;

        /// <summary>Draw each worker's current path. Useful when judging scheduler behaviour.</summary>
        public bool ShowPaths = true;

        /// <summary>Draw worker names next to the figures.</summary>
        public bool ShowWorkerNames = true;

        /// <summary>Draw the right-hand roster and order panel.</summary>
        public bool ShowSidePanel = true;

        public string Caption;
    }

    /// <summary>
    /// Renders a <see cref="SimWorld"/> to a bitmap using the shared <see cref="Palette"/>.
    ///
    /// The layout here is the game's visual specification: a top status strip, a single
    /// screen containing the whole factory, and a right-hand roster. The Unity renderer
    /// is expected to reproduce this composition, so preview frames stay a valid stand-in
    /// for real screenshots when reviewing art direction.
    /// </summary>
    public sealed class WorldRenderer
    {
        private readonly RenderOptions _options;

        private const int TopBarHeight = 52;
        private const int SidePanelWidth = 404;
        private const int Margin = 16;

        public WorldRenderer(RenderOptions options = null)
        {
            _options = options ?? new RenderOptions();
        }

        public Raster Render(SimWorld world)
        {
            var raster = new Raster(_options.CanvasWidth, _options.CanvasHeight);
            raster.Clear(Palette.Yard.Darken(35));

            int viewWidth = _options.CanvasWidth - (_options.ShowSidePanel ? SidePanelWidth : 0) - Margin * 2;
            int viewHeight = _options.CanvasHeight - TopBarHeight - Margin * 2;

            int tile = ChooseTileSize(world.Map, viewWidth, viewHeight);
            int worldPixelWidth = world.Map.Width * tile;
            int worldPixelHeight = world.Map.Height * tile;

            int originX = Margin + Math.Max(0, (viewWidth - worldPixelWidth) / 2);
            int originY = TopBarHeight + Margin + Math.Max(0, (viewHeight - worldPixelHeight) / 2);

            DrawFloor(raster, world, originX, originY, tile);
            DrawConveyors(raster, world, originX, originY, tile);
            if (_options.ShowPaths) DrawPaths(raster, world, originX, originY, tile);
            DrawBuildings(raster, world, originX, originY, tile);
            DrawWorkers(raster, world, originX, originY, tile);
            DrawTopBar(raster, world);
            if (_options.ShowSidePanel) DrawSidePanel(raster, world);
            if (!string.IsNullOrEmpty(_options.Caption)) DrawCaption(raster, _options.Caption);

            return raster;
        }

        private int ChooseTileSize(TileMap map, int viewWidth, int viewHeight)
        {
            int byWidth = viewWidth / map.Width;
            int byHeight = viewHeight / map.Height;
            int tile = Math.Min(byWidth, byHeight);
            if (tile < 6) tile = 6;
            return Math.Min(tile, _options.TilePixels);
        }

        /// <summary>Converts a tile coordinate to screen pixels. Tile Y grows up, screen Y grows down.</summary>
        private static void TileToScreen(GridPos pos, int originX, int originY, int tile, int mapHeight,
            out int x, out int y)
        {
            x = originX + pos.X * tile;
            y = originY + (mapHeight - 1 - pos.Y) * tile;
        }

        // ------------------------------------------------------------------ floor

        private void DrawFloor(Raster raster, SimWorld world, int originX, int originY, int tile)
        {
            var map = world.Map;

            // Drop shadow under the whole slab gives the factory a physical presence.
            raster.FillRect(originX + 4, originY + 4, map.Width * tile, map.Height * tile,
                Palette.Yard.Darken(50));

            for (int ty = 0; ty < map.Height; ty++)
            {
                for (int tx = 0; tx < map.Width; tx++)
                {
                    var pos = new GridPos(tx, ty);
                    TileToScreen(pos, originX, originY, tile, map.Height, out int sx, out int sy);

                    var terrain = map.GetTerrain(pos);
                    if (terrain == TerrainKind.Yard)
                    {
                        raster.FillRect(sx, sy, tile, tile, Palette.Yard);
                        continue;
                    }

                    var floor = ((tx + ty) & 1) == 0 ? Palette.FloorA : Palette.FloorB;
                    raster.FillRect(sx, sy, tile, tile, floor);
                    raster.FillRect(sx, sy, tile, 1, Palette.FloorLine);
                    raster.FillRect(sx, sy, 1, tile, Palette.FloorLine);
                }
            }
        }

        // -------------------------------------------------------------- conveyors

        /// <summary>
        /// Belts are drawn under the stations, as floor-level infrastructure: a recessed
        /// bed, two rails, a direction chevron, and the items riding along. Making the
        /// flow itself visible is what turns a grid of boxes into a factory.
        /// </summary>
        private void DrawConveyors(Raster raster, SimWorld world, int originX, int originY, int tile)
        {
            int mapHeight = world.Map.Height;

            for (int i = 0; i < world.Buildings.Count; i++)
            {
                var belt = world.Buildings[i];
                if (!belt.IsConveyor) continue;

                TileToScreen(belt.Origin, originX, originY, tile, mapHeight, out int sx, out int sy);

                raster.FillRect(sx, sy, tile, tile, Palette.ConveyorBed);

                bool horizontal = belt.Facing == Direction.East || belt.Facing == Direction.West;
                int railThickness = Math.Max(1, tile / 12);
                int railInset = Math.Max(2, tile / 6);

                if (horizontal)
                {
                    raster.FillRect(sx, sy + railInset, tile, railThickness, Palette.ConveyorRail);
                    raster.FillRect(sx, sy + tile - railInset - railThickness, tile, railThickness, Palette.ConveyorRail);
                }
                else
                {
                    raster.FillRect(sx + railInset, sy, railThickness, tile, Palette.ConveyorRail);
                    raster.FillRect(sx + tile - railInset - railThickness, sy, railThickness, tile, Palette.ConveyorRail);
                }

                DrawChevron(raster, sx, sy, tile, belt.Facing);
                DrawBeltItems(raster, belt, sx, sy, tile);
            }
        }

        /// <summary>
        /// Two solid chevrons per tile pointing along the belt. Solid beats outlined here:
        /// direction has to be legible at a glance across a whole factory, and thin marks
        /// disappear at this zoom.
        /// </summary>
        private void DrawChevron(Raster raster, int sx, int sy, int tile, Direction facing)
        {
            int thickness = Math.Max(2, tile / 9);
            int span = Math.Max(3, tile / 3);
            var color = Palette.ConveyorArrow;

            for (int index = 0; index < 2; index++)
            {
                int along = tile / 4 + index * tile / 2;
                for (int i = 0; i < span; i++)
                {
                    int offset = i - span / 2;
                    int depth = span / 2 - Math.Abs(offset);

                    // depth is largest on the centre row, which must be the chevron tip:
                    // the tip has to lead in the direction of travel, not trail it.
                    switch (facing)
                    {
                        case Direction.East:
                            raster.FillRect(sx + along + depth - thickness, sy + tile / 2 + offset, thickness, 1, color);
                            break;
                        case Direction.West:
                            raster.FillRect(sx + tile - along - depth, sy + tile / 2 + offset, thickness, 1, color);
                            break;
                        case Direction.North:
                            // Screen Y grows downwards, so north points up the screen.
                            raster.FillRect(sx + tile / 2 + offset, sy + tile - along - depth, 1, thickness, color);
                            break;
                        case Direction.South:
                            raster.FillRect(sx + tile / 2 + offset, sy + along + depth - thickness, 1, thickness, color);
                            break;
                    }
                }
            }
        }

        private void DrawBeltItems(Raster raster, BuildingInstance belt, int sx, int sy, int tile)
        {
            int chip = Math.Max(4, tile / 3);

            for (int slot = 0; slot < ConveyorState.Capacity; slot++)
            {
                var item = belt.Conveyor.ItemAt(slot);
                if (item == ItemId.None) continue;

                int permille = belt.Conveyor.PositionPermille(slot);
                int travel = tile - chip;
                int offset = travel * permille / 1000;

                int x, y;
                switch (belt.Facing)
                {
                    case Direction.East: x = sx + offset; y = sy + (tile - chip) / 2; break;
                    case Direction.West: x = sx + travel - offset; y = sy + (tile - chip) / 2; break;
                    case Direction.North: x = sx + (tile - chip) / 2; y = sy + travel - offset; break;
                    default: x = sx + (tile - chip) / 2; y = sy + offset; break;
                }

                raster.FillRect(x, y, chip, chip, Palette.BuildingEdge);
                raster.FillRect(x + 1, y + 1, chip - 2, chip - 2, Palette.ForItem(item));
            }
        }

        // ------------------------------------------------------------------ paths

        private void DrawPaths(Raster raster, SimWorld world, int originX, int originY, int tile)
        {
            int mapHeight = world.Map.Height;
            var trail = Palette.WorkerBody.WithAlpha(46);

            for (int i = 0; i < world.Workers.Count; i++)
            {
                var worker = world.Workers[i];
                if (!worker.HasPath) continue;

                var previous = worker.Pos;
                for (int step = worker.PathCursor; step < worker.Path.Count; step++)
                {
                    var next = worker.Path[step];
                    TileToScreen(previous, originX, originY, tile, mapHeight, out int ax, out int ay);
                    TileToScreen(next, originX, originY, tile, mapHeight, out int bx, out int by);
                    raster.DrawLine(ax + tile / 2, ay + tile / 2, bx + tile / 2, by + tile / 2, trail);
                    previous = next;
                }
            }
        }

        // -------------------------------------------------------------- buildings

        private void DrawBuildings(Raster raster, SimWorld world, int originX, int originY, int tile)
        {
            int mapHeight = world.Map.Height;

            for (int i = 0; i < world.Buildings.Count; i++)
            {
                var building = world.Buildings[i];
                if (building.IsConveyor) continue; // drawn earlier, at floor level

                int w = building.Width * tile;
                int h = building.Height * tile;

                // Screen position of the top-left corner: origin is the bottom-left tile.
                var topLeftTile = new GridPos(building.Origin.X, building.Origin.Y + building.Height - 1);
                TileToScreen(topLeftTile, originX, originY, tile, mapHeight, out int sx, out int sy);

                var accent = Palette.ForBuilding(building.Kind);
                int radius = Math.Max(2, tile / 5);

                // Shadow, plate, then accent panel: three values, matching the palette layers.
                raster.FillRoundedRect(sx + 2, sy + 3, w - 2, h - 2, radius, Palette.BuildingEdge);
                raster.FillRoundedRect(sx + 1, sy + 1, w - 3, h - 3, radius, Palette.BuildingBody);

                int inset = Math.Max(2, tile / 8);
                raster.FillRoundedRect(sx + inset + 1, sy + inset + 1, w - inset * 2 - 3, h - inset * 2 - 3,
                    Math.Max(1, radius - 1), accent);

                // Light from above.
                raster.FillRect(sx + inset + 2, sy + inset + 1, w - inset * 2 - 5, 1, accent.Lighten(24));

                DrawBuildingGlyph(raster, building, sx, sy, w, h, accent);
                DrawBuildingReadouts(raster, building, sx, sy, w, h, tile);
            }
        }

        /// <summary>
        /// Role pictograms. Deliberately geometric: at 24 pixels per tile the silhouette
        /// carries the meaning, and detail would only turn to mush.
        /// </summary>
        private void DrawBuildingGlyph(Raster raster, BuildingInstance building, int sx, int sy, int w, int h, RgbColor accent)
        {
            int cx = sx + w / 2;
            int cy = sy + h / 2;
            var ink = accent.Darken(52);
            int unit = Math.Max(1, w / 16);

            switch (building.Kind)
            {
                case BuildingKind.Intake:
                    raster.FillRect(cx - unit, cy - unit * 5, unit * 2, unit * 7, ink);
                    FillTriangleDown(raster, cx, cy + unit * 3, unit * 4, ink);
                    raster.FillRect(cx - unit * 6, cy - unit * 6, unit * 12, unit + 1, ink);
                    break;

                case BuildingKind.Shipping:
                    raster.FillRect(cx - unit, cy - unit * 2, unit * 2, unit * 7, ink);
                    FillTriangleUp(raster, cx, cy - unit * 5, unit * 4, ink);
                    raster.FillRect(cx - unit * 6, cy + unit * 5, unit * 12, unit + 1, ink);
                    break;

                case BuildingKind.Sawbench:
                    raster.FillCircle(cx, cy, unit * 5, ink);
                    raster.FillCircle(cx, cy, unit * 4 - 1, accent);
                    raster.FillCircle(cx, cy, unit, ink);
                    for (int i = 0; i < 8; i++)
                    {
                        double angle = i * Math.PI / 4.0;
                        int px = cx + (int)Math.Round(Math.Cos(angle) * unit * 6);
                        int py = cy + (int)Math.Round(Math.Sin(angle) * unit * 6);
                        raster.FillRect(px - unit / 2 - 1, py - unit / 2 - 1, unit + 1, unit + 1, ink);
                    }
                    break;

                case BuildingKind.Lathe:
                    raster.FillRect(cx - unit * 6, cy - unit, unit * 12, unit * 2, ink);
                    raster.FillRect(cx - unit * 6, cy - unit * 4, unit + 1, unit * 8, ink);
                    raster.FillRect(cx + unit * 5, cy - unit * 4, unit + 1, unit * 8, ink);
                    raster.FillCircle(cx, cy, unit * 2, ink);
                    break;

                case BuildingKind.AssemblyBench:
                    raster.FillRect(cx - unit * 5, cy - unit * 5, unit * 3, unit * 3, ink);
                    raster.FillRect(cx + unit * 2, cy - unit * 5, unit * 3, unit * 3, ink);
                    raster.FillRect(cx - unit * 5, cy + unit * 2, unit * 3, unit * 3, ink);
                    raster.FillRect(cx + unit * 2, cy + unit * 2, unit * 3, unit * 3, ink);
                    raster.FillRect(cx - unit, cy - unit, unit * 2, unit * 2, ink);
                    break;

                case BuildingKind.Storage:
                    raster.FillRect(sx + w / 6, cy - unit * 3, w - w / 3, Math.Max(1, unit), ink);
                    raster.FillRect(sx + w / 6, cy + unit * 2, w - w / 3, Math.Max(1, unit), ink);
                    break;

                case BuildingKind.BreakRoom:
                    raster.FillRect(cx - unit * 4, cy - unit * 3, unit * 7, unit * 6, ink);
                    raster.FillRect(cx - unit * 3, cy - unit * 2, unit * 5, unit * 4, accent);
                    raster.FillRect(cx + unit * 3, cy - unit * 2, unit * 2, unit * 3, ink);
                    raster.FillRect(cx - unit * 2, cy - unit * 6, unit, unit * 2, ink);
                    raster.FillRect(cx + unit, cy - unit * 6, unit, unit * 2, ink);
                    break;
            }
        }

        /// <summary>
        /// Buffer contents and work progress drawn on the building itself. Putting state
        /// on the object rather than in a tooltip is what lets a player diagnose a stalled
        /// line by looking at it, which is the core readability goal for this genre.
        /// </summary>
        private void DrawBuildingReadouts(Raster raster, BuildingInstance building, int sx, int sy, int w, int h, int tile)
        {
            int chip = Math.Max(3, tile / 4);
            int gap = Math.Max(1, chip / 4);

            // Input buffer along the bottom edge.
            if (building.Def.InputSlots > 0)
            {
                DrawBufferStrip(raster, building.Input, sx + 3, sy + h - chip - 3, w - 6, chip, gap);
            }

            // Output buffer along the top edge.
            if (building.Def.OutputSlots > 0)
            {
                DrawBufferStrip(raster, building.Output, sx + 3, sy + 3, w - 6, chip, gap);
            }

            if (!building.Def.IsStation) return;

            var recipe = building.CurrentRecipe();
            if (recipe == null) return;

            int required = recipe.WorkTicks * 100;
            if (building.WorkProgress <= 0) return;

            int barHeight = Math.Max(2, tile / 6);
            int barY = sy + h / 2 + tile / 3;
            raster.DrawBar(sx + 4, barY, w - 8, barHeight, building.WorkProgress, required,
                Palette.ProgressFill, Palette.ProgressTrack);
        }

        private void DrawBufferStrip(Raster raster, Inventory inventory, int x, int y, int width, int chip, int gap)
        {
            int drawn = 0;
            for (int slot = 0; slot < inventory.SlotCount; slot++)
            {
                var stack = inventory[slot];
                if (stack.IsEmpty) continue;

                int cx = x + drawn * (chip + gap);
                if (cx + chip > x + width) break;

                raster.FillRect(cx, y, chip, chip, Palette.BuildingEdge);
                raster.FillRect(cx + 1, y + 1, chip - 2, chip - 2, Palette.ForItem(stack.Item));
                drawn++;
            }
        }

        // ---------------------------------------------------------------- workers

        private void DrawWorkers(Raster raster, SimWorld world, int originX, int originY, int tile)
        {
            int mapHeight = world.Map.Height;

            for (int i = 0; i < world.Workers.Count; i++)
            {
                var worker = world.Workers[i];
                TileToScreen(worker.Pos, originX, originY, tile, mapHeight, out int sx, out int sy);

                int cx = sx + tile / 2;
                int cy = sy + tile / 2;
                int radius = Math.Max(3, tile / 3);

                bool tired = worker.Stamina <= SimConfig.StaminaSeekRestThreshold;
                var body = tired ? Palette.WorkerTired : Palette.WorkerBody;

                raster.FillCircle(cx + 1, cy + 2, radius, Palette.WorkerOutline.WithAlpha(120));
                raster.FillCircle(cx, cy, radius, Palette.WorkerOutline);
                raster.FillCircle(cx, cy, radius - 1, body);

                // Carried load rides on the worker's shoulder.
                if (!worker.Carried.IsEmpty)
                {
                    int chip = Math.Max(3, tile / 3);
                    int chipX = cx + radius - 1;
                    int chipY = cy - radius - chip + 2;
                    raster.FillRect(chipX - 1, chipY - 1, chip + 2, chip + 2, Palette.BuildingEdge);
                    raster.FillRect(chipX, chipY, chip, chip, Palette.ForItem(worker.Carried.Item));
                }

                // Stamina pip under the figure, only when it matters.
                if (worker.Stamina < WorkerUnit.MaxStamina * 2 / 3)
                {
                    int barWidth = tile - 4;
                    raster.DrawBar(cx - barWidth / 2, cy + radius + 2, barWidth, 2,
                        worker.Stamina, WorkerUnit.MaxStamina,
                        tired ? Palette.Warning : Palette.ProgressFill, Palette.ProgressTrack);
                }

                if (_options.ShowWorkerNames && tile >= 16)
                {
                    int textWidth = BitmapFont.MeasureWidth(worker.Name);
                    BitmapFont.DrawOutlined(raster, cx - textWidth / 2, cy - radius - 12,
                        worker.Name, Palette.TextPrimary, Palette.BuildingEdge);
                }
            }
        }

        // ----------------------------------------------------------------- chrome

        private void DrawTopBar(Raster raster, SimWorld world)
        {
            int width = _options.CanvasWidth;
            raster.FillRect(0, 0, width, TopBarHeight, Palette.PanelBackground);
            raster.FillRect(0, TopBarHeight - 1, width, 1, Palette.PanelBorder);

            int day = world.Tick / SimConfig.TicksPerDay + 1;
            int dayProgress = world.Tick % SimConfig.TicksPerDay;

            int x = 18;
            x += DrawStat(raster, x, "DAY", day.ToString(), Palette.TextPrimary);
            x += DrawStat(raster, x, "CASH", FormatMoney(world.Ledger.Balance), MoneyColor(world.Ledger.Balance));
            x += DrawStat(raster, x, "STAFF", world.Workers.Count.ToString(), Palette.TextPrimary);
            x += DrawStat(raster, x, "SHIPPED", world.TotalUnitsShipped.ToString(), Palette.TextPrimary);
            x += DrawStat(raster, x, "CRAFTS", world.TotalCraftsCompleted.ToString(), Palette.TextPrimary);
            DrawStat(raster, x, "TICK", world.Tick.ToString(), Palette.TextSecondary);

            // Day progress hairline across the very top.
            raster.DrawBar(0, 0, width, 2, dayProgress, SimConfig.TicksPerDay,
                Palette.AccentIntake, Palette.PanelBackground);
        }

        private int DrawStat(Raster raster, int x, string label, string value, RgbColor valueColor)
        {
            BitmapFont.Draw(raster, x, 12, label, Palette.TextSecondary);
            BitmapFont.Draw(raster, x, 26, value, valueColor, 2);

            int width = Math.Max(BitmapFont.MeasureWidth(label), BitmapFont.MeasureWidth(value, 2));
            return width + 34;
        }

        private void DrawSidePanel(Raster raster, SimWorld world)
        {
            int x = _options.CanvasWidth - SidePanelWidth;
            int y = TopBarHeight;
            int height = _options.CanvasHeight - TopBarHeight;

            raster.FillRect(x, y, SidePanelWidth, height, Palette.PanelBackground);
            raster.FillRect(x, y, 1, height, Palette.PanelBorder);

            int cursor = y + 18;
            cursor = DrawOrdersSection(raster, world, x + 18, cursor);
            cursor += 12;
            DrawRosterSection(raster, world, x + 18, cursor);
        }

        private int DrawOrdersSection(Raster raster, SimWorld world, int x, int y)
        {
            BitmapFont.Draw(raster, x, y, "ORDERS", Palette.TextSecondary);
            y += 18;

            int shown = 0;
            for (int i = 0; i < world.Orders.Count && shown < 4; i++)
            {
                var order = world.Orders[i];
                if (order.State == OrderState.Available) continue;

                var statusColor = order.State == OrderState.Fulfilled ? Palette.ProgressFill
                    : order.State == OrderState.Failed ? Palette.PlacementInvalid
                    : Palette.TextPrimary;

                string label = ItemLabel(order.Item);
                BitmapFont.Draw(raster, x, y, label, statusColor);
                string count = order.Delivered + "/" + order.Quantity;
                BitmapFont.Draw(raster, x + 210, y, count, statusColor);

                raster.DrawBar(x, y + 11, 300, 4, order.Delivered, order.Quantity,
                    statusColor, Palette.ProgressTrack);

                y += 26;
                shown++;
            }

            if (shown == 0)
            {
                BitmapFont.Draw(raster, x, y, "NO ACTIVE ORDERS", Palette.TextSecondary);
                y += 20;
            }

            return y;
        }

        private void DrawRosterSection(Raster raster, SimWorld world, int x, int y)
        {
            BitmapFont.Draw(raster, x, y, "STAFF", Palette.TextSecondary);
            y += 18;

            for (int i = 0; i < world.Workers.Count && i < 10; i++)
            {
                var worker = world.Workers[i];
                bool tired = worker.Stamina <= SimConfig.StaminaSeekRestThreshold;

                raster.FillCircle(x + 5, y + 4, 4, tired ? Palette.WorkerTired : Palette.WorkerBody);
                BitmapFont.Draw(raster, x + 16, y, worker.Name, Palette.TextPrimary);
                BitmapFont.Draw(raster, x + 90, y, TaskLabel(worker), Palette.TextSecondary);

                raster.DrawBar(x + 216, y + 1, 80, 5, worker.Stamina, WorkerUnit.MaxStamina,
                    tired ? Palette.Warning : Palette.ProgressFill, Palette.ProgressTrack);
                raster.DrawBar(x + 302, y + 1, 60, 5, worker.Morale, WorkerUnit.MaxMorale,
                    Palette.AccentBreakRoom, Palette.ProgressTrack);

                y += 20;
            }

            y += 8;
            BitmapFont.Draw(raster, x + 216, y, "STAMINA", Palette.TextSecondary);
            BitmapFont.Draw(raster, x + 302, y, "MORALE", Palette.TextSecondary);
        }

        private void DrawCaption(Raster raster, string caption)
        {
            int scale = 1;
            int width = BitmapFont.MeasureWidth(caption, scale) + 20;
            int height = BitmapFont.GlyphHeight * scale + 14;
            int x = 16;
            int y = _options.CanvasHeight - height - 12;

            raster.FillRect(x, y, width, height, Palette.PanelBackground);
            raster.StrokeRect(x, y, width, height, 1, Palette.PanelBorder);
            BitmapFont.Draw(raster, x + 10, y + 7, caption, Palette.TextPrimary, scale);
        }

        // ----------------------------------------------------------------- labels

        private static string TaskLabel(WorkerUnit worker)
        {
            if (worker.Task == null) return "IDLE";
            switch (worker.Task.Kind)
            {
                case TaskKind.Haul: return "HAUL " + ItemLabel(worker.Task.Item);
                case TaskKind.Operate: return "WORK";
                case TaskKind.Rest: return "BREAK";
                default: return "IDLE";
            }
        }

        private static string ItemLabel(ItemId item)
        {
            switch (item)
            {
                case ItemId.Log: return "LOG";
                case ItemId.Plank: return "PLANK";
                case ItemId.ChairLeg: return "LEG";
                case ItemId.Seat: return "SEAT";
                case ItemId.WoodChair: return "CHAIR";
                case ItemId.Fabric: return "FABRIC";
                case ItemId.MetalRod: return "ROD";
                case ItemId.OfficeChair: return "OFFICE CHAIR";
                case ItemId.Bookshelf: return "SHELF";
                default: return "NONE";
            }
        }

        private static string FormatMoney(int cents)
        {
            int whole = cents / 100;
            return "$" + whole.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
        }

        private static RgbColor MoneyColor(int cents)
            => cents < 0 ? Palette.PlacementInvalid : Palette.TextPrimary;

        private static void FillTriangleUp(Raster raster, int cx, int apexY, int halfWidth, RgbColor color)
        {
            for (int i = 0; i <= halfWidth; i++)
            {
                raster.FillRect(cx - i, apexY + i, i * 2 + 1, 1, color);
            }
        }

        private static void FillTriangleDown(Raster raster, int cx, int apexY, int halfWidth, RgbColor color)
        {
            for (int i = 0; i <= halfWidth; i++)
            {
                raster.FillRect(cx - i, apexY - i, i * 2 + 1, 1, color);
            }
        }
    }
}
