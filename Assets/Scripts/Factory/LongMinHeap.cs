using System.Collections.Generic;

namespace OrbitalFoundry
{
    /// <summary>
    /// A reusable min-heap of longs for route searches: the smallest key pops first. Unlike a SortedSet it allocates
    /// nothing per insert once it has grown, and it keeps duplicates (a search skips a vertex it has already settled).
    /// </summary>
    public sealed class LongMinHeap
    {
        readonly List<long> items = new List<long>();

        public int Count { get { return items.Count; } }

        public void Clear() { items.Clear(); }

        public void Push(long key)
        {
            items.Add(key);
            int i = items.Count - 1;
            while (i > 0)
            {
                int parent = (i - 1) / 2;
                if (items[parent] <= key) break;
                items[i] = items[parent];
                i = parent;
            }
            items[i] = key;
        }

        public long Pop()
        {
            long top = items[0];
            long last = items[items.Count - 1];
            items.RemoveAt(items.Count - 1);
            int n = items.Count;
            if (n == 0) return top;
            int i = 0;
            while (true)
            {
                int child = 2 * i + 1;
                if (child >= n) break;
                if (child + 1 < n && items[child + 1] < items[child]) child++;
                if (items[child] >= last) break;
                items[i] = items[child];
                i = child;
            }
            items[i] = last;
            return top;
        }
    }
}
