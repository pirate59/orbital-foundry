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

        /// <summary>What plays each seat that is not a person here: its AI, or a replayed or networked command stream.</summary>
        readonly System.Collections.Generic.Dictionary<FactionId, ISideController> controllers = new System.Collections.Generic.Dictionary<FactionId, ISideController>();

        /// <summary>
        /// Lockstep multiplayer, when on: people's commands go out to every machine and run a few ticks later, and a
        /// tick waits for every person's inputs. Null in a game played on this machine alone.
        /// </summary>
        public LockstepSession Lockstep { get; private set; }

        /// <summary>
        /// The page is out of view (a browser tab in the background, throttled to about a frame a second). Set by the
        /// presentation; online it lets this machine send its inputs further ahead and catch up harder.
        /// </summary>
        public bool PageHidden;

        /// <summary>How far ahead (ticks) a page out of view sends its inputs: over the second a hidden tab waits between frames.</summary>
        public const int HiddenLeadTicks = 30;

        /// <summary>The game speed every machine in a lockstep match shares (0 paused), set by the relay.</summary>
        public int SharedSpeed = 1;

        /// <summary>The person a lockstep tick is waiting for (Neutral when none, or not in lockstep).</summary>
        public FactionId WaitingFor { get; private set; }

        double accumulatorMs;
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
            Lockstep = null; // a new world starts alone; UseLockstep joins it to other machines
            WaitingFor = FactionId.Neutral;
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
            unsaved = expanded;
            sinceAutosave = 0f;
            thinkingDue = false;
            Revision++;
        }

        /// <summary>Submits a player command through the shared router; a success is saved with the next autosave.</summary>
        public CommandResult Submit(GameCommand cmd)
        {
            if (World == null) return WorldLookup.Fail("NOT_READY", "The game is not loaded.");
            if (Perspective.Spectating) return WorldLookup.Fail("SPECTATING", "You are watching an exhibition: every seat is played by an AI.");
            // In lockstep the command goes to every machine and runs a few ticks from now; its result comes later.
            if (Lockstep != null) return Lockstep.Submit(cmd);
            var r = Services.Commands.Execute(World, Catalog, Map, cmd);
            LastCommandResult = r;
            if (CommandSubmitted != null) CommandSubmitted(cmd, r);
            if (r.success)
            {
                // A legacy yard surrounded by belts can expand as soon as the player clears a neighbour.
                if (Lockstep == null) ExpandLegacyShipyards(false);
                Revision++;
                unsaved = true;
            }
            return r;
        }

        /// <summary>
        /// Plays this match in lockstep with other machines through <paramref name="session"/> (made for this loop, after
        /// Initialize). The game speed is <see cref="SharedSpeed"/>, the same for every machine (the relay passes it on).
        /// </summary>
        public void UseLockstep(LockstepSession session)
        {
            Lockstep = session;
            SpeedMultiplier = 1;
            SharedSpeed = 1;
            session.LocalCommandRan += (cmd, r) =>
            {
                LastCommandResult = r;
                if (CommandSubmitted != null) CommandSubmitted(cmd, r);
                if (r.success) { Revision++; unsaved = true; }
            };
        }

        /// <summary>Whether the next tick may run: always alone; in lockstep once every person's inputs are in.</summary>
        bool NextTickReady()
        {
            if (Lockstep == null) return true;
            WaitingFor = Lockstep.ReadyFor(World.tick + 1) ? FactionId.Neutral : Lockstep.Missing(World.tick + 1);
            return WaitingFor == FactionId.Neutral;
        }

        /// <summary>
        /// One whole tick if it may run (in lockstep, once every person's inputs are in): tests and tools that step
        /// machines by hand. False when it is still waiting.
        /// </summary>
        public bool TryTickNormal()
        {
            if (World == null || World.mode != MatchMode.Factory) return false;
            if (Lockstep != null) Lockstep.Pump();
            if (!NextTickReady()) return false;
            TickNormal();
            return true;
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
                if (Lockstep == null && (last == null || last.kind != AlertKind.SaveError)) WorldLookup.AddAlert(World, AlertKind.SaveError, LastSaveResult.message, -1, FactionId.Neutral);
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
                    int speed = Lockstep != null ? Math.Max(0, SharedSpeed) : Math.Max(1, SpeedMultiplier);
                    if (Lockstep != null)
                    {
                        Lockstep.Pump();
                        // Out of view, send inputs well ahead, so the machines in view never wait on this one.
                        Lockstep.ExtraLead = PageHidden ? HiddenLeadTicks : 0;
                        NextTickReady(); // sends this frame's inputs, whether or not a tick is due
                        // Behind the others (a returning player, or a page out of view): catch up as fast as a frame
                        // allows (nobody is watching a hidden page, so it may take longer), then play on.
                        if (Lockstep.Behind > Lockstep.InputDelay * 4)
                        {
                            var clock = System.Diagnostics.Stopwatch.StartNew();
                            int budget = PageHidden ? 250 : 25;
                            while (clock.ElapsedMilliseconds < budget && World.mode == MatchMode.Factory && NextTickReady()) TickNormal();
                            accumulatorMs = 0;
                        }
                    }
                    accumulatorMs += deltaSeconds * 1000.0 * speed;
                    int ticks = 0;
                    // Last frame's tick finishes its thinking now, on a frame of its own when no tick is due.
                    FinishTick();
                    while (accumulatorMs >= rules.tickMilliseconds && ticks < rules.maxCatchUpTicksPerFrame * speed && World.mode == MatchMode.Factory
                        && NextTickReady())
                    {
                        accumulatorMs -= rules.tickMilliseconds;
                        FinishTick();
                        TickFactoryHalf();
                        thinkingDue = true;
                        ticks++;
                    }
                    // Discard backlog (e.g. after tab suspension): no offline progress. Waiting on another machine keeps
                    // one tick in hand, so play resumes the moment its inputs arrive.
                    if (WaitingFor != FactionId.Neutral) accumulatorMs = Math.Min(accumulatorMs, rules.tickMilliseconds);
                    else if (accumulatorMs >= rules.tickMilliseconds) accumulatorMs = 0;
                }
                else
                {
                    accumulatorMs = 0;
                    FinishTick(); // the tick that left Factory mode still finishes its outcome check
                }

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
            // Battle turns that ran out of time are ended for their seats.
            if (w.mode == MatchMode.Factory) s.Encounters.TickBattles(w);
            t = Perf.Begin();
            if (w.mode == MatchMode.Factory) s.Raids.TickWorldTravel(w, Catalog, Map, s.Encounters);
            Perf.End(Perf.Stage.Raids, t);
            s.Raids.RaiseWarningIfDue(w, Catalog.rules);
            s.Ownership.EvaluateOutcome(w, Catalog.rules);
            if (Lockstep != null) Lockstep.AfterTick(w.tick);
            Revision++;
        }

        /// <summary>
        /// Plays out every battle turn an AI holds, whole, until each battle waits on a person or ends (tests and tools;
        /// in play the AIs take their turns a move at a time as part of their tick).
        /// </summary>
        public void RunAiTurnImmediately()
        {
            if (World == null) return;
            for (int guard = 0; guard < 64; guard++)
            {
                EncounterState next = null;
                foreach (var e in World.encounters)
                    if (e.active && ControllerFor(e.turn) is AIController) { next = e; break; }
                if (next == null) break;
                ((AIController)ControllerFor(next.turn)).PlayTacticalTurn(World, Services.Commands, next);
            }
            World.encounters.RemoveAll(x => !x.active);
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
        /// to a person when null. It moves in seat order each tick, battle turns included.
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
