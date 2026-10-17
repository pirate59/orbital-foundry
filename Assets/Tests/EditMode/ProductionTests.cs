using NUnit.Framework;

namespace OrbitalFoundry.Tests
{
    /// <summary>Starting production from the HQ, dismantling, and earning craft through a shipyard.</summary>
    public class ProductionTests
    {
        TestWorld t;
        const FactionId P = FactionId.Player;

        [SetUp]
        public void SetUp() { t = new TestWorld(); }

        [TearDown]
        public void TearDown() { t.Dispose(); }

        [Test]
        public void NewMachinesWaitForTheirRecipeToBeChosen()
        {
            t.Build(P, "extractor", 0, 1, 1);
            var refinery = t.Build(P, "refinery", 0, 3, 1, chooseRecipe: false);
            t.Line(P, 0, 1, 1, 0, 2, 1, 0, 3, 1);
            Assert.AreEqual("", refinery.selectedRecipeId, "machines start with no recipe");
            for (int i = 0; i < 100; i++) t.FactoryTick();
            Assert.IsFalse(t.W.pullRequests.Exists(r => r.targetBuildingId == refinery.id), "with no recipe it asks for nothing");
            Assert.AreEqual(0, WorldLookup.TotalItems(refinery.inventory));
            t.MustRun(new GameCommand { kind = CommandKind.SelectRecipe, actor = P, buildingId = refinery.id, contentId = "refine_ore" });
            for (int i = 0; i < 200; i++) t.FactoryTick();
            Assert.Greater(WorldLookup.TotalItems(refinery.inventory), 0, "once chosen it starts pulling ore");
        }

        [Test]
        public void RawOreIsNotBoughtButRefinedGoodsAre()
        {
            t.Build(P, "extractor", 0, 1, 1);
            t.Build(P, "sale_depot", 0, 3, 1);
            t.Line(P, 0, 1, 1, 0, 2, 1, 0, 3, 1);
            for (int i = 0; i < 300; i++) t.FactoryTick();
            Assert.AreEqual(0, t.Player.cumulativeSales, "a depot does not buy raw ore");
            Assert.AreEqual("NOT_SELLABLE", t.Run(new GameCommand { kind = CommandKind.ToggleDepotItem, actor = P, buildingId = WorldLookup.GetBuildingAt(t.W, 0, 3, 1).id, contentId = "ore" }).code);

            // Through a refinery the same ore earns.
            t.Build(P, "extractor", 0, 11, 3);
            t.Build(P, "refinery", 0, 13, 3);
            t.Build(P, "sale_depot", 0, 15, 3);
            t.Line(P, 0, 11, 3, 0, 12, 3, 0, 13, 3);
            t.Line(P, 0, 13, 3, 0, 14, 3, 0, 15, 3);
            int start = t.Player.currency;
            for (int i = 0; i < 600; i++) t.FactoryTick();
            Assert.Greater(t.Player.cumulativeSales, 0, "iron bars sell");
            Assert.AreEqual(start + t.Player.cumulativeSales, t.Player.currency);
        }

        [Test]
        public void FactoriesAreServedBeforeDepots()
        {
            t.SellRawOre(); // test setup: the depot counts ore by selling it
            // The refinery and the depot share one ore extractor (one exit belt that splits); the refinery must still
            // get every ore it asks for.
            t.Build(P, "extractor", 0, 1, 1);
            var refinery = t.Build(P, "refinery", 0, 3, 1);
            t.Build(P, "sale_depot", 0, 2, 3);
            t.Line(P, 0, 1, 1, 0, 2, 1, 0, 3, 1);
            t.Line(P, 0, 2, 1, 0, 2, 2, 0, 2, 3);
            for (int i = 0; i < 400; i++) t.FactoryTick();
            Assert.Greater(WorldLookup.CountItem(refinery.inventory, "iron_bar"), 5, "refinery kept producing");
            Assert.Greater(t.Player.cumulativeSales, 0, "surplus ore was sold");
        }

