using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace OrbitalFoundry.Tests
{
    /// <summary>Random maps: guaranteed starting kits, rare spread, open space around asteroids, and saves.</summary>
    public class MapGeneratorTests
    {
        static int Count(MapCellDefinition cell, string item)
        {
            int n = 0;
            foreach (var node in cell.nodes) if (node.itemId == item) n++;
            return n;
        }

        [Test]
        public void EverySeedGivesAValidFairMap()
        {
            var catalog = CatalogRepository.LoadContent();
            for (int seed = 1; seed <= 60; seed++)
            {
                var map = CatalogRepository.LoadMap(MapGenerator.IdFor(seed), catalog); // validates
                var totals = new Dictionary<string, int>();
                foreach (var cell in map.cells)
                {
                    foreach (var n in cell.nodes) { int v; totals.TryGetValue(n.itemId, out v); totals[n.itemId] = v + 1; }
                    Assert.IsTrue(MapGenerator.StaysConnected(cell), "seed " + seed + " cell " + cell.id + " is cut up by scenery");
                    if (cell.headquartersFor != FactionId.Neutral)
                    {
                        Assert.AreEqual(2, Count(cell, "ore"), "seed " + seed + " HQ iron");
                        Assert.AreEqual(1, Count(cell, "copper_ore"), "seed " + seed + " HQ copper");
                        Assert.AreEqual(1, Count(cell, "ice"), "seed " + seed + " HQ ice");
                        Assert.AreEqual(1, Count(cell, "crystal"), "seed " + seed + " HQ Ion");
                        Assert.AreEqual(1, Count(cell, "silicon_ore"), "seed " + seed + " HQ silicon");
                    }
                    else if (cell.startingOwner != FactionId.Neutral)
                    {
                        foreach (var item in new[] { "ore", "copper_ore", "crystal", "ice" })
                            Assert.GreaterOrEqual(Count(cell, item), 1, "seed " + seed + " outpost " + cell.id + " " + item);
                    }
                }
                foreach (var rare in new[] { "titanium_ore", "sulphur" })
                    Assert.AreEqual(2, totals.ContainsKey(rare) ? totals[rare] : 0, "seed " + seed + " " + rare);
                foreach (var extra in new[] { "unobtanium_ore", "complex_carbon", "gold_ore" })
                    Assert.AreEqual(1, totals.ContainsKey(extra) ? totals[extra] : 0, "seed " + seed + " " + extra);
            }
        }

        [Test]
        public void SameSeedSameMapDifferentSeedDifferentMap()
        {
            var template = CatalogRepository.LoadMap(MapGenerator.TemplateMapId);
            string a = JsonUtility.ToJson(MapGenerator.Generate(template, 42));
            Assert.AreEqual(a, JsonUtility.ToJson(MapGenerator.Generate(template, 42)));
            Assert.AreNotEqual(a, JsonUtility.ToJson(MapGenerator.Generate(template, 43)));
        }

        [Test]
        public void SavesRebuildTheirGeneratedMap()
        {
            using (var t = new TestWorld(MapGenerator.IdFor(1234)))
            {
                t.FactoryTick();
                Assert.IsTrue(t.Loop.Saves.Save(t.W, t.Catalog).success);
                WorldState loaded;
                var template = CatalogRepository.LoadMap(MapGenerator.TemplateMapId, t.Catalog);
                var r = t.Loop.Saves.TryLoad(t.Catalog, template, out loaded);
                Assert.IsTrue(r.success, r.code + " " + r.message);
                Assert.AreEqual(MapGenerator.IdFor(1234), t.Loop.Saves.LoadedMap.id);
                Assert.AreEqual(t.W.nodes.Count, loaded.nodes.Count);
            }
        }

        [Test]
        public void AiBuildsAFactoryOnAGeneratedMap()
        {
            using (var t = new TestWorld(MapGenerator.IdFor(77)))
            {
                for (int i = 0; i < 6000 && t.Ai.cumulativeSales == 0; i++) t.Loop.TickNormal();
                Assert.Greater(t.Ai.cumulativeSales, 0, "AI planner: " + t.S.AiPlanner.LastReason);
            }
        }
    }
}
