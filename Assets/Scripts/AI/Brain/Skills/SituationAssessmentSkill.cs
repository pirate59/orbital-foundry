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
            // "Player" figures are the rival's: the strongest enemy (the one opponent in a two-seat match).
            var rival = ctx.Player.id;
            m.AiCells = m.PlayerCells = 0;
            foreach (var cell in w.cells)
            {
                if (cell.owner == AiSide.Me) m.AiCells++;
                else if (cell.owner == rival) m.PlayerCells++;
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
                else if (b.owner == rival) m.PlayerMachines++;
            }
            var stats = ctx.Services.Factory.Stats;
            m.AiSalesPerMinute = stats.CreditsPerMinute(c, AiSide.Me);
            m.PlayerSalesPerMinute = p.SeesPlayerEconomy ? stats.CreditsPerMinute(c, rival) : 0f;

            // An enemy's trade win: watch whichever enemy is closest to it.
            int target = TradeService.Target(w, c);
            m.PlayerWinProgress = 0f;
            m.PlayerHolding = false;
            if (p.SeesPlayerEconomy && target > 0)
                foreach (var f in w.factions)
                {
                    if (f.suppressed || !AiSide.IsEnemy(w, f.id)) continue;
                    m.PlayerWinProgress = System.Math.Max(m.PlayerWinProgress, (float)f.trade.rate / target);
                    if (f.trade.heldSlices > 0) m.PlayerHolding = true;
                }

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

        // Visible, threatening enemy ships (cell and strength), AI strength per cell, and each enemy's whole fleet,
        // gathered once a run.
        readonly List<KeyValuePair<int, int>> raiders = new List<KeyValuePair<int, int>>();
        readonly Dictionary<int, int> aiShips = new Dictionary<int, int>();
        readonly int[] enemyStrength = new int[WorldLookup.FactionSlots], enemyCraft = new int[WorldLookup.FactionSlots];

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
            System.Array.Clear(enemyStrength, 0, enemyStrength.Length);
            System.Array.Clear(enemyCraft, 0, enemyCraft.Length);
            foreach (var k in w.crafts)
            {
                if (!WorldLookup.IsCraftLive(k)) continue;
                int s = AiWorldModel.Strength(c, k);
                if (k.owner == AiSide.Me)
                {
                    m.AiStrength += s; m.AiCraft++;
                    int had; aiShips.TryGetValue(k.cellId, out had); aiShips[k.cellId] = had + s;
                }
                else if (AiSide.IsEnemy(w, k.owner))
                {
                    enemyStrength[(int)k.owner] += s; enemyCraft[(int)k.owner]++;
                    // Ships parked in their own side's cells are holding them, not threatening; only ships out in the
                    // open or heading for an AI cell count, whichever enemy they belong to.
                    if (Threatening(ctx, k) && Visible(ctx, k.cellId)) raiders.Add(new KeyValuePair<int, int>(k.cellId, s));
                }
            }
            // The military balance is against the strongest single enemy fleet.
            for (int i = 0; i < enemyStrength.Length; i++)
                if (enemyStrength[i] > m.PlayerStrength) { m.PlayerStrength = enemyStrength[i]; m.PlayerCraft = enemyCraft[i]; }

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

        /// <summary>An enemy ship out of its own side's land, or travelling to a cell of the AI's side.</summary>
        static bool Threatening(AiContext ctx, CraftState k)
        {
            var w = ctx.World;
            var at = WorldLookup.GetCell(w, k.cellId);
            if (at == null || !WorldLookup.AreAllies(w, at.owner, k.owner)) return true;
            var bound = WorldLookup.GetCell(w, k.worldDestinationCellId);
            return k.worldPathCellIds.Count > 0 && bound != null && WorldLookup.AreAllies(w, bound.owner, AiSide.Me);
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
