using NUnit.Framework;
using UnityEngine;

namespace OrbitalFoundry.Tests
{
    /// <summary>The Meshy visual style: models load by item id, fit a cargo slot, and anything missing falls back.</summary>
    public class MeshyStyleTests
    {
        GameObject parent;

        [SetUp]
        public void SetUp() { parent = new GameObject("Parent"); }

        [TearDown]
        public void TearDown()
        {
            MeshyLibrary.Enabled = false;
            Object.DestroyImmediate(parent);
        }

        [Test]
        public void AModelIsScaledToACargoSlotAndStoodOnTheBelt()
        {
            MeshyLibrary.Enabled = true;
            var cargo = MeshyLibrary.Cargo("test_block", parent.transform, 1, new Vector3[5], 0.62f);
            Assert.IsNotNull(cargo, "the test model loads by its name");
            var bounds = cargo.GetComponent<MeshFilter>().sharedMesh.bounds;
            Assert.AreEqual(0.26f, Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z), 0.001f, "largest side fits the slot");
            Assert.AreEqual(0f, bounds.min.y, 0.001f, "resting on the belt");
            Assert.AreEqual(0f, bounds.center.x, 0.001f, "centred");
            Assert.AreEqual(0f, bounds.center.z, 0.001f, "centred");
            Assert.IsNotNull(cargo.GetComponent<MeshRenderer>(), "one renderer, like a built-in cargo model");
        }

        [Test]
        public void AStackIsAPileOfTheModel()
        {
            MeshyLibrary.Enabled = true;
            var spots = new[] { new Vector3(-0.08f, 0, 0), new Vector3(0.08f, 0, 0), new Vector3(0, 0.13f, 0), Vector3.zero, Vector3.zero };
            var one = MeshyLibrary.Cargo("test_block", parent.transform, 1, spots, 0.62f).GetComponent<MeshFilter>().sharedMesh;
            var three = MeshyLibrary.Cargo("test_block", parent.transform, 3, spots, 0.62f).GetComponent<MeshFilter>().sharedMesh;
            Assert.AreEqual(one.vertexCount * 3, three.vertexCount);
        }

        [Test]
        public void LightWeightOrAMissingModelKeepsTheBuiltInLook()
        {
            MeshyLibrary.Enabled = false;
            Assert.IsNull(MeshyLibrary.Cargo("test_block", parent.transform, 1, new Vector3[5], 0.62f), "Light weight never uses Meshy");
            MeshyLibrary.Enabled = true;
            Assert.IsNull(MeshyLibrary.Cargo("no_such_item", parent.transform, 1, new Vector3[5], 0.62f));
            var builtIn = ModelLibrary.Cargo("no_such_item", Color.red, parent.transform);
            Assert.IsNotNull(builtIn.GetComponent<MeshRenderer>(), "the built-in model stands in");
        }

        [Test]
        public void TheVisualStyleIsASavedSetting()
        {
            var copy = JsonUtility.FromJson<GameSettings>(JsonUtility.ToJson(new GameSettings { visualStyle = GameSettings.VisualMeshy }));
            Assert.AreEqual(GameSettings.VisualMeshy, copy.visualStyle);
            Assert.AreEqual(GameSettings.VisualLightWeight, new GameSettings().visualStyle, "Light weight is the default");
        }
    }
}
