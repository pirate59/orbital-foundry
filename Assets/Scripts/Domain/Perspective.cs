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

        /// <summary>
        /// The battle the screen shows: the one the selected ship fights in; else one where it is the viewer's turn (it
        /// comes first, so a turn never burns away unseen); else the battle the viewer chose to watch; else any battle the
        /// viewer is in (a spectator: the first battle). Null when there is none to show.
        /// </summary>
        public static EncounterState FocusBattle(WorldState w, int selectedCraftId, int watchBattleId = -1)
        {
            if (w == null) return null;
            var selected = WorldLookup.BattleOf(w, selectedCraftId);
            if (selected != null) return selected;
            foreach (var e in w.encounters) if (e.active && !Spectating && e.turn == Viewer) return e;
            var watched = watchBattleId >= 0 ? WorldLookup.GetBattle(w, watchBattleId) : null;
            if (watched != null) return watched;
            foreach (var e in w.encounters) if (e.active && (Spectating || e.sides.Contains(Viewer))) return e;
            return null;
        }

        /// <summary>Whether the viewer fights in a battle (or is a spectator, who sees every battle).</summary>
        public static bool InBattle(EncounterState e) { return e != null && (Spectating || e.sides.Contains(Viewer)); }

        /// <summary>"Seat 3".</summary>
        public static string SeatName(FactionId f) { return "Seat " + (int)f; }

        /// <summary>Starts a thing's name by owner: "Your " or "Enemy "; "Seat 3 " while spectating.</summary>
        public static string OwnerWord(FactionId owner) { return Spectating ? SeatName(owner) + " " : owner == Viewer ? "Your " : "Enemy "; }
    }
}
