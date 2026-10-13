using System;

namespace OrbitalFoundry
{
    /// <summary>Currency, sales and purchases. Spending never reduces cumulative sales.</summary>
    public sealed class EconomyService
    {
        readonly OwnershipService ownership;

        public EconomyService(OwnershipService ownership)
        {
            this.ownership = ownership;
        }

        public bool CanSpend(FactionState f, int amount)
        {
            return f != null && amount >= 0 && f.currency >= amount;
        }

        public bool Spend(FactionState f, int amount)
        {
            if (!CanSpend(f, amount)) return false;
            f.currency -= amount;
            return true;
        }

        public static int SalePrice(FactionState f, ItemDefinition item)
        {
            if (item == null || !item.sellable) return 0;
            long price = (long)item.baseSaleValue * Math.Max(0, f.saleMultiplierPercent) / 100;
            if (price < 1) price = 1;
            return price > int.MaxValue ? int.MaxValue : (int)price;
        }

        /// <summary>True when the faction has every tech that unlocks this item (items with no unlocking tech are always allowed).</summary>
        public static bool IsItemUnlocked(ContentCatalog c, FactionState f, string itemId)
        {
            foreach (var t in c.techs)
                if (t.unlockedItemId == itemId && !f.unlockedTechIds.Contains(t.id)) return false;
            return true;
        }

        public static bool IsRecipeUnlocked(ContentCatalog c, FactionState f, RecipeDefinition r)
        {
            return r != null && (string.IsNullOrEmpty(r.requiredTechId) || f.unlockedTechIds.Contains(r.requiredTechId));
        }

        /// <summary>Sells items for the catalog price without outcome evaluation; returns the value earned.</summary>
        public int Sell(FactionState f, ItemDefinition item, int quantity)
        {
            if (f == null || item == null || !item.sellable || quantity <= 0) return 0;
            long value = (long)SalePrice(f, item) * quantity;
            int earned = (int)Math.Min(value, int.MaxValue);
            f.currency = SaturatingAdd(f.currency, earned);
            f.cumulativeSales = SaturatingAdd(f.cumulativeSales, earned);
            return earned;
        }

        /// <summary>Sells and, for the player, re-evaluates the combined victory condition.</summary>
        public int Sell(WorldState w, ContentCatalog c, FactionState f, ItemDefinition item, int quantity)
        {
            int earned = Sell(f, item, quantity);
            if (earned > 0 && f.id == FactionId.Player && ownership != null) ownership.EvaluateOutcome(w, c.rules);
            return earned;
        }

        static int SaturatingAdd(int a, int b)
        {
            long sum = (long)a + b;
            return sum > int.MaxValue ? int.MaxValue : (int)sum;
        }

        public CommandResult BuyUpgrade(WorldState w, ContentCatalog c, GameCommand cmd)
        {
            var f = WorldLookup.GetFaction(w, cmd.actor);
            if (f == null || cmd.actor == FactionId.Neutral) return WorldLookup.Fail("NEUTRAL_ACTOR", "Only a faction can buy upgrades.");
            var u = CatalogRepository.GetUpgrade(c, cmd.contentId);
            if (u == null) return WorldLookup.Fail("UNKNOWN_UPGRADE", "That upgrade does not exist.");
            if (f.purchasedUpgradeIds.Contains(u.id)) return WorldLookup.Fail("ALREADY_OWNED", u.displayName + " is already purchased.");
            if (!Spend(f, u.cost)) return WorldLookup.Fail("NO_MONEY", "Not enough currency (" + u.cost + " needed).");
            f.purchasedUpgradeIds.Add(u.id);
            f.saleMultiplierPercent = Math.Max(f.saleMultiplierPercent, u.saleMultiplierPercent);
            return WorldLookup.Ok("Purchased " + u.displayName + ".");
        }

        public CommandResult UnlockTech(WorldState w, ContentCatalog c, GameCommand cmd)
        {
            var f = WorldLookup.GetFaction(w, cmd.actor);
            if (f == null || cmd.actor == FactionId.Neutral) return WorldLookup.Fail("NEUTRAL_ACTOR", "Only a faction can unlock technology.");
            var t = CatalogRepository.GetTech(c, cmd.contentId);
            if (t == null) return WorldLookup.Fail("UNKNOWN_TECH", "That technology does not exist.");
            if (f.unlockedTechIds.Contains(t.id)) return WorldLookup.Fail("ALREADY_OWNED", t.displayName + " is already unlocked.");
            if (!Spend(f, t.cost)) return WorldLookup.Fail("NO_MONEY", "Not enough currency (" + t.cost + " needed).");
            f.unlockedTechIds.Add(t.id);
            return WorldLookup.Ok("Unlocked " + t.displayName + ".");
        }

        public CommandResult BuyCraft(WorldState w, ContentCatalog c, GameCommand cmd)
        {
            var f = WorldLookup.GetFaction(w, cmd.actor);
            if (f == null || cmd.actor == FactionId.Neutral) return WorldLookup.Fail("NEUTRAL_ACTOR", "Only a faction can buy craft.");
            if (f.suppressed) return WorldLookup.Fail("SUPPRESSED", "This faction can no longer buy craft.");
            var def = CatalogRepository.GetCraft(c, cmd.contentId);
            if (def == null) return WorldLookup.Fail("UNKNOWN_CRAFT", "That craft type does not exist.");
            var cell = WorldLookup.GetCell(w, cmd.targetCellId);
            if (cell == null) return WorldLookup.Fail("BAD_CELL", "Choose a cell to launch the craft from.");
            if (cell.owner != cmd.actor) return WorldLookup.Fail("NOT_OWNER", "Craft can only launch from a cell you own.");
            if (WorldLookup.HasOpposingCraft(w, cell.id, cmd.actor)) return WorldLookup.Fail("CONTESTED", "That cell is contested.");
            if (!Spend(f, def.cost)) return WorldLookup.Fail("NO_MONEY", "Not enough currency (" + def.cost + " needed).");
            var craft = new CraftState
            {
                id = WorldLookup.AllocateId(w),
                owner = cmd.actor,
                typeId = def.id,
                cellId = cell.id,
                currentHealth = def.maxHealth
            };
            w.crafts.Add(craft);
            WorldLookup.AutoAssignDefender(w, cell.id);
            return WorldLookup.Ok("Launched " + def.displayName + ".");
        }
    }
}
