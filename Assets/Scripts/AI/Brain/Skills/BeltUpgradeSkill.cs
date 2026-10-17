using System.Collections.Generic;

namespace OrbitalFoundry
{
    /// <summary>
    /// Belt Upgrades: finds the AI's belt lines where items keep queuing for the belt itself (stuck partway along a
    /// route, not waiting at the end for a slow machine) and upgrades the whole line a tier, as the player can, when it
    /// can spare the credits. Each belt keeps a count that rises while an item waits on it and
    /// falls while none does, so a line that is busy most of the time qualifies even though its queue comes and goes.
    /// Medium acts after half a minute of queuing, Hard after ten seconds; Easy never upgrades belts.
    /// </summary>
    public sealed class BeltUpgradeSkill : IAiSkill
    {
        public string Name { get { return "Belt upgrades"; } }
        public AiSkillArea Area { get { return AiSkillArea.Logistics; } }
        public string Status { get; private set; }

        // How long each belt has been queuing, net: up while an item waits on it, down while none does.
        readonly Dictionary<int, int> queuedFor = new Dictionary<int, int>();
        readonly HashSet<int> waitingNow = new HashSet<int>();
        readonly List<int> cleared = new List<int>();

        public int EveryThinks(AiContext ctx) { return ctx.Profile.AssessEveryThinks; }

        public void Think(AiContext ctx)
        {
            var w = ctx.World;
            var c = ctx.Catalog;
            var d = ctx.Decisions;
            var p = ctx.Profile;
            d.UpgradeBeltId = -1;
            if (p.Level < 1) { d.BeltReason = "not upgrading belts"; Status = d.BeltReason; return; }

            waitingNow.Clear();
            foreach (var t in w.tokens)
            {
                // Only an item stuck partway, waiting for the next belt, is held up by belt speed. One waiting at the
                // end of its route is waiting for a slow machine to take it, and a faster belt would not help.
                if (t.status != TokenStatus.Waiting || t.segmentIndex < 0 || t.segmentIndex >= t.routeSegmentIds.Count - 1) continue;
                var belt = WorldLookup.GetBelt(w, t.routeSegmentIds[t.segmentIndex]);
                if (belt != null && belt.owner == AiSide.Me && belt.fromCellId == belt.toCellId) waitingNow.Add(belt.id);
            }
            cleared.Clear();
            foreach (var id in queuedFor.Keys) if (!waitingNow.Contains(id)) cleared.Add(id);
            foreach (var id in cleared)
            {
                int left = queuedFor[id] - 1;
                if (left <= 0 || WorldLookup.GetBelt(w, id) == null) queuedFor.Remove(id);
                else queuedFor[id] = left;
            }
            foreach (var id in waitingNow)
            {
                int n;
                queuedFor.TryGetValue(id, out n);
                queuedFor[id] = n + 2; // rises faster than it falls: busy more than a third of the time adds up
            }

            // Ticks of queuing before it acts, as thinks of this skill.
            int ticksPerRun = System.Math.Max(1, c.rules.aiThinkIntervalTicks * EveryThinks(ctx));
            int needed = 2 * (p.Level < 2 ? 300 : 100) / ticksPerRun;
            int worst = -1, worstRuns = 0;
            foreach (var kv in queuedFor)
            {
                var belt = WorldLookup.GetBelt(w, kv.Key);
                if (belt == null || belt.tier + 1 >= c.rules.beltTiers.Count) continue; // already the fastest
                if (kv.Value > worstRuns || (kv.Value == worstRuns && kv.Key < worst)) { worstRuns = kv.Value; worst = kv.Key; }
            }
            if (worst >= 0 && worstRuns >= needed)
            {
                var line = ctx.Services.Belts.LineOf(w, worst);
                var next = c.rules.beltTiers[WorldLookup.GetBelt(w, worst).tier + 1];
                int cost = next.upgradeCostPerSegment * System.Math.Max(1, line.Count);
                if (ctx.Faction.currency >= cost + BudgetSkill.BaseCraftReserve)
                {
                    d.UpgradeBeltId = worst;
                    queuedFor.Remove(worst);
                    d.BeltReason = "upgrading a " + line.Count + "-belt line to " + next.name + " (" + cost + ")";
                }
                else d.BeltReason = "a line needs " + next.name + " (" + cost + " credits)";
            }
            else d.BeltReason = queuedFor.Count == 0 ? "belts flowing" : queuedFor.Count + " belts queuing (busiest " + worstRuns + "/" + needed + ")";
            Status = d.BeltReason;
        }
    }
}
