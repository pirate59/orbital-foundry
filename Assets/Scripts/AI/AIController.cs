using System.Collections.Generic;

namespace OrbitalFoundry
{
    /// <summary>
    /// Normal-play AI: executes at most one queued factory command every <see cref="CommandIntervalTicks"/> ticks,
    /// asks the planner for the next step otherwise, and makes expansion, purchase and raid decisions with real
    /// AI currency. All orders go through CommandRouter.
    /// </summary>
    public sealed class AIController : ISideController
    {
        public const int CommandIntervalTicks = 10;
        static readonly string[] CraftRotation = { "bomber", "guard", "fortress", "bomber", "guard" };
        // A defensive AI builds holders, not raiders.
        static readonly string[] DefensiveRotation = { "guard", "fortress" };

        /// <summary>After the planner finds nothing to build, it looks again this many ticks later rather than every tick.</summary>
        public const int IdleReplanTicks = 10;

        readonly GameServices s;
        public string LastAction = "";

        /// <summary>
        /// The AI's runtime memory: anything it learns or works out while playing that need not survive a save (it is
        /// rebuilt from the world after a load). Kept out of WorldState so a deeper AI can remember a lot without
        /// growing saves. Cleared when a different world is played.
        /// </summary>
        public readonly AiMemory Memory = new AiMemory();

        /// <summary>The AI's skills: situation, stance, budget and targets, which the steps below act on.</summary>
        public readonly AiBrain Brain = new AiBrain();
        // Runtime only: when an idle planner may look again. A loaded game simply plans on its first tick.
        int idleUntilTick = -1;
        WorldState idleWorld;

        /// <summary>The seat this controller plays (the game's AI plays FactionId.Ai; other AI seats and the arena's side A play theirs).</summary>
        public readonly FactionId Me;
        /// <summary>Its own profile, or null to play the match difficulty's preset.</summary>
        public AiProfile Profile;
        /// <summary>Prefix for its lines in AiTiming, so two AIs in one match are timed apart.</summary>
        public string TimingTag = "";
        readonly AIFactoryPlanner ownPlanner;
        readonly AITactics tactics;

        /// <summary>The game's AI: plays FactionId.Ai with the shared planner and tactics.</summary>
        public AIController(GameServices services) : this(services, FactionId.Ai, services.AiPlanner, services.AiTactics) { }

        /// <summary>An AI for any seat, with its own planner and tactics. Its planning state is saved with the seat.</summary>
        public AIController(GameServices services, FactionId me, AIFactoryPlanner planner, AITactics tactics)
        {
            s = services;
            Me = me;
            ownPlanner = planner;
            this.tactics = tactics;
        }

        /// <summary>The planner this AI plans with.</summary>
        public AIFactoryPlanner Planner { get { return ownPlanner; } }

        AiSide.Scope Enter()
        {
            return AiSide.Enter(Me, ownPlanner == s.AiPlanner ? null : ownPlanner, Profile, TimingTag);
        }

        public void Tick(WorldState w, ContentCatalog c, MapDefinition map, CommandRouter router, AIFactoryPlanner planner)
        {
            using (Enter()) TickAsSide(w, c, map, router, planner ?? ownPlanner);
        }

        /// <summary>Ticks with this controller's own planner.</summary>
        public void Tick(WorldState w, ContentCatalog c, MapDefinition map, CommandRouter router)
        {
            Tick(w, c, map, router, ownPlanner);
        }