        [Test]
        public void DismantlingOwnStructuresRefundsHalfAndClearsDependants()
        {
            var ext = t.Build(P, "extractor", 0, 1, 1);
            t.Build(P, "refinery", 0, 3, 1);
            var belts = t.Line(P, 0, 1, 1, 0, 2, 1, 0, 3, 1);
            for (int i = 0; i < 30 && t.W.tokens.Count == 0; i++) t.FactoryTick();
            Assert.Greater(t.W.tokens.Count, 0);

            int before = t.Player.currency;
            t.MustRun(new GameCommand { kind = CommandKind.Dismantle, actor = P, beltId = belts[0].id });
            Assert.AreEqual(before + 1, t.Player.currency);
            Assert.IsNull(WorldLookup.GetBelt(t.W, belts[0].id));
            Assert.AreEqual(0, t.W.tokens.Count, "items on the removed belt are lost");

            t.MustRun(new GameCommand { kind = CommandKind.Dismantle, actor = P, buildingId = ext.id });
            Assert.AreEqual(before + 1 + 35, t.Player.currency);
            Assert.AreEqual(-1, WorldLookup.GetNode(t.W, 101).extractorBuildingId, "node is free again");
            t.Build(P, "extractor", 0, 1, 1);
        }

        [Test]
        public void CannotDismantleEnemyOrOutsideOwnCells()
        {
            var aiExt = t.Build(FactionId.Ai, "extractor", 7, 1, 1);
            Assert.AreEqual("NOT_OWNER", t.Run(new GameCommand { kind = CommandKind.Dismantle, actor = P, buildingId = aiExt.id }).code);
        }

        [Test]
        public void ShipyardFillsARotatedThreeByTwoSpaceWithoutChangingOtherBuildings()
        {
            var placed = t.Run(new GameCommand { kind = CommandKind.PlaceBuilding, actor = P,
                contentId = "shipyard", fromCellId = 0, fromX = 3, fromY = 3, rotation = 1 });
            Assert.IsTrue(placed.success, placed.code + " " + placed.message);
            var yard = WorldLookup.GetBuildingAt(t.W, 0, 3, 3);
            Assert.IsTrue(yard.extendedFootprint);
            Assert.AreEqual(1, yard.rotation);
            // Facing north: three tiles up from the anchor, two across to the west.
            foreach (var tile in new[] { new[] { 3, 4 }, new[] { 3, 5 }, new[] { 2, 3 }, new[] { 2, 4 }, new[] { 2, 5 } })
                Assert.AreSame(yard, GridRules.FootprintAt(t.W, t.Catalog, 0, tile[0], tile[1]), "covers " + tile[0] + "," + tile[1]);
            Assert.IsNull(GridRules.FootprintAt(t.W, t.Catalog, 0, 3, 3), "the anchor tile itself stays the building's own tile");
            Assert.IsNull(GridRules.FootprintAt(t.W, t.Catalog, 0, 4, 3));
            Assert.IsNull(GridRules.FootprintAt(t.W, t.Catalog, 0, 3, 6));
            Assert.IsNull(GridRules.FootprintAt(t.W, t.Catalog, 0, 1, 4));
            Assert.AreEqual("FOOTPRINT", t.Run(new GameCommand { kind = CommandKind.PlaceBelt, actor = P,
                fromCellId = 0, fromX = 3, fromY = 4, toCellId = 0, toX = 4, toY = 4 }).code);
            Assert.IsFalse(t.Run(new GameCommand { kind = CommandKind.PlaceBuilding, actor = P,
                contentId = "storage", fromCellId = 0, fromX = 3, fromY = 4 }).success);
            Assert.IsTrue(t.Run(new GameCommand { kind = CommandKind.PlaceBuilding, actor = P,
                contentId = "storage", fromCellId = 0, fromX = 4, fromY = 3 }).success);
        }

