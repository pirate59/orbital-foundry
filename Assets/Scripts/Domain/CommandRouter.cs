using System.Collections.Generic;

namespace OrbitalFoundry
{
    /// <summary>
    /// The single mutation path for player and AI orders. Validates the mapped command fields and match mode before
    /// any state change, charges currency only after a legal action, and returns a stable code plus a readable message.
    /// </summary>
    public sealed class CommandRouter
    {
        readonly GameServices s;
        bool executing;

        public CommandRouter(GameServices services)
        {
            s = services;
        }

        public CommandResult Execute(WorldState w, ContentCatalog c, MapDefinition map, GameCommand cmd)
        {
            if (cmd == null) return WorldLookup.Fail("MALFORMED", "Empty command.");
            if (executing) return WorldLookup.Fail("REENTRANT", "Commands cannot be issued while another command is running.");
            executing = true;
            try
            {
                var r = Validate(w, cmd);
                if (!r.success) return r;
                r = Dispatch(w, c, map, cmd);
                if (r.success) r.stateChanged = true;
                return r;
            }
            finally
            {
                executing = false;
            }
        }

        static bool IsFactoryOnly(CommandKind k)
        {
            switch (k)
            {
                case CommandKind.PlaceBuilding:
                case CommandKind.PlaceBelt:
                case CommandKind.SelectRecipe:
                case CommandKind.Research:
                case CommandKind.BuyCraft:
                case CommandKind.MoveWorld:
                case CommandKind.StartAttack:
                case CommandKind.Dismantle:
                case CommandKind.ToggleDepotItem:
                case CommandKind.UpgradeBelt:
                case CommandKind.LinkDronePad:
                case CommandKind.SetPriority:
                    return true;
                default:
                    return false;
            }
        }

        static bool IsTacticalOnly(CommandKind k)
        {
            return k == CommandKind.TacticalMove || k == CommandKind.TacticalAttack || k == CommandKind.EndTurn || k == CommandKind.Retreat;
        }

        CommandResult Validate(WorldState w, GameCommand cmd)
        {
            if (cmd.actor != FactionId.Player && cmd.actor != FactionId.Ai) return WorldLookup.Fail("NEUTRAL_ACTOR", "Only a faction can issue commands.");
            if (w.mode == MatchMode.Ended) return WorldLookup.Fail("MATCH_ENDED", "The match has ended.");
            if (cmd.actor == FactionId.Ai && WorldLookup.IsSuppressed(w, FactionId.Ai)) return WorldLookup.Fail("SUPPRESSED", "The AI has withdrawn.");
            if (IsFactoryOnly(cmd.kind) && w.mode != MatchMode.Factory) return WorldLookup.Fail("WRONG_MODE", "Factory orders are paused during battle.");
            if (IsTacticalOnly(cmd.kind) && w.mode != MatchMode.Tactical) return WorldLookup.Fail("WRONG_MODE", "That order is only available in battle.");

            bool content = !string.IsNullOrEmpty(cmd.contentId);
            switch (cmd.kind)
            {
                case CommandKind.PlaceBuilding:
                    if (!content || cmd.fromCellId < 0) return Malformed("PlaceBuilding needs a building type and a tile.");
                    break;
                case CommandKind.PlaceBelt:
                    if (cmd.fromCellId < 0 || cmd.toCellId < 0) return Malformed("PlaceBelt needs both endpoints.");
                    break;
                case CommandKind.SelectRecipe:
                    if (cmd.buildingId < 0 || !content) return Malformed("SelectRecipe needs a building and a recipe.");
                    break;
                case CommandKind.Research:
                    if (!content) return Malformed("Research needs a node ID.");
                    break;
                case CommandKind.BuyCraft:
                    if (!content || cmd.buildingId < 0) return Malformed("Ordering a craft needs a craft type and a shipyard.");
                    break;
                case CommandKind.AssignDefender:
                case CommandKind.MoveWorld:
                case CommandKind.StartAttack:
                case CommandKind.TacticalMove:
                case CommandKind.StartCapture:
                    if (cmd.craftId < 0 || cmd.targetCellId < 0) return Malformed("This order needs a craft and a target cell.");
                    break;
                case CommandKind.TacticalAttack:
                    if (cmd.craftId < 0 || cmd.targetCellId < 0) return Malformed("Attack needs a craft and a target cell.");
                    break;
                case CommandKind.Dismantle:
                    if ((cmd.buildingId < 0) == (cmd.beltId < 0)) return Malformed("Dismantle needs exactly one building or belt.");
                    break;
                case CommandKind.UpgradeBelt:
                    if (cmd.beltId < 0) return Malformed("Upgrade needs a belt.");
                    break;
                case CommandKind.LinkDronePad:
                    if (cmd.buildingId < 0 || (cmd.contentId != "unlink" && cmd.toCellId < 0)) return Malformed("A drone link needs a pad and a destination tile.");
                    break;
                case CommandKind.SetPriority:
                    if (cmd.buildingId < 0 || !content) return Malformed("Setting a priority needs a building and a level.");
                    break;
                case CommandKind.ToggleDepotItem:
                    if (cmd.buildingId < 0 || !content) return Malformed("Toggling a depot item needs a depot and an item.");
                    break;
                case CommandKind.EndTurn:
                case CommandKind.Retreat:
                    break;
                default:
                    return Malformed("Unknown command.");
            }
            return WorldLookup.Ok();
        }