        void TickAsSide(WorldState w, ContentCatalog c, MapDefinition map, CommandRouter router, AIFactoryPlanner planner)
        {
            if (w.mode != MatchMode.Factory || WorldLookup.IsSuppressed(w, AiSide.Me)) return;
            Memory.Bind(w);
            var ai = AiSide.StateOf(w);
            var faction = WorldLookup.GetFaction(w, AiSide.Me);
            if (faction == null) return;
            PlayBattles(w, c, router);
            if (IsThinkTick(w, c))
            {
                Brain.Think(w, c, map, s, Memory);
                var decided = Brain.Decisions;
                planner.ResearchAllowed = decided.ResearchAllowed;
                planner.WorkScale = Brain.Profile.WorkScale;
                planner.ProductOrder.Clear();
                planner.ProductOrder.AddRange(decided.ProductOrder);
                planner.ResearchTarget = decided.ResearchTarget;
                planner.PowerFirst = decided.PowerFirst;
                planner.PowerMarginMw = decided.PowerMarginMw;
                planner.IonWanted = decided.IonWanted;
                planner.Saturated.Clear();
                planner.Saturated.UnionWith(decided.Saturated);
                planner.NewChainCell = decided.NewChainCell;
                planner.SmeltCells.Clear();
                planner.SmeltCells.UnionWith(decided.SmeltCells);
                planner.HoldNewMachines = decided.HoldNewMachines;
                planner.ResearchQueueLimit = Brain.Profile.ResearchQueue;
                // Delivery priorities (Solar supply chain): free and instant, like the player's priority buttons.
                foreach (var order in decided.PriorityCommands) router.Execute(w, c, map, order);
                decided.PriorityCommands.Clear();
                foreach (var order in decided.InstantCommands) router.Execute(w, c, map, order);
                decided.InstantCommands.Clear();
                // A queuing belt line: upgrade it (Belt upgrades), between plans.
                if (decided.UpgradeBeltId >= 0 && ai.queuedCommands.Count == 0)
                {
                    long beltStart = AiTiming.Begin();
                    var up = router.Execute(w, c, map, new GameCommand { kind = CommandKind.UpgradeBelt, actor = AiSide.Me, beltId = decided.UpgradeBeltId, contentId = "line" });
                    AiTiming.End("Carry out queued orders", beltStart);
                    if (up.success) LastAction = "Upgraded a belt line.";
                    decided.UpgradeBeltId = -1;
                }
                // A better layout or an outlet for stuck goods: queue it between plans.
                if (ai.queuedCommands.Count == 0 && decided.OutletCommands.Count > 0)
                {
                    ai.queuedCommands.AddRange(decided.OutletCommands);
                    decided.OutletCommands.Clear();
                    LastAction = "Adding a sale outlet.";
                }
                else if (ai.queuedCommands.Count == 0 && decided.SupplyCommands.Count > 0)
                {
                    ai.queuedCommands.AddRange(decided.SupplyCommands);
                    decided.SupplyCommands.Clear();
                    LastAction = "Moving a refinery out to its ore.";
                }
                else if (ai.queuedCommands.Count == 0 && decided.HubCommands.Count > 0)
                {
                    ai.queuedCommands.AddRange(decided.HubCommands);
                    decided.HubCommands.Clear();
                    LastAction = "Adding a Storage hub.";
                }
                else if (ai.queuedCommands.Count == 0 && decided.RefactorCommands.Count > 0)
                {
                    ai.queuedCommands.AddRange(decided.RefactorCommands);
                    decided.RefactorCommands.Clear();
                    LastAction = "Refactoring a belt line.";
                }
                else if (ai.queuedCommands.Count == 0 && decided.RelayoutCommands.Count > 0)
                {
                    ai.queuedCommands.AddRange(decided.RelayoutCommands);
                    decided.RelayoutCommands.Clear();
                    LastAction = "Clearing out unused belts and machines.";
                }
                // A machine stuck for a minute: replan (never in the middle of carrying out a plan).
                if (decided.ReplanNow && ai.queuedCommands.Count == 0) planner.Reconsider(w, "bottleneck: " + decided.ReplanReason);
            }

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
                long orderStart = AiTiming.Begin();
                var r = router.Execute(w, c, map, cmd);
                AiTiming.End("Carry out queued orders", orderStart);
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

            // Planning and military decisions happen on think ticks only (a few times a second), each within the
            // planner's work budget; queued orders above still go out on their own beat.
            if (!IsThinkTick(w, c)) return;
            if (idleWorld != w) { idleWorld = w; idleUntilTick = -1; }
            if (w.tick >= idleUntilTick)
            {
                long planStart = AiTiming.Begin();
                var plan = planner.PlanNext(w, c, map, s.AiBelts, s.Belts);
                AiTiming.End("Factory planner (chains, layout, belts, power, Forts, research)", planStart);
                if (plan.success)
                {
                    ai.goal = AiGoal.BuildIncome;
                    ai.targetItemId = plan.productItemId;
                    ai.queuedCommands.AddRange(plan.commands);
                    return;
                }
                if (plan.reason == AIFactoryPlanner.Searching) return;
                idleUntilTick = w.tick + IdleReplanTicks;
            }

            if (w.tick < ai.nextPlanningTick) return;
            ai.nextPlanningTick = w.tick + CommandIntervalTicks;
            if (c.rules.aiExpands || c.rules.aiRaids || c.rules.aiDefends)
            {
                long militaryStart = AiTiming.Begin();
                MilitaryStep(w, c, map, router, faction);
                AiTiming.End("Military step (repairs, raids, garrison, ships, expansion)", militaryStart);
            }
        }