        [Test]
        public void SavedOneTileShipyardClaimsAFreeNeighbourOnLoad()
        {
            var yard = t.Build(P, "shipyard", 0, 6, 6);
            yard.extendedFootprint = false; // JSON from a build before shipyards grew past one tile
            t.Build(P, "storage", 0, 7, 6);
            t.Loop.Initialize(t.Catalog, t.Map, t.W, t.Loop.Saves);
            Assert.IsTrue(yard.extendedFootprint);
            Assert.AreEqual(1, yard.rotation, "the occupied east tile makes the shipyard face north");
            Assert.AreSame(yard, GridRules.FootprintAt(t.W, t.Catalog, 0, 6, 7));
        }

        [Test]
        public void SavedShipyardExpandsWhenAnAdjacentTileIsCleared()
        {
            var yard = t.Build(P, "shipyard", 0, 6, 6);
            yard.extendedFootprint = false;
            // Each facing covers two of the anchor's neighbours, so blocking east and west blocks every facing.
            var east = t.Build(P, "storage", 0, 7, 6);
            t.Build(P, "storage", 0, 5, 6);
            t.Loop.Initialize(t.Catalog, t.Map, t.W, t.Loop.Saves);
            Assert.IsFalse(yard.extendedFootprint);
            Assert.IsTrue(t.Loop.Submit(new GameCommand { kind = CommandKind.Dismantle, actor = P, buildingId = east.id }).success);
            Assert.IsTrue(yard.extendedFootprint);
            Assert.AreSame(yard, GridRules.FootprintAt(t.W, t.Catalog, 0, 7, 6));
        }

        [Test]
        public void SavedShipyardClaimsTileAfterSurroundingBeltIsRemoved()
        {
            var yard = t.Build(P, "shipyard", 0, 6, 6);
            yard.extendedFootprint = false;
            var east = t.Belt(P, 0, 6, 6, 0, 7, 6);
            t.Belt(P, 0, 6, 6, 0, 5, 6);
            t.Loop.Initialize(t.Catalog, t.Map, t.W, t.Loop.Saves);
            Assert.IsFalse(yard.extendedFootprint);
            Assert.IsTrue(t.Loop.Submit(new GameCommand { kind = CommandKind.Dismantle, actor = P, beltId = east.id }).success);
            Assert.IsTrue(yard.extendedFootprint);
            Assert.AreSame(yard, GridRules.FootprintAt(t.W, t.Catalog, 0, 7, 6));
        }

        [Test]
        public void CraftRequireAShipyardAndPartsSentFromProduction()
        {
            var noYard = t.Run(new GameCommand { kind = CommandKind.BuyCraft, actor = P, contentId = "guard", buildingId = 999 });
            Assert.IsFalse(noYard.success);

            t.Player.currency += 400; // test setup: fund the yard and its supply
            t.Player.ion += 200;
            var maker = t.Build(P, "assembler", 0, 6, 4);
            var yard = t.Build(P, "shipyard", 0, 6, 6);
            t.Line(P, 0, 6, 4, 0, 6, 5, 0, 6, 6);
            var guardDef = CatalogRepository.GetCraft(t.Catalog, "guard");
            WorldLookup.AddItem(maker.inventory, "iron_plate", 8); // test setup: some plates just made on the line
            int crafts = t.W.crafts.Count;
            t.MustRun(new GameCommand { kind = CommandKind.BuyCraft, actor = P, contentId = "guard", buildingId = yard.id });
            Assert.AreEqual("SHIPYARD_BUSY", t.Run(new GameCommand { kind = CommandKind.BuyCraft, actor = P, contentId = "bomber", buildingId = yard.id }).code);

            for (int i = 0; i < 2000 && WorldLookup.CountItem(yard.inventory, "iron_plate") < 8; i++) t.FactoryTick();
            Assert.AreEqual(8, WorldLookup.CountItem(yard.inventory, "iron_plate"), "the assembler's plates were sent along the belt");
            foreach (var part in guardDef.components) // test setup: the rest of the parts arrive too
                WorldLookup.AddItem(yard.inventory, part.itemId, part.quantity - WorldLookup.CountItem(yard.inventory, part.itemId));
            for (int i = 0; i < guardDef.buildTicks * 3 && t.W.crafts.Count == crafts; i++) t.FactoryTick();
            Assert.AreEqual(crafts + 1, t.W.crafts.Count, "guard launched once all its parts arrived");
            var guard = t.W.crafts[t.W.crafts.Count - 1];
            Assert.AreEqual("guard", guard.typeId);
            Assert.AreEqual(0, guard.cellId);
            Assert.AreEqual("", yard.orderedCraftTypeId);
            Assert.AreEqual(0, WorldLookup.CountItem(yard.inventory, "iron_plate"), "parts were consumed");
        }

