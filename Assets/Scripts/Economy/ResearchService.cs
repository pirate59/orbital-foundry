using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace OrbitalFoundry
{
    public enum ResearchVisibility { Hidden = 0, Hinted = 1, Open = 2, Owned = 3 }

    /// <summary>
    /// The research web: what a faction has bought, what it may buy next, what is still undiscovered, and the bonuses
    /// and unlocks its levels grant. Each faction researches separately and starts every match from nothing.
    /// </summary>
    public static class ResearchService
    {
        public const string UnlockRecipe = "unlock_recipe";
        public const string UnlockBuilding = "unlock_building";
        public const string MachineSpeed = "machine_speed_percent";
        public const string ResearchIon = "research_ion_percent";
        public const string ExtractionSpeed = "extraction_speed_percent";
        public const string IonYield = "ion_yield_percent";
        public const string SalePrice = "sale_price_percent";
        public const string SolarOutputPercent = "solar_output_percent";
        public const string DroneRange = "drone_range";
        public const string DroneSpeed = "drone_speed_percent";
        public const string DroneCapacity = "drone_capacity";

        public static readonly string[] EffectKinds = { UnlockRecipe, UnlockBuilding, MachineSpeed, ResearchIon, ExtractionSpeed, IonYield, SalePrice, SolarOutputPercent, DroneRange, DroneSpeed, DroneCapacity };

        /// <summary>Recipes and buildings that some research level unlocks; anything not listed is available from the start.</summary>
        sealed class Gates
        {
            public readonly Dictionary<string, ResearchNodeDefinition> Recipes = new Dictionary<string, ResearchNodeDefinition>();
            public readonly Dictionary<string, ResearchNodeDefinition> Buildings = new Dictionary<string, ResearchNodeDefinition>();
        }

        static readonly ConditionalWeakTable<ContentCatalog, Gates> GateCache = new ConditionalWeakTable<ContentCatalog, Gates>();

        static Gates GatesFor(ContentCatalog c)
        {
            return GateCache.GetValue(c, cat =>
            {
                var g = new Gates();
                foreach (var node in cat.research)
                    foreach (var level in node.levels)
                        foreach (var e in level.effects)
                        {
                            if (e.kind == UnlockRecipe && !g.Recipes.ContainsKey(e.targetId)) g.Recipes[e.targetId] = node;
                            if (e.kind == UnlockBuilding && !g.Buildings.ContainsKey(e.targetId)) g.Buildings[e.targetId] = node;
                        }
                return g;
            });
        }

        // ---------------------------------------------------------------- levels and discovery

        public static int Level(FactionState f, string nodeId)
        {
            if (f == null) return 0;
            foreach (var p in f.research) if (p.nodeId == nodeId) return p.level;
            return 0;
        }

        /// <summary>Every prerequisite has reached its minimum level, so the first level may be bought.</summary>
        public static bool IsOpen(FactionState f, ResearchNodeDefinition node)
        {
            foreach (var p in node.prerequisites)
                if (Level(f, p.nodeId) < p.minimumLevel) return false;
            return true;
        }

        /// <summary>
        /// Owned and open nodes show their names; a node whose parent is owned or open is an undiscovered outline;
        /// anything further out is not drawn at all.
        /// </summary>
        public static ResearchVisibility Visibility(ContentCatalog c, FactionState f, ResearchNodeDefinition node)
        {
            if (Level(f, node.id) > 0) return ResearchVisibility.Owned;
            if (IsOpen(f, node)) return ResearchVisibility.Open;
            foreach (var p in node.prerequisites)
            {
                var parent = CatalogRepository.GetResearch(c, p.nodeId);
                if (parent != null && (Level(f, parent.id) > 0 || IsOpen(f, parent))) return ResearchVisibility.Hinted;
            }
            return ResearchVisibility.Hidden;
        }

        /// <summary>The next level to buy, or null when the node is maxed.</summary>
        public static ResearchLevelDefinition NextLevel(FactionState f, ResearchNodeDefinition node)
        {
            int level = Level(f, node.id);
            return level < node.levels.Count ? node.levels[level] : null;
        }

        /// <summary>Why the next level cannot be bought right now, or null when it can.</summary>
        public static CommandResult CheckPurchase(ContentCatalog c, FactionState f, ResearchNodeDefinition node)
        {
            if (f == null) return WorldLookup.Fail("NEUTRAL_ACTOR", "Only a faction can research.");
            if (f.suppressed) return WorldLookup.Fail("SUPPRESSED", "This faction can no longer research.");
            var next = NextLevel(f, node);
            if (next == null) return WorldLookup.Fail("MAXED", node.displayName + " is fully researched.");
            if (Level(f, node.id) == 0 && !IsOpen(f, node)) return WorldLookup.Fail("LOCKED", "Research its prerequisites first.");
            if (f.currency < next.creditCost) return WorldLookup.Fail("NO_MONEY", "Not enough credits (" + next.creditCost + " needed).");
            int ion = IonPrice(c, f, next);
            if (f.ion < ion) return WorldLookup.Fail("NO_ION", "Not enough Ion (" + ion + " needed).");
            return null;
        }

        public static CommandResult Purchase(WorldState w, ContentCatalog c, GameCommand cmd)
        {
            var f = WorldLookup.GetFaction(w, cmd.actor);
            if (f == null || cmd.actor == FactionId.Neutral) return WorldLookup.Fail("NEUTRAL_ACTOR", "Only a faction can research.");
            var node = CatalogRepository.GetResearch(c, cmd.contentId);
            if (node == null) return WorldLookup.Fail("UNKNOWN_RESEARCH", "That research does not exist.");
            var refusal = CheckPurchase(c, f, node);
            if (refusal != null) return refusal;
            var next = NextLevel(f, node);
            f.currency -= next.creditCost;
            f.ion -= IonPrice(c, f, next);
            int level = Level(f, node.id) + 1;
            var progress = f.research.Find(p => p.nodeId == node.id);
            if (progress == null) f.research.Add(new ResearchProgress { nodeId = node.id, level = level });
            else progress.level = level;
            return WorldLookup.Ok("Researched " + node.displayName + (node.levels.Count > 1 ? " " + level : "") + ".");
        }

        // ---------------------------------------------------------------- unlocks

        public static ResearchNodeDefinition RecipeGate(ContentCatalog c, string recipeId)
        {
            ResearchNodeDefinition node;
            return GatesFor(c).Recipes.TryGetValue(recipeId, out node) ? node : null;
        }

        public static ResearchNodeDefinition BuildingGate(ContentCatalog c, string buildingId)
        {
            ResearchNodeDefinition node;
            return GatesFor(c).Buildings.TryGetValue(buildingId, out node) ? node : null;
        }

        public static bool IsRecipeUnlocked(ContentCatalog c, FactionState f, RecipeDefinition r)
        {
            if (r == null) return false;
            var gate = RecipeGate(c, r.id);
            return gate == null || HasEffect(f, gate, UnlockRecipe, r.id);
        }

        public static bool IsBuildingUnlocked(ContentCatalog c, FactionState f, string buildingId)
        {
            var gate = BuildingGate(c, buildingId);
            return gate == null || HasEffect(f, gate, UnlockBuilding, buildingId);
        }

        /// <summary>Raw resources are always available; a made item needs at least one unlocked recipe that makes it.</summary>
        public static bool IsItemUnlocked(ContentCatalog c, FactionState f, string itemId)
        {
            bool made = false;
            foreach (var r in c.recipes)
            {
                if (r.outputItemId != itemId) continue;
                made = true;
                if (IsRecipeUnlocked(c, f, r)) return true;
            }
            return !made;
        }

        static bool HasEffect(FactionState f, ResearchNodeDefinition node, string kind, string targetId)
        {
            int owned = Level(f, node.id);
            for (int i = 0; i < owned && i < node.levels.Count; i++)
                foreach (var e in node.levels[i].effects)
                    if (e.kind == kind && e.targetId == targetId) return true;
            return false;
        }

        // ---------------------------------------------------------------- bonuses

        /// <summary>Sum of a percentage bonus across owned levels. Effects with an empty target apply to everything.</summary>
        public static int Bonus(ContentCatalog c, FactionState f, string kind, string targetId = "")
        {
            if (f == null || f.research.Count == 0) return 0;
            int total = 0;
            foreach (var p in f.research)
            {
                var node = CatalogRepository.GetResearch(c, p.nodeId);
                if (node == null) continue;
                for (int i = 0; i < p.level && i < node.levels.Count; i++)
                    foreach (var e in node.levels[i].effects)
                        if (e.kind == kind && (e.targetId == "" || e.targetId == targetId)) total += e.amount;
            }
            return total;
        }

        /// <summary>Ticks per cycle after machine-speed research: +100% speed halves the time.</summary>
        public static int CraftTicks(ContentCatalog c, FactionState f, string buildingId, RecipeDefinition recipe)
        {
            int speed = Bonus(c, f, MachineSpeed, buildingId);
            return Math.Max(1, (int)Math.Ceiling(recipe.craftTicks * 100.0 / (100 + Math.Max(0, speed))));
        }

        /// <summary>A research level's Ion price after research-efficiency research, never more than 90% off. Ion is spent only here.</summary>
        public static int IonPrice(ContentCatalog c, FactionState f, ResearchLevelDefinition level)
        {
            int saved = Math.Min(90, Math.Max(0, Bonus(c, f, ResearchIon)));
            return (int)Math.Ceiling(level.ionCost * (100 - saved) / 100.0);
        }

        /// <summary>Ticks between extractions after extraction-speed research.</summary>
        public static int ExtractionTicks(ContentCatalog c, FactionState f, string itemId, int rateTicks)
        {
            int speed = Bonus(c, f, ExtractionSpeed, itemId);
            return Math.Max(1, (int)Math.Ceiling(rateTicks * 100.0 / (100 + Math.Max(0, speed))));
        }

        /// <summary>Ion produced by one extraction or generator cycle after yield research.</summary>
        public static int IonAmount(ContentCatalog c, FactionState f, int baseAmount)
        {
            return baseAmount * (100 + Math.Max(0, Bonus(c, f, IonYield))) / 100;
        }

        /// <summary>Power (MW) from a solar source after Photovoltaics-style research.</summary>
        public static int SolarOutput(ContentCatalog c, FactionState f, int baseMw)
        {
            return baseMw * (100 + Math.Max(0, Bonus(c, f, SolarOutputPercent))) / 100;
        }

        public static int SalePercent(ContentCatalog c, FactionState f)
        {
            return 100 + Bonus(c, f, SalePrice);
        }
    }
}
