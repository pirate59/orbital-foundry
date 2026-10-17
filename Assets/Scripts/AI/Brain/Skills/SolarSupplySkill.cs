using System.Collections.Generic;

namespace OrbitalFoundry
{
    /// <summary>
    /// Solar Supply Chain: keeps new Solar Arrays coming when the AI is short of power. An array needs a free slot
    /// (slots come with Forts: the catalog's arraySlots, counted by GridRules.ArraySlots) and its goods in Storage.
    /// Out of slots, it raises its Fort zones under construction to very high delivery priority, so their materials
    /// arrive first and the Forts bring more slots. With slots free but the goods missing, it raises its Storage, so the
    /// array goods reach it before machine top-ups and sale depots. When power has caught up it puts back only the
    /// priorities it raised. Medium and Hard; Easy leaves priorities alone.
    /// </summary>
    public sealed class SolarSupplySkill : IAiSkill
    {
        public string Name { get { return "Solar supply chain"; } }
        public AiSkillArea Area { get { return AiSkillArea.Economy; } }
        public string Status { get; private set; }

        const int VeryHigh = 2, Normal = 0;
        // Buildings this skill raised, to put back once power has caught up.
        readonly HashSet<int> raised = new HashSet<int>();

        public int EveryThinks(AiContext ctx) { return ctx.Every(5f); }

        public void Think(AiContext ctx)
        {
            var w = ctx.World;
            var c = ctx.Catalog;
            var d = ctx.Decisions;
            var f = ctx.Faction;
            // (The controller empties PriorityCommands after carrying them out; other skills add to it too.)
            if (ctx.Profile.Level < 1) { d.SolarReason = "leaves priorities alone"; Status = d.SolarReason; return; }
            BuildingDefinition array = null;
            foreach (var def in c.buildings) if (def.kind == BuildingKind.SolarArray) array = def;
            if (array == null) { d.SolarReason = "no Solar Arrays in this game"; Status = d.SolarReason; return; }

            bool short_ = d.PowerFirst || f.powerDemand * 100 > f.powerSupply * 95;
            if (!short_)
            {
                // Power has caught up: put back what it raised.
                foreach (int id in raised)
                {
                    var b = WorldLookup.GetBuilding(w, id);
                    if (b != null && b.priority == VeryHigh) d.PriorityCommands.Add(Priority(id, Normal));
                }
                raised.Clear();
                d.SolarReason = "power is fine";
                Status = d.SolarReason;
                return;
            }

            int free = 0;
            foreach (var cell in w.cells)
            {
                if (cell.owner != AiSide.Me || !cell.factoryOnline) continue;
                int used;
                int slots = GridRules.ArraySlots(w, c, AiSide.Me, cell.id, out used);
                free += System.Math.Max(0, slots - used);
            }

            if (free == 0)
            {
                // More slots come with Forts: hurry the Fort zones being built.
                int zones = 0;
                foreach (var b in w.buildings)
                {
                    if (b.owner != AiSide.Me || !b.underConstruction) continue;
                    var def = CatalogRepository.GetBuilding(c, b.typeId);
                    if (def == null || def.kind != BuildingKind.ConstructionZone) continue;
                    zones++;
                    if (b.priority < VeryHigh) { d.PriorityCommands.Add(Priority(b.id, VeryHigh)); raised.Add(b.id); }
                }
                d.SolarReason = zones > 0 ? "out of array slots: hurrying " + zones + " Fort zones for more" : "out of array slots: needs more Fort cells";
                Status = d.SolarReason;
                return;
            }

            if (!EconomyService.HasStoredGoods(w, c, AiSide.Me, array.goodsCost))
            {
                // Slots free, goods missing: the planner's Storage (its lowest-numbered) gets them first.
                BuildingState storage = null;
                foreach (var b in w.buildings)
                {
                    if (b.owner != AiSide.Me || !WorldLookup.IsBuildingActive(w, b)) continue;
                    var def = CatalogRepository.GetBuilding(c, b.typeId);
                    if (def != null && def.kind == BuildingKind.Storage && (storage == null || b.id < storage.id)) storage = b;
                }
                if (storage != null && storage.priority < VeryHigh) { d.PriorityCommands.Add(Priority(storage.id, VeryHigh)); raised.Add(storage.id); }
                d.SolarReason = storage != null ? free + " array slots free: rushing " + EconomyService.IngredientText(c, array.goodsCost) + " to Storage"
                    : free + " array slots free: needs a Storage for the array goods";
                Status = d.SolarReason;
                return;
            }
            d.SolarReason = free + " array slots free and the goods are in Storage: arrays next";
            Status = d.SolarReason;
        }

        static GameCommand Priority(int buildingId, int level)
        {
            return new GameCommand { kind = CommandKind.SetPriority, actor = AiSide.Me, buildingId = buildingId, contentId = DeliveryPriority.Names[level + 2] };
        }
    }
}
