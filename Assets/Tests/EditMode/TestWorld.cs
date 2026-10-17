using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

namespace OrbitalFoundry.Tests
{
    /// <summary>Builds isolated game worlds for EditMode tests. Saves go to a temporary directory only.</summary>
    public sealed class TestWorld : IDisposable
    {
        public readonly ContentCatalog Catalog;
        public readonly MapDefinition Map;
        public readonly GameLoop Loop = new GameLoop();
        public readonly string SaveDir;

        public WorldState W { get { return Loop.World; } }
        public GameServices S { get { return Loop.Services; } }

        public TestWorld(string mapId = "map_a", Action<MapDefinition> editMap = null)
        {
            Catalog = CatalogRepository.LoadContent();
            Map = CatalogRepository.LoadMap(mapId, Catalog);
            if (editMap != null) editMap(Map);
            SaveDir = Path.Combine(Path.GetTempPath(), "OrbitalFoundryTests", Guid.NewGuid().ToString("N"));
            var world = GameFactory.CreateNew(Catalog, Map, 1234);
            Loop.Initialize(Catalog, Map, world, new SaveService(SaveDir));
        }

        /// <summary>
        /// Test setup for flow tests that count deliveries by a Sale Depot's sales: raw ores (and nothing else) sell
        /// for 1 each again. The shipped game does not buy raw ore.
        /// </summary>
        public void SellRawOre()
        {
            foreach (var item in Catalog.items)
            {
                if (item.sellable || item.id == Catalog.rules.ionItemId || Catalog.recipes.Exists(r => r.outputItemId == item.id)) continue;
                item.sellable = true;
                item.baseSaleValue = 1;
            }
        }

        public void Dispose()
        {
            try { if (Directory.Exists(SaveDir)) Directory.Delete(SaveDir, true); } catch (IOException) { }
        }

        public CommandResult Run(GameCommand cmd)
        {
            return S.Commands.Execute(W, Catalog, Map, cmd);
        }

        public void MustRun(GameCommand cmd)
        {
            var r = Run(cmd);
            Assert.IsTrue(r.success, cmd.kind + " failed: " + r.code + " " + r.message);
        }

        /// <summary>
        /// Places a building. Machines start with no recipe, so for test convenience this picks the first one the
        /// faction can run, and storages store every good (pass chooseRecipe false to keep it empty).
        /// </summary>
        public BuildingState Build(FactionId actor, string type, int cell, int x, int y, bool chooseRecipe = true)
        {
            MustRun(new GameCommand { kind = CommandKind.PlaceBuilding, actor = actor, contentId = type, fromCellId = cell, fromX = x, fromY = y });
            var b = WorldLookup.GetBuildingAt(W, cell, x, y);
            var def = CatalogRepository.GetBuilding(Catalog, type);
            // Storages start storing nothing; for test convenience they store everything (pass chooseRecipe false to keep it empty).
            if (chooseRecipe && b != null && def.kind == BuildingKind.Storage)
                MustRun(new GameCommand { kind = CommandKind.ToggleDepotItem, actor = actor, buildingId = b.id, contentId = CommandRouter.StoreAll });
            if (chooseRecipe && b != null && string.IsNullOrEmpty(b.selectedRecipeId))
                foreach (var rid in def.allowedRecipeIds)
                    if (ResearchService.IsRecipeUnlocked(Catalog, WorldLookup.GetFaction(W, actor), CatalogRepository.GetRecipe(Catalog, rid)))
                    {
                        MustRun(new GameCommand { kind = CommandKind.SelectRecipe, actor = actor, buildingId = b.id, contentId = rid });
                        break;
                    }
            return b;
        }

        public BeltSegmentState Belt(FactionId actor, int c1, int x1, int y1, int c2, int x2, int y2)
        {
            MustRun(new GameCommand { kind = CommandKind.PlaceBelt, actor = actor, fromCellId = c1, fromX = x1, fromY = y1, toCellId = c2, toX = x2, toY = y2 });
            return W.belts[W.belts.Count - 1];
        }

        /// <summary>Lays belts along consecutive (cell,x,y) points.</summary>
        public List<BeltSegmentState> Line(FactionId actor, params int[] cxy)
        {
            var list = new List<BeltSegmentState>();
            for (int i = 0; i + 5 < cxy.Length; i += 3)
                list.Add(Belt(actor, cxy[i], cxy[i + 1], cxy[i + 2], cxy[i + 3], cxy[i + 4], cxy[i + 5]));
            return list;
        }

        /// <summary>Lays belts along a straight global row from gx0 to gx1 (inclusive), crossing cell borders as needed.</summary>
        public void GlobalRow(FactionId actor, int gy, int gx0, int gx1)
        {
            int step = gx1 > gx0 ? 1 : -1;
            for (int gx = gx0; gx != gx1; gx += step)
            {
                int c1, x1, y1, c2, x2, y2;
                Assert.IsTrue(GridRules.TryGlobalToLocal(Map, gx, gy, out c1, out x1, out y1));
                Assert.IsTrue(GridRules.TryGlobalToLocal(Map, gx + step, gy, out c2, out x2, out y2));
                Belt(actor, c1, x1, y1, c2, x2, y2);
            }
        }

