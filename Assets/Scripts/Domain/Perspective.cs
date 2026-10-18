namespace OrbitalFoundry
{
    /// <summary>
    /// Whose eyes this machine's passing messages are written for: the battle log line and order replies, which are
    /// never saved and so may differ between machines. Alerts are saved and written for their recipient instead
    /// (<see cref="WorldLookup.Who"/>), so they read the same everywhere.
    /// </summary>
    public static class Perspective
    {
        /// <summary>The seat played by the person at this machine (while spectating: the seat the view follows).</summary>
        public static FactionId Viewer = FactionId.Player;

        /// <summary>An exhibition: every seat is an AI and the person here only watches. Nothing is "yours".</summary>
        public static bool Spectating;

        /// <summary>
        /// How the viewer refers to <paramref name="f"/>, starting a sentence: "You", "Your ally", "The AI", "The enemy";
        /// "Seat 3" while spectating.
        /// </summary>
        public static string WhoStart(WorldState w, FactionId f) { return Spectating ? SeatName(f) : WorldLookup.WhoStart(w, Viewer, f); }

        /// <summary>"Seat 3".</summary>
        public static string SeatName(FactionId f) { return "Seat " + (int)f; }

        /// <summary>Starts a thing's name by owner: "Your " or "Enemy "; "Seat 3 " while spectating.</summary>
        public static string OwnerWord(FactionId owner) { return Spectating ? SeatName(owner) + " " : owner == Viewer ? "Your " : "Enemy "; }
    }
}