        /// <summary>
        /// Whether the AI thinks this tick: every rules.aiThinkIntervalTicks ticks, on the world clock, offset by its seat
        /// so several AIs do not all plan on the same tick (their heaviest work would land in one frame).
        /// </summary>
        public static bool IsThinkTick(WorldState w, ContentCatalog c)
        {
            int every = System.Math.Max(1, c.rules.aiThinkIntervalTicks);
            return (w.tick + (int)AiSide.Me) % every == 0;
        }

        static bool ResolvePlaceholders(WorldState w, GameCommand cmd)
        {
            if (cmd.kind != CommandKind.SelectRecipe || cmd.buildingId >= 0) return true;
            var b = WorldLookup.GetBuildingAt(w, cmd.fromCellId, cmd.fromX, cmd.fromY);
            if (b == null || b.owner != AiSide.Me) return false;
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
                case CommandKind.Research: return 0; // the planner checked the faction's next level before queueing it
                case CommandKind.BuyCraft: { var d = CatalogRepository.GetCraft(c, cmd.contentId); return d != null ? d.cost : 0; }
                default: return 0;
            }
        }

        // ---------------------------------------------------------------- military

        /// <summary>Below this share of its HP an AI craft goes home to a shipyard for repairs, if it has one.</summary>
        const float RepairBelowShare = 0.6f;

        /// <summary>Whether an AI craft is too damaged to send out, with a shipyard able to repair it.</summary>
        public static bool WantsRepair(WorldState w, ContentCatalog c, CraftState craft)
        {
            var def = CatalogRepository.GetCraft(c, craft.typeId);
            if (def == null || def.repairParts.Count == 0 || craft.currentHealth >= def.maxHealth * RepairBelowShare) return false;
            return ShipyardCell(w, c, craft.cellId) >= 0;
        }

        /// <summary>A cell with a working AI shipyard (the given one if it has one, else any), or -1.</summary>
        static int ShipyardCell(WorldState w, ContentCatalog c, int prefer)
        {
            int any = -1;
            foreach (var b in w.buildings)
            {
                if (b.owner != AiSide.Me || !WorldLookup.IsBuildingActive(w, b)) continue;
                var def = CatalogRepository.GetBuilding(c, b.typeId);
                if (def == null || def.kind != BuildingKind.Shipyard) continue;
                if (b.cellId == prefer) return prefer;
                if (any < 0) any = b.cellId;
            }
            return any;
        }

        /// <summary>
        /// Sends one badly damaged AI craft that is free to leave home to the nearest cell with an AI shipyard, where
        /// it waits to be repaired. True when an order was given.
        /// </summary>
        bool RepairStep(WorldState w, ContentCatalog c, MapDefinition map, CommandRouter router, List<CraftState> mayLeave)
        {
            foreach (var craft in mayLeave)
            {
                if (craft.id == AiSide.StateOf(w).raidCraftId || !WantsRepair(w, c, craft)) continue;
                int best = FactorySimulation.NearestRepairYard(w, c, map, s.Movement, craft);
                if (best == craft.cellId) best = -1; // already docked: stay and heal
                if (best >= 0 && Run(w, c, map, router, new GameCommand { kind = CommandKind.MoveWorld, actor = AiSide.Me, craftId = craft.id, targetCellId = best }))
                {
                    LastAction = "Sending a damaged craft home for repairs.";
                    return true;
                }
            }
            return false;
        }

