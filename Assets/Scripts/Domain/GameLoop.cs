using System;

namespace OrbitalFoundry
{
    /// <summary>Composition root for the plain C# rule services. Construct once per loaded catalog and map.</summary>
    public sealed class GameServices
    {
        public readonly ContentCatalog Catalog;
        public readonly MapDefinition Map;
        public readonly BeltNetwork Belts = new BeltNetwork();
        public readonly PullRouter Pull = new PullRouter();
        public readonly FactorySimulation Factory = new FactorySimulation();
        public readonly OwnershipService Ownership = new OwnershipService();
        public readonly EconomyService Economy;
        public readonly CaptureService Capture = new CaptureService();
        public readonly CombatResolver Combat = new CombatResolver();
        public readonly MovementRules Movement = new MovementRules();
        public readonly EncounterService Encounters;
        public readonly RaidService Raids;
        public readonly CommandRouter Commands;
        public readonly AIBeltPlanner AiBelts = new AIBeltPlanner();
        public readonly AIFactoryPlanner AiPlanner = new AIFactoryPlanner();
        public readonly AITactics AiTactics;
        public readonly AIController Ai;

        public GameServices(ContentCatalog catalog, MapDefinition map)
        {
            Catalog = catalog;
            Map = map;
            Economy = new EconomyService(Ownership);
            Encounters = new EncounterService(catalog, map, Combat, Movement, Capture, Ownership);
            Raids = new RaidService(Movement, Capture);
            Belts.Map = map;
            Belts.Catalog = catalog;
            Commands = new CommandRouter(this);
            AiTactics = new AITactics(this);
            Ai = new AIController(this);
        }
    }

    /// <summary>
    /// Fixed-step orchestration. Real frame time accumulates into 100 ms normal ticks (capped per frame, excess
    /// discarded so a suspended tab adds no factory time). Tactical mode never advances the tick.
    /// </summary>
    public sealed class GameLoop
    {
        public GameServices Services { get; private set; }
        public WorldState World { get; private set; }
        public ContentCatalog Catalog { get; private set; }
        public MapDefinition Map { get; private set; }
        public SaveService Saves { get; private set; }

        /// <summary>Increments whenever world state changes; the view refreshes only when it moves.</summary>
        public int Revision { get; private set; }
        public CommandResult LastCommandResult { get; private set; }
        public CommandResult LastSaveResult { get; private set; }

        /// <summary>Fraction of the next normal tick already accumulated, for smooth interpolation in the view.</summary>
        public float TickFraction
        {
            get { return Catalog == null ? 0f : UnityEngine.Mathf.Clamp01((float)(accumulatorMs / Catalog.rules.tickMilliseconds)); }
        }

        /// <summary>Factory clock multiplier (1, 2 or 4). Tactical turns are unaffected.</summary>
        public int SpeedMultiplier = 1;

        /// <summary>Raised after every player command, accepted or not, for presentation feedback such as sound.</summary>
        public event Action<GameCommand, CommandResult> CommandSubmitted;

        /// <summary>Seconds between AI tactical actions so the player can follow them.</summary>
        public float AiTacticalStepSeconds = 0.6f;
        /// <summary>Play the AI's whole battle turn at once instead of a move at a time.</summary>
        public bool AiTurnInstant;

        /// <summary>What plays each seat that is not a person here: its AI, or a replayed or networked command stream.</summary>
        readonly System.Collections.Generic.Dictionary<FactionId, ISideController> controllers = new System.Collections.Generic.Dictionary<FactionId, ISideController>();

        double accumulatorMs;
        float aiStepTimer;
        /// <summary>Seconds of play since the last autosave.</summary>
        float sinceAutosave;
        /// <summary>The world has changed since it was last saved.</summary>
        bool unsaved;
        /// <summary>
        /// The live loop's last tick has run its factory half but not yet its AI and raids half, which waits for the next
        /// frame so the two never share one frame's budget. Always finished before the next tick or a save.
        /// </summary>
        bool thinkingDue;
        bool inUpdate;

        public void Initialize(ContentCatalog catalog, MapDefinition map, WorldState world, SaveService saves)
        {
            Catalog = catalog;
            Map = map;
            World = world;
            Saves = saves;
            bool expanded = ExpandLegacyShipyards(true);
            expanded |= DroneLinks.EnsureReturnLegs(world); // links saved before they flew both ways
            Services = new GameServices(catalog, map);
            Services.Belts.Rebuild(world);
            // Every AI seat gets its AI, on every machine; seats played by people get none.
            controllers.Clear();
            foreach (var f in world.factions)
            {
                if (!f.aiControlled) continue;
                controllers[f.id] = f.id == FactionId.Ai ? Services.Ai
                    : new AIController(Services, f.id, new AIFactoryPlanner(), new AITactics(Services));
            }
            accumulatorMs = 0;
            aiStepTimer = 0;
            unsaved = expanded;
            sinceAutosave = 0f;
            thinkingDue = false;
            Revision++;
        }

