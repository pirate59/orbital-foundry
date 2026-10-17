using System.Collections.Generic;

namespace OrbitalFoundry
{
    /// <summary>The part of the game a skill plays (matches the AI skills inventory).</summary>
    public enum AiSkillArea { Strategy, Economy, Logistics, Research, Territory, Military, Tactics }

    /// <summary>
    /// One AI skill: a small, self-contained piece of judgement that reads the situation and writes decisions for the
    /// rest of the AI to act on. Skills run in order on the AI's think ticks, each on its own cadence, and never issue
    /// orders themselves; the controller and planners carry decisions out through the CommandRouter, like a player.
    /// A skill must be deterministic: the same world gives the same decisions on any device.
    /// </summary>
    public interface IAiSkill
    {
        string Name { get; }
        AiSkillArea Area { get; }
        /// <summary>How many thinks between runs (1 = every think). May depend on the profile.</summary>
        int EveryThinks(AiContext ctx);
        void Think(AiContext ctx);
        /// <summary>What it decided last time, in a few words, for the debug readout.</summary>
        string Status { get; }
    }

    /// <summary>
    /// A skill that also runs when something happens (research finished, a shipyard went idle), not only on its
    /// timer: the brain asks every think, and a true answer runs it at once.
    /// </summary>
    public interface IAiEventSkill
    {
        bool Triggered(AiContext ctx);
    }

    /// <summary>What a skill gets to work with: the world and rules, the AI's profile, its picture of the match, and the shared decisions.</summary>
    public sealed class AiContext
    {
        public WorldState World;
        public ContentCatalog Catalog;
        public MapDefinition Map;
        public GameServices Services;
        public FactionState Faction;
        public FactionState Player;
        public AiProfile Profile;
        public AiWorldModel Model;
        public AiDecisions Decisions;
        public AiMemory Memory;

        // Cell-to-cell distances for this map, worked out once (see Distance).
        internal MapDefinition DistanceMap;
        internal Dictionary<(int, int), int> Distances = new Dictionary<(int, int), int>();

        /// <summary>Thinks in a span of game time: cadences are written in seconds, whatever the think interval.</summary>
        public int Every(float seconds)
        {
            float thinkSeconds = System.Math.Max(1, Catalog.rules.aiThinkIntervalTicks) * Catalog.rules.tickMilliseconds / 1000f;
            float scale = Profile != null && Profile.CadenceScale > 0f ? Profile.CadenceScale : 1f;
            return System.Math.Max(1, (int)System.Math.Round(seconds * scale / thinkSeconds));
        }

        /// <summary>Cells between two cells (as WorldLookup.CellDistance), looked up rather than searched each time.</summary>
        public int Distance(int from, int to)
        {
            int d;
            if (!Distances.TryGetValue((from, to), out d)) Distances[(from, to)] = d = WorldLookup.CellDistance(Map, from, to);
            return d;
        }
    }
}
