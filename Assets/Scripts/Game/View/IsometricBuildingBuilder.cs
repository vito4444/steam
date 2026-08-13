using System;
using UnityEngine;
using Worker.Core;

namespace Worker.Game
{
    /// <summary>
    /// Assembles each building out of primitives into a recognisable machine.
    ///
    /// The first isometric pass gave every building the same silhouette: a tinted box
    /// with one lump on top. Colour alone was carrying all of the identification, which
    /// fails the moment two machines sit next to each other or the player is colour
    /// blind. Every kind here gets its own outline instead: the sawbench has a visible
    /// blade, the lathe a spindle between two headstocks, the break room a pitched roof
    /// and lit windows.
    ///
    /// Silhouette is deliberately doing the work rather than surface detail. At this
    /// camera distance a machine is perhaps eighty pixels across, so its outline is
    /// legible and its texture is not.
    /// </summary>
    public sealed class IsometricBuildingBuilder
    {
        /// <summary>Top of the plinth every machine stands on, in world units.</summary>
        public const float Deck = 0.32f;

        private readonly Func<BuildingKind, int, Material> _shade;
        private readonly Material _metal;
        private readonly Material _darkMetal;
        private readonly Material _glass;
        private readonly Material _hazard;

        public IsometricBuildingBuilder(
            Func<BuildingKind, int, Material> shade,
            Material metal, Material darkMetal, Material glass, Material hazard)
        {
            _shade = shade;
            _metal = metal;
            _darkMetal = darkMetal;
            _glass = glass;
            _hazard = hazard;
        }

        public void Build(Transform parent, BuildingInstance building)
        {
            switch (building.Kind)
            {
                case BuildingKind.Sawbench: BuildSawbench(parent, building); break;
                case BuildingKind.Lathe: BuildLathe(parent, building); break;
                case BuildingKind.AssemblyBench: BuildAssembly(parent, building); break;
                case BuildingKind.Intake: BuildDock(parent, building, inbound: true); break;
                case BuildingKind.Shipping: BuildDock(parent, building, inbound: false); break;
                case BuildingKind.Storage: BuildRack(parent, building); break;
                case BuildingKind.BreakRoom: BuildBreakRoom(parent, building); break;
                case BuildingKind.Wall: BuildWall(parent, building); break;
                default: BuildGenericBlock(parent, building); break;
            }
        }

        // ---------------------------------------------------------------- machines

