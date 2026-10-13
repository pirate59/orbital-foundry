using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace OrbitalFoundry.Tests
{
    /// <summary>The help popups' content: every link resolves and every building has an explanation.</summary>
    public class HelpContentTests
    {
        [Test]
        public void EveryTopicLinkResolvesAndEveryBuildingIsExplained()
        {
            var asset = Resources.Load<TextAsset>("help");
            Assert.IsNotNull(asset, "Resources/help.json");
            var content = JsonUtility.FromJson<HelpContent>(asset.text);
            var ids = new HashSet<string>();
            foreach (var t in content.topics)
            {
                Assert.IsTrue(ids.Add(t.id), "duplicate topic " + t.id);
                Assert.IsNotEmpty(t.title, t.id);
                Assert.IsNotEmpty(t.sections, t.id);
            }
            foreach (var t in content.topics)
                foreach (var r in t.related) Assert.IsTrue(ids.Contains(r), t.id + " links to missing topic " + r);
            foreach (var b in CatalogRepository.LoadContent().buildings)
                Assert.IsTrue(ids.Contains(b.id), "no help topic for building " + b.id);
            foreach (var id in new[] { "getting_started", "ion", "resource_nodes", "territory", "crafts", "research", "belts" })
                Assert.IsTrue(ids.Contains(id), "the HUD links to " + id);
        }
    }
}