        [Test]
        public void BeltsDeliverIntoAnyTileOfTheShipyard()
        {
            t.Player.currency += 600; // test setup
            var yard = t.Build(P, "shipyard", 0, 6, 6); // covers x 6-8, y 6-7
            var maker = t.Build(P, "assembler", 0, 8, 4);
            // Into the far corner, not the anchor the shipyard was placed from.
            t.Line(P, 0, 8, 4, 0, 8, 5, 0, 8, 6);
            WorldLookup.AddItem(maker.inventory, "iron_plate", 8); // test setup: plates just made
            t.MustRun(new GameCommand { kind = CommandKind.BuyCraft, actor = P, contentId = "guard", buildingId = yard.id });
            for (int i = 0; i < 2000 && WorldLookup.CountItem(yard.inventory, "iron_plate") < 8; i++) t.FactoryTick();
            Assert.AreEqual(8, WorldLookup.CountItem(yard.inventory, "iron_plate"), "a belt ending on any shipyard tile delivers to it");
            // Belts may end on the shipyard, but never leave it or run through it.
            Assert.AreEqual("FOOTPRINT", t.Run(new GameCommand { kind = CommandKind.PlaceBelt, actor = P,
                fromCellId = 0, fromX = 7, fromY = 7, toCellId = 0, toX = 7, toY = 8 }).code);
            Assert.AreEqual("FOOTPRINT", t.Run(new GameCommand { kind = CommandKind.PlaceBelt, actor = P,
                fromCellId = 0, fromX = 7, fromY = 6, toCellId = 0, toX = 7, toY = 7 }).code);
            Assert.IsTrue(t.Run(new GameCommand { kind = CommandKind.PlaceBelt, actor = P,
                fromCellId = 0, fromX = 7, fromY = 8, toCellId = 0, toX = 7, toY = 7 }).success, "into the top edge");
        }

        [Test]
        public void AnIdleShipyardRepairsADockedCraftOneHpAtATime()
        {
            t.Player.currency += 400; // test setup
            var yard = t.Build(P, "shipyard", 0, 6, 6);
            var guard = WorldLookup.CraftsInCell(t.W, 0, P)[0];
            var def = CatalogRepository.GetCraft(t.Catalog, "guard");
            guard.currentHealth = 5; // test setup: back from a fight with half its ships
            CollectionAssert.AreEqual(def.repairParts, FactorySimulation.ShipyardNeeds(t.W, t.Catalog, yard), "an idle yard asks for one HP of repairs");
            for (int i = 0; i < def.repairTicks * 3; i++) t.FactoryTick();
            Assert.AreEqual(5, guard.currentHealth, "no repair without its parts");
            foreach (var part in def.repairParts) WorldLookup.AddItem(yard.inventory, part.itemId, part.quantity); // test setup: delivered
            for (int i = 0; i < def.repairTicks * 3 && guard.currentHealth == 5; i++) t.FactoryTick();
            Assert.AreEqual(6, guard.currentHealth, "one HP back: one more ship in the fleet");
            Assert.AreEqual(0, WorldLookup.TotalItems(yard.inventory), "the repair used its parts");
            // An order comes first.
            t.MustRun(new GameCommand { kind = CommandKind.BuyCraft, actor = P, contentId = "guard", buildingId = yard.id });
            CollectionAssert.AreEqual(def.components, FactorySimulation.ShipyardNeeds(t.W, t.Catalog, yard));
            var fortress = CatalogRepository.GetCraft(t.Catalog, "fortress");
            Assert.IsTrue(fortress.repairParts.Exists(p => p.itemId == "purified_water"), "fortress repairs keep Purified Water in demand");
        }

