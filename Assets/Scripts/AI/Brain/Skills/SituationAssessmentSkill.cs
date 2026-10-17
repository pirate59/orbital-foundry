using System.Collections.Generic;

namespace OrbitalFoundry
{
    /// <summary>
    /// Economy Assessment (the slow half of Situation Assessment): what changes over minutes. Credits, cells and
    /// machines for both sides, sales rates, how close the player is to a trade win (if the profile lets it read the
    /// player's economy), and each AI cell's Fort defence. Every 3 seconds.
    /// </summary>
    public sealed class EconomyAssessmentSkill : IAiSkill
    {
        public string Name { get { return "Situation assessment (economy)"; } }
        public AiSkillArea Area { get { return AiSkillArea.Strategy; } }
        public string Status { get; private set; }

        public int EveryThinks(AiContext ctx) { return ctx.Every(3f); }

        public void Think(AiContext ctx)
        {
            var w = ctx.World;
            var c = ctx.Catalog;
            var m = ctx.Model;
            var p = ctx.Profile;

            m.AiCredits = ctx.Faction.currency;
            m.AiIon = ctx.Faction.ion;
            m.AiPowerSpare = ctx.Faction.powerSupply - ctx.Faction.powerDemand;
            m.PlayerCredits = p.SeesPlayerEconomy ? ctx.Player.currency : 0;
            m.AiCells = m.PlayerCells = 0;
            foreach (var cell in w.cells)
            {
                if (cell.owner == AiSide.Me) m.AiCells++;
                else if (cell.owner == AiSide.Foe) m.PlayerCells++;
            }
            m.AiMachines = m.PlayerMachines = 0;
            m.FortDefence.Clear();
            foreach (var b in w.buildings)
            {
                if (b.owner == AiSide.Me)
                {
                    m.AiMachines++;
                    int fort = AiWorldModel.FortStrength(c, b);
                    if (fort > 0) { int had; m.FortDefence.TryGetValue(b.cellId, out had); m.FortDefence[b.cellId] = had + fort; }
                }
                else if (b.owner == AiSide.Foe) m.PlayerMachines++;
            }
            var stats = ctx.Services.Factory.Stats;
            m.AiSalesPerMinute = stats.CreditsPerMinute(c, AiSide.Me);
            m.PlayerSalesPerMinute = p.SeesPlayerEconomy ? stats.CreditsPerMinute(c, AiSide.Foe) : 0f;

            // The player's trade win.
            int target = TradeService.Target(w, c);
            m.PlayerWinProgress = p.SeesPlayerEconomy && target > 0 ? (float)w.trade.rate / target : 0f;
            m.PlayerHolding = p.SeesPlayerEconomy && w.trade.heldSlices > 0;

            m.EconomyBalance = p.SeesPlayerEconomy
                ? (m.AiSalesPerMinute + 1f) / (m.PlayerSalesPerMinute + 1f)
                : (m.AiMachines + 1f) / (m.PlayerMachines + 1f);
            Status = "economy " + m.EconomyBalance.ToString("0.0") + "x";
        }
    }

    /// <summary>
    /// Situation Assessment (the fast half): what changes in seconds. Fleet strength for both sides and the threat
    /// to each AI cell from player fleets it can see, against the ships there and the Fort defence the economy half
    /// last counted. Every assessment (the profile's AssessEveryThinks).
    /// </summary>
    public sealed class SituationAssessmentSkill : IAiSkill
    {
        public string Name { get { return "Situation assessment (threats)"; } }
        public AiSkillArea Area { get { return AiSkillArea.Strategy; } }
        public string Status { get; private set; }

        // Visible, threatening player ships (cell and strength), and AI strength per cell, gathered once a run.
        readonly List<KeyValuePair<int, int>> raiders = new List<KeyValuePair<int, int>>();
        readonly Dictionary<int, int> aiShips = new Dictionary<int, int>();

        public int EveryThinks(AiContext ctx) { return ctx.Profile.AssessEveryThinks; }

        public void Think(AiContext ctx)
        {
            var w = ctx.World;
            var c = ctx.Catalog;
            var m = ctx.Model;
            m.AssessedTick = w.tick;

            // Strength: both whole fleets. What the player has is common knowledge; sight only limits where the AI
            // notices a fleet (the threat map below), so an unseen fleet is never mistaken for no fleet.
            m.AiStrength = m.PlayerStrength = m.AiCraft = m.PlayerCraft = 0;
            raiders.Clear();
            aiShips.Clear();
            foreach (var k in w.crafts)
            {
                if (!WorldLookup.IsCraftLive(k)) continue;
                int s = AiWorldModel.Strength(c, k);
                if (k.owner == AiSide.Me)
                {
                    m.AiStrength += s; m.AiCraft++;
                    int had; aiShips.TryGetValue(k.cellId, out had); aiShips[k.cellId] = had + s;
                }
                else if (k.owner == AiSide.Foe)
                {
                    m.PlayerStrength += s; m.PlayerCraft++;
                    // Ships parked in the player's own cells are holding them, not threatening; only ships out in the
                    // open or heading for an AI cell count.
                    if (Threatening(ctx, k) && Visible(ctx, k.cellId)) raiders.Add(new KeyValuePair<int, int>(k.cellId, s));
                }
            }

            // Threat to each AI cell: visible player strength in or next to it (half for two cells out), against its defence.
            m.ThreatTo.Clear();
            m.DefenceOf.Clear();
            m.MostThreatenedCell = -1;
            m.WorstThreatGap = 0;
            foreach (var cell in w.cells)
            {
                if (cell.owner != AiSide.Me) continue;
                int threat = 0, defence, fort;
                aiShips.TryGetValue(cell.id, out defence);
                m.FortDefence.TryGetValue(cell.id, out fort);
                defence += fort;
                foreach (var r in raiders)
                {
                    int d = ctx.Distance(r.Key, cell.id);
                    if (d >= 0 && d <= 2) threat += d <= 1 ? r.Value : r.Value / 2;
                }
                m.ThreatTo[cell.id] = threat;
                m.DefenceOf[cell.id] = defence;
                if (threat - defence > m.WorstThreatGap) { m.WorstThreatGap = threat - defence; m.MostThreatenedCell = cell.id; }
            }

            m.MilitaryBalance = (m.AiStrength + 1f) / (m.PlayerStrength + 1f);
            Status = "economy " + m.EconomyBalance.ToString("0.0") + "x, military " + m.MilitaryBalance.ToString("0.0") + "x"
                + (m.MostThreatenedCell >= 0 ? ", cell " + m.MostThreatenedCell + " threatened" : "");
        }

        /// <summary>A player ship out of the player's own land, or travelling to an AI cell.</summary>
        static bool Threatening(AiContext ctx, CraftState k)
        {
            var at = WorldLookup.GetCell(ctx.World, k.cellId);
            if (at == null || at.owner != AiSide.Foe) return true;
            var bound = WorldLookup.GetCell(ctx.World, k.worldDestinationCellId);
            return k.worldPathCellIds.Count > 0 && bound != null && bound.owner == AiSide.Me;
        }

        /// <summary>Whether the AI can see a cell: within the profile's sight of one of its own.</summary>
        static bool Visible(AiContext ctx, int cellId)
        {
            if (ctx.Profile.SightCells >= 99) return true;
            foreach (var cell in ctx.World.cells)
            {
                if (cell.owner != AiSide.Me) continue;
                int d = ctx.Distance(cell.id, cellId);
                if (d >= 0 && d <= ctx.Profile.SightCells) return true;
            }
            return false;
        }
    }
}
