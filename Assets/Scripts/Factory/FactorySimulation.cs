using System.Collections.Generic;

namespace OrbitalFoundry
{
    /// <summary>
    /// One normal-play factory tick in fixed order: extraction, recipes, pull requests, token movement and
    /// delivery, then depot sales. Never runs in Tactical or Ended mode.
    /// </summary>
    public sealed class FactorySimulation
    {
        public void Tick(WorldState w, ContentCatalog c, MapDefinition map, BeltNetwork network, PullRouter router, EconomyService economy)
        {
            if (w.mode != MatchMode.Factory) return;
            network.EnsureCurrent(w);
            TickExtractors(w, c);
            TickRecipes(w, c);
            router.RefreshRequests(w, c);
            router.DispatchOldest(w, c, network);
            TickTokens(w, c, network);
            SellDepots(w, c, economy);
        }

        static void TickExtractors(WorldState w, ContentCatalog c)
        {
            foreach (var node in w.nodes)
            {
                if (node.extractorBuildingId == -1) continue;
                var b = WorldLookup.GetBuilding(w, node.extractorBuildingId);
                if (b == null) { node.extractorBuildingId = -1; continue; }
                if (!WorldLookup.IsBuildingActive(w, b)) continue;
                var def = CatalogRepository.GetBuilding(c, b.typeId);
                if (node.ticksUntilOutput > 0) node.ticksUntilOutput--;
                if (node.ticksUntilOutput > 0) continue;
                // Hold at zero while full; output as soon as room opens.
                if (def == null || WorldLookup.TotalItems(b.inventory) >= def.inventoryCapacity) continue;
                WorldLookup.AddItem(b.inventory, node.itemId, 1);
                node.ticksUntilOutput = node.rateTicks;
            }
        }

        static void TickRecipes(WorldState w, ContentCatalog c)
        {
            foreach (var b in w.buildings)
            {
                if (!WorldLookup.IsBuildingActive(w, b)) continue;
                var def = CatalogRepository.GetBuilding(c, b.typeId);
                if (def == null || (def.kind != BuildingKind.Refinery && def.kind != BuildingKind.Assembler)) continue;
                if (string.IsNullOrEmpty(b.selectedRecipeId) || !def.allowedRecipeIds.Contains(b.selectedRecipeId)) continue;
                var recipe = CatalogRepository.GetRecipe(c, b.selectedRecipeId);
                var faction = WorldLookup.GetFaction(w, b.owner);
                if (!EconomyService.IsRecipeUnlocked(c, faction, recipe)) continue;
                if (!HasInputs(b, recipe)) continue;

                if (b.recipeProgressTicks < recipe.craftTicks) b.recipeProgressTicks++;
                if (b.recipeProgressTicks < recipe.craftTicks) continue;

                int inputTotal = 0;
                foreach (var i in recipe.inputs) inputTotal += i.quantity;
                int after = WorldLookup.TotalItems(b.inventory) - inputTotal + recipe.outputQuantity;
                if (after > def.inventoryCapacity) continue; // hold completed progress until output room opens

                foreach (var i in recipe.inputs) WorldLookup.RemoveItem(b.inventory, i.itemId, i.quantity);
                WorldLookup.AddItem(b.inventory, recipe.outputItemId, recipe.outputQuantity);
                b.recipeProgressTicks = 0;
            }
        }

        public static bool HasInputs(BuildingState b, RecipeDefinition recipe)
        {
            foreach (var i in recipe.inputs)
                if (WorldLookup.CountItem(b.inventory, i.itemId) < i.quantity) return false;
            return true;
        }

        static void TickTokens(WorldState w, ContentCatalog c, BeltNetwork network)
        {
            var removals = new List<ItemTokenState>();
            var ordered = new List<ItemTokenState>(w.tokens);
            ordered.Sort((a, b) => a.id.CompareTo(b.id));
            foreach (var t in ordered)
            {
                if (t.segmentIndex < 0 || t.segmentIndex >= t.routeSegmentIds.Count) { removals.Add(t); continue; }
                var current = network.GetSegment(w, t.routeSegmentIds[t.segmentIndex]);
                if (current == null) { removals.Add(t); continue; }
                if (!WorldLookup.IsBeltActive(w, current)) { t.status = TokenStatus.Frozen; continue; }
                if (t.status == TokenStatus.Frozen) t.status = TokenStatus.Moving;

                if (t.ticksUntilNextSegment > 0) t.ticksUntilNextSegment--;
                if (t.ticksUntilNextSegment > 0) continue;

                if (t.segmentIndex == t.routeSegmentIds.Count - 1)
                {
                    var target = WorldLookup.GetBuilding(w, t.targetBuildingId);
                    if (target == null) { removals.Add(t); continue; }
                    var def = CatalogRepository.GetBuilding(c, target.typeId);
                    if (!WorldLookup.IsBuildingActive(w, target) || def == null || WorldLookup.TotalItems(target.inventory) >= def.inventoryCapacity)
                    {
                        t.status = TokenStatus.Waiting;
                        continue;
                    }
                    WorldLookup.AddItem(target.inventory, t.itemId, 1);
                    removals.Add(t);
                    continue;
                }

                var next = network.GetSegment(w, t.routeSegmentIds[t.segmentIndex + 1]);
                if (next == null)
                {
                    // A later edge was permanently removed: replan from the end of the current edge.
                    if (!Replan(w, network, t, current)) { t.status = TokenStatus.Waiting; continue; }
                    next = network.GetSegment(w, t.routeSegmentIds[t.segmentIndex + 1]);
                    if (next == null) { t.status = TokenStatus.Waiting; continue; }
                }
                if (!WorldLookup.IsBeltActive(w, next) || !network.HasCapacity(w, c, next.id))
                {
                    t.status = TokenStatus.Waiting;
                    continue;
                }
                t.segmentIndex++;
                t.ticksUntilNextSegment = c.rules.beltTravelTicksPerSegment;
                t.status = TokenStatus.Moving;
            }
            foreach (var t in removals) w.tokens.Remove(t);
        }

        static bool Replan(WorldState w, BeltNetwork network, ItemTokenState t, BeltSegmentState current)
        {
            var target = WorldLookup.GetBuilding(w, t.targetBuildingId);
            if (target == null) return false;
            var path = network.FindRouteFromVertex(w, current.owner, current.toCellId, current.toX, current.toY, target, null);
            if (!path.success) return false;
            var route = new List<int>();
            for (int i = 0; i <= t.segmentIndex; i++) route.Add(t.routeSegmentIds[i]);
            route.AddRange(path.segmentIds);
            t.routeSegmentIds = route;
            return true;
        }

        static void SellDepots(WorldState w, ContentCatalog c, EconomyService economy)
        {
            foreach (var b in w.buildings)
            {
                if (!WorldLookup.IsBuildingActive(w, b)) continue;
                var def = CatalogRepository.GetBuilding(c, b.typeId);
                if (def == null || def.kind != BuildingKind.SaleDepot || b.inventory.Count == 0) continue;
                var faction = WorldLookup.GetFaction(w, b.owner);
                for (int i = b.inventory.Count - 1; i >= 0; i--)
                {
                    var stack = b.inventory[i];
                    var item = CatalogRepository.GetItem(c, stack.itemId);
                    if (item == null || !item.sellable) continue;
                    economy.Sell(w, c, faction, item, stack.quantity);
                    b.inventory.RemoveAt(i);
                }
            }
        }
    }
}