        private void BuildSawbench(Transform parent, BuildingInstance building)
        {
            var body = _shade(building.Kind, 0);
            var dark = _shade(building.Kind, 42);

            Plinth(parent, building);

            // Table with a slot down the middle for the blade to rise through.
            Box(parent, "TableLeft", body, new Vector3(1.7f, 0.34f, 0.62f), new Vector3(0f, Deck + 0.17f, -0.45f));
            Box(parent, "TableRight", body, new Vector3(1.7f, 0.34f, 0.62f), new Vector3(0f, Deck + 0.17f, 0.45f));

            // The blade. A thin flattened cylinder standing on edge, which is the single
            // feature that makes this machine identifiable at a glance.
            var blade = Cylinder(parent, "Blade", _metal, new Vector3(0.66f, 0.03f, 0.66f),
                new Vector3(0f, Deck + 0.5f, 0f));
            blade.localRotation = Quaternion.Euler(90f, 0f, 0f);

            Box(parent, "Motor", dark, new Vector3(0.5f, 0.4f, 0.44f), new Vector3(-0.6f, Deck + 0.2f, 0f));

            // Extraction line running from the blade housing down to the floor. Real
            // woodworking machines are defined as much by their ducting as their tools.
            Cylinder(parent, "DustPipe", _darkMetal, new Vector3(0.16f, 0.5f, 0.16f),
                new Vector3(0.66f, Deck + 0.55f, -0.62f));
            var duct = Cylinder(parent, "DustDuct", _darkMetal, new Vector3(0.14f, 0.45f, 0.14f),
                new Vector3(0.66f, Deck + 0.95f, -0.34f));
            duct.localRotation = Quaternion.Euler(90f, 0f, 0f);

            // Operator console: a panel with a lit readout and two buttons.
            Box(parent, "Console", dark, new Vector3(0.34f, 0.44f, 0.16f), new Vector3(0.52f, Deck + 0.42f, 0.8f));
            Box(parent, "Screen", _glass, new Vector3(0.2f, 0.13f, 0.03f), new Vector3(0.52f, Deck + 0.56f, 0.89f));
            Box(parent, "ButtonA", _hazard, new Vector3(0.06f, 0.06f, 0.03f), new Vector3(0.45f, Deck + 0.38f, 0.89f));
            Box(parent, "ButtonB", _metal, new Vector3(0.06f, 0.06f, 0.03f), new Vector3(0.58f, Deck + 0.38f, 0.89f));

            // Guard rail on the operator side.
            for (int i = 0; i < 3; i++)
            {
                Cylinder(parent, "GuardPost", _hazard, new Vector3(0.06f, 0.24f, 0.06f),
                    new Vector3(-0.7f + i * 0.7f, Deck + 0.24f, 0.86f));
            }
            Cylinder(parent, "GuardRail", _hazard, new Vector3(0.05f, 0.75f, 0.05f),
                new Vector3(0f, Deck + 0.46f, 0.86f)).localRotation = Quaternion.Euler(0f, 0f, 90f);

            // Timber on the table, mid cut, plus offcuts underneath.
            Box(parent, "Stock", _shade(building.Kind, 18), new Vector3(1.25f, 0.11f, 0.16f),
                new Vector3(0.1f, Deck + 0.4f, -0.72f));
            Box(parent, "Offcut", _shade(building.Kind, 30), new Vector3(0.4f, 0.07f, 0.12f),
                new Vector3(-0.5f, Deck + 0.38f, 0.42f));
            Box(parent, "Sawdust", _shade(building.Kind, 12), new Vector3(0.44f, 0.05f, 0.3f),
                new Vector3(0.22f, 0.05f, 0.62f));
        }

        private void BuildLathe(Transform parent, BuildingInstance building)
        {
            var body = _shade(building.Kind, 0);
            var dark = _shade(building.Kind, 45);

            Plinth(parent, building);

            Box(parent, "Bed", body, new Vector3(1.72f, 0.3f, 0.66f), new Vector3(0f, Deck + 0.15f, 0f));
            Box(parent, "Headstock", body, new Vector3(0.5f, 0.62f, 0.72f), new Vector3(-0.62f, Deck + 0.44f, 0f));
            Box(parent, "Tailstock", dark, new Vector3(0.34f, 0.42f, 0.6f), new Vector3(0.68f, Deck + 0.36f, 0f));

            // The workpiece turning between the two ends.
            var spindle = Cylinder(parent, "Spindle", _metal, new Vector3(0.2f, 0.5f, 0.2f),
                new Vector3(0.03f, Deck + 0.42f, 0f));
            spindle.localRotation = Quaternion.Euler(0f, 0f, 90f);

            Box(parent, "ToolPost", _darkMetal, new Vector3(0.22f, 0.26f, 0.22f), new Vector3(0.1f, Deck + 0.43f, 0.42f));
            Box(parent, "Chips", _shade(building.Kind, 22), new Vector3(0.5f, 0.07f, 0.3f),
                new Vector3(0.3f, Deck + 0.33f, -0.55f));
            Cylinder(parent, "Wheel", _darkMetal, new Vector3(0.3f, 0.05f, 0.3f), new Vector3(-0.9f, Deck + 0.5f, 0f))
                .localRotation = Quaternion.Euler(0f, 0f, 90f);

            // Coolant arm reaching over the work, and the tray that catches swarf.
            Cylinder(parent, "CoolantPost", _metal, new Vector3(0.06f, 0.36f, 0.06f),
                new Vector3(-0.5f, Deck + 0.6f, -0.42f));
            var arm = Cylinder(parent, "CoolantArm", _metal, new Vector3(0.05f, 0.3f, 0.05f),
                new Vector3(-0.26f, Deck + 0.92f, -0.42f));
            arm.localRotation = Quaternion.Euler(0f, 0f, 90f);
            Box(parent, "Nozzle", _hazard, new Vector3(0.07f, 0.07f, 0.07f),
                new Vector3(-0.02f, Deck + 0.9f, -0.42f));

            Box(parent, "ChipTray", _darkMetal, new Vector3(1.3f, 0.08f, 0.34f),
                new Vector3(0f, Deck + 0.06f, 0.62f));

            // Blanks waiting to be turned, standing in a rack.
            for (int i = 0; i < 3; i++)
            {
                Cylinder(parent, "Blank", _shade(building.Kind, 16), new Vector3(0.11f, 0.22f, 0.11f),
                    new Vector3(0.5f + i * 0.16f, Deck + 0.44f, -0.66f));
            }

            Box(parent, "LatheConsole", dark, new Vector3(0.3f, 0.36f, 0.14f), new Vector3(0.72f, Deck + 0.4f, 0.72f));
            Box(parent, "LatheScreen", _glass, new Vector3(0.17f, 0.1f, 0.03f), new Vector3(0.72f, Deck + 0.5f, 0.8f));
        }

