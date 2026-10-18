namespace OrbitalFoundry
{
    /// <summary>
    /// The AI's dice. Each roll is fixed by the match's saved seed, the seat and what it is for, so every machine in a
    /// lockstep game and every reload of a save rolls the same, while another match rolls differently. It keeps no
    /// state of its own. The AI must take its chance from here only: never UnityEngine.Random, the clock, or anything
    /// else that differs between machines.
    /// </summary>
    public static class AiRandom
    {
        /// <summary>0 to 1, fixed for this match, seat and purpose.</summary>
        public static float Unit(WorldState w, FactionId seat, int purpose)
        {
            uint h = (uint)w.randomSeed * 0x9E3779B1u ^ (uint)seat * 0x85EBCA77u ^ (uint)purpose * 0xC2B2AE3Du;
            h ^= h >> 16;
            h *= 0x7FEB352Du;
            h ^= h >> 15;
            h *= 0x846CA68Bu;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / 16777216f;
        }

        /// <summary>-1 to 1, fixed for this match, seat and purpose.</summary>
        public static float Spread(WorldState w, FactionId seat, int purpose)
        {
            return Unit(w, seat, purpose) * 2f - 1f;
        }

        // Purposes, so no two uses share a roll.
        public const int Aggression = 1, Risk = 2, FirstAttack = 3, AttackTie = 1000, ExpansionTie = 2000;
    }
}
