using NUnit.Framework;
using UnityEngine;

namespace OrbitalFoundry.Tests
{
    /// <summary>
    /// The live loop runs each tick's AI and raids half on the frame after its factory half; play must come out exactly
    /// as if every tick ran whole.
    /// </summary>
    public class TickPacingTests
    {
        static GameLoop NewLoop(ContentCatalog catalog, MapDefinition map)
        {
            var loop = new GameLoop();
            loop.Initialize(catalog, map, GameFactory.CreateNew(catalog, map, 4321), null);
            return loop;
        }

        [Test]
        public void SplitTicksPlayTheSameAsWholeTicks()
        {
            var catalog = CatalogRepository.LoadContent();
            var map = CatalogRepository.LoadMap("map_a", catalog);
            var whole = NewLoop(catalog, map);
            var split = NewLoop(catalog, map);
            float tickSeconds = catalog.rules.tickMilliseconds / 1000f;
            const int ticks = 1200; // long enough for the AI to plan and build
            for (int i = 0; i < ticks; i++) whole.TickNormal();
            for (int i = 0; i < ticks; i++)
            {
                split.Update(tickSeconds * 0.5f);
                split.Update(tickSeconds * 0.5f); // a tick's factory half lands on this frame...
            }
            split.Update(0f); // ...and its thinking half on the next
            Assert.AreEqual(ticks, split.World.tick);
            Assert.Greater(split.World.buildings.FindAll(b => b.owner == FactionId.Ai).Count, 1, "the AI acted during the run");
            Assert.AreEqual(JsonUtility.ToJson(whole.World), JsonUtility.ToJson(split.World));
        }
    }
}