        void MilitaryStep(WorldState w, ContentCatalog c, MapDefinition map, CommandRouter router, FactionState faction)
        {
            var ai = AiSide.StateOf(w);
            var mayLeave = FreeCraft(w, c);
            // The headquarters is never left empty: a ship comes home before anything else.
            if (HeadquartersGuardStep(w, c, map, router)) return;
            // Damaged craft head home for repairs first; until healed they are not sent raiding, expanding or garrisoning.
            if (RepairStep(w, c, map, router, mayLeave)) return;
            var free = mayLeave.FindAll(k => !WantsRepair(w, c, k));

            // Claim the cell a craft is standing in when it is clear.
            if (c.rules.aiExpands)
            {
                foreach (var craft in AllLive(w))
                {
                    if (craft.worldPathCellIds.Count > 0) continue;
                    var cell = WorldLookup.GetCell(w, craft.cellId);
                    if (cell.owner == AiSide.Me || WorldLookup.HasOpposingCraft(w, cell.id, AiSide.Me)) continue;
                    if (w.captureHolds.Exists(h => h.craftId == craft.id || (h.cellId == cell.id && h.faction == AiSide.Me))) continue;
                    if (Run(w, c, map, router, new GameCommand { kind = CommandKind.StartCapture, actor = AiSide.Me, craftId = craft.id, targetCellId = cell.id })) return;
                }
            }

            // Strike: the brain's attack fleet gathers next to its target, then attacks together. Ships in the
            // strike are kept out of garrison and expansion duty.
            var plan = Brain.Decisions;
            free = free.FindAll(k => !plan.StrikeGroup.Contains(k.id));
            if (c.rules.aiRaids && plan.StrikeGroup.Count > 0 && plan.RallyCell >= 0 && ai.raidCraftId == -1 && StrikeStep(w, c, map, router, plan)) return;

            // Garrison: send a spare craft to the nearest cell the AI holds that has no defender.
            if (c.rules.aiDefends && GarrisonStep(w, c, map, router)) return;

            // Order craft at an idle shipyard once income is flowing, keeping a reserve. The shipyard still has to
            // receive the craft's components from the AI's factory before anything launches. A purely defensive AI
            // stops at one craft per cell it holds.
            bool military = c.rules.aiExpands || c.rules.aiRaids;
            int wanted = plan.MaxCraft;
            if (AllLive(w).Count + PendingOrders(w) < wanted)
            {
                var yard = IdleShipyard(w, c);
                // The brain's build order (fleet composition), else the old rotation; the first whose parts it can make.
                CraftDefinition def = null;
                string type = "";
                if (plan.BuildOrder.Count > 0)
                {
                    foreach (var choice in plan.BuildOrder)
                    {
                        var candidate = CatalogRepository.GetCraft(c, choice);
                        if (candidate != null && ownPlanner.CanSupply(w, c, faction, candidate)) { def = candidate; type = choice; break; }
                    }
                }
                else
                {
                    var rotation = military ? CraftRotation : DefensiveRotation;
                    int start = AllLive(w).Count + PendingOrders(w);
                    for (int k = 0; k < rotation.Length && def == null; k++)
                    {
                        type = rotation[(start + k) % rotation.Length];
                        var candidate = CatalogRepository.GetCraft(c, type);
                        if (ownPlanner.CanSupply(w, c, faction, candidate)) def = candidate;
                    }
                }
                if (yard != null && def != null && faction.currency >= def.cost + plan.CraftReserve
                    && Run(w, c, map, router, new GameCommand { kind = CommandKind.BuyCraft, actor = AiSide.Me, contentId = type, buildingId = yard.id }))
                    return;
            }

            // Expand: send a spare craft to the cell the brain picked (the nearest clear one if it cannot get there),
            // when its pacing says so and fewer claims than it allows are under way.
            if (c.rules.aiExpands && free.Count > 0 && plan.ExpandNow && ExpansionsUnderway(w, plan) < plan.MaxExpansions)
            {
                var craft = free[0];
                int target = plan.ExpansionCell;
                bool sent = target >= 0 && Run(w, c, map, router, new GameCommand { kind = CommandKind.MoveWorld, actor = AiSide.Me, craftId = craft.id, targetCellId = target });
                if (!sent)
                {
                    target = ChooseExpansionTarget(w, map, craft);
                    sent = target >= 0 && Run(w, c, map, router, new GameCommand { kind = CommandKind.MoveWorld, actor = AiSide.Me, craftId = craft.id, targetCellId = target });
                }
                if (sent)
                {
                    ai.goal = AiGoal.Expand;
                    ai.targetCellId = target;
                }
            }
        }

