using System;
using System.Collections.Generic;

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

        /// <summary>Catalog value scaled by the faction's sale-price research, never below 1.</summary>
        public static int SalePrice(ContentCatalog c, FactionState f, ItemDefinition item)
        {
            if (item == null || !item.sellable) return 0;
            long price = (long)item.baseSaleValue * ResearchService.SalePercent(c, f) / 100;
            if (price < 1) price = 1;
            return price > int.MaxValue ? int.MaxValue : (int)price;
        }

        /// <summary>Sells items for the catalog price without outcome evaluation; returns the value earned.</summary>
        public int Sell(ContentCatalog c, FactionState f, ItemDefinition item, int quantity)
        {
            if (f == null || item == null || !item.sellable || quantity <= 0) return 0;
            long value = (long)SalePrice(c, f, item) * quantity;
            int earned = (int)Math.Min(value, int.MaxValue);
            f.currency = SaturatingAdd(f.currency, earned);
            f.cumulativeSales = SaturatingAdd(f.cumulativeSales, earned);
            return earned;
        }

        /// <summary>Sells and, for the player, re-evaluates the combined victory condition.</summary>
        public int Sell(WorldState w, ContentCatalog c, FactionState f, ItemDefinition item, int quantity)
        {
            int earned = Sell(c, f, item, quantity);
            if (earned > 0 && f.id == FactionId.Player && ownership != null) ownership.EvaluateOutcome(w, c.rules);
            return earned;
        }

        public static int SaturatingAdd(int a, int b)
        {
            long sum = (long)a + b;
            return sum > int.MaxValue ? int.MaxValue : (int)sum;
        }

        /// <summary>
        /// Orders a craft at one of the actor's shipyards. The credit fee is paid now; the shipyard then pulls the
        /// craft's components by belt and launches the craft when they have all arrived and the build time elapses.
        /// </summary>
        public CommandResult BuyCraft(WorldState w, ContentCatalog c, GameCommand cmd)
        {
            var f = WorldLookup.GetFaction(w, cmd.actor);
            if (f == null || cmd.actor == FactionId.Neutral) return WorldLookup.Fail("NEUTRAL_ACTOR", "Only a faction can order craft.");
            if (f.suppressed) return WorldLookup.Fail("SUPPRESSED", "This faction can no longer build craft.");
            var def = CatalogRepository.GetCraft(c, cmd.contentId);
            if (def == null) return WorldLookup.Fail("UNKNOWN_CRAFT", "That craft type does not exist.");
            var yard = WorldLookup.GetBuilding(w, cmd.buildingId);
            var yardDef = yard != null ? CatalogRepository.GetBuilding(c, yard.typeId) : null;
            if (yard == null || yardDef == null || yardDef.kind != BuildingKind.Shipyard)
                return WorldLookup.Fail("NEEDS_SHIPYARD", "Craft are built at a Shipyard. Build one and select it first.");
            if (yard.owner != cmd.actor) return WorldLookup.Fail("WRONG_OWNER", "That shipyard is not yours.");
            if (!WorldLookup.IsBuildingActive(w, yard)) return WorldLookup.Fail("INACTIVE", "That shipyard is not working.");
            if (!string.IsNullOrEmpty(yard.orderedCraftTypeId)) return WorldLookup.Fail("SHIPYARD_BUSY", "That shipyard is already building a craft.");
            if (!Spend(f, def.cost)) return WorldLookup.Fail("NO_MONEY", "Not enough currency (" + def.cost + " needed).");
            yard.orderedCraftTypeId = def.id;
            yard.recipeProgressTicks = 0;
            return WorldLookup.Ok(def.displayName + " ordered. Deliver " + ComponentText(c, def) + " to the shipyard by belt.");
        }

        public static string ComponentText(ContentCatalog c, CraftDefinition def)
        {
            return IngredientText(c, def.components);
        }

        /// <summary>True when the faction's working Storage buildings together hold every listed good.</summary>
        public static bool HasStoredGoods(WorldState w, ContentCatalog c, FactionId owner, List<IngredientDefinition> goods)
        {
            foreach (var g in goods)
                if (StoredCount(w, c, owner, g.itemId) < g.quantity) return false;
            return true;
        }

        public static int StoredCount(WorldState w, ContentCatalog c, FactionId owner, string itemId)
        {
            int n = 0;
            foreach (var b in w.buildings)
                if (b.owner == owner && IsStorage(c, b) && WorldLookup.IsBuildingActive(w, b)) n += WorldLookup.CountItem(b.inventory, itemId);
            return n;
        }

        /// <summary>Removes goods from the faction's Storage buildings, lowest building ID first.</summary>
        public static void TakeStoredGoods(WorldState w, ContentCatalog c, FactionId owner, List<IngredientDefinition> goods)
        {
            foreach (var g in goods)
            {
                int left = g.quantity;
                foreach (var b in w.buildings)
                {
                    if (left <= 0) break;
                    if (b.owner != owner || !IsStorage(c, b) || !WorldLookup.IsBuildingActive(w, b)) continue;
                    int take = Math.Min(left, WorldLookup.CountItem(b.inventory, g.itemId));
                    if (take > 0) { WorldLookup.RemoveItem(b.inventory, g.itemId, take); left -= take; }
                }
            }
        }

        static bool IsStorage(ContentCatalog c, BuildingState b)
        {
            var def = CatalogRepository.GetBuilding(c, b.typeId);
            return def != null && def.kind == BuildingKind.Storage;
        }

        /// <summary>"4 Iron Bar, 1 Signal Relay" for any list of goods.</summary>
        public static string IngredientText(ContentCatalog c, List<IngredientDefinition> list)
        {
            var parts = new List<string>();
            foreach (var part in list)
            {
                var item = CatalogRepository.GetItem(c, part.itemId);
                parts.Add(part.quantity + " " + (item != null ? item.displayName : part.itemId));
            }
            return string.Join(", ", parts);
        }

        /// <summary>Creates a craft in a cell and gives the cell a defender if it has none.</summary>
        public static CraftState SpawnCraft(WorldState w, CraftDefinition def, FactionId owner, int cellId)
        {
            var craft = new CraftState
            {
                id = WorldLookup.AllocateId(w),
                owner = owner,
                typeId = def.id,
                cellId = cellId,
                currentHealth = def.maxHealth
            };
            w.crafts.Add(craft);
            WorldLookup.AutoAssignDefender(w, cellId);
            return craft;
        }
    }
}