        public CraftState AddCraft(FactionId owner, string type, int cellId, bool defender)
        {
            var def = CatalogRepository.GetCraft(Catalog, type);
            var craft = new CraftState { id = WorldLookup.AllocateId(W), owner = owner, typeId = type, cellId = cellId, currentHealth = def.maxHealth };
            W.crafts.Add(craft);
            if (defender)
            {
                // Only a cell's owner can assign its defender.
                var cell = WorldLookup.GetCell(W, cellId);
                cell.owner = owner;
                cell.defenderCraftId = craft.id;
            }
            return craft;
        }

        /// <summary>Removes the map's starting craft so combat tests control every participant.</summary>
        public void ClearCraft()
        {
            W.crafts.Clear();
            foreach (var c in W.cells) c.defenderCraftId = -1;
        }

        /// <summary>Test setup: gives a faction research levels without paying for them.</summary>
        public void Research(FactionId f, string nodeId, int level)
        {
            var faction = WorldLookup.GetFaction(W, f);
            var p = faction.research.Find(r => r.nodeId == nodeId);
            if (p == null) faction.research.Add(new ResearchProgress { nodeId = nodeId, level = level });
            else p.level = level;
        }

        public void SetOwner(int cellId, FactionId owner)
        {
            WorldLookup.GetCell(W, cellId).owner = owner;
        }

        /// <summary>Test setup: gives the owner the cell with a completed Fort at its centre, so its factory is online.</summary>
        public BuildingState Fortify(int cellId, FactionId owner)
        {
            SetOwner(cellId, owner);
            int x, y;
            Assert.IsTrue(GameFactory.FindCentreTile(W, WorldLookup.GetCellDef(Map, cellId), out x, out y));
            var fort = new BuildingState { id = WorldLookup.AllocateId(W), owner = owner, typeId = "fort", cellId = cellId, x = x, y = y, anchor = true };
            W.buildings.Add(fort);
            OwnershipService.RefreshFactoryOnline(W);
            return fort;
        }

        /// <summary>Normal tick without AI or travel, for isolated factory/ownership tests.</summary>
        public void FactoryTick()
        {
            if (W.mode != MatchMode.Factory) return;
            W.tick++;
            S.Ownership.ExpireInactiveStructures(W, Catalog);
            S.Belts.EnsureCurrent(W);
            S.Factory.Tick(W, Catalog, Map, S.Belts, S.Pull, S.Economy);
            S.Capture.TickNormalHolds(W, Catalog.rules, S.Ownership, Catalog);
            S.Ownership.EvaluateOutcome(W, Catalog.rules);
        }

        public void AssertBeltCapacity()
        {
            var load = new Dictionary<int, int>();
            foreach (var t in W.tokens)
            {
                int seg = t.routeSegmentIds[t.segmentIndex];
                int n; load.TryGetValue(seg, out n);
                load[seg] = n + 1;
                Assert.LessOrEqual(n + 1, Catalog.rules.beltCapacityPerSegment, "Belt " + seg + " exceeded capacity at tick " + W.tick);
            }
        }

        public FactionState Player { get { return WorldLookup.GetFaction(W, FactionId.Player); } }
        public FactionState Ai { get { return WorldLookup.GetFaction(W, FactionId.Ai); } }

        /// <summary>
        /// Builds a complete relay chain in the player HQ (cell 0 of map_a). Iron and copper bars merge onto one shared
        /// edge before the assembler, so mixed items share capacity.
        /// </summary>
        public void BuildPlayerRelayChain()
        {
            var P = FactionId.Player;
            Build(P, "extractor", 0, 1, 1);     // ore node 101
            Build(P, "refinery", 0, 3, 1);
            Build(P, "extractor", 0, 4, 4);     // copper node 102
            var copper = Build(P, "refinery", 0, 4, 2);
            MustRun(new GameCommand { kind = CommandKind.SelectRecipe, actor = P, buildingId = copper.id, contentId = "refine_copper" });
            Build(P, "assembler", 0, 3, 3);
            Build(P, "sale_depot", 0, 1, 3);
            Line(P, 0, 1, 1, 0, 2, 1, 0, 3, 1);              // ore -> refinery
            Line(P, 0, 3, 1, 0, 3, 2);                       // refinery -> merge vertex
            Line(P, 0, 4, 4, 0, 4, 3, 0, 4, 2);              // copper -> copper refinery
            Line(P, 0, 4, 2, 0, 3, 2);                       // copper refinery -> merge vertex
            Line(P, 0, 3, 2, 0, 3, 3);                       // shared edge into assembler
            Line(P, 0, 3, 3, 0, 2, 3, 0, 1, 3);              // assembler -> depot
        }
    }
}
