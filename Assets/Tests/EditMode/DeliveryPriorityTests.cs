using NUnit.Framework;

namespace OrbitalFoundry.Tests
{
    /// <summary>
    /// Delivery priority decides only between buildings competing for the same goods: a High tank off a T-junction
    /// fills before a Low shop, the shop sells the overflow, and the tank's own output stays neutral.
    /// </summary>
    public class DeliveryPriorityTests
    {
        TestWorld t;
        const FactionId P = FactionId.Player;

        [SetUp]
        public void SetUp() { t = new TestWorld(); t.SellRawOre(); } // the depots here count ore delivered by selling it

        [TearDown]
        public void TearDown() { t.Dispose(); }

        CommandResult SetPriority(BuildingState b, string level)
        {
            return t.Run(new GameCommand { kind = CommandKind.SetPriority, actor = P, buildingId = b.id, contentId = level });
        }

        /// <summary>An ore extractor feeding a T-junction at (3,1): a tank down to (3,3) and a shop along to (5,1).</summary>
        void TJunction(out BuildingState tank, out BuildingState shop)
        {
            t.Build(P, "extractor", 0, 1, 1);
            tank = t.Build(P, "storage", 0, 3, 3);
            shop = t.Build(P, "sale_depot", 0, 5, 1);
            t.Line(P, 0, 1, 1, 0, 2, 1, 0, 3, 1, 0, 4, 1, 0, 5, 1);
            t.Line(P, 0, 3, 1, 0, 3, 2, 0, 3, 3);
        }

        [Test]
        public void WithoutPriorityTheTankAndShopShare()
        {
            BuildingState tank, shop;
            TJunction(out tank, out shop);
            for (int i = 0; i < 400; i++) t.FactoryTick();
            Assert.Greater(t.Player.cumulativeSales, 0, "at Normal the shop gets a share straight away");
            Assert.Greater(WorldLookup.CountItem(tank.inventory, "ore"), 0);
        }

        [Test]
        public void AHighTankFillsBeforeALowShopWhichSellsTheOverflow()
        {
            BuildingState tank, shop;
            TJunction(out tank, out shop);
            Assert.IsTrue(SetPriority(tank, "high").success);
            Assert.IsTrue(SetPriority(shop, "low").success);
            int capacity = CatalogRepository.GetBuilding(t.Catalog, "storage").inventoryCapacity;
            // While the tank has real room (stock plus ore on its way below capacity), nothing reaches the shop.
            for (int i = 0; i < 6000 && WorldLookup.TotalItems(tank.inventory) + FactorySimulation.Inbound(t.W, tank.id, "ore") < capacity - 4; i++)
            {
                t.FactoryTick();
                Assert.IsFalse(t.W.tokens.Exists(k => k.targetBuildingId == shop.id), "the shop waits while the tank has room (tank at " + WorldLookup.TotalItems(tank.inventory) + ")");
            }
            Assert.AreEqual(0, t.Player.cumulativeSales);
            for (int i = 0; i < 600 && t.Player.cumulativeSales == 0; i++) t.FactoryTick();
            Assert.Greater(t.Player.cumulativeSales, 0, "once the tank is full the shop sells the overflow");
            Assert.GreaterOrEqual(WorldLookup.TotalItems(tank.inventory) + FactorySimulation.Inbound(t.W, tank.id, "ore"), capacity - 1, "the tank is full (counting ore on its way)");
        }

        [Test]
        public void ATanksPriorityDoesNotCarryToWhatItFeeds()
        {
            // The High tank hands ore on to a Normal refinery behind it; the refinery needs no priority of its own.
            BuildingState tank, shop;
            TJunction(out tank, out shop);
            SetPriority(tank, "high");
            SetPriority(shop, "low");
            var refinery = t.Build(P, "refinery", 0, 3, 5);
            t.Line(P, 0, 3, 3, 0, 3, 4, 0, 3, 5);
            for (int i = 0; i < 600; i++) t.FactoryTick();
            Assert.AreEqual(0, refinery.priority, "the refinery stays Normal");
            Assert.Greater(WorldLookup.CountItem(refinery.inventory, "iron_bar") + WorldLookup.CountItem(refinery.inventory, "ore") + t.W.tokens.FindAll(k => k.targetBuildingId == refinery.id).Count, 0,
                "the refinery is supplied through the tank");
        }

        [Test]
        public void OnlyReceivingBuildingsTakeAPriority()
        {
            var extractor = t.Build(P, "extractor", 0, 1, 1);
            Assert.AreEqual("NO_PRIORITY", SetPriority(extractor, "high").code);
            var shop = t.Build(P, "sale_depot", 0, 5, 1);
            Assert.AreEqual("MALFORMED", SetPriority(shop, "urgent").code);
            Assert.IsTrue(SetPriority(shop, "very_high").success);
            Assert.AreEqual(2, shop.priority);
        }
    }
}