        static CommandResult Malformed(string message)
        {
            return WorldLookup.Fail("MALFORMED", message);
        }

        CommandResult Dispatch(WorldState w, ContentCatalog c, MapDefinition map, GameCommand cmd)
        {
            switch (cmd.kind)
            {
                case CommandKind.PlaceBuilding: return PlaceBuilding(w, c, map, cmd);
                case CommandKind.PlaceBelt: return PlaceBelt(w, c, map, cmd);
                case CommandKind.SelectRecipe: return SelectRecipe(w, c, cmd);
                case CommandKind.Research: return ResearchService.Purchase(w, c, cmd);
                case CommandKind.BuyCraft: return s.Economy.BuyCraft(w, c, cmd);
                case CommandKind.AssignDefender: return s.Encounters.AssignDefender(w, cmd);
                case CommandKind.MoveWorld: return MoveWorld(w, c, map, cmd);
                case CommandKind.StartAttack: return StartAttack(w, c, map, cmd);
                case CommandKind.TacticalMove: return s.Encounters.ApplyMove(w, cmd);
                case CommandKind.TacticalAttack: return s.Encounters.ApplyAttack(w, cmd);
                case CommandKind.StartCapture:
                    return w.mode == MatchMode.Tactical ? s.Encounters.StartCapture(w, cmd) : s.Capture.StartHold(w, cmd, c.rules);
                case CommandKind.EndTurn: return s.Encounters.EndTurn(w, cmd);
                case CommandKind.Retreat: return s.Encounters.Retreat(w, cmd);
                case CommandKind.Dismantle: return Dismantle(w, c, cmd);
                case CommandKind.UpgradeBelt: return UpgradeBelt(w, c, cmd);
                case CommandKind.LinkDronePad: return DroneLinks.Link(w, c, map, s.Belts, s.Economy, cmd);
                case CommandKind.SetPriority: return DeliveryPriority.Set(w, c, cmd);
                case CommandKind.ToggleDepotItem: return ToggleDepotItem(w, c, cmd);
                default: return Malformed("Unknown command.");
            }
        }

        CommandResult PlaceBuilding(WorldState w, ContentCatalog c, MapDefinition map, GameCommand cmd)
        {
            var def = CatalogRepository.GetBuilding(c, cmd.contentId);
            var r = GridRules.CanPlaceBuilding(w, c, map, def, cmd.actor, cmd.fromCellId, cmd.fromX, cmd.fromY);
            if (!r.success) return r;
            var faction = WorldLookup.GetFaction(w, cmd.actor);
            if (!ResearchService.IsBuildingUnlocked(c, faction, def.id))
            {
                var gate = ResearchService.BuildingGate(c, def.id);
                return WorldLookup.Fail("LOCKED_BUILDING", "Requires research: " + (gate != null ? gate.displayName : "unknown") + ".");
            }
            if (def.goodsCost.Count > 0 && !EconomyService.HasStoredGoods(w, c, cmd.actor, def.goodsCost))
                return WorldLookup.Fail("NEEDS_GOODS", def.displayName + " needs " + EconomyService.IngredientText(c, def.goodsCost) + " in your Storage.");
            if (!s.Economy.Spend(faction, def.cost)) return WorldLookup.Fail("NO_MONEY", "Not enough currency (" + def.cost + " needed).");
            EconomyService.TakeStoredGoods(w, c, cmd.actor, def.goodsCost);
            var b = new BuildingState
            {
                id = WorldLookup.AllocateId(w),
                owner = cmd.actor,
                typeId = def.id,
                cellId = cmd.fromCellId,
                x = cmd.fromX,
                y = cmd.fromY,
                anchor = def.kind == BuildingKind.Fort || def.kind == BuildingKind.Headquarters,
                underConstruction = def.materials.Count > 0,
                targetCellId = def.kind == BuildingKind.ConstructionZone ? GridRules.BorderNeighbour(map, cmd.fromCellId, cmd.fromX, cmd.fromY) : -1
            };
            if (def.kind == BuildingKind.Extractor)
            {
                var node = WorldLookup.GetNodeAt(w, cmd.fromCellId, cmd.fromX, cmd.fromY);
                node.extractorBuildingId = b.id;
                node.extractionCarry = 0;
                node.ticksUntilOutput = ResearchService.NextExtractionTicks(c, faction, node.itemId, node.rateTicks, ref node.extractionCarry);
                b.nodeId = node.id;
            }
            // Machines start with no recipe: the owner chooses what each one makes.
            w.buildings.Add(b);
            if (b.underConstruction)
                return WorldLookup.Ok(def.displayName + " placed" + (b.targetCellId >= 0 ? " facing cell " + b.targetCellId : "")
                    + ". Belt in " + EconomyService.IngredientText(c, def.materials) + " to finish it.");
            return WorldLookup.Ok("Built " + def.displayName + ".");
        }