        [Test]
        public void TheTestStartGivesFundsAndOneOfEveryShip()
        {
            GameFactory.MakeTestStart(t.W, t.Catalog);
            Assert.AreEqual(20000, t.Player.currency);
            Assert.AreEqual(20000, t.Player.ion);
            int home = t.W.cells.Find(c => c.headquartersFor == P).id;
            foreach (var craft in t.Catalog.crafts)
                Assert.IsTrue(t.W.crafts.Exists(k => k.owner == P && k.typeId == craft.id && k.cellId == home), craft.displayName + " at the HQ");
            Assert.AreEqual(1, t.W.crafts.FindAll(k => k.owner == P && k.typeId == "guard").Count, "the starting guard counts as the guard");
        }

        [Test]
        public void StorageSuppliesAShipyardByBelt()
        {
            t.Player.currency += 400; // test setup
            var storage = t.Build(P, "storage", 0, 6, 4);
            var yard = t.Build(P, "shipyard", 0, 6, 6);
            t.Line(P, 0, 6, 4, 0, 6, 5, 0, 6, 6);
            var guardDef = CatalogRepository.GetCraft(t.Catalog, "guard");
            foreach (var part in guardDef.components) WorldLookup.AddItem(storage.inventory, part.itemId, part.quantity); // test setup: parts put by
            int crafts = t.W.crafts.Count;
            t.MustRun(new GameCommand { kind = CommandKind.BuyCraft, actor = P, contentId = "guard", buildingId = yard.id });
            for (int i = 0; i < 20000 && t.W.crafts.Count == crafts; i++) t.FactoryTick();
            Assert.AreEqual(crafts + 1, t.W.crafts.Count, "stored parts are sent along the belt and the guard launches");
            Assert.AreEqual(0, WorldLookup.TotalItems(storage.inventory), "every part came out of storage");
        }

        [Test]
        public void AnOrderedShipWaitsForEveryPartToBeSentToTheShipyard()
        {
            t.Player.currency += 400; // test setup
            var yard = t.Build(P, "shipyard", 0, 6, 6);
            var guard = CatalogRepository.GetCraft(t.Catalog, "guard");
            t.MustRun(new GameCommand { kind = CommandKind.BuyCraft, actor = P, contentId = "guard", buildingId = yard.id });
            int crafts = t.W.crafts.Count;
            for (int i = 0; i < guard.buildTicks * 3; i++) t.FactoryTick();
            Assert.AreEqual(crafts, t.W.crafts.Count, "nothing launches without its parts");
            Assert.AreEqual(0, yard.recipeProgressTicks, "the build does not even start");
            // Every part but the last relay arrives: still waiting.
            foreach (var part in guard.components) WorldLookup.AddItem(yard.inventory, part.itemId, part.quantity); // test setup: delivered parts
            WorldLookup.RemoveItem(yard.inventory, "relay", 1);
            for (int i = 0; i < guard.buildTicks * 3; i++) t.FactoryTick();
            Assert.AreEqual(crafts, t.W.crafts.Count, "one relay short");
            WorldLookup.AddItem(yard.inventory, "relay", 1);
            for (int i = 0; i < guard.buildTicks * 3 && t.W.crafts.Count == crafts; i++) t.FactoryTick();
            Assert.AreEqual(crafts + 1, t.W.crafts.Count, "launched once the last part arrived");
            Assert.AreEqual(0, WorldLookup.TotalItems(yard.inventory), "the parts were used up");
        }

        [Test]
        public void TheShipyardHoldsAFlyingFortressWorthOfParts()
        {
            var yard = CatalogRepository.GetBuilding(t.Catalog, "shipyard");
            foreach (var craft in t.Catalog.crafts)
            {
                int parts = 0;
                foreach (var p in craft.components) parts += p.quantity;
                Assert.LessOrEqual(parts, yard.inventoryCapacity, craft.displayName);
            }
            // 48 parts for a Flying Fortress, plus room for a repair's leftover parts so they never block an order.
            Assert.AreEqual(56, yard.inventoryCapacity);
        }