        static int OwnedCells(WorldState w)
        {
            int n = 0;
            foreach (var cell in w.cells) if (cell.owner == AiSide.Me) n++;
            return n;
        }

        /// <summary>
        /// Moves one spare AI craft (live, idle, not a cell's defender, not raiding) to the nearest AI cell whose
        /// defender slot is empty and which no AI craft is already heading to. Arrival makes it the defender.
        /// </summary>
        bool GarrisonStep(WorldState w, ContentCatalog c, MapDefinition map, CommandRouter router)
        {
            var spare = new List<CraftState>();
            foreach (var craft in AllLive(w))
            {
                if (craft.worldPathCellIds.Count > 0 || craft.id == AiSide.StateOf(w).raidCraftId || WorldLookup.IsDefender(w, craft)) continue;
                if (w.captureHolds.Exists(h => h.craftId == craft.id)) continue;
                if (WantsRepair(w, c, craft)) continue; // waiting at the shipyard to be healed
                var here = WorldLookup.GetCell(w, craft.cellId);
                if (here == null || here.owner != AiSide.Me) continue;
                spare.Add(craft);
            }
            if (spare.Count == 0) return false;
            // The brain's guard order (most worth guarding first), else the cells in id order.
            var order = new List<CellState>();
            foreach (int id in Brain.Decisions.GuardOrder)
            {
                var guarded = WorldLookup.GetCell(w, id);
                if (guarded != null && guarded.owner == AiSide.Me) order.Add(guarded);
            }
            if (order.Count == 0) order.AddRange(w.cells);
            // A spare ship already standing in an undefended cell holds it where it is, before any ship is moved.
            foreach (var cell in order)
            {
                if (cell.owner != AiSide.Me) continue;
                var holder = WorldLookup.GetCraft(w, cell.defenderCraftId);
                if (holder != null && WorldLookup.IsCraftLive(holder) && holder.cellId == cell.id) continue;
                var here = spare.Find(k => k.cellId == cell.id);
                if (here != null && Guard(w, c, map, router, here, cell)) return true;
            }
            foreach (var cell in order)
            {
                if (cell.owner != AiSide.Me) continue;
                var current = WorldLookup.GetCraft(w, cell.defenderCraftId);
                if (current != null && WorldLookup.IsCraftLive(current) && current.cellId == cell.id) continue;
                if (w.crafts.Exists(k => k.owner == AiSide.Me && k.worldPathCellIds.Count > 0 && k.worldPathCellIds[k.worldPathCellIds.Count - 1] == cell.id)) continue;
                CraftState best = null;
                int bestLength = int.MaxValue;
                foreach (var craft in spare)
                {
                    if (craft.cellId == cell.id && Guard(w, c, map, router, craft, cell)) return true;
                    var path = s.Movement.FindWorldPath(w, map, AiSide.Me, craft.cellId, cell.id);
                    if (path != null && path.Count < bestLength) { best = craft; bestLength = path.Count; }
                }
                if (best != null && Run(w, c, map, router, new GameCommand { kind = CommandKind.MoveWorld, actor = AiSide.Me, craftId = best.id, targetCellId = cell.id }))
                {
                    AiSide.StateOf(w).goal = AiGoal.Defend;
                    AiSide.StateOf(w).targetCellId = cell.id;
                    return true;
                }
            }
            // Every cell has its defender: while defending, a spare ship joins the threatened cell as a reserve.
            int reserve = Brain.Decisions.ReserveCell;
            if (reserve >= 0 && !spare.Exists(k => k.cellId == reserve)
                && !w.crafts.Exists(k => k.owner == AiSide.Me && k.worldPathCellIds.Count > 0 && k.worldPathCellIds[k.worldPathCellIds.Count - 1] == reserve))
            {
                foreach (var craft in spare)
                    if (Run(w, c, map, router, new GameCommand { kind = CommandKind.MoveWorld, actor = AiSide.Me, craftId = craft.id, targetCellId = reserve }))
                    {
                        AiSide.StateOf(w).goal = AiGoal.Defend;
                        AiSide.StateOf(w).targetCellId = reserve;
                        LastAction = "Reserve to threatened cell " + reserve + ".";
                        return true;
                    }
            }
            return false;
        }