        private void BuildAssembly(Transform parent, BuildingInstance building)
        {
            var body = _shade(building.Kind, 0);
            var dark = _shade(building.Kind, 45);

            Plinth(parent, building);
            Box(parent, "Bench", body, new Vector3(1.74f, 0.3f, 1.74f), new Vector3(0f, Deck + 0.15f, 0f));

            // Four corner jigs plus a raised centre: reads as a place where parts are
            // brought together, which is what the machine does.
            for (int i = 0; i < 4; i++)
            {
                float dx = (i % 2 == 0 ? -1f : 1f) * 0.56f;
                float dz = (i < 2 ? -1f : 1f) * 0.56f;
                Box(parent, "Jig", dark, new Vector3(0.34f, 0.2f, 0.34f), new Vector3(dx, Deck + 0.4f, dz));
            }

            Box(parent, "Cradle", _metal, new Vector3(0.6f, 0.16f, 0.6f), new Vector3(0f, Deck + 0.38f, 0f));

            // Tool board standing along the back edge.
            Box(parent, "Board", dark, new Vector3(1.5f, 0.6f, 0.08f), new Vector3(0f, Deck + 0.6f, -0.85f));
            for (int i = 0; i < 3; i++)
            {
                Box(parent, "Tool", _metal, new Vector3(0.09f, 0.26f, 0.05f),
                    new Vector3(-0.4f + i * 0.4f, Deck + 0.62f, -0.79f));
            }

            Cylinder(parent, "Lamp", _darkMetal, new Vector3(0.07f, 0.4f, 0.07f), new Vector3(0.78f, Deck + 0.7f, 0.78f));
            Box(parent, "LampHead", _glass, new Vector3(0.26f, 0.08f, 0.26f), new Vector3(0.78f, Deck + 1.06f, 0.78f));

            // Parts bins along the front edge, each holding something.
            for (int i = 0; i < 3; i++)
            {
                float x = -0.55f + i * 0.55f;
                Box(parent, "Bin", dark, new Vector3(0.36f, 0.16f, 0.28f), new Vector3(x, Deck + 0.38f, 0.72f));
                Box(parent, "BinContents", _shade(building.Kind, 14 + i * 10),
                    new Vector3(0.28f, 0.1f, 0.2f), new Vector3(x, Deck + 0.46f, 0.72f));
            }

            // A chair part way through assembly on the cradle: four legs and a seat.
            for (int i = 0; i < 4; i++)
            {
                float dx = (i % 2 == 0 ? -1f : 1f) * 0.16f;
                float dz = (i < 2 ? -1f : 1f) * 0.16f;
                Cylinder(parent, "ChairLeg", _shade(building.Kind, 22), new Vector3(0.07f, 0.16f, 0.07f),
                    new Vector3(dx, Deck + 0.5f, dz));
            }
            Box(parent, "ChairSeat", _shade(building.Kind, 18), new Vector3(0.44f, 0.06f, 0.44f),
                new Vector3(0f, Deck + 0.63f, 0f));

            // Tools hanging off the board rather than only painted on it.
            Cylinder(parent, "Clamp", _metal, new Vector3(0.05f, 0.14f, 0.05f),
                new Vector3(0.55f, Deck + 0.5f, -0.79f));
        }

