namespace OrbitalFoundry
{
    /// <summary>
    /// Whatever plays a side through commands: an AI, or a stream of recorded or networked commands. The loop calls it
    /// at fixed points (once in each tick's thinking half, and on that side's battle turns), so anything it issues
    /// lands at the same point of play on every run.
    /// </summary>
    public interface ISideController
    {
        /// <summary>The side's move in a normal tick's thinking half.</summary>
        void Tick(WorldState w, ContentCatalog c, MapDefinition map, CommandRouter router);

        /// <summary>The side's whole battle turn.</summary>
        void PlayTacticalTurn(WorldState w, CommandRouter router);
    }
}