        /// <summary>
        /// Makes a ship already in the cell its defender. Through the router like every other order, so it is journalled
        /// and a peer replaying this seat's commands sees the same defender.
        /// </summary>
        bool Guard(WorldState w, ContentCatalog c, MapDefinition map, CommandRouter router, CraftState craft, CellState cell)
        {
            return Run(w, c, map, router, new GameCommand { kind = CommandKind.AssignDefender, actor = AiSide.Me, craftId = craft.id, targetCellId = cell.id });
        }

        bool Run(WorldState w, ContentCatalog c, MapDefinition map, CommandRouter router, GameCommand cmd)
        {
            if (LeavesHeadquartersBare(w, cmd))
            {
                LastAction = cmd.kind + " held back: it would leave the headquarters unguarded";
                return false;
            }
            var r = router.Execute(w, c, map, cmd);
            LastAction = cmd.kind + (r.success ? " ok" : " rejected: " + r.code);
            return r.success;
        }

        /// <summary>The cell holding this side's headquarters, or -1.</summary>
        static int HeadquartersCell(WorldState w)
        {
            foreach (var cell in w.cells)
                if (cell.headquartersFor == AiSide.Me && cell.owner == AiSide.Me) return cell.id;
            return -1;
        }

        /// <summary>Its live ships standing in a cell (not travelling).</summary>
        static int ShipsStandingIn(WorldState w, int cellId)
        {
            int n = 0;
            foreach (var k in w.crafts)
                if (k.owner == AiSide.Me && WorldLookup.IsCraftLive(k) && k.cellId == cellId && k.worldPathCellIds.Count == 0) n++;
            return n;
        }

        /// <summary>Whether this ship is the last one standing in its side's headquarters cell (and so stays there).</summary>
        public static bool IsLastHeadquartersGuard(WorldState w, CraftState craft)
        {
            int hq = HeadquartersCell(w);
            return craft != null && hq >= 0 && craft.cellId == hq && craft.worldPathCellIds.Count == 0 && ShipsStandingIn(w, hq) <= 1;
        }

        /// <summary>
        /// Whether an order would send away the last ship standing in its headquarters cell. An empty headquarters falls
        /// to the first enemy ship that reaches it, without a battle, so one ship always stays (Headquarters guard).
        /// </summary>
        static bool LeavesHeadquartersBare(WorldState w, GameCommand cmd)
        {
            if (cmd.craftId < 0 || (cmd.kind != CommandKind.MoveWorld && cmd.kind != CommandKind.StartAttack && cmd.kind != CommandKind.StartCapture)) return false;
            var craft = WorldLookup.GetCraft(w, cmd.craftId);
            int hq = HeadquartersCell(w);
            if (craft == null || hq < 0 || craft.cellId != hq || craft.worldPathCellIds.Count > 0) return false;
            if (cmd.kind != CommandKind.StartAttack && cmd.targetCellId == hq) return false; // staying put
            return ShipsStandingIn(w, hq) <= 1;
        }

