using NUnit.Framework;
using UnityEngine;

namespace OrbitalFoundry.Tests
{
    /// <summary>The scattered asteroid field: irregular, repeatable per map, and never crowded.</summary>
    public class MapLayoutTests
    {
        [Test]
        public void AsteroidsAlwaysKeepTheirClearance()
        {
            var catalog = CatalogRepository.LoadContent();
            for (int seed = 1; seed <= 40; seed++)
            {
                var map = CatalogRepository.LoadMap(MapGenerator.IdFor(seed * 7919), catalog);
                for (int i = 0; i < map.cells.Count; i++)
                    for (int j = i + 1; j < map.cells.Count; j++)
                    {
                        var a = map.cells[i];
                        var b = map.cells[j];
                        var pa = MapLayout.Origin(map, a);
                        var pb = MapLayout.Origin(map, b);
                        float gapX = Mathf.Max(pb.x - (pa.x + a.gridWidth), pa.x - (pb.x + b.gridWidth));
                        float gapZ = Mathf.Max(pb.z - (pa.z + a.gridHeight), pa.z - (pb.z + b.gridHeight));
                        Assert.GreaterOrEqual(Mathf.Max(gapX, gapZ), MapLayout.MinClearance - 0.05f,
                            map.id + ": cells " + a.id + " and " + b.id + " are too close");
                    }
            }
        }

        [Test]
        public void EachMapScattersDifferentlyButRepeatably()
        {
            var catalog = CatalogRepository.LoadContent();
            var first = CatalogRepository.LoadMap(MapGenerator.IdFor(101), catalog);
            var again = CatalogRepository.LoadMap(MapGenerator.IdFor(101), catalog);
            var other = CatalogRepository.LoadMap(MapGenerator.IdFor(202), catalog);
            var cell = first.cells[3];
            Assert.AreEqual(MapLayout.Origin(first, cell), MapLayout.Origin(again, again.cells[3]), "the same map lays out the same way");
            Assert.AreNotEqual(MapLayout.Origin(first, cell), MapLayout.Origin(other, other.cells[3]), "another map scatters differently");
        }
    }
}