        [Test]
        public void LaterCraftWaitForTheirPartsToBeResearched()
        {
            t.Player.currency += 400; // test setup
            var yard = t.Build(P, "shipyard", 0, 6, 6);
            Assert.AreEqual("NEEDS_RESEARCH", t.Run(new GameCommand { kind = CommandKind.BuyCraft, actor = P, contentId = "bomber", buildingId = yard.id }).code,
                "a Heavy Bomber needs Reinforced Plate, Explosives and Electronics, which start locked");
            Assert.AreEqual("NEEDS_RESEARCH", t.Run(new GameCommand { kind = CommandKind.BuyCraft, actor = P, contentId = "fortress", buildingId = yard.id }).code,
                "a Flying Fortress needs Titanium and Unobtanium parts");
            t.MustRun(new GameCommand { kind = CommandKind.BuyCraft, actor = P, contentId = "guard", buildingId = yard.id });
        }

        [Test]
        public void BeltJoiningMidLineMergesAndBranchingMidLineSplits()
        {
            t.SellRawOre(); // test setup: the depot counts ore by selling it
            t.Player.currency += 500; // test setup
            // Merge: copper joins the middle of the ore line, and the depot sells both.
            t.Build(P, "extractor", 0, 1, 1);
            t.Build(P, "extractor", 0, 4, 4);
            t.Build(P, "sale_depot", 0, 5, 1);
            t.Line(P, 0, 1, 1, 0, 1, 2, 0, 2, 2, 0, 3, 2, 0, 4, 2, 0, 5, 2, 0, 5, 1);
            t.Line(P, 0, 4, 4, 0, 4, 3, 0, 4, 2);
            // Split: a branch off the middle of the same line feeds a refinery.
            var refinery = t.Build(P, "refinery", 0, 2, 4);
            t.Line(P, 0, 2, 2, 0, 2, 3, 0, 2, 4);
            for (int i = 0; i < 600; i++)
            {
                t.FactoryTick();
                t.AssertBeltCapacity();
            }
            Assert.Greater(WorldLookup.CountItem(refinery.inventory, "iron_bar"), 3, "the branch fed the refinery");
            // Ore alone (after the refinery's share) cannot reach this total; copper must arrive through the merge too.
            Assert.Greater(t.Player.cumulativeSales, 40, "the depot sold from both merged lines");
        }

        [Test]
        public void ANewStorageStoresNothingUntilGoodsAreChosen()
        {
            t.Build(P, "extractor", 0, 1, 1);
            var storage = t.Build(P, "storage", 0, 3, 1, chooseRecipe: false);
            t.Line(P, 0, 1, 1, 0, 2, 1, 0, 3, 1);
            Assert.AreEqual(0, storage.storedItemIds.Count, "nothing on its list, like a machine with no recipe");
            for (int i = 0; i < 200; i++) t.FactoryTick();
            Assert.AreEqual(0, WorldLookup.TotalItems(storage.inventory), "so it collects nothing");

            t.MustRun(new GameCommand { kind = CommandKind.ToggleDepotItem, actor = P, buildingId = storage.id, contentId = "ore" });
            CollectionAssert.AreEqual(new[] { "ore" }, storage.storedItemIds);
            for (int i = 0; i < 200; i++) t.FactoryTick();
            Assert.Greater(WorldLookup.CountItem(storage.inventory, "ore"), 0, "ore switched on: collected");

            t.MustRun(new GameCommand { kind = CommandKind.ToggleDepotItem, actor = P, buildingId = storage.id, contentId = CommandRouter.StoreAll });
            Assert.IsTrue(storage.storedItemIds.Contains("copper_ore") && storage.storedItemIds.Contains("relay"), "Store all");
            Assert.IsFalse(storage.storedItemIds.Contains(t.Catalog.rules.ionItemId), "Ion is never stored");
            t.MustRun(new GameCommand { kind = CommandKind.ToggleDepotItem, actor = P, buildingId = storage.id, contentId = CommandRouter.StoreNone });
            Assert.AreEqual(0, storage.storedItemIds.Count, "Store none");
        }