        /// <summary>Submits a player command through the shared router; a success is saved with the next autosave.</summary>
        public CommandResult Submit(GameCommand cmd)
        {
            if (World == null) return WorldLookup.Fail("NOT_READY", "The game is not loaded.");
            var r = Services.Commands.Execute(World, Catalog, Map, cmd);
            LastCommandResult = r;
            if (CommandSubmitted != null) CommandSubmitted(cmd, r);
            if (r.success)
            {
                // A legacy yard surrounded by belts can expand as soon as the player clears a neighbour.
                ExpandLegacyShipyards(false);
                Revision++;
                unsaved = true;
            }
            return r;
        }

        bool ExpandLegacyShipyards(bool alertBlocked)
        {
            bool expanded = false;
            foreach (var yard in World.buildings)
            {
                if (yard.typeId != "shipyard" || yard.extendedFootprint) continue;
                for (int turn = 0; turn < 4; turn++)
                    if (GridRules.CanReserveShipyardExtra(World, Catalog, Map, yard.cellId, yard.x, yard.y, turn).success)
                    {
                        yard.rotation = turn;
                        yard.extendedFootprint = true;
                        expanded = true;
                        break;
                    }
                if (alertBlocked && !yard.extendedFootprint)
                    WorldLookup.AddAlert(World, AlertKind.Info,
                        "The existing Shipyard in cell " + yard.cellId + " needs a clear space beside it to expand. Clear belts and structures next to it.", yard.cellId, yard.owner);
            }
            return expanded;
        }

        /// <summary>Saves now (the menu's Save, losing focus, pausing and quitting), finishing any half-run tick first.</summary>
        public CommandResult FlushSave()
        {
            if (World != null) FinishTick();
            unsaved = false;
            sinceAutosave = 0f;
            if (Saves == null || World == null) return null;
            LastSaveResult = Saves.Save(World, Catalog);
            if (!LastSaveResult.success && LastSaveResult.code != "SAVE_BLOCKED")
            {
                // Avoid flooding alerts on repeated failures.
                var last = World.alerts.Count > 0 ? World.alerts[World.alerts.Count - 1] : null;
                // A failed save is this machine's problem alone. Lockstep (phase 6) must move this out of the shared world.
                if (last == null || last.kind != AlertKind.SaveError) WorldLookup.AddAlert(World, AlertKind.SaveError, LastSaveResult.message, -1, FactionId.Neutral);
            }
            Revision++;
            return LastSaveResult;
        }

        public void Update(float deltaSeconds)
        {
            if (World == null || inUpdate) return;
            if (float.IsNaN(deltaSeconds) || float.IsInfinity(deltaSeconds) || deltaSeconds < 0f) deltaSeconds = 0f;
            inUpdate = true;
            try
            {
                var rules = Catalog.rules;
                if (World.mode == MatchMode.Factory)
                {
                    int speed = Math.Max(1, SpeedMultiplier);
                    accumulatorMs += deltaSeconds * 1000.0 * speed;
                    int ticks = 0;
                    // Last frame's tick finishes its thinking now, on a frame of its own when no tick is due.
                    FinishTick();
                    while (accumulatorMs >= rules.tickMilliseconds && ticks < rules.maxCatchUpTicksPerFrame * speed && World.mode == MatchMode.Factory)
                    {
                        accumulatorMs -= rules.tickMilliseconds;
                        FinishTick();
                        TickFactoryHalf();
                        thinkingDue = true;
                        ticks++;
                    }
                    // Discard backlog (e.g. after tab suspension): no offline progress.
                    if (accumulatorMs >= rules.tickMilliseconds) accumulatorMs = 0;
                }
                else
                {
                    accumulatorMs = 0;
                    FinishTick(); // the tick that left Factory mode still finishes its outcome check
                }

                if (World.mode == MatchMode.Tactical) UpdateTactical(deltaSeconds);

                // Autosave on a fixed real-time beat, whatever the game speed, rather than after every change.
                sinceAutosave += deltaSeconds;
                if (sinceAutosave >= rules.autosaveSeconds)
                {
                    sinceAutosave = 0f;
                    if (unsaved) FlushSave();
                }
            }
            finally
            {
                inUpdate = false;
            }
        }

