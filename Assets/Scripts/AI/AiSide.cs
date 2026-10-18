using System;

namespace OrbitalFoundry
{
    /// <summary>
    /// Which side the AI code is playing just now. The AI's skills, planner and tactics speak of "me" and "my
    /// opponent" through <see cref="Me"/> and <see cref="Foe"/> rather than naming a faction, so one set of AI code can
    /// play any seat: the game's AI plays <see cref="FactionId.Ai"/> (the default), other AI seats play theirs, and the
    /// headless arena runs a second AI as <see cref="FactionId.Player"/>. An <see cref="AIController"/> enters its side
    /// for each tick or battle turn and leaves it afterwards. The game runs on one thread, so this is a plain static.
    /// </summary>
    public static class AiSide
    {
        /// <summary>The faction the AI code is playing.</summary>
        public static FactionId Me { get; private set; } = FactionId.Ai;

        /// <summary>The faction it plays against.</summary>
        public static FactionId Foe { get { return WorldLookup.Opponent(Me); } }

        /// <summary>The side's planner when it is not the game's own (null: GameServices.AiPlanner).</summary>
        public static AIFactoryPlanner Planner { get; private set; }

        /// <summary>The side's profile when it was given one (null: the match difficulty's).</summary>
        public static AiProfile Profile { get; private set; }

        /// <summary>Prefix for this side's lines in <see cref="AiTiming"/> ("" for the game's AI).</summary>
        public static string TimingTag { get; private set; } = "";

        /// <summary>The side's planning state, saved with its seat.</summary>
        public static AiState StateOf(WorldState w)
        {
            return WorldLookup.AiStateOf(w, Me);
        }

        /// <summary>Plays as <paramref name="me"/> until the returned scope is disposed, then goes back to the side before.</summary>
        public static Scope Enter(FactionId me, AIFactoryPlanner planner, AiProfile profile, string timingTag)
        {
            var before = new Scope(Me, Planner, Profile, TimingTag);
            Me = me;
            Planner = planner;
            Profile = profile;
            TimingTag = timingTag ?? "";
            return before;
        }

        /// <summary>Restores the side that was being played before <see cref="Enter"/>.</summary>
        public struct Scope : IDisposable
        {
            readonly FactionId me;
            readonly AIFactoryPlanner planner;
            readonly AiProfile profile;
            readonly string tag;

            internal Scope(FactionId me, AIFactoryPlanner planner, AiProfile profile, string tag)
            {
                this.me = me; this.planner = planner; this.profile = profile; this.tag = tag;
            }

            public void Dispose()
            {
                Me = me;
                Planner = planner;
                Profile = profile;
                TimingTag = tag;
            }
        }
    }
}
