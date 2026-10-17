namespace OrbitalFoundry
{
    /// <summary>
    /// Attack Target (military goal): picks the player cell most worth taking that the AI can expect to beat. Value
    /// is what is there (machines, sale depots, the headquarters); ease is the strength of the fleet it could send
    /// (its free, healthy ships) over the cell's defence (craft and Forts). A target needs at least the profile's ease.
    /// When countering a trade win, sale depots count three times over, so the AI goes after the player's income.
    /// </summary>
    public sealed class AttackTargetSkill : IAiSkill
    {
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
            // The odds are judged on the fleet it could send: free, healthy ships, and any already gathering.
            int fleet = 0;
            foreach (var k in AIController.FreeCraft(w, c))
                if (!AIController.WantsRepair(w, c, k)) fleet += AiWorldModel.Strength(c, k);
            foreach (int id in d.StrikeGroup)
            {
                var k = WorldLookup.GetCraft(w, id);
                if (k != null && WorldLookup.IsCraftLive(k) && k.worldPathCellIds.Count > 0) fleet += AiWorldModel.Strength(c, k);
            }

            int best = -1;
            float bestScore = float.MinValue, bestEase = 0f;
            string bestWhy = "";
            foreach (var cell in w.cells)
            {
                if (cell.owner != AiSide.Foe) continue;
                int distance = ExpansionTargetSkill.DistanceFromAi(ctx, cell.id);
                if (distance < 0) continue;
                // A strike gathers next to its target first: a cell deep in enemy land, with no free cell beside it,
                // cannot be struck (yet). Its border cells come first.
                if (AttackFleetSkill.RallyCell(ctx, cell.id) < 0) continue;
                int machines = 0, depots = 0, links = 0, defence = 0;
                foreach (var b in w.buildings)
                {
                    if (b.owner != AiSide.Foe || b.cellId != cell.id) continue;
                    var def = CatalogRepository.GetBuilding(c, b.typeId);
                    if (def == null) continue;
                    if (def.kind == BuildingKind.SaleDepot) depots++;
                    else if (def.kind == BuildingKind.Storage || DroneLinks.IsPad(c, b)) links++; // supply lines
                    else machines++;
                    defence += AiWorldModel.FortStrength(c, b);
                }
                foreach (var k in w.crafts)
                    if (k.owner == AiSide.Foe && k.cellId == cell.id) defence += AiWorldModel.Strength(c, k);
                float ease = (fleet + 1f) / (defence + 1f);
                if (ease < ctx.Profile.NeededEase) continue;
                // Economic raids: depots and supply lines (Storage, drone pads) hurt the player's income; a sharp AI
                // and a countering one weigh them more.
                bool economic = countering || ctx.Profile.Level >= 2;
                float value = machines * 4 + depots * (countering ? 18 : economic ? 10 : 6) + links * (economic ? 8 : 3)
                    + (cell.headquartersFor == AiSide.Foe ? 30 : 0);
                int lost;
                if (d.LostCells.TryGetValue(cell.id, out lost)) value += lost * 3; // it was ours: take it back
                float score = value * System.Math.Min(ease, 3f) - distance * 5;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = cell.id;
                    bestEase = ease;
                    bestWhy = "cell " + cell.id + " (" + machines + " machines, " + depots + " depots, " + links + " supply links, ease " + ease.ToString("0.0") + "x)";
                }
            }
            d.AttackCell = best;
            d.AttackEase = bestEase;
            d.AttackReason = bestWhy;
            Status = best >= 0 ? "attack " + bestWhy : "no target it expects to beat";
        }
    }
}