        /// <summary>
        /// Headquarters guard: with no ship of its in the headquarters cell and none on the way, the nearest one comes
        /// home, before any other military order (strike ships included). True when it gave the order.
        /// </summary>
        bool HeadquartersGuardStep(WorldState w, ContentCatalog c, MapDefinition map, CommandRouter router)
        {
            int hq = HeadquartersCell(w);
            if (hq < 0 || ShipsStandingIn(w, hq) > 0) return false;
            foreach (var k in w.crafts)
                if (k.owner == AiSide.Me && WorldLookup.IsCraftLive(k) && k.worldPathCellIds.Count > 0 && k.worldDestinationCellId == hq) return false;
            CraftState best = null;
            int bestLength = int.MaxValue;
            foreach (var craft in AllLive(w))
            {
                if (craft.worldPathCellIds.Count > 0 || w.captureHolds.Exists(h => h.craftId == craft.id)) continue;
                // A ship that alone holds a cell of its with no Fort (the defender, or the only ship there) stays put:
                // taking it away would lose that cell instead.
                var home = WorldLookup.GetCell(w, craft.cellId);
                if (home != null && home.owner == AiSide.Me && craft.cellId != hq && !OwnershipService.HasOwnAnchor(w, c, home.id, AiSide.Me)
                    && (WorldLookup.IsDefender(w, craft) || ShipsStandingIn(w, craft.cellId) <= 1)) continue;
                var path = s.Movement.FindWorldPath(w, map, AiSide.Me, craft.cellId, hq);
                if (path != null && path.Count < bestLength) { best = craft; bestLength = path.Count; }
            }
            if (best == null) return false;
            var r = router.Execute(w, c, map, new GameCommand { kind = CommandKind.MoveWorld, actor = AiSide.Me, craftId = best.id, targetCellId = hq });
            LastAction = r.success ? "Bringing a ship home to guard the headquarters." : "MoveWorld rejected: " + r.code;
            if (r.success) Brain.Decisions.StrikeGroup.Remove(best.id);
            return r.success;
        }

        static int PendingOrders(WorldState w)
        {
            int n = 0;
            foreach (var b in w.buildings) if (b.owner == AiSide.Me && !string.IsNullOrEmpty(b.orderedCraftTypeId)) n++;
            return n;
        }

        static BuildingState IdleShipyard(WorldState w, ContentCatalog c)
        {
            foreach (var b in w.buildings)
            {
                if (b.owner != AiSide.Me || !string.IsNullOrEmpty(b.orderedCraftTypeId) || !WorldLookup.IsBuildingActive(w, b)) continue;
                var def = CatalogRepository.GetBuilding(c, b.typeId);
                if (def != null && def.kind == BuildingKind.Shipyard) return b;
            }
            return null;
        }

        static List<CraftState> AllLive(WorldState w)
        {
            var list = new List<CraftState>();
            foreach (var craft in w.crafts) if (craft.owner == AiSide.Me && WorldLookup.IsCraftLive(craft)) list.Add(craft);
            list.Sort((a, b) => a.id.CompareTo(b.id));
            return list;
        }

        /// <summary>
        /// Live AI craft that may leave: not travelling or holding a point, and either not a defender or defending a
        /// non-HQ cell that a Fort already holds.
        /// </summary>
        public static List<CraftState> FreeCraft(WorldState w, ContentCatalog c)
        {
            var list = new List<CraftState>();
            foreach (var craft in AllLive(w))
            {
                if (craft.worldPathCellIds.Count > 0) continue;
                if (WorldLookup.IsDefender(w, craft))
                {
                    var home = WorldLookup.GetCell(w, craft.cellId);
                    bool fortified = home.headquartersFor == FactionId.Neutral && OwnershipService.HasOwnAnchor(w, c, home.id, AiSide.Me);
                    if (!fortified) continue;
                }
                if (w.captureHolds.Exists(h => h.craftId == craft.id)) continue;
                var cell = WorldLookup.GetCell(w, craft.cellId);
                if (cell == null || cell.owner != AiSide.Me) continue;
                list.Add(craft);
            }
            return list;
        }

        /// <summary>AI craft travelling that are not raiding or gathering for a strike: claims under way.</summary>
        static int ExpansionsUnderway(WorldState w, AiDecisions plan)
        {
            int n = 0;
            foreach (var craft in w.crafts)
                if (craft.owner == AiSide.Me && craft.worldPathCellIds.Count > 0 && craft.id != AiSide.StateOf(w).raidCraftId && !plan.StrikeGroup.Contains(craft.id)) n++;
            return n;
        }

