namespace OrbitalFoundry
{
    /// <summary>
    /// Attack Target (military goal): picks the enemy cell most worth taking that it can expect to win a whole battle
    /// for. Value is what is there (machines, sale depots, the headquarters); the odds come from playing the battle out
    /// with the fleet it could send (AiEngagement): a win within the round limit, keeping the health the profile
    /// insists on, with attack ships at its core (defence ships alone only in an emergency).
    /// When countering a trade win, sale depots count three times over, so the AI goes after the player's income.
    /// </summary>
    public sealed class AttackTargetSkill : IAiSkill
    {

        readonly System.Collections.Generic.List<CraftState> fleet = new System.Collections.Generic.List<CraftState>();
        public string Name { get { return "Attack target (military)"; } }
        public AiSkillArea Area { get { return AiSkillArea.Military; } }
        public string Status { get; private set; }

        public int EveryThinks(AiContext ctx) { return ctx.Every(2.5f); }

        public void Think(AiContext ctx)
        {
            var w = ctx.World;
            var c = ctx.Catalog;
            var d = ctx.Decisions;
            bool countering = d.Stance == AiStance.Counter;
            // The odds are judged on the fleet it could send (free, healthy ships, and any already gathering), by playing
            // the whole battle out (combat readiness), not by comparing strengths.
            fleet.Clear();
            foreach (var k in AIController.FreeCraft(w, c))
                if (!AIController.WantsRepair(w, c, k) && !AIController.IsLastHeadquartersGuard(w, k)) fleet.Add(k);
            foreach (int id in d.StrikeGroup)
            {
                var k = WorldLookup.GetCraft(w, id);
                if (k != null && WorldLookup.IsCraftLive(k) && k.worldPathCellIds.Count > 0 && !fleet.Contains(k)) fleet.Add(k);
            }


            // In the Attack (or Counter) stance with nothing it can beat yet, it plans for a target and builds the fleet to
            // win it: how many ships that takes comes from the battle played out with the extra ships imagined.
            bool buildUp = d.Stance == AiStance.Attack || countering;
            int room = System.Math.Max(0, ctx.Profile.MaxCraft + ctx.Model.AiCells - ctx.Model.AiCraft);
            int planCell = -1, planNeed = 0;
            string planType = "";
            var buildable = buildUp ? AiEngagement.Buildable(ctx) : new System.Collections.Generic.List<string>();
            float planScore = float.MinValue;
            int best = -1;
            float bestScore = float.MinValue, bestEase = 0f;
            string bestWhy = "";
            foreach (var cell in w.cells)
            {
                if (!AiSide.IsEnemy(w, cell.owner)) continue;
                int distance = ExpansionTargetSkill.DistanceFromAi(ctx, cell.id);
                if (distance < 0) continue;
                // A strike gathers next to its target first: a cell deep in enemy land, with no free cell beside it,
                // cannot be struck (yet). Its border cells come first.
                if (AttackFleetSkill.RallyCell(ctx, cell.id) < 0) continue;
                int machines = 0, depots = 0, links = 0, defence = 0;
                foreach (var b in w.buildings)
                {
                    if (!AiSide.IsEnemy(w, b.owner) || b.cellId != cell.id) continue;
                    var def = CatalogRepository.GetBuilding(c, b.typeId);
                    if (def == null) continue;
                    if (def.kind == BuildingKind.SaleDepot) depots++;
                    else if (def.kind == BuildingKind.Storage || DroneLinks.IsPad(c, b)) links++; // supply lines
                    else machines++;
                    defence += AiWorldModel.FortStrength(c, b);
                }
                foreach (var k in w.crafts)
                    if (AiSide.IsEnemy(w, k.owner) && k.cellId == cell.id) defence += AiWorldModel.Strength(c, k);
                var sim = AiEngagement.Simulate(w, c, fleet, cell.id);
                if (!AiEngagement.Ready(c, fleet, sim, ctx.Profile, countering))
                {
                    // Not yet: worth building up for? The ships it would need (of the type it builds next).
                    if (!buildUp) continue;
                    var option = AiEngagement.Cheapest(w, c, fleet, cell.id, ctx.Profile, countering, buildable, room);
                    if (!option.Found || option.Ships <= 0) continue;
                    float worth = machines * 4 + depots * 8 + links * 4 + (AiSide.IsEnemy(w, cell.headquartersFor) ? 30 : 0);
                    float planValue = worth * 100f / (100f + option.Price) - distance * 5;
                    if (planValue > planScore) { planScore = planValue; planCell = cell.id; planNeed = option.Ships; planType = option.TypeId; }
                    continue;
                }
                float ease = 1f + sim.Kept * 2f; // 1 (a narrow win) to 3 (it would barely be touched)
                // Economic raids: depots and supply lines (Storage, drone pads) hurt the player's income; a sharp AI
                // and a countering one weigh them more.
                bool economic = countering || ctx.Profile.Level >= 2;
                float value = machines * 4 + depots * (countering ? 18 : economic ? 10 : 6) + links * (economic ? 8 : 3)
                    + (AiSide.IsEnemy(w, cell.headquartersFor) ? 30 : 0);
                int lost;
                if (d.LostCells.TryGetValue(cell.id, out lost)) value += lost * 3; // it was ours: take it back
                value += AllyFocus(w, cell.id); // pile in where an ally is already striking
                float score = value * System.Math.Min(ease, 3f) - distance * 5
                    + AiRandom.Unit(w, AiSide.Me, AiRandom.AttackTie + cell.id) * ctx.Profile.Variety * 10f; // near-ties go either way
                if (score > bestScore)
                {
                    bestScore = score;
                    best = cell.id;
                    bestEase = ease;
                    bestWhy = "cell " + cell.id + " (" + machines + " machines, " + depots + " depots, " + links + " supply links; wins in "
                        + sim.Rounds + " rounds keeping " + (int)(sim.Kept * 100) + "% of the fleet)";
                }
            }
            d.AttackShipsNeeded = 0;
            if (best < 0 && planCell >= 0)
            {
                best = planCell;
                bestEase = 0f;
                d.AttackShipsNeeded = planNeed;
                d.StrikeBuildType = planType;
                bestWhy = "cell " + planCell + " (building up: " + planNeed + " more " + planType + (planNeed == 1 ? "" : "s") + ", the cheapest way to win a full battle)";
            }
            d.AttackCell = best;
            d.AttackEase = bestEase;
            d.AttackReason = bestWhy;
            Status = best >= 0 ? (d.AttackShipsNeeded > 0 ? "plan " : "attack ") + bestWhy
                : fleet.Count == 0 ? "no ships free to attack"
                : "not combat ready: no target its free ships (" + fleet.Count + ", " + fleet.FindAll(k => AiEngagement.StrikeCore(c, k)).Count
                    + " attack or support) would win a full battle for";
            if (best < 0) d.AttackReason = Status;
        }

        /// <summary>
        /// Worth added to a target an ally is going after: its raid is bound there or a battle there is its attack.
        /// Teammates who strike together win more than two who strike apart.
        /// </summary>
        static float AllyFocus(WorldState w, int cellId)
        {
            float bonus = 0f;
            foreach (var f in w.factions)
            {
                if (f.suppressed || !AiSide.IsAlly(w, f.id)) continue;
                if (f.ai.raidTargetCellId == cellId) bonus += 12f;
                if (w.encounter.active && w.encounter.targetCellId == cellId && w.encounter.attacker == f.id) bonus += 12f;
            }
            return bonus;
        }
    }
}