        private void BuildDock(Transform parent, BuildingInstance building, bool inbound)
        {
            var body = _shade(building.Kind, 0);
            var dark = _shade(building.Kind, 40);

            // Loading platform, lower than a machine so trucks read as pulling up to it.
            Box(parent, "Platform", body, new Vector3(1.9f, 0.42f, 1.9f), new Vector3(0f, 0.21f, 0f));
            Box(parent, "Lip", _hazard, new Vector3(1.9f, 0.06f, 0.24f), new Vector3(0f, 0.45f, 0.86f));

            // Back wall with a shutter, and a canopy over the bay.
            Box(parent, "BackWall", dark, new Vector3(1.9f, 1.5f, 0.16f), new Vector3(0f, 0.75f, -0.9f));
            Box(parent, "Shutter", _metal, new Vector3(1.3f, 1.05f, 0.06f), new Vector3(0f, 0.62f, -0.79f));

            for (int i = 0; i < 5; i++)
            {
                Box(parent, "Slat", _darkMetal, new Vector3(1.3f, 0.04f, 0.02f),
                    new Vector3(0f, 0.28f + i * 0.2f, -0.75f));
            }

            var canopy = Box(parent, "Canopy", dark, new Vector3(2.0f, 0.1f, 1.1f), new Vector3(0f, 1.52f, -0.25f));
            canopy.localRotation = Quaternion.Euler(-9f, 0f, 0f);

            Cylinder(parent, "PostL", _darkMetal, new Vector3(0.1f, 0.75f, 0.1f), new Vector3(-0.85f, 0.78f, 0.2f));
            Cylinder(parent, "PostR", _darkMetal, new Vector3(0.1f, 0.75f, 0.1f), new Vector3(0.85f, 0.78f, 0.2f));

            // Direction marker: a wedge pointing in for intake, out for dispatch.
            var arrow = Box(parent, "Marker", _hazard, new Vector3(0.5f, 0.03f, 0.5f), new Vector3(0f, 0.44f, 0.2f));
            arrow.localRotation = Quaternion.Euler(0f, 45f, 0f);
            arrow.localScale = inbound ? new Vector3(0.5f, 0.03f, 0.5f) : new Vector3(0.42f, 0.03f, 0.42f);

            // Steps down off the platform, bollards protecting the corners, and goods
            // waiting on the deck. A loading bay with nothing on it looks decorative.
            for (int i = 0; i < 3; i++)
            {
                Box(parent, "Step", dark, new Vector3(0.5f, 0.08f, 0.16f),
                    new Vector3(-0.62f, 0.36f - i * 0.1f, 1.0f + i * 0.16f));
            }

            for (int side = -1; side <= 1; side += 2)
            {
                Cylinder(parent, "Bollard", _hazard, new Vector3(0.14f, 0.28f, 0.14f),
                    new Vector3(side * 0.78f, 0.6f, 1.05f));
            }

            Box(parent, "Goods", _shade(building.Kind, 16), new Vector3(0.42f, 0.3f, 0.42f),
                new Vector3(0.42f, 0.57f, 0.34f));
            Box(parent, "GoodsTwo", _shade(building.Kind, 26), new Vector3(0.34f, 0.24f, 0.34f),
                new Vector3(0.42f, 0.84f, 0.3f));
        }

