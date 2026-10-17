using System.Collections.Generic;

namespace OrbitalFoundry
{
    /// <summary>
    /// What the AI keeps in mind between thinks without saving it: scratch results, estimates and plans in progress.
    /// It is runtime only, so after a load the AI starts with an empty memory and works things out again from the
    /// world. Bound to one world at a time; playing a different world (a new game or a load) empties it.
    /// </summary>
    public sealed class AiMemory
    {
        WorldState world;
        readonly Dictionary<string, object> notes = new Dictionary<string, object>();

        /// <summary>The world this memory is about; a different one empties it.</summary>
        public void Bind(WorldState w)
        {
            if (world == w) return;
            world = w;
            notes.Clear();
        }

        /// <summary>Remembers a value under a key (replacing any earlier one).</summary>
        public void Set<T>(string key, T value) { notes[key] = value; }

        /// <summary>A remembered value, or <paramref name="fallback"/> when there is none of that type.</summary>
        public T Get<T>(string key, T fallback = default(T))
        {
            object v;
            return notes.TryGetValue(key, out v) && v is T ? (T)v : fallback;
        }

        public bool Forget(string key) { return notes.Remove(key); }

        public int Count { get { return notes.Count; } }
    }
}
