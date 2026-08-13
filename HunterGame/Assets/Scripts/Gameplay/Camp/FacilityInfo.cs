using System;
using Hunter.Gameplay.Items;

namespace Hunter.Gameplay.Camp
{
    /// Names, blurbs, and the per-level numbers behind every facility.
    ///
    /// The camp screen has to promise an exact number before the player spends, and the raid
    /// has to deliver that same number. Both read these constants, so a tuning change cannot
    /// leave the shop advertising a bonus the run does not grant.
    public static class FacilityInfo
    {
        public const float BaseCarryWeight = 32f;
        public const int BaseCarrySlots = 18;
        public const float BaseVitality = 100f;

        public const float VaultWeightPerLevel = 4.5f;
        public const int VaultSlotsPerLevel = 2;
        public const float ShrineVitalityPerLevel = 14f;
        public const float ForgeDamagePerLevel = 0.08f;

        /// Damage the forge delivers at a level: the tier's base swing plus the honing that
        /// every level buys. Applied to the live profile by RunController.
        public static float ForgeDamage(int forgeLevel)
        {
            int level = Math.Max(0, forgeLevel);
            return Combat.MeleeProfile.ForForgeLevel(level).BaseDamage * ForgeDamageMultiplier(level);
        }

        public static float ForgeDamageMultiplier(int forgeLevel) =>
            1f + Math.Max(0, forgeLevel) * ForgeDamagePerLevel;

        public static string DisplayName(FacilityKind kind) => kind switch
        {
            FacilityKind.Vault => "VAULT",
            FacilityKind.Forge => "FORGE",
            FacilityKind.Alchemy => "ALCHEMY",
            FacilityKind.Cartographer => "CARTOGRAPHER",
            FacilityKind.Shrine => "SHRINE",
            _ => kind.ToString().ToUpperInvariant(),
        };

        public static string Tagline(FacilityKind kind) => kind switch
        {
            FacilityKind.Vault => "Carry more out",
            FacilityKind.Forge => "Better blade on insertion",
            FacilityKind.Alchemy => "Start with draughts",
            FacilityKind.Cartographer => "Know more ways home",
            FacilityKind.Shrine => "Survive more punishment",
            _ => string.Empty,
        };

        /// What the next level changes, stated as the exact before and after values the run
        /// will use. Vague copy here is what makes an upgrade feel like it did nothing.
        public static string NextEffect(FacilityKind kind, int level)
        {
            switch (kind)
            {
                case FacilityKind.Vault:
                    return $"{Weight(level):0.#} \u2192 {Weight(level + 1):0.#} kg" +
                           $"   \u00b7   {Slots(level)} \u2192 {Slots(level + 1)} slots";

                case FacilityKind.Shrine:
                    return $"{Vitality(level):0} \u2192 {Vitality(level + 1):0} vitality";

                case FacilityKind.Forge:
                {
                    // Tiers only change at three of the six levels, so without the damage
                    // line the other purchases would advertise no change at all.
                    string from = WeaponName(level);
                    string to = WeaponName(level + 1);
                    string damage = $"{ForgeDamage(level):0} \u2192 {ForgeDamage(level + 1):0} damage";
                    return from == to ? $"{to}   \u00b7   {damage}" : $"{from} \u2192 {to}   \u00b7   {damage}";
                }

                case FacilityKind.Alchemy:
                    return $"{level} \u2192 {level + 1} draughts on insertion";

                case FacilityKind.Cartographer:
                    return $"{1 + level} \u2192 {2 + level} extraction points known";

                default:
                    return string.Empty;
            }
        }

        /// The effect the camp is granting right now, for the summary line.
        public static string CurrentEffect(FacilityKind kind, int level)
        {
            switch (kind)
            {
                case FacilityKind.Vault:
                    return $"{Weight(level):0.#} kg  \u00b7  {Slots(level)} slots";
                case FacilityKind.Shrine:
                    return $"{Vitality(level):0} vitality";
                case FacilityKind.Forge:
                    return $"{WeaponName(level)}  \u00b7  {ForgeDamage(level):0} dmg";
                case FacilityKind.Alchemy:
                    return level == 0 ? "none" : $"{level} draughts";
                case FacilityKind.Cartographer:
                    return $"{1 + level} known";
                default:
                    return string.Empty;
            }
        }

        public static float Weight(int vaultLevel) =>
            BaseCarryWeight + Math.Max(0, vaultLevel) * VaultWeightPerLevel;

        public static int Slots(int vaultLevel) =>
            BaseCarrySlots + Math.Max(0, vaultLevel) * VaultSlotsPerLevel;

        public static float Vitality(int shrineLevel) =>
            BaseVitality + Math.Max(0, shrineLevel) * ShrineVitalityPerLevel;

        /// The catalog carries Chinese display names, which the built-in UI font has no
        /// glyphs for. Until there is a font pipeline the camp screen uses its own English
        /// names so the rows stay readable rather than rendering as blanks.
        public static string WeaponName(int forgeLevel) => forgeLevel switch
        {
            <= 0 => "Rusted Blade",
            1 or 2 => "Hunter's Falchion",
            _ => "Warden Halberd",
        };

        /// The forge tier table. Shared with CampState so the shop and the raid agree.
        public static ItemDefinition WeaponAt(int forgeLevel) => forgeLevel switch
        {
            <= 0 => ItemCatalog.RustedBlade,
            1 or 2 => ItemCatalog.HuntersFalchion,
            _ => ItemCatalog.WardenHalberd,
        };
    }
}