        [Test]
        public void OlderSavesKeepWhatTheirStoragesCollected()
        {
            var storage = t.Build(P, "storage", 0, 3, 1, chooseRecipe: false);
            storage.blockedItemIds.Add("ore"); // test setup: a schema 2 storage that refused only ore
            SaveService.UpgradeStorageLists(t.W, t.Catalog);
            Assert.IsFalse(storage.storedItemIds.Contains("ore"), "what it refused stays off");
            Assert.IsTrue(storage.storedItemIds.Contains("copper_ore"), "everything else stays on");
            Assert.AreEqual(0, storage.blockedItemIds.Count);
        }

        [Test]
        public void DepotSellListFiltersWhatItPulls()
        {
            t.SellRawOre(); // test setup: the depot counts ore by selling it
            t.Build(P, "extractor", 0, 1, 1);
            var depot = t.Build(P, "sale_depot", 0, 3, 1);
            t.Line(P, 0, 1, 1, 0, 2, 1, 0, 3, 1);
            t.MustRun(new GameCommand { kind = CommandKind.ToggleDepotItem, actor = P, buildingId = depot.id, contentId = "ore" });
            CollectionAssert.Contains(depot.blockedItemIds, "ore");
            for (int i = 0; i < 200; i++) t.FactoryTick();
            Assert.AreEqual(0, t.Player.cumulativeSales, "ore is off the sell list");
            t.MustRun(new GameCommand { kind = CommandKind.ToggleDepotItem, actor = P, buildingId = depot.id, contentId = "ore" });
            for (int i = 0; i < 200; i++) t.FactoryTick();
            Assert.Greater(t.Player.cumulativeSales, 0);
            Assert.AreEqual("NOT_DEPOT", t.Run(new GameCommand { kind = CommandKind.ToggleDepotItem, actor = P, buildingId = WorldLookup.GetBuildingAt(t.W, 0, 1, 1).id, contentId = "ore" }).code);
        }

        [Test]
        public void DrawingOverABeltTheOtherWayReversesIt()
        {
            var forward = t.Belt(P, 0, 2, 2, 0, 3, 2);
            int before = t.Player.currency;
            var r = t.Run(new GameCommand { kind = CommandKind.PlaceBelt, actor = P, fromCellId = 0, fromX = 3, fromY = 2, toCellId = 0, toX = 2, toY = 2 });
            Assert.IsTrue(r.success, r.message);
            Assert.IsNull(WorldLookup.GetBelt(t.W, forward.id));
            Assert.AreEqual(1, t.W.belts.Count);
            Assert.AreEqual(3, t.W.belts[0].fromX);
            Assert.AreEqual(before, t.Player.currency, "reversing is free");
        }

        [Test]
        public void NeedsFollowTheSelectedRecipeOrOrder()
        {
            var assembler = t.Build(P, "assembler", 0, 3, 3);
            var needs = FactorySimulation.CurrentNeeds(t.W, t.Catalog, assembler);
            Assert.AreEqual(2, needs.Count);
            t.Player.research.Add(new ResearchProgress { nodeId = "quantum_cores", level = 1 });
            t.MustRun(new GameCommand { kind = CommandKind.SelectRecipe, actor = P, buildingId = assembler.id, contentId = "build_core" });
            needs = FactorySimulation.CurrentNeeds(t.W, t.Catalog, assembler);
            Assert.AreEqual(2, needs[0].quantity, "quantum core needs 2 iron bars");
        }

        [Test]
        public void MovingAUnitIntoANeutralCellClaimsItAutomatically()
        {
            var guard = WorldLookup.CraftsInCell(t.W, 0, P)[0];
            t.MustRun(new GameCommand { kind = CommandKind.MoveWorld, actor = P, craftId = guard.id, targetCellId = 1 });
            for (int i = 0; i < 400 && WorldLookup.GetCell(t.W, 1).owner != P; i++) t.Loop.TickNormal();
            Assert.AreEqual(P, WorldLookup.GetCell(t.W, 1).owner, "arrival starts the hold, which completes on its own");
            Assert.AreEqual(guard.id, WorldLookup.GetCell(t.W, 1).defenderCraftId);
        }