        /// <summary>
        /// Gathers the strike group at its rally cell, then attacks: one ship's attack order opens the battle, and
        /// every ship next to the target fights in it. True when it gave an order.
        /// </summary>
        bool StrikeStep(WorldState w, ContentCatalog c, MapDefinition map, CommandRouter router, AiDecisions plan)
        {
            if (plan.LaunchNow)
            {
                // Any ship of the group may open the attack: the first that may leave.
                CraftState lead = null;
                foreach (int id in plan.StrikeGroup)
                {
                    var k = WorldLookup.GetCraft(w, id);
                    if (k != null && !IsLastHeadquartersGuard(w, k)) { lead = k; break; }
                }
                if (lead != null && Run(w, c, map, router, new GameCommand { kind = CommandKind.StartAttack, actor = AiSide.Me, craftId = lead.id, targetCellId = plan.AttackCell }))
                {
                    AiSide.StateOf(w).goal = AiGoal.Raid;
                    // A bolder profile strikes more often: from the full interval down to half of it.
                    AiSide.StateOf(w).nextRaidTick = w.tick + (int)(c.rules.raidIntervalTicks * (1.25f - Brain.Profile.Aggression));
                    LastAction = "Strike on cell " + plan.AttackCell + " with " + plan.StrikeGroup.Count + " ships.";
                    return true;
                }
                return false;
            }
            if (plan.StrikeAssembled) return false; // gathered: waiting for the moment
            bool ordered = false;
            foreach (int id in plan.StrikeGroup)
            {
                var k = WorldLookup.GetCraft(w, id);
                if (k == null || k.cellId == plan.RallyCell || k.worldPathCellIds.Count > 0) continue;
                if (Run(w, c, map, router, new GameCommand { kind = CommandKind.MoveWorld, actor = AiSide.Me, craftId = k.id, targetCellId = plan.RallyCell })) ordered = true;
            }
            if (ordered) LastAction = "Gathering " + plan.StrikeGroup.Count + " ships in cell " + plan.RallyCell + ".";
            return ordered;
        }

        int ChooseExpansionTarget(WorldState w, MapDefinition map, CraftState craft)
        {
            int best = -1, bestScore = int.MaxValue;
            foreach (var cell in w.cells)
            {
                if (cell.owner != FactionId.Neutral || WorldLookup.HasOpposingCraft(w, cell.id, AiSide.Me)) continue;
                if (!ExpansionTargetSkill.CanFortify(w, s.Catalog, map, cell.id)) continue; // a claim it could never hold
                var path = s.Movement.FindWorldPath(w, map, AiSide.Me, craft.cellId, cell.id);
                if (path == null) continue;
                int nodes = 0;
                foreach (var n in w.nodes) if (n.cellId == cell.id) nodes++;
                int score = path.Count * 10 - nodes * 3;
                if (score < bestScore) { bestScore = score; best = cell.id; }
            }
            return best;
        }

        /// <summary>
        /// Its moves in every battle where it is its turn: one action each rules.aiBattleStepTicks ticks counted from the
        /// turn's start, so people can follow them (0: the whole turn at once). Part of its tick and kept in nothing but
        /// the turn clock, so every machine (and a replay) agrees when it moves.
        /// </summary>
        void PlayBattles(WorldState w, ContentCatalog c, CommandRouter router)
        {
            int step = c.rules.aiBattleStepTicks;
            for (int i = 0; i < w.encounters.Count; i++)
            {
                var e = w.encounters[i];
                if (!e.active || e.turn != AiSide.Me) continue;
                int since = w.tick - e.turnStartedTick;
                if (step > 0 && (since <= 0 || since % step != 0)) continue;
                long start = AiTiming.Begin();
                if (step <= 0) tactics.PlayTurn(w, router, e);
                else tactics.Step(w, router, e);
                AiTiming.End("Battle tactics", start);
            }
        }

        /// <summary>Plays its whole turn in a battle at once (tests and tools).</summary>
        public void PlayTacticalTurn(WorldState w, CommandRouter router, EncounterState e)
        {
            using (Enter())
            {
                if (WorldLookup.IsSuppressed(w, AiSide.Me)) return;
                long start = AiTiming.Begin();
                tactics.PlayTurn(w, router, e);
                AiTiming.End("Battle tactics", start);
            }
        }
    }
}