        void UpdateTactical(float deltaSeconds)
        {
            var e = World.encounter;
            var side = e.active ? ControllerFor(e.turn) : null;
            if (side == null) { aiStepTimer = 0; return; } // a person's turn: wait for their orders
            var ai = side as AIController;
            if (ai == null || AiTurnInstant)
            {
                side.PlayTacticalTurn(World, Services.Commands);
                Revision++;
                unsaved = true;
                return;
            }
            // An AI plays a move at a time, so people can follow it.
            aiStepTimer += deltaSeconds;
            if (aiStepTimer < AiTacticalStepSeconds) return;
            aiStepTimer = 0;
            ai.PlayTacticalStep(World, Services.Commands);
            Revision++;
            unsaved = true;
        }

        /// <summary>One normal tick in deterministic order, both halves at once. Does nothing outside Factory mode.</summary>
        public void TickNormal()
        {
            if (World == null || World.mode != MatchMode.Factory) return;
            FinishTick();
            TickFactoryHalf();
            TickThinkingHalf();
        }

        /// <summary>Runs the thinking half of a tick whose factory half has run, if one is waiting.</summary>
        void FinishTick()
        {
            if (!thinkingDue) return;
            thinkingDue = false;
            TickThinkingHalf();
        }

        /// <summary>The first half of a tick: expiry, the factory, capture holds and upkeep.</summary>
        void TickFactoryHalf()
        {
            var s = Services;
            var w = World;
            w.tick++;
            Perf.Tick();
            long t = Perf.Begin();
            s.Ownership.ExpireInactiveStructures(w, Catalog);
            s.Belts.EnsureCurrent(w);
            Perf.End(Perf.Stage.Expire, t);
            s.Factory.Tick(w, Catalog, Map, s.Belts, s.Pull, s.Economy);
            ResearchService.Tick(w, Catalog);
            t = Perf.Begin();
            s.Capture.TickNormalHolds(w, Catalog.rules, s.Ownership, Catalog);
            if (w.mode == MatchMode.Factory) s.Ownership.TickUpkeep(w, Catalog);
            Perf.End(Perf.Stage.Capture, t);
            Revision++;
            unsaved = true;
        }

        /// <summary>
        /// The second half of a tick: the AI's turn, raids, the raid warning and the outcome check. Runs before the next
        /// tick's first half, so the order of play matches running both halves together.
        /// </summary>
        void TickThinkingHalf()
        {
            var s = Services;
            var w = World;
            long t = Perf.Begin();
            // Each seat's controller moves in seat order.
            foreach (var f in w.factions)
            {
                if (w.mode != MatchMode.Factory) break;
                var side = ControllerFor(f.id);
                if (side != null) side.Tick(w, Catalog, Map, s.Commands);
            }
            Perf.End(Perf.Stage.Ai, t);
            t = Perf.Begin();
            if (w.mode == MatchMode.Factory) s.Raids.TickWorldTravel(w, Catalog, Map, s.Encounters);
            Perf.End(Perf.Stage.Raids, t);
            s.Raids.RaiseWarningIfDue(w, Catalog.rules);
            s.Ownership.EvaluateOutcome(w, Catalog.rules);
            Revision++;
        }

        /// <summary>Plays the battle turn of the seat whose turn it is, if a controller plays it (headless/test helper).</summary>
        public void RunAiTurnImmediately()
        {
            if (World == null || World.mode != MatchMode.Tactical) return;
            var side = ControllerFor(World.encounter.turn);
            if (side == null) return;
            side.PlayTacticalTurn(World, Services.Commands);
            Revision++;
        }

        /// <summary>What plays <paramref name="seat"/> when no person does here, or null.</summary>
        public ISideController ControllerFor(FactionId seat)
        {
            ISideController side;
            return controllers.TryGetValue(seat, out side) ? side : null;
        }

        /// <summary>
        /// Hands <paramref name="seat"/> to <paramref name="side"/> (an AI, or a replayed or networked command stream), or
        /// to a person when null. It moves in seat order each tick and plays the seat's battle turns.
        /// </summary>
        public void SetController(FactionId seat, ISideController side)
        {
            if (side == null) controllers.Remove(seat);
            else controllers[seat] = side;
        }

        /// <summary>
        /// What plays seat 1 without a person: the arena's second AI, or a replayed command stream. Null in the game.
        /// </summary>
        public ISideController PlayerSide
        {
            get { return ControllerFor(FactionId.Player); }
            set { SetController(FactionId.Player, value); }
        }
    }
}
