using NUnit.Framework;

namespace OrbitalFoundry.Tests
{
    /// <summary>Solar power capacity, even slowdown, Solar Arrays, and Forts deployed from border construction zones.</summary>
    public class PowerAndExpansionTests
    {
        TestWorld t;
        const FactionId P = FactionId.Player;

        [SetUp]
        public void SetUp() { t = new TestWorld(); }

        [TearDown]
        public void TearDown() { t.Dispose(); }

        [Test]
        public void HeadquartersPanelsPowerWorkingMachines()
        {
            t.Build(P, "extractor", 0, 1, 1);
            var refinery = t.Build(P, "refinery", 0, 3, 1);
            t.Line(P, 0, 1, 1, 0, 2, 1, 0, 3, 1);
            t.FactoryTick();
            Assert.AreEqual(20, t.Player.powerSupply);
            Assert.AreEqual(1, t.Player.powerDemand, "the refinery has no ore yet, so only the extractor draws");
            for (int i = 0; i < 60; i++) t.FactoryTick();
            Assert.AreEqual(3, t.Player.powerDemand, "extractor 1 MW + working refinery 2 MW");
            Assert.AreEqual(1f, t.S.Factory.PowerFactor(P));
        }

        [Test]
        public void OverloadSlowsEveryMachineEvenly()
        {
            t.Build(P, "extractor", 0, 1, 1);
            var refinery = t.Build(P, "refinery", 0, 3, 1);
            t.Line(P, 0, 1, 1, 0, 2, 1, 0, 3, 1);
            for (int i = 0; i < 400; i++) t.FactoryTick();
            int fullSpeed = WorldLookup.CountItem(refinery.inventory, "iron_bar");

            using (var slow = new TestWorld())
            {
                CatalogRepository.GetBuilding(slow.Catalog, "headquarters").powerSupply = 1; // test setup: a third of the 3 MW needed
                slow.Build(P, "extractor", 0, 1, 1);
                var r2 = slow.Build(P, "refinery", 0, 3, 1);
                slow.Line(P, 0, 1, 1, 0, 2, 1, 0, 3, 1);
                float lowest = 1f;
                for (int i = 0; i < 400; i++) { slow.FactoryTick(); lowest = System.Math.Min(lowest, slow.S.Factory.PowerFactor(P)); }
                Assert.AreEqual(1f / 3f, lowest, 0.001f, "1 MW for 3 MW of demand");
                int slowed = WorldLookup.CountItem(r2.inventory, "iron_bar");
                Assert.Greater(slowed, 0, "machines slow down but never stop outright");
                Assert.Less(slowed, fullSpeed);
            }
        }

        [Test]
        public void MachinesNeedAFortOutsideTheHeadquartersCell()
        {
            t.SetOwner(1, P);
            Assert.AreEqual("NEEDS_FORT", t.Run(new GameCommand { kind = CommandKind.PlaceBuilding, actor = P, contentId = "extractor", fromCellId = 1, fromX = 2, fromY = 3 }).code);
            var fort = t.Fortify(1, P);
            var ext = t.Build(P, "extractor", 1, 2, 3);
            t.FactoryTick();
            Assert.IsTrue(WorldLookup.IsBuildingActive(t.W, ext));

            t.MustRun(new GameCommand { kind = CommandKind.Dismantle, actor = P, buildingId = fort.id });
            t.FactoryTick();
            Assert.IsFalse(WorldLookup.GetCell(t.W, 1).factoryOnline);
            Assert.IsFalse(WorldLookup.IsBuildingActive(t.W, ext), "losing the Fort switches the cell's machines off");
        }

        [Test]
        public void ConstructionZoneIsFedByBeltAndWaitsForTheClaim()
        {
            // Plates come from a local assembler; the zone sits on cell 0's east edge, facing cell 1.
            t.Player.currency += 1000; // test setup
            var zone = t.Build(P, "fort_site", 0, 14, 5);
            Assert.AreEqual(1, zone.targetCellId);
            Assert.AreEqual("NOT_BORDER", t.Run(new GameCommand { kind = CommandKind.PlaceBuilding, actor = P, contentId = "fort_site", fromCellId = 0, fromX = 6, fromY = 5 }).code);
            var needs = FactorySimulation.CurrentNeeds(t.W, t.Catalog, zone);
            Assert.AreEqual(2, needs.Count, "plates and wire");

            var storage = t.Build(P, "storage", 0, 12, 5);
            t.Line(P, 0, 12, 5, 0, 13, 5, 0, 14, 5);
            foreach (var m in CatalogRepository.GetBuilding(t.Catalog, "fort_site").materials)
                WorldLookup.AddItem(storage.inventory, m.itemId, m.quantity); // test setup: stock to deliver
            for (int i = 0; i < 400 && !FactorySimulation.HasAll(zone, needs); i++) t.FactoryTick();
            Assert.IsTrue(FactorySimulation.HasAll(zone, needs), "materials arrived by belt");

            t.FactoryTick();
            Assert.IsNotNull(WorldLookup.GetBuilding(t.W, zone.id), "it waits until cell 1 is claimed");
            t.SetOwner(1, P);
            t.FactoryTick();
            Assert.IsNull(WorldLookup.GetBuilding(t.W, zone.id));
            Assert.IsTrue(WorldLookup.GetCell(t.W, 1).factoryOnline, "the deployed Fort brings cell 1 online");
        }

        [Test]
        public void SolarArraysTakeStoredGoodsAndSlots()
        {
            t.Player.currency += 2000; // test setup
            var array = CatalogRepository.GetBuilding(t.Catalog, "solar_array");
            Assert.AreEqual("NEEDS_GOODS", t.Run(new GameCommand { kind = CommandKind.PlaceBuilding, actor = P, contentId = "solar_array", fromCellId = 0, fromX = 9, fromY = 1 }).code);
            var storage = t.Build(P, "storage", 0, 12, 1);
            foreach (var g in array.goodsCost) WorldLookup.AddItem(storage.inventory, g.itemId, g.quantity * 4); // enough for four
            for (int i = 0; i < 3; i++) t.Build(P, "solar_array", 0, 9 + i, 1);
            Assert.AreEqual("NO_ARRAY_SLOT", t.Run(new GameCommand { kind = CommandKind.PlaceBuilding, actor = P, contentId = "solar_array", fromCellId = 0, fromX = 9, fromY = 2 }).code,
                "the HQ cell has three slots");
            foreach (var g in array.goodsCost) Assert.AreEqual(g.quantity, WorldLookup.CountItem(storage.inventory, g.itemId), "three arrays' worth taken");
            t.FactoryTick();
            Assert.AreEqual(20 + 3 * array.powerSupply, t.Player.powerSupply);
        }
    }
}
