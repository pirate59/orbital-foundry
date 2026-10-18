namespace OrbitalFoundry
{
    /// <summary>
    /// Whose eyes this machine's passing messages are written for: the battle log line and order replies, which are
    /// never saved and so may differ between machines. Alerts are saved and written for their recipient instead
    /// (<see cref="WorldLookup.Who"/>), so they read the same everywhere.
    /// </summary>
    public static class Perspective
    {
        /// <summary>The seat played by the person at this machine.</summary>
        public static FactionId Viewer = FactionId.Player;

        /// <summary>How the viewer refers to <paramref name="f"/>, starting a sentence: "You", "Your ally", "The AI", "The enemy".</summary>
        public static string WhoStart(WorldState w, FactionId f) { return WorldLookup.WhoStart(w, Viewer, f); }
    }
}
