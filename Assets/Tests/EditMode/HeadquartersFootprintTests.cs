using NUnit.Framework;

namespace OrbitalFoundry.Tests
{
    /// <summary>The headquarters fills the 3x3 square around its tile: nothing is built or belted there.</summary>
    public class HeadquartersFootprintTests
    {
        const FactionId P = FactionId.Player;

        [Test]
        public void NothingIsBuiltOrBeltedInsideTheHeadquartersSquare()
        {
            using (var t = new TestWorld())
            {
                var hq = t.W.buildings.Find(b => b.typeId == "headquarters" && b.owner == P);
                var place = new GameCommand { kind = CommandKind.PlaceBuilding, actor = P, contentId = "storage", fromCellId = hq.cellId, fromX = hq.x + 1, fromY = hq.y + 1 };
                Assert.AreEqual("FOOTPRINT", t.Run(place).code, "a corner of the square is taken");
                var belt = new GameCommand { kind = CommandKind.PlaceBelt, actor = P, fromCellId = hq.cellId, fromX = hq.x + 2, fromY = hq.y, toCellId = hq.cellId, toX = hq.x + 1, toY = hq.y };
                Assert.AreEqual("FOOTPRINT", t.Run(belt).code, "a belt cannot run into it");
                place.fromX = hq.x + 2;
                place.fromY = hq.y + 2;
                Assert.IsTrue(t.Run(place).success, "just outside the square is fine");
            }
        }
    }
}
