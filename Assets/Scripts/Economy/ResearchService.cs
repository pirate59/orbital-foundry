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
        /// <summary>Turns taken off a craft's attack cooldown (targetId the craft type), down to one turn.</summary>
        public const string AttackCooldown = "attack_cooldown";
        public const string DroneCapacity = "drone_capacity";
        /// <summary>Extra units an extractor mines per cycle.</summary>
        public const string ExtractionYield = "extraction_yield";
        /// <summary>Extra raw units per belt slot (Bulk Crates).</summary>
        public const string RawStackSize = "raw_stack_size";
        /// <summary>Extra exit belts an extractor may feed at once (Multi-Chute Extractors).</summary>
        public const string ExtractorChutes = "extractor_chutes";

        public static readonly string[] EffectKinds = { UnlockRecipe, UnlockBuilding, MachineSpeed, ResearchIon, ExtractionSpeed, IonYield, SalePrice, SolarOutputPercent, DroneRange, DroneSpeed, DroneCapacity, ExtractionYield, RawStackSize, ExtractorChutes, AttackCooldown };

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
            // Queued levels count: what a queued node leads to can be seen, and queued behind it.
            if (Level(f, node.id) > 0) return ResearchVisibility.Owned;
            if (IsOpenPlanned(f, node)) return ResearchVisibility.Open;
            foreach (var p in node.prerequisites)
            {
                var parent = CatalogRepository.GetResearch(c, p.nodeId);
                if (parent != null && (PlannedLevel(f, parent.id) > 0 || IsOpenPlanned(f, parent))) return ResearchVisibility.Hinted;
            }
            return ResearchVisibility.Hidden;
        }

        /// <summary>The next level to buy, or null when the node is maxed.</summary>
        public static ResearchLevelDefinition NextLevel(FactionState f, ResearchNodeDefinition node)
        {
            int level = Level(f, node.id);
            return level < node.levels.Count ? node.levels[level] : null;
        }

        // ---------------------------------------------------------------- the queue

        /// <summary>How many research levels a faction may have queued at once (the AI works on one at a time).</summary>
        /// <summary>Research levels either side may have queued (the AI chooses how many it uses: AiProfile.ResearchQueue).</summary>
        public const int MaxQueue = 5;

        /// <summary>Owned levels plus those waiting in the queue: what the faction will have once it all finishes.</summary>
        public static int PlannedLevel(FactionState f, string nodeId)
        {
            int level = Level(f, nodeId);
            if (f != null) foreach (var j in f.researchQueue) if (j.nodeId == nodeId) level++;
            return level;
        }

        public static int QueuedLevels(FactionState f, string nodeId)
        {
            return PlannedLevel(f, nodeId) - Level(f, nodeId);
        }

        /// <summary>As <see cref="IsOpen"/>, counting queued levels as done: a node may be queued behind its prerequisites.</summary>
        public static bool IsOpenPlanned(FactionState f, ResearchNodeDefinition node)
        {
            foreach (var p in node.prerequisites)
                if (PlannedLevel(f, p.nodeId) < p.minimumLevel) return false;
            return true;
        }

        /// <summary>The next level to queue (after any already queued), or null when the rest are owned or queued.</summary>
        public static ResearchLevelDefinition NextPlannedLevel(FactionState f, ResearchNodeDefinition node)
        {
            int level = PlannedLevel(f, node.id);
            return level < node.levels.Count ? node.levels[level] : null;
        }

        /// <summary>The job being worked on, or null when the queue is empty.</summary>
        public static ResearchJob Current(FactionState f)
        {
            return f != null && f.researchQueue.Count > 0 ? f.researchQueue[0] : null;
        }

        /// <summary>A node's tier: 0 with no prerequisites, else one more than its deepest prerequisite.</summary>
        public static int Tier(ContentCatalog c, ResearchNodeDefinition node)
        {
            return Tier(c, node, 0);
        }

        static int Tier(ContentCatalog c, ResearchNodeDefinition node, int depth)
        {
            if (node == null || depth > 32) return 0;
            int tier = 0;
            foreach (var p in node.prerequisites)
                tier = Math.Max(tier, 1 + Tier(c, CatalogRepository.GetResearch(c, p.nodeId), depth + 1));
            return tier;
        }

        /// <summary>Minutes a tier-0 first level takes; each tier down the web, and each further level, adds one.</summary>
        public const int BaseResearchMinutes = 2;

        /// <summary>How long a level (1-based) of a node takes to research, in minutes.</summary>
        public static int ResearchMinutes(ContentCatalog c, ResearchNodeDefinition node, int level)
        {
            return BaseResearchMinutes + Tier(c, node) + Math.Max(0, level - 1);
        }

        public static int ResearchTicks(ContentCatalog c, ResearchNodeDefinition node, int level)
        {
            return ResearchMinutes(c, node, level) * 60000 / Math.Max(1, c.rules.tickMilliseconds);
        }

        /// <summary>"3:00" for a tick count.</summary>
        public static string Clock(ContentCatalog c, int ticks)
        {
            int seconds = (int)Math.Ceiling(Math.Max(0, ticks) * (double)c.rules.tickMilliseconds / 1000.0);
            return (seconds / 60) + ":" + (seconds % 60).ToString("00");
        }

        /// <summary>Ticks until the whole queue up to and including <paramref name="index"/> is done.</summary>
        public static int TicksUntilDone(FactionState f, int index)
        {
            int total = 0;
            for (int i = 0; i <= index && i < f.researchQueue.Count; i++) total += f.researchQueue[i].ticksTotal - f.researchQueue[i].ticksDone;
            return total;
        }

        /// <summary>
        /// Works on each faction's first queued level; a finished level takes effect at once and the next one starts.
        /// Runs with factory time, so research pauses during battles like everything else.
        /// </summary>
        public static void Tick(WorldState w, ContentCatalog c)
        {
            foreach (var f in w.factions)
            {
                if (f.suppressed || f.researchQueue.Count == 0) continue;
                var job = f.researchQueue[0];
                job.ticksDone++;
                if (job.ticksDone < job.ticksTotal) continue;
                f.researchQueue.RemoveAt(0);
                var node = CatalogRepository.GetResearch(c, job.nodeId);
                if (node == null) continue;
                var progress = f.research.Find(p => p.nodeId == node.id);
                if (progress == null) f.research.Add(new ResearchProgress { nodeId = node.id, level = job.level });
                else progress.level = Math.Max(progress.level, job.level);
                var next = Current(f);
                var nextNode = next != null ? CatalogRepository.GetResearch(c, next.nodeId) : null;
                WorldLookup.AddAlert(w, AlertKind.Info, "Research complete: " + node.displayName + (node.levels.Count > 1 ? " " + job.level : "") + "."
                    + (nextNode != null ? " Now researching " + nextNode.displayName + "." : ""), -1, f.id);
            }
        }

        /// <summary>
        /// Takes the last queued level of a node out of the queue, with every later one that no longer has its
        /// prerequisites, and refunds everything paid for them (goods go back to Storage).
        /// </summary>
        public static CommandResult Cancel(WorldState w, ContentCatalog c, GameCommand cmd)
        {
            var f = WorldLookup.GetFaction(w, cmd.actor);
            if (f == null || cmd.actor == FactionId.Neutral) return WorldLookup.Fail("NEUTRAL_ACTOR", "Only a faction can research.");
            int at = f.researchQueue.FindLastIndex(j => j.nodeId == cmd.contentId);
            if (at < 0) return WorldLookup.Fail("NOT_QUEUED", "That research is not queued.");
            var removed = new List<ResearchJob> { f.researchQueue[at] };
            f.researchQueue.RemoveAt(at);
            // Walk the rest in order: a level whose prerequisites (or own earlier level) are gone goes too.
            var levels = new Dictionary<string, int>();
            foreach (var p in f.research) levels[p.nodeId] = p.level;
            for (int i = 0; i < f.researchQueue.Count; i++)
            {
                var job = f.researchQueue[i];
                var node = CatalogRepository.GetResearch(c, job.nodeId);
                int have; levels.TryGetValue(job.nodeId, out have);
                bool open = node != null && job.level == have + 1;
                if (open && have == 0)
                    foreach (var p in node.prerequisites)
                    {
                        int pl; levels.TryGetValue(p.nodeId, out pl);
                        if (pl < p.minimumLevel) { open = false; break; }
                    }
                if (!open) { removed.Add(job); f.researchQueue.RemoveAt(i); i--; continue; }
                levels[job.nodeId] = job.level;
            }
            BuildingState store = null;
            foreach (var b in w.buildings)
            {
                var def = CatalogRepository.GetBuilding(c, b.typeId);
                if (b.owner == f.id && def != null && def.kind == BuildingKind.Storage && WorldLookup.IsBuildingActive(w, b)) { store = b; break; }
            }
            int credits = 0, ion = 0;
            foreach (var job in removed)
            {
                credits += job.creditsPaid;
                ion += job.ionPaid;
                if (store != null) foreach (var g in job.goodsPaid) WorldLookup.AddItem(store.inventory, g.itemId, g.quantity);
            }
            f.currency += credits;
            f.ion += ion;
            var first = CatalogRepository.GetResearch(c, removed[0].nodeId);
            return WorldLookup.Ok("Cancelled " + (first != null ? first.displayName : removed[0].nodeId)
                + (removed.Count > 1 ? " and " + (removed.Count - 1) + " research queued after it" : "")
                + " (+" + credits + " credits, +" + ion + " Ion refunded).");
        }

        /// <summary>Why the next level cannot be queued right now, or null when it can.</summary>
        public static CommandResult CheckPurchase(ContentCatalog c, FactionState f, ResearchNodeDefinition node)
        {
            if (f == null) return WorldLookup.Fail("NEUTRAL_ACTOR", "Only a faction can research.");
            if (f.suppressed) return WorldLookup.Fail("SUPPRESSED", "This faction can no longer research.");
            var next = NextPlannedLevel(f, node);
            if (next == null)
                return WorldLookup.Fail("MAXED", QueuedLevels(f, node.id) > 0 ? node.displayName + " is already queued." : node.displayName + " is fully researched.");
            if (PlannedLevel(f, node.id) == 0 && !IsOpenPlanned(f, node)) return WorldLookup.Fail("LOCKED", "Research (or queue) its prerequisites first.");
            if (f.researchQueue.Count >= MaxQueue) return WorldLookup.Fail("QUEUE_FULL", "The research queue is full (" + MaxQueue + ").");
            if (f.currency < next.creditCost) return WorldLookup.Fail("NO_MONEY", "Not enough credits (" + next.creditCost + " needed).");
            int ion = IonPrice(c, f, next);
            if (f.ion < ion) return WorldLookup.Fail("NO_ION", "Not enough Ion (" + ion + " needed).");
            return null;
        }

        /// <summary>
        /// As <see cref="CheckPurchase(ContentCatalog, FactionState, ResearchNodeDefinition)"/>, and the level's goods must
        /// be waiting in the faction's Storage buildings (the AI's as well as the player's).
        /// </summary>
        public static CommandResult CheckPurchase(WorldState w, ContentCatalog c, FactionState f, ResearchNodeDefinition node)
        {
            var refusal = CheckPurchase(c, f, node);
            if (refusal != null) return refusal;
            var next = NextPlannedLevel(f, node);
            if (PaysGoods(f, next) && !EconomyService.HasStoredGoods(w, c, f.id, next.goods))
                return WorldLookup.Fail("NO_GOODS", "Needs " + EconomyService.IngredientText(c, next.goods) + " in your Storage buildings.");
            return null;
        }

        /// <summary>A craft's attack cooldown for this faction after research: 0 when it has none, never below one turn otherwise.</summary>
        public static int CooldownTurns(ContentCatalog c, FactionState f, CraftDefinition craft)
        {
            if (craft == null || craft.cooldownTurns <= 0) return 0;
            int cut = 0;
            if (f != null)
                foreach (var node in c.research)
                {
                    int owned = Level(f, node.id);
                    for (int i = 0; i < owned && i < node.levels.Count; i++)
                        foreach (var e in node.levels[i].effects)
                            if (e.kind == AttackCooldown && (string.IsNullOrEmpty(e.targetId) || e.targetId == craft.id)) cut += e.amount;
                }
            return Math.Max(1, craft.cooldownTurns - cut);
        }

        /// <summary>Whether the level costs goods from Storage (the same for the player and the AI).</summary>
        public static bool PaysGoods(FactionState f, ResearchLevelDefinition level)
        {
            return f != null && level != null && level.goods.Count > 0;
        }

        public static CommandResult Purchase(WorldState w, ContentCatalog c, GameCommand cmd)
        {
            var f = WorldLookup.GetFaction(w, cmd.actor);
            if (f == null || cmd.actor == FactionId.Neutral) return WorldLookup.Fail("NEUTRAL_ACTOR", "Only a faction can research.");
            var node = CatalogRepository.GetResearch(c, cmd.contentId);
            if (node == null) return WorldLookup.Fail("UNKNOWN_RESEARCH", "That research does not exist.");
            var refusal = CheckPurchase(w, c, f, node);
            if (refusal != null) return refusal;
            var next = NextPlannedLevel(f, node);
            int level = PlannedLevel(f, node.id) + 1;
            int ion = IonPrice(c, f, next);
            var job = new ResearchJob
            {
                nodeId = node.id, level = level, ticksTotal = ResearchTicks(c, node, level),
                creditsPaid = next.creditCost, ionPaid = ion
            };
            if (PaysGoods(f, next))
            {
                EconomyService.TakeStoredGoods(w, c, f.id, next.goods);
                foreach (var g in next.goods) job.goodsPaid.Add(new IngredientDefinition { itemId = g.itemId, quantity = g.quantity });
            }
            f.currency -= next.creditCost;
            f.ion -= ion;
            f.researchQueue.Add(job);
            string name = node.displayName + (node.levels.Count > 1 ? " " + level : "");
            if (f.researchQueue.Count == 1) return WorldLookup.Ok("Researching " + name + ": done in " + Clock(c, job.ticksTotal) + ".");
            return WorldLookup.Ok("Queued " + name + " (" + f.researchQueue.Count + " in the queue; done in " + Clock(c, TicksUntilDone(f, f.researchQueue.Count - 1)) + ").");
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
        /// <summary>
        /// Ticks to the node's next cycle with extraction research, carrying the leftover part of a tick into the
        /// next cycle (in <paramref name="carry"/>), so cycles average exactly the researched speed instead of being
        /// rounded back up to the base time.
        /// </summary>
        public static int NextExtractionTicks(ContentCatalog c, FactionState f, string itemId, int rateTicks, ref int carry)
        {
            int divisor = 100 + Math.Max(0, Bonus(c, f, ExtractionSpeed, itemId));
            if (carry < 0 || carry >= divisor) carry = 0; // research changed the divisor: start afresh
            int total = rateTicks * 100 + carry;
            int ticks = total / divisor;
            carry = total % divisor;
            return Math.Max(1, ticks);
        }

        /// <summary>Units an extractor mines per cycle for this faction: the base plus yield research.</summary>
        public static int ExtractionOutput(ContentCatalog c, FactionState f, string itemId)
        {
            return c.rules.nodeOutputPerCycle + Math.Max(0, Bonus(c, f, ExtractionYield, itemId));
        }

        /// <summary>An extractor's buffer: its catalog size, or two cycles' output when research makes cycles bigger.</summary>
        public static int ExtractorCapacity(BuildingDefinition def, int output)
        {
            return Math.Max(def != null ? def.inventoryCapacity : 0, output * 2);
        }

        /// <summary>Units of an item one belt slot carries for this faction: raw goods in stacks grown by Bulk Crates, made goods one at a time.</summary>
        public static int StackSize(ContentCatalog c, FactionState f, string itemId)
        {
            int size = CatalogRepository.StackSize(c, itemId);
            return size > 1 ? size + Math.Max(0, Bonus(c, f, RawStackSize)) : size;
        }

        /// <summary>How many exit belts one extractor may feed at once: 1, plus Multi-Chute research.</summary>
        public static int Chutes(ContentCatalog c, FactionState f)
        {
            return 1 + Math.Max(0, Bonus(c, f, ExtractorChutes));
        }

        /// <summary>The researched extraction time in seconds, averaged over cycles, for display.</summary>
        public static float ExtractionSeconds(ContentCatalog c, FactionState f, string itemId, int rateTicks)
        {
            int divisor = 100 + Math.Max(0, Bonus(c, f, ExtractionSpeed, itemId));
            return rateTicks * 100f / divisor * c.rules.tickMilliseconds / 1000f;
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
