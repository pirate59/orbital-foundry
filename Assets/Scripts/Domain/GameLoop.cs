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

        const float SaveCoalesceSeconds = 1f;

        double accumulatorMs;
        float aiStepTimer;
        float saveDueIn = -1f;
        int ticksSinceAutosave;
        bool inUpdate;

        public void Initialize(ContentCatalog catalog, MapDefinition map, WorldState world, SaveService saves)
        {
            Catalog = catalog;
            Map = map;
            World = world;
            Saves = saves;
            bool expanded = ExpandLegacyShipyards(true);
            Services = new GameServices(catalog, map);
            Services.Belts.Rebuild(world);
            accumulatorMs = 0;
            aiStepTimer = 0;
            saveDueIn = expanded ? SaveCoalesceSeconds : -1f;
            ticksSinceAutosave = 0;
            Revision++;
        }

        /// <summary>Submits a player command through the shared router and queues a save on success.</summary>
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
                QueueSave();
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
                        "The existing Shipyard in cell " + yard.cellId + " needs one adjacent tile free of belts and structures to expand. Clear a tile beside it.", yard.cellId);
            }
            return expanded;
        }

        public void QueueSave()
        {
            if (saveDueIn < 0f) saveDueIn = SaveCoalesceSeconds;
        }

        public CommandResult FlushSave()
        {
            saveDueIn = -1f;
            if (Saves == null || World == null) return null;
            LastSaveResult = Saves.Save(World, Catalog);
            if (!LastSaveResult.success && LastSaveResult.code != "SAVE_BLOCKED")
            {
                // Avoid flooding alerts on repeated failures.
                var last = World.alerts.Count > 0 ? World.alerts[World.alerts.Count - 1] : null;
                if (last == null || last.kind != AlertKind.SaveError) WorldLookup.AddAlert(World, AlertKind.SaveError, LastSaveResult.message, -1);
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
                    while (accumulatorMs >= rules.tickMilliseconds && ticks < rules.maxCatchUpTicksPerFrame * speed && World.mode == MatchMode.Factory)
                    {
                        accumulatorMs -= rules.tickMilliseconds;
                        TickNormal();
                        ticks++;
                    }
                    // Discard backlog (e.g. after tab suspension): no offline progress.
                    if (accumulatorMs >= rules.tickMilliseconds) accumulatorMs = 0;
                }
                else
                {
                    accumulatorMs = 0;
                }

                if (World.mode == MatchMode.Tactical) UpdateTactical(deltaSeconds);

                if (saveDueIn >= 0f)
                {
                    saveDueIn -= deltaSeconds;
                    if (saveDueIn <= 0f) FlushSave();
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
            if (!e.active || e.turn != FactionId.Ai) { aiStepTimer = 0; return; }
            aiStepTimer += deltaSeconds;
            if (aiStepTimer < AiTacticalStepSeconds) return;
            aiStepTimer = 0;
            Services.Ai.PlayTacticalStep(World, Services.Commands);
            Revision++;
            QueueSave();
        }

        /// <summary>One normal tick in deterministic order. Does nothing outside Factory mode.</summary>
        public void TickNormal()
        {
            if (World == null || World.mode != MatchMode.Factory) return;
            var s = Services;
            var w = World;
            w.tick++;
            Perf.Tick();
            long t = Perf.Begin();
            s.Ownership.ExpireInactiveStructures(w, Catalog);
            s.Belts.EnsureCurrent(w);
            Perf.End(Perf.Stage.Expire, t);
            s.Factory.Tick(w, Catalog, Map, s.Belts, s.Pull, s.Economy);
            t = Perf.Begin();
            s.Capture.TickNormalHolds(w, Catalog.rules, s.Ownership, Catalog);
            if (w.mode == MatchMode.Factory) s.Ownership.TickUpkeep(w, Catalog);
            Perf.End(Perf.Stage.Capture, t);
            t = Perf.Begin();
            if (w.mode == MatchMode.Factory) s.Ai.Tick(w, Catalog, Map, s.Commands, s.AiPlanner);
            Perf.End(Perf.Stage.Ai, t);
            t = Perf.Begin();
            if (w.mode == MatchMode.Factory) s.Raids.TickWorldTravel(w, Catalog, Map, s.Encounters);
            Perf.End(Perf.Stage.Raids, t);
            s.Raids.RaiseWarningIfDue(w, Catalog.rules);
            s.Ownership.EvaluateOutcome(w, Catalog.rules);
            Revision++;

            ticksSinceAutosave++;
            if (ticksSinceAutosave >= Catalog.rules.autosaveEveryTicks)
            {
                ticksSinceAutosave = 0;
                QueueSave();
            }
        }

        /// <summary>Runs AI tactical turns to completion (headless/test helper).</summary>
        public void RunAiTurnImmediately()
        {
            if (World != null && World.mode == MatchMode.Tactical && World.encounter.turn == FactionId.Ai)
            {
                Services.Ai.PlayTacticalTurn(World, Services.Commands);
                Revision++;
            }
        }
    }
}
