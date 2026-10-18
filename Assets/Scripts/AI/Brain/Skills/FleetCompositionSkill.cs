using System.Collections.Generic;

namespace OrbitalFoundry
{
    /// <summary>
    /// Fleet Composition: which ship to build next, against what it faces. Forts in the way call for Bombers;
    /// a player Flying Fortress calls for Rail Lancers, which out-range it; a fleet of three or more without a
    /// Fortress gets one for its shield; claiming new cells calls for cheap Guards. A defensive game alternates
    /// Guards and Fortresses as before. The controller orders the first type on the list it can supply.
    /// </summary>
    public sealed class FleetCompositionSkill : IAiSkill, IAiEventSkill
    {
        bool hadIdleYard;

        /// <summary>A shipyard just went idle: what to build next matters now.</summary>
        public bool Triggered(AiContext ctx)
        {
            bool idle = false;
            foreach (var b in ctx.World.buildings)
            {
                if (b.owner != AiSide.Me || !string.IsNullOrEmpty(b.orderedCraftTypeId)) continue;
                var def = CatalogRepository.GetBuilding(ctx.Catalog, b.typeId);
                if (def != null && def.kind == BuildingKind.Shipyard) { idle = true; break; }
            }
            bool fired = idle && !hadIdleYard;
            hadIdleYard = idle;
            return fired;
        }

        public string Name { get { return "Fleet composition"; } }
        public AiSkillArea Area { get { return AiSkillArea.Military; } }
        public string Status { get; private set; }

        public int EveryThinks(AiContext ctx) { return ctx.Every(5f); } // and when a shipyard goes idle

        public void Think(AiContext ctx)
        {
            var w = ctx.World;
            var c = ctx.Catalog;
            var d = ctx.Decisions;
            var r = c.rules;
            d.BuildOrder.Clear();
            bool military = r.aiExpands || r.aiRaids;
            if (!military)
            {
                // Holders, not raiders: alternate the cheap holder and the shield.
                int fortresses = Count(w, "fortress");
                Add(d, Count(w, "guard") <= fortresses ? "guard" : "fortress");
                Add(d, "guard");
                Add(d, "fortress");
                d.BuildReason = "holding its cells";
                Status = d.BuildReason;
                return;
            }

            // What the target holds decides the counter.
            int forts = 0, playerFortresses = 0;
            if (d.AttackCell >= 0)
            {
                foreach (var b in w.buildings)
                    if (b.owner == AiSide.Foe && b.cellId == d.AttackCell && AiWorldModel.FortStrength(c, b) > 0) forts++;
                foreach (var k in w.crafts)
                    if (k.owner == AiSide.Foe && k.cellId == d.AttackCell && WorldLookup.IsCraftLive(k) && k.typeId == "fortress") playerFortresses++;
            }
            if (playerFortresses == 0)
                foreach (var k in w.crafts)
                    if (k.owner == AiSide.Foe && WorldLookup.IsCraftLive(k) && k.typeId == "fortress") playerFortresses++;

            int fleet = ctx.Model.AiCraft;
            string why;
            if (!string.IsNullOrEmpty(d.StrikeBuildType) && d.StrikeShortfall > 0)
            {
                Add(d, d.StrikeBuildType);
                why = d.StrikeShortfall + " more for the strike on cell " + d.AttackCell + " (the cheapest way to win it)";
            }
            else if (fleet >= 3 && Count(w, "fortress") == 0) { Add(d, "fortress"); why = "a shield for the fleet"; }
            else if (forts > 0) { Add(d, "bomber"); why = "bombers for the Forts at cell " + d.AttackCell; }
            else if (playerFortresses > 0) { Add(d, "lancer"); why = "lancers to out-range the player's Fortress"; }
            else if (d.Stance == AiStance.Expand && r.aiExpands) { Add(d, "guard"); why = "guards to claim cells"; }
            else { Add(d, "bomber"); why = "bombers to hit hard"; }
            // Whatever it can make if the first choice's parts are out of reach.
            Add(d, "bomber");
            Add(d, "guard");
            Add(d, "fortress");
            Add(d, "lancer");
            d.BuildReason = why;
            Status = "next: " + d.BuildOrder[0] + " (" + why + ")";
        }

        static void Add(AiDecisions d, string type)
        {
            if (!d.BuildOrder.Contains(type)) d.BuildOrder.Add(type);
        }

        static int Count(WorldState w, string type)
        {
            int n = 0;
            foreach (var k in w.crafts) if (k.owner == AiSide.Me && WorldLookup.IsCraftLive(k) && k.typeId == type) n++;
            return n;
        }
    }
}
