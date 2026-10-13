using System.Collections.Generic;

namespace OrbitalFoundry
{
    /// <summary>
    /// Normal-play AI: executes at most one queued factory command every <see cref="CommandIntervalTicks"/> ticks,
    /// asks the planner for the next step otherwise, and makes expansion, purchase and raid decisions with real
    /// AI currency. All orders go through CommandRouter.
    /// </summary>
    public sealed class AIController
    {
        public const int CommandIntervalTicks = 10;
        const int CraftReserve = 120;
        const int MaxAiCraft = 6;
        static readonly string[] CraftRotation = { "tank", "artillery", "strike", "guard" };

        readonly GameServices s;
        public string LastAction = "";

        public AIController(GameServices services)
        {
            s = services;
        }

        public void Tick(WorldState w, ContentCatalog c, MapDefinition map, CommandRouter router, AIFactoryPlanner planner)
        {
            if (w.mode != MatchMode.Factory || WorldLookup.IsSuppressed(w, FactionId.Ai)) return;
            var ai = w.ai;
            var faction = WorldLookup.GetFaction(w, FactionId.Ai);

            if (ai.queuedCommands.Count > 0)
            {
                if (w.tick < ai.nextPlanningTick) return;
                var cmd = ai.queuedCommands[0];
                if (!ResolvePlaceholders(w, cmd))
                {
                    planner.Reconsider(w, "placed building missing");
                    ai.nextPlanningTick = w.tick + CommandIntervalTicks;
                    return;
                }
                int cost = EstimateCost(c, cmd);
                if (faction.currency < cost) return; // wait for income; never spend below zero
                var r = router.Execute(w, c, map, cmd);
                ai.nextPlanningTick = w.tick + CommandIntervalTicks;
                if (r.success)
                {
                    ai.queuedCommands.RemoveAt(0);
                    LastAction = cmd.kind + " ok";
                }
                else
                {
                    LastAction = cmd.kind + " rejected: " + r.code;
                    planner.Reconsider(w, r.code);
                }
                return;
            }

            var plan = planner.PlanNext(w, c, map, s.AiBelts, s.Belts);
            if (plan.success)
            {
                ai.goal = AiGoal.BuildIncome;
                ai.targetItemId = plan.productItemId;
                ai.queuedCommands.AddRange(plan.commands);
                return;
            }
            if (plan.reason == AIFactoryPlanner.Searching) return;

            if (w.tick < ai.nextPlanningTick) return;
            ai.nextPlanningTick = w.tick + CommandIntervalTicks;
            if (c.rules.aiExpands || c.rules.aiRaids) MilitaryStep(w, c, map, router, faction);
        }

        static bool ResolvePlaceholders(WorldState w, GameCommand cmd)
        {
            if (cmd.kind != CommandKind.SelectRecipe || cmd.buildingId >= 0) return true;
            var b = WorldLookup.GetBuildingAt(w, cmd.fromCellId, cmd.fromX, cmd.fromY);
            if (b == null || b.owner != FactionId.Ai) return false;
            cmd.buildingId = b.id;
            cmd.fromCellId = -1;
            cmd.fromX = cmd.fromY = 0;
            return true;
        }

        static int EstimateCost(ContentCatalog c, GameCommand cmd)
        {
            switch (cmd.kind)
            {
                case CommandKind.PlaceBuilding: { var d = CatalogRepository.GetBuilding(c, cmd.contentId); return d != null ? d.cost : 0; }
                case CommandKind.PlaceBelt: return c.rules.beltCost;
                case CommandKind.BuyUpgrade: { var d = CatalogRepository.GetUpgrade(c, cmd.contentId); return d != null ? d.cost : 0; }
                case CommandKind.UnlockTech: { var d = CatalogRepository.GetTech(c, cmd.contentId); return d != null ? d.cost : 0; }
                case CommandKind.BuyCraft: { var d = CatalogRepository.GetCraft(c, cmd.contentId); return d != null ? d.cost : 0; }
                default: return 0;
            }
        }

        // ---------------------------------------------------------------- military

        void MilitaryStep(WorldState w, ContentCatalog c, MapDefinition map, CommandRouter router, FactionState faction)
        {
            var ai = w.ai;
            var free = FreeCraft(w);

            // Claim the cell a craft is standing in when it is clear.
            if (c.rules.aiExpands)
            {
                foreach (var craft in AllLive(w))
                {
                    if (craft.worldPathCellIds.Count > 0) continue;
                    var cell = WorldLookup.GetCell(w, craft.cellId);
                    if (cell.owner == FactionId.Ai || WorldLookup.HasOpposingCraft(w, cell.id, FactionId.Ai)) continue;
                    if (w.captureHolds.Exists(h => h.craftId == craft.id || (h.cellId == cell.id && h.faction == FactionId.Ai))) continue;
                    if (Run(w, c, map, router, new GameCommand { kind = CommandKind.StartCapture, actor = FactionId.Ai, craftId = craft.id, targetCellId = cell.id })) return;
                }
            }

            // Raid when due.
            if (c.rules.aiRaids && w.tick >= ai.nextRaidTick && ai.raidCraftId == -1 && free.Count > 0)
            {
                var raider = free[free.Count - 1];
                int target = ChooseRaidTarget(w, map, raider);
                if (target >= 0 && Run(w, c, map, router, new GameCommand { kind = CommandKind.StartAttack, actor = FactionId.Ai, craftId = raider.id, targetCellId = target }))
                {
                    ai.goal = AiGoal.Raid;
                    ai.nextRaidTick = w.tick + c.rules.raidIntervalTicks;
                    return;
                }
            }

            // Order craft at an idle shipyard once income is flowing, keeping a reserve. The shipyard still has to
            // receive the craft's components from the AI's factory before anything launches.
            if (AllLive(w).Count + PendingOrders(w) < MaxAiCraft)
            {
                var yard = IdleShipyard(w, c);
                string type = CraftRotation[(AllLive(w).Count + PendingOrders(w)) % CraftRotation.Length];
                var def = CatalogRepository.GetCraft(c, type);
                if (yard != null && def != null && faction.currency >= def.cost + CraftReserve
                    && Run(w, c, map, router, new GameCommand { kind = CommandKind.BuyCraft, actor = FactionId.Ai, contentId = type, buildingId = yard.id }))
                    return;
            }

            // Expand: send one spare craft to the nearest clear neutral cell.
            if (c.rules.aiExpands && free.Count > 0 && !AnyExpansionUnderway(w))
            {
                var craft = free[0];
                int target = ChooseExpansionTarget(w, map, craft);
                if (target >= 0 && Run(w, c, map, router, new GameCommand { kind = CommandKind.MoveWorld, actor = FactionId.Ai, craftId = craft.id, targetCellId = target }))
                {
                    ai.goal = AiGoal.Expand;
                    ai.targetCellId = target;
                }
            }
        }