        CommandResult PlaceBelt(WorldState w, ContentCatalog c, MapDefinition map, GameCommand cmd)
        {
            var step = new BeltStep
            {
                fromCellId = cmd.fromCellId, fromX = cmd.fromX, fromY = cmd.fromY,
                toCellId = cmd.toCellId, toX = cmd.toX, toY = cmd.toY
            };
            var r = GridRules.CanPlaceBelt(w, map, cmd.actor, step);
            if (r.code == "OPPOSITE_EDGE")
            {
                // Drawing over your own belt the other way reverses it, free of charge.
                var opposite = w.belts.Find(b => b.fromCellId == step.toCellId && b.fromX == step.toX && b.fromY == step.toY
                                              && b.toCellId == step.fromCellId && b.toX == step.fromX && b.toY == step.fromY);
                if (opposite == null || opposite.owner != cmd.actor || opposite.inactiveUntilTick != -1) return r;
                OwnershipService.RemoveStructures(w, new HashSet<int>(), new HashSet<int> { opposite.id });
                r = GridRules.CanPlaceBelt(w, map, cmd.actor, step);
                if (!r.success) return r;
                w.belts.Add(new BeltSegmentState
                {
                    id = WorldLookup.AllocateId(w),
                    owner = cmd.actor,
                    fromCellId = step.fromCellId, fromX = step.fromX, fromY = step.fromY,
                    toCellId = step.toCellId, toX = step.toX, toY = step.toY
                });
                s.Belts.Rebuild(w);
                return WorldLookup.Ok("Belt reversed.");
            }
            if (!r.success) return r;
            var faction = WorldLookup.GetFaction(w, cmd.actor);
            if (!s.Economy.Spend(faction, c.rules.beltCost)) return WorldLookup.Fail("NO_MONEY", "Not enough currency for a belt (" + c.rules.beltCost + ").");
            w.belts.Add(new BeltSegmentState
            {
                id = WorldLookup.AllocateId(w),
                owner = cmd.actor,
                fromCellId = step.fromCellId, fromX = step.fromX, fromY = step.fromY,
                toCellId = step.toCellId, toX = step.toX, toY = step.toY
            });
            s.Belts.Rebuild(w);
            return WorldLookup.Ok();
        }

        /// <summary>Switches whether a sale depot accepts one sellable item.</summary>
        CommandResult ToggleDepotItem(WorldState w, ContentCatalog c, GameCommand cmd)
        {
            var b = WorldLookup.GetBuilding(w, cmd.buildingId);
            if (b == null) return WorldLookup.Fail("BAD_BUILDING", "That building does not exist.");
            if (b.owner != cmd.actor) return WorldLookup.Fail("WRONG_OWNER", "That building is not yours.");
            var def = CatalogRepository.GetBuilding(c, b.typeId);
            bool depot = def != null && def.kind == BuildingKind.SaleDepot;
            bool storage = def != null && def.kind == BuildingKind.Storage;
            if (!depot && !storage) return WorldLookup.Fail("NOT_DEPOT", "Only sale depots and storages have an item list.");
            var item = CatalogRepository.GetItem(c, cmd.contentId);
            if (item == null || (depot && !item.sellable)) return WorldLookup.Fail("NOT_SELLABLE", "That item cannot be sold.");
            bool nowBlocked = !b.blockedItemIds.Remove(item.id);
            if (nowBlocked) b.blockedItemIds.Add(item.id);
            w.pullRequests.RemoveAll(r => r.targetBuildingId == b.id && r.itemId == item.id && nowBlocked);
            string verb = depot ? "sold" : "stored";
            return WorldLookup.Ok(item.displayName + (nowBlocked ? " will no longer be " + verb + " here." : " will be " + verb + " here."));
        }