        [Test]
        public void StorageJoinsSeveralProducersToSeveralMachines()
        {
            // Two ore extractors feed one storage; two refineries draw only from that storage.
            t.Build(P, "extractor", 0, 1, 1);
            t.Build(P, "extractor", 0, 11, 3);
            var storage = t.Build(P, "storage", 0, 6, 1);
            var refineryA = t.Build(P, "refinery", 0, 6, 4);
            var refineryB = t.Build(P, "refinery", 0, 5, 0);
            t.Line(P, 0, 1, 1, 0, 2, 1, 0, 3, 1, 0, 4, 1, 0, 5, 1, 0, 6, 1);
            t.Line(P, 0, 11, 3, 0, 11, 2, 0, 11, 1, 0, 10, 1, 0, 9, 1, 0, 8, 1, 0, 7, 1, 0, 6, 1);
            t.Line(P, 0, 6, 1, 0, 6, 2, 0, 6, 3, 0, 6, 4);
            t.Line(P, 0, 6, 1, 0, 6, 0, 0, 5, 0);
            Assert.IsFalse(t.S.Belts.FindShortestRoute(t.W, WorldLookup.GetBuildingAt(t.W, 0, 1, 1).id, refineryA.id).success,
                "producers reach the refineries only through the storage");
            // Long enough for the refineries to fill up, so ore backs up into the storage.
            for (int i = 0; i < 2000; i++) { t.FactoryTick(); t.AssertBeltCapacity(); }
            Assert.Greater(WorldLookup.CountItem(refineryA.inventory, "iron_bar"), 2);
            Assert.Greater(WorldLookup.CountItem(refineryB.inventory, "iron_bar"), 2);
            Assert.Greater(WorldLookup.CountItem(storage.inventory, "ore"), 0, "surplus ore is held in storage");
        }

        [Test]
        public void StoragesDoNotFeedEachOther()
        {
            t.Build(P, "extractor", 0, 1, 1);
            var first = t.Build(P, "storage", 0, 3, 1);
            var second = t.Build(P, "storage", 0, 5, 1);
            t.Line(P, 0, 1, 1, 0, 2, 1, 0, 3, 1);
            t.Line(P, 0, 3, 1, 0, 4, 1, 0, 5, 1);
            for (int i = 0; i < 400; i++) t.FactoryTick();
            Assert.Greater(WorldLookup.CountItem(first.inventory, "ore"), 0);
            Assert.AreEqual(0, WorldLookup.TotalItems(second.inventory));
        }

        [Test]
        public void StorageAcceptListFiltersWhatItCollects()
        {
            t.Build(P, "extractor", 0, 1, 1);
            var storage = t.Build(P, "storage", 0, 3, 1);
            t.Line(P, 0, 1, 1, 0, 2, 1, 0, 3, 1);
            t.MustRun(new GameCommand { kind = CommandKind.ToggleDepotItem, actor = P, buildingId = storage.id, contentId = "ore" });
            for (int i = 0; i < 300; i++) t.FactoryTick();
            Assert.AreEqual(0, WorldLookup.CountItem(storage.inventory, "ore"));
        }

        [Test]
        public void StorageReceivingGoodsIsNotReportedIdle()
        {
            // A long lane keeps two ore in flight at once, so no fresh ore request is waiting while it is served.
            t.Build(P, "extractor", 0, 1, 1);
            var storage = t.Build(P, "storage", 0, 7, 1);
            t.Line(P, 0, 1, 1, 0, 2, 1, 0, 3, 1, 0, 4, 1, 0, 5, 1, 0, 6, 1, 0, 7, 1);
            for (int i = 0; i < 300; i++)
            {
                t.FactoryTick();
                if (i > 150) Assert.IsFalse(t.S.Pull.BlockedReasonByBuilding.ContainsKey(storage.id), "tick " + t.W.tick + ": " + string.Join(";", t.W.pullRequests.ConvertAll(r => r.targetBuildingId + "/" + r.itemId + "@" + r.createdTick)));
            }
        }
    }
}