        bool Run(WorldState w, ContentCatalog c, MapDefinition map, CommandRouter router, GameCommand cmd)
        {
            var r = router.Execute(w, c, map, cmd);
            LastAction = cmd.kind + (r.success ? " ok" : " rejected: " + r.code);
            return r.success;
        }

        static int PendingOrders(WorldState w)
        {
            int n = 0;
            foreach (var b in w.buildings) if (b.owner == FactionId.Ai && !string.IsNullOrEmpty(b.orderedCraftTypeId)) n++;
            return n;
        }

        static BuildingState IdleShipyard(WorldState w, ContentCatalog c)
        {
            foreach (var b in w.buildings)
            {
                if (b.owner != FactionId.Ai || !string.IsNullOrEmpty(b.orderedCraftTypeId) || !WorldLookup.IsBuildingActive(w, b)) continue;
                var def = CatalogRepository.GetBuilding(c, b.typeId);
                if (def != null && def.kind == BuildingKind.Shipyard) return b;
            }
            return null;
        }

        static List<CraftState> AllLive(WorldState w)
        {
            var list = new List<CraftState>();
            foreach (var craft in w.crafts) if (craft.owner == FactionId.Ai && WorldLookup.IsCraftLive(craft)) list.Add(craft);
            list.Sort((a, b) => a.id.CompareTo(b.id));
            return list;
        }

        /// <summary>Live AI craft that are not defending a cell, travelling, or holding a control point.</summary>
        static List<CraftState> FreeCraft(WorldState w)
        {
            var list = new List<CraftState>();
            foreach (var craft in AllLive(w))
            {
                if (craft.worldPathCellIds.Count > 0 || WorldLookup.IsDefender(w, craft)) continue;
                if (w.captureHolds.Exists(h => h.craftId == craft.id)) continue;
                var cell = WorldLookup.GetCell(w, craft.cellId);
                if (cell == null || cell.owner != FactionId.Ai) continue;
                list.Add(craft);
            }
            return list;
        }

        static bool AnyExpansionUnderway(WorldState w)
        {
            foreach (var craft in w.crafts)
                if (craft.owner == FactionId.Ai && craft.worldPathCellIds.Count > 0 && craft.id != w.ai.raidCraftId) return true;
            return false;
        }

        int ChooseExpansionTarget(WorldState w, MapDefinition map, CraftState craft)
        {
            int best = -1, bestScore = int.MaxValue;
            foreach (var cell in w.cells)
            {
                if (cell.owner != FactionId.Neutral || WorldLookup.HasOpposingCraft(w, cell.id, FactionId.Ai)) continue;
                var path = s.Movement.FindWorldPath(w, map, FactionId.Ai, craft.cellId, cell.id);
                if (path == null) continue;
                int nodes = 0;
                foreach (var n in w.nodes) if (n.cellId == cell.id) nodes++;
                int score = path.Count * 10 - nodes * 3;
                if (score < bestScore) { bestScore = score; best = cell.id; }
            }
            return best;
        }

        int ChooseRaidTarget(WorldState w, MapDefinition map, CraftState craft)
        {
            int best = -1, bestScore = int.MaxValue;
            foreach (var cell in w.cells)
            {
                if (cell.owner != FactionId.Player) continue;
                var path = s.Movement.FindStagingPath(w, map, FactionId.Ai, craft.cellId, cell.id);
                if (path == null) continue;
                bool defended = WorldLookup.HasOpposingCraft(w, cell.id, FactionId.Ai);
                int score = path.Count * 10 + (defended ? 5 : 0) + (cell.headquartersFor == FactionId.Player ? 8 : 0);
                if (score < bestScore) { bestScore = score; best = cell.id; }
            }
            return best;
        }

        /// <summary>Plays one AI tactical action (the game loop paces these so the player can follow).</summary>
        public bool PlayTacticalStep(WorldState w, CommandRouter router)
        {
            if (WorldLookup.IsSuppressed(w, FactionId.Ai)) return false;
            return s.AiTactics.Step(w, router);
        }

        public void PlayTacticalTurn(WorldState w, CommandRouter router)
        {
            if (WorldLookup.IsSuppressed(w, FactionId.Ai)) return;
            s.AiTactics.PlayTurn(w, router);
        }
    }
}
