using System.Collections.Generic;

namespace OrbitalFoundry
{
    /// <summary>
    /// The AI's picture of the match, rebuilt by the Situation Assessment skill: both sides' economies and fleets,
    /// the threat to each AI cell, and how close the player is to a trade win. Runtime only (never saved).
    /// What it holds about the player depends on what the profile lets the AI see.
    /// </summary>
    public sealed class AiWorldModel
    {
        public int AssessedTick = -1;

        // Economy, per side.
        public int AiCredits, AiIon, AiPowerSpare, AiCells, AiMachines;
        public int PlayerCredits, PlayerCells, PlayerMachines;
        public float AiSalesPerMinute, PlayerSalesPerMinute;

        // Military, per side: a rough fighting strength (see Strength).
        public int AiStrength, PlayerStrength;
        public int AiCraft, PlayerCraft;

        /// <summary>Player fighting strength near each AI cell (nearer counts more), and the AI's defence there.</summary>
        public readonly Dictionary<int, int> ThreatTo = new Dictionary<int, int>();
        public readonly Dictionary<int, int> DefenceOf = new Dictionary<int, int>();
        /// <summary>Fort defence in each AI cell (counted by the slower economy assessment; Forts change rarely).</summary>
        public readonly Dictionary<int, int> FortDefence = new Dictionary<int, int>();
        /// <summary>The AI cell under the greatest threat beyond its defence, or -1.</summary>
        public int MostThreatenedCell = -1;
        public int WorstThreatGap;

        /// <summary>The player's trade rate as a share of their target (0 when the AI cannot see it), and whether the hold is running.</summary>
        public float PlayerWinProgress;
        public bool PlayerHolding;

        /// <summary>AI economy over the player's (1 = level), and AI military over the player's.</summary>
        public float EconomyBalance = 1f, MilitaryBalance = 1f;

        /// <summary>A rough fighting strength: HP times what it deals and shrugs off (attack, return fire, armour).</summary>
        public static int Strength(ContentCatalog c, CraftState k)
        {
            var d = CatalogRepository.GetCraft(c, k.typeId);
            if (d == null || !WorldLookup.IsCraftLive(k)) return 0;
            int attack = System.Math.Max(d.attackDamage, d.rangedAttackDamage);
            return k.currentHealth * (attack + d.defenceDamage + 2 * d.armour);
        }

        /// <summary>A Fort's fighting strength on the same scale (it only returns fire).</summary>
        public static int FortStrength(ContentCatalog c, BuildingState fort)
        {
            var d = CatalogRepository.GetBuilding(c, fort.typeId);
            if (d == null || d.combatHealth <= 0) return 0;
            return System.Math.Max(0, d.combatHealth - fort.damage) * (d.combatReturnFire + 2 * d.combatArmour);
        }
    }
}