        /// <summary>
        /// Shelving with its actual contents on the shelves.
        ///
        /// Drawing real stock is not decoration: where material has piled up is the
        /// first thing a player looks for when a line stalls, and putting it on the
        /// object removes a trip to a menu.
        /// </summary>
        private void BuildRack(Transform parent, BuildingInstance building)
        {
            var frame = _shade(building.Kind, 30);

            for (int i = 0; i < 4; i++)
            {
                float dx = (i % 2 == 0 ? -1f : 1f) * 0.4f;
                float dz = (i < 2 ? -1f : 1f) * 0.4f;
                Box(parent, "Upright", frame, new Vector3(0.1f, 0.95f, 0.1f), new Vector3(dx, 0.475f, dz));
            }

            for (int level = 0; level < 3; level++)
            {
                Box(parent, "Shelf", _shade(building.Kind, 10),
                    new Vector3(0.92f, 0.06f, 0.92f), new Vector3(0f, 0.2f + level * 0.34f, 0f));
            }

            var container = building.Def.OutputSlots > 0 ? building.Output : building.Input;
            int drawn = 0;

            for (int slot = 0; slot < container.SlotCount && drawn < 6; slot++)
            {
                var stack = container[slot];
                if (stack.IsEmpty) continue;

                int level = drawn / 2;
                float dx = (drawn % 2 == 0) ? -0.2f : 0.2f;

                var crate = Box(parent, "Crate", null,
                    new Vector3(0.32f, 0.24f, 0.32f),
                    new Vector3(dx, 0.35f + level * 0.34f, 0f));
                crate.GetComponent<Renderer>().sharedMaterial = CrateMaterial(stack.Item);
                drawn++;
            }
        }

        private Func<ItemId, Material> _crateMaterialLookup;

        public void SetCrateMaterialLookup(Func<ItemId, Material> lookup) => _crateMaterialLookup = lookup;

        private Material CrateMaterial(ItemId item) => _crateMaterialLookup?.Invoke(item);

        private void BuildBreakRoom(Transform parent, BuildingInstance building)
        {
            var body = _shade(building.Kind, 0);
            var dark = _shade(building.Kind, 40);

            Box(parent, "Floor", dark, new Vector3(1.94f, 0.16f, 1.94f), new Vector3(0f, 0.08f, 0f));

            // Three walls and an opening, so the interior stays visible from this camera.
            Box(parent, "WallBack", body, new Vector3(1.94f, 0.95f, 0.14f), new Vector3(0f, 0.55f, -0.9f));
            Box(parent, "WallLeft", body, new Vector3(0.14f, 0.95f, 1.94f), new Vector3(-0.9f, 0.55f, 0f));
            Box(parent, "WallRight", body, new Vector3(0.14f, 0.95f, 1.1f), new Vector3(0.9f, 0.55f, -0.42f));

            Box(parent, "WindowBack", _glass, new Vector3(0.8f, 0.4f, 0.04f), new Vector3(0f, 0.68f, -0.84f));
            Box(parent, "WindowLeft", _glass, new Vector3(0.04f, 0.4f, 0.7f), new Vector3(-0.84f, 0.68f, 0.2f));

            // Pitched roof, tilted so it catches the key light differently to every flat
            // top around it.
            var roofA = Box(parent, "RoofA", _shade(building.Kind, 22), new Vector3(2.1f, 0.1f, 1.2f),
                new Vector3(0f, 1.16f, -0.5f));
            roofA.localRotation = Quaternion.Euler(-22f, 0f, 0f);

            var roofB = Box(parent, "RoofB", _shade(building.Kind, 34), new Vector3(2.1f, 0.1f, 1.2f),
                new Vector3(0f, 1.16f, 0.5f));
            roofB.localRotation = Quaternion.Euler(22f, 0f, 0f);

            // Furniture, visible through the open side.
            Cylinder(parent, "TableLeg", dark, new Vector3(0.1f, 0.2f, 0.1f), new Vector3(0.25f, 0.28f, 0.25f));
            Cylinder(parent, "TableTop", _shade(building.Kind, 12), new Vector3(0.62f, 0.04f, 0.62f),
                new Vector3(0.25f, 0.5f, 0.25f));
            Box(parent, "Stool", dark, new Vector3(0.24f, 0.26f, 0.24f), new Vector3(-0.3f, 0.29f, 0.4f));
            Box(parent, "Locker", dark, new Vector3(0.34f, 0.7f, 0.28f), new Vector3(-0.55f, 0.51f, -0.6f));
        }

