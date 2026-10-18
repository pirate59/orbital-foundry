using System;
using System.Collections.Generic;
using UnityEngine;

namespace OrbitalFoundry
{
    /// <summary>
    /// One command a side issued, and when: the side's <see cref="slot"/> (the how-many-th time the loop called on that
    /// side) and the tick it fell in. Replaying the same commands in the same slots reproduces the side's play exactly,
    /// which is all lockstep multiplayer sends over the wire.
    /// </summary>
    [Serializable]
    public sealed class JournalEntry
    {
        public int slot;
        public int tick;
        public GameCommand command;
    }

    /// <summary>A world hash at a tick, for finding the first point two runs of one match part ways.</summary>
    [Serializable]
    public sealed class WorldCheckpoint
    {
        public int tick;
        public string hash;
    }

    /// <summary>A stable fingerprint of the whole saved world, equal on every machine that played the same match.</summary>
    public static class WorldHash
    {
        public static string Of(WorldState w) { return Of(w, FactionId.Neutral); }

        /// <summary>
        /// The fingerprint, leaving out the AI planning state of <paramref name="standIn"/>: a seat a test plays with an AI
        /// standing in for a person. A person's seat has no AI state, so a replay of its commands alone leaves it empty.
        /// </summary>
        public static string Of(WorldState w, FactionId standIn)
        {
            var seat = standIn == FactionId.Neutral ? null : WorldLookup.GetFaction(w, standIn);
            var kept = seat != null ? seat.ai : null;
            if (seat != null) seat.ai = new AiState();
            // FNV-1a over the world's save form: every field the game saves, in a fixed order.
            string json;
            try { json = JsonUtility.ToJson(w); }
            finally { if (seat != null) seat.ai = kept; }
            ulong h = 14695981039346656037UL;
            for (int i = 0; i < json.Length; i++)
            {
                h ^= json[i];
                h *= 1099511628211UL;
            }
            return h.ToString("x16");
        }
    }

    /// <summary>
    /// Plays a side with another controller (an AI standing in for a person) and writes down every command it issues
    /// into <see cref="Entries"/>, so the side can later be replayed by <see cref="ReplaySide"/>.
    /// </summary>
    public sealed class RecordingSide : ISideController
    {
        readonly ISideController inner;
        public readonly List<JournalEntry> Entries = new List<JournalEntry>();
        int slot;

        public RecordingSide(ISideController inner) { this.inner = inner; }

        public void Tick(WorldState w, ContentCatalog c, MapDefinition map, CommandRouter router)
        {
            Record(w, router, () => inner.Tick(w, c, map, router));
        }

        public void PlayTacticalTurn(WorldState w, CommandRouter router)
        {
            Record(w, router, () => inner.PlayTacticalTurn(w, router));
        }

        void Record(WorldState w, CommandRouter router, Action play)
        {
            slot++;
            int now = slot;
            Action<GameCommand, CommandResult> note = (cmd, r) => Entries.Add(new JournalEntry { slot = now, tick = w.tick, command = Copy(cmd) });
            router.Executed += note;
            try { play(); }
            finally { router.Executed -= note; }
        }

        /// <summary>A copy, so later changes to the issuer's command object cannot rewrite history.</summary>
        public static GameCommand Copy(GameCommand cmd)
        {
            return JsonUtility.FromJson<GameCommand>(JsonUtility.ToJson(cmd));
        }
    }

    /// <summary>
    /// Plays a side from a journal alone, with no AI behind it: in each slot it issues exactly the commands recorded
    /// for that slot. A slot that falls on a different tick from the recording means play has already parted ways;
    /// the first one is kept in <see cref="FirstMisplacedSlot"/>.
    /// </summary>
    public sealed class ReplaySide : ISideController
    {
        readonly ContentCatalog catalog;
        readonly MapDefinition map;
        readonly Dictionary<int, List<JournalEntry>> bySlot = new Dictionary<int, List<JournalEntry>>();
        int slot;

        /// <summary>The first slot whose tick did not match the recording, or -1.</summary>
        public int FirstMisplacedSlot = -1;

        public ReplaySide(IEnumerable<JournalEntry> entries, ContentCatalog catalog, MapDefinition map)
        {
            this.catalog = catalog;
            this.map = map;
            foreach (var e in entries)
            {
                List<JournalEntry> list;
                if (!bySlot.TryGetValue(e.slot, out list)) bySlot[e.slot] = list = new List<JournalEntry>();
                list.Add(e);
            }
        }

        public void Tick(WorldState w, ContentCatalog c, MapDefinition map, CommandRouter router) { Play(w, router); }

        public void PlayTacticalTurn(WorldState w, CommandRouter router) { Play(w, router); }

        void Play(WorldState w, CommandRouter router)
        {
            slot++;
            List<JournalEntry> list;
            if (!bySlot.TryGetValue(slot, out list)) return;
            foreach (var e in list)
            {
                if (e.tick != w.tick && FirstMisplacedSlot < 0) FirstMisplacedSlot = slot;
                router.Execute(w, catalog, map, RecordingSide.Copy(e.command));
            }
        }
    }
}