        /// <summary>
        /// Raises a belt (or, with contentId "line", every belt in its line) to the tier above the chosen belt's.
        /// Belts already at or above that tier are left alone; the price is the new tier's per-belt cost.
        /// </summary>
        CommandResult UpgradeBelt(WorldState w, ContentCatalog c, GameCommand cmd)
        {
            var belt = WorldLookup.GetBelt(w, cmd.beltId);
            if (belt == null) return WorldLookup.Fail("BAD_BELT", "That belt does not exist.");
            if (belt.owner != cmd.actor) return WorldLookup.Fail("WRONG_OWNER", "That belt is not yours.");
            if (!WorldLookup.IsBeltActive(w, belt)) return WorldLookup.Fail("INACTIVE", "That belt is not working.");
            int target = belt.tier + 1;
            if (target >= c.rules.beltTiers.Count) return WorldLookup.Fail("MAX_TIER", "That belt is already the fastest kind.");
            var tier = c.rules.beltTiers[target];
            var segments = cmd.contentId == "line" ? s.Belts.LineOf(w, belt.id) : new List<BeltSegmentState> { belt };
            segments.RemoveAll(seg => seg.tier >= target || seg.owner != cmd.actor || !WorldLookup.IsBeltActive(w, seg));
            int cost = tier.upgradeCostPerSegment * segments.Count;
            var faction = WorldLookup.GetFaction(w, cmd.actor);
            if (!s.Economy.Spend(faction, cost)) return WorldLookup.Fail("NO_MONEY", "Not enough currency (" + cost + " needed).");
            foreach (var seg in segments) seg.tier = target;
            return WorldLookup.Ok("Upgraded " + segments.Count + " belt" + (segments.Count == 1 ? "" : "s") + " to " + tier.name + ".");
        }

        CommandResult SelectRecipe(WorldState w, ContentCatalog c, GameCommand cmd)
        {
            var b = WorldLookup.GetBuilding(w, cmd.buildingId);
            if (b == null) return WorldLookup.Fail("BAD_BUILDING", "That building does not exist.");
            if (b.owner != cmd.actor) return WorldLookup.Fail("WRONG_OWNER", "That building is not yours.");
            var def = CatalogRepository.GetBuilding(c, b.typeId);
            if (def == null || !def.allowedRecipeIds.Contains(cmd.contentId)) return WorldLookup.Fail("BAD_RECIPE", "This building cannot run that recipe.");
            var recipe = CatalogRepository.GetRecipe(c, cmd.contentId);
            var faction = WorldLookup.GetFaction(w, cmd.actor);
            if (!ResearchService.IsRecipeUnlocked(c, faction, recipe))
            {
                var gate = ResearchService.RecipeGate(c, recipe.id);
                return WorldLookup.Fail("LOCKED_RECIPE", "Requires research: " + (gate != null ? gate.displayName : "unknown") + ".");
            }
            if (b.selectedRecipeId == recipe.id) return WorldLookup.Fail("UNCHANGED", "That recipe is already selected.");
            b.selectedRecipeId = recipe.id;
            b.recipeProgressTicks = 0;
            return WorldLookup.Ok("Recipe set.");
        }

        CommandResult MoveWorld(WorldState w, ContentCatalog c, MapDefinition map, GameCommand cmd)
        {
            var craft = WorldLookup.GetCraft(w, cmd.craftId);
            if (craft == null || !WorldLookup.IsCraftLive(craft)) return WorldLookup.Fail("CRAFT_UNAVAILABLE", "That craft is unavailable.");
            if (craft.owner != cmd.actor) return WorldLookup.Fail("WRONG_OWNER", "That craft is not yours.");
            if (craft.cellId == cmd.targetCellId && craft.worldPathCellIds.Count == 0) return WorldLookup.Fail("ALREADY_THERE", "The craft is already there.");
            var path = s.Movement.FindWorldPath(w, map, craft.owner, craft.cellId, cmd.targetCellId);
            if (path == null) return WorldLookup.Fail("NO_ROUTE", "No route: enemy craft block the way or hold the destination. Use Attack instead.");
            w.captureHolds.RemoveAll(h => h.craftId == craft.id);
            s.Raids.BeginTravel(w, c, craft, path, cmd.targetCellId);
            return WorldLookup.Ok("Craft moving to cell " + cmd.targetCellId + ".");
        }