        private void BuildWall(Transform parent, BuildingInstance building)
        {
            Box(parent, "Panel", _shade(building.Kind, 20), new Vector3(1.0f, 1.1f, 0.42f), new Vector3(0f, 0.55f, 0f));
            Box(parent, "Cap", _shade(building.Kind, 44), new Vector3(1.02f, 0.1f, 0.5f), new Vector3(0f, 1.13f, 0f));
            Box(parent, "Base", _shade(building.Kind, 50), new Vector3(1.04f, 0.14f, 0.52f), new Vector3(0f, 0.07f, 0f));

            // Deterministic variation from the tile coordinate: some bays get a window,
            // some a downpipe, the rest a pilaster. A perfectly uniform wall is the most
            // obvious tell that a scene was generated.
            int variant = Mathf.Abs(building.Origin.X * 7 + building.Origin.Y * 13) % 3;

            switch (variant)
            {
                case 0:
                    Box(parent, "Window", _glass, new Vector3(0.52f, 0.34f, 0.06f), new Vector3(0f, 0.78f, -0.22f));
                    Box(parent, "WindowFrame", _shade(building.Kind, 52),
                        new Vector3(0.6f, 0.42f, 0.04f), new Vector3(0f, 0.78f, -0.2f));
                    break;

                case 1:
                    Cylinder(parent, "Downpipe", _shade(building.Kind, 46), new Vector3(0.12f, 0.58f, 0.12f),
                        new Vector3(0.38f, 0.58f, -0.24f));
                    Box(parent, "PipeBracket", _shade(building.Kind, 56),
                        new Vector3(0.18f, 0.06f, 0.1f), new Vector3(0.38f, 0.86f, -0.2f));
                    break;

                default:
                    Box(parent, "Pilaster", _shade(building.Kind, 34),
                        new Vector3(0.22f, 1.16f, 0.5f), new Vector3(-0.32f, 0.58f, 0f));
                    break;
            }
        }

        private void BuildGenericBlock(Transform parent, BuildingInstance building)
        {
            Plinth(parent, building);
            Box(parent, "Body", _shade(building.Kind, 0),
                new Vector3(building.Width - 0.3f, 0.6f, building.Height - 0.3f),
                new Vector3(0f, Deck + 0.3f, 0f));
        }

        private void Plinth(Transform parent, BuildingInstance building)
        {
            Box(parent, "Plinth", _shade(building.Kind, 55),
                new Vector3(building.Width - 0.08f, Deck, building.Height - 0.08f),
                new Vector3(0f, Deck * 0.5f, 0f));
        }

        // ------------------------------------------------------------- primitives

        private static Transform Box(Transform parent, string name, Material material, Vector3 scale, Vector3 position)
            => MeshObjects.Box(name, parent, material, scale, position);

        private static Transform Cylinder(Transform parent, string name, Material material, Vector3 scale, Vector3 position)
            => MeshObjects.Cylinder(name, parent, material, scale, position);
    }
}
