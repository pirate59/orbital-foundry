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
            Services = new GameServices(catalog, map);
            Services.Belts.Rebuild(world);
            accumulatorMs = 0;
            aiStepTimer = 0;
            saveDueIn = -1f;
            ticksSinceAutosave = 0;
            Revision++;
        }

        /// <summary>Submits a player command through the shared router and queues a save on success.</summary>
        public CommandResult Submit(GameCommand cmd)
        {
            if (World == null) return WorldLookup.Fail("NOT_READY", "The game is not loaded.");
            var r = Services.Commands.Execute(World, Catalog, Map, cmd);
            LastCommandResult = r;
            if (r.success)
            {
                Revision++;
                QueueSave();
            }
            return r;
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
                    accumulatorMs += deltaSeconds * 1000.0;
                    int ticks = 0;
                    while (accumulatorMs >= rules.tickMilliseconds && ticks < rules.maxCatchUpTicksPerFrame && World.mode == MatchMode.Factory)
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
            s.Ownership.ExpireInactiveStructures(w, Catalog);
            s.Belts.EnsureCurrent(w);
            s.Factory.Tick(w, Catalog, Map, s.Belts, s.Pull, s.Economy);
            s.Capture.TickNormalHolds(w, Catalog.rules, s.Ownership, Catalog);
            if (w.mode == MatchMode.Factory) s.Ai.Tick(w, Catalog, Map, s.Commands, s.AiPlanner);
            if (w.mode == MatchMode.Factory) s.Raids.TickWorldTravel(w, Catalog, Map, s.Encounters);
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