        CommandResult StartAttack(WorldState w, ContentCatalog c, MapDefinition map, GameCommand cmd)
        {
            var craft = WorldLookup.GetCraft(w, cmd.craftId);
            if (craft == null || !WorldLookup.IsCraftLive(craft)) return WorldLookup.Fail("CRAFT_UNAVAILABLE", "That craft is unavailable.");
            if (craft.owner != cmd.actor) return WorldLookup.Fail("WRONG_OWNER", "That craft is not yours.");
            var target = WorldLookup.GetCell(w, cmd.targetCellId);
            if (target == null) return WorldLookup.Fail("BAD_CELL", "That cell does not exist.");
            if (target.owner == cmd.actor) return WorldLookup.Fail("OWN_CELL", "You cannot attack your own cell.");
            if (craft.cellId == target.id) return WorldLookup.Fail("ALREADY_THERE", "The craft is already in that cell; claim its control point instead.");
            bool defended = WorldLookup.HasOpposingCraft(w, target.id, cmd.actor);
            w.captureHolds.RemoveAll(h => h.craftId == craft.id);
            int distance = WorldLookup.CellDistance(map, craft.cellId, target.id);
            var def = CatalogRepository.GetCraft(c, craft.typeId);
            bool inPosition = distance == 1 || (def != null && distance > 1 && distance <= def.range);
            if (defended && inPosition)
            {
                craft.worldPathCellIds.Clear();
                craft.worldDestinationCellId = -1;
                return s.Encounters.Start(w, c, map, target.id, cmd.actor);
            }
            // Travel to a staging cell; an undefended target is walked into and claimed on arrival.
            return s.Raids.ScheduleRaid(w, map, c, craft.id, target.id);
        }

        /// <summary>Refund share when a faction dismantles its own structure.</summary>
        public const int DismantleRefundPercent = 50;

        /// <summary>
        /// Removes one building or belt. A faction may always dismantle its own structures in cells it holds (half the
        /// cost back; stored items and items on the belt are lost). After the AI headquarters falls, the player may also
        /// clear inactive AI structures in cells the player has claimed.
        /// </summary>
        CommandResult Dismantle(WorldState w, ContentCatalog c, GameCommand cmd)
        {
            var buildings = new HashSet<int>();
            var belts = new HashSet<int>();
            FactionId owner;
            int cellId;
            int cost;
            string name;
            if (cmd.buildingId >= 0)
            {
                var b = WorldLookup.GetBuilding(w, cmd.buildingId);
                if (b == null) return WorldLookup.Fail("BAD_BUILDING", "That building does not exist.");
                var def = CatalogRepository.GetBuilding(c, b.typeId);
                if (def != null && def.kind == BuildingKind.Headquarters) return WorldLookup.Fail("NOT_ALLOWED", "Headquarters cannot be dismantled.");
                owner = b.owner; cellId = b.cellId; cost = def != null ? def.cost : 0; name = def != null ? def.displayName : b.typeId;
                buildings.Add(b.id);
            }
            else
            {
                var belt = WorldLookup.GetBelt(w, cmd.beltId);
                if (belt == null) return WorldLookup.Fail("BAD_BELT", "That belt does not exist.");
                owner = belt.owner; cellId = belt.toCellId; cost = c.rules.beltCost; name = "Belt";
                belts.Add(belt.id);
            }
            var cell = WorldLookup.GetCell(w, cellId);
            if (cell == null || cell.owner != cmd.actor) return WorldLookup.Fail("NOT_OWNER", "You can only dismantle structures in cells you hold.");

            int refund = 0;
            if (owner == cmd.actor)
            {
                refund = cost * DismantleRefundPercent / 100;
            }
            else
            {
                bool clearingAi = cmd.actor == FactionId.Player && owner == FactionId.Ai && WorldLookup.IsSuppressed(w, FactionId.Ai);
                bool inactive = buildings.Count > 0 ? WorldLookup.GetBuilding(w, cmd.buildingId).inactiveUntilTick != -1
                                                    : WorldLookup.GetBelt(w, cmd.beltId).inactiveUntilTick != -1;
                if (!clearingAi || !inactive) return WorldLookup.Fail("NOT_ALLOWED", "You can only dismantle your own structures (or inactive AI ones after its HQ falls).");
            }
            OwnershipService.RemoveStructures(w, buildings, belts);
            s.Belts.Rebuild(w);
            if (refund > 0) WorldLookup.GetFaction(w, cmd.actor).currency += refund;
            return WorldLookup.Ok(name + " dismantled" + (refund > 0 ? " (+" + refund + " refunded)." : "."));
        }
    }
}
