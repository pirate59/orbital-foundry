namespace OrbitalFoundry
{
    /// <summary>
    /// Whatever plays a side through commands: an AI, or a stream of recorded or networked commands. The loop calls it
    /// once in each tick's thinking half, battle turns included, so anything it issues lands at the same point of play
    /// on every run.
    /// </summary>
    public interface ISideController
    {
        /// <summary>The side's move in a tick's thinking half, its battle orders included.</summary>
        void Tick(WorldState w, ContentCatalog c, MapDefinition map, CommandRouter router);

    }
}
