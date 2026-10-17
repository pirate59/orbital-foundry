using System.Collections.Generic;

namespace OrbitalFoundry
{
    /// <summary>
    /// Defence Posture: which of its cells the AI guards first, and where spare ships wait. Each cell is ranked by
    /// what it would lose there (machines, its headquarters, its shipyards) and by the threat next to it, so the
    /// headquarters and a threatened factory get defenders before a quiet outpost. While defending, spare ships
    /// gather at the most threatened cell as a reserve on top of its defender.
    /// </summary>
    public sealed class DefencePostureSkill : IAiSkill
    {
        public string Name { get { return "Defence posture"; } }
        public AiSkillArea Area { get { return AiSkillArea.Territory; } }
        public string Status { get; private set; }

        readonly Dictionary<int, int> priority = new Dictionary<int, int>();

        public int EveryThinks(AiContext ctx) { return ctx.Decisions.Stance == AiStance.Defend ? ctx.Profile.AssessEveryThinks : ctx.Every(5f); } // quick under threat

        public void Think(AiContext ctx)
        {
            var w = ctx.World;
            var c = ctx.Catalog;
            var d = ctx.Decisions;
            priority.Clear();
            d.GuardOrder.Clear();
            foreach (var cell in w.cells)
            {
                if (cell.owner != AiSide.Me) continue;
                int value = cell.headquartersFor == AiSide.Me ? 30 : 0;
                foreach (var b in w.buildings)
                {
                    if (b.owner != AiSide.Me || b.cellId != cell.id) continue;
                    var def = CatalogRepository.GetBuilding(c, b.typeId);
                    value += def != null && def.kind == BuildingKind.Shipyard ? 8 : 2;
                }
                int threat;
                ctx.Model.ThreatTo.TryGetValue(cell.id, out threat);
                priority[cell.id] = value + threat / 10;
                d.GuardOrder.Add(cell.id);
            }
            d.GuardOrder.Sort((a, b) => priority[a] != priority[b] ? priority[b].CompareTo(priority[a]) : a.CompareTo(b));
            d.ReserveCell = d.Stance == AiStance.Defend ? ctx.Model.MostThreatenedCell : -1;
            Status = d.GuardOrder.Count > 0
                ? "guard cell " + d.GuardOrder[0] + " first" + (d.ReserveCell >= 0 ? ", reserves to cell " + d.ReserveCell : "")
                : "nothing to guard";
        }
    }
}
