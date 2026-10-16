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
            var cargo = MeshyLibrary.Cargo("test_block", Color.red, parent.transform, 1, new Vector3[5], 0.62f);
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
            var one = MeshyLibrary.Cargo("test_block", Color.red, parent.transform, 1, spots, 0.62f).GetComponent<MeshFilter>().sharedMesh;
            var three = MeshyLibrary.Cargo("test_block", Color.red, parent.transform, 3, spots, 0.62f).GetComponent<MeshFilter>().sharedMesh;
            Assert.AreEqual(Triangles(one) * 3, Triangles(three), "three copies of the model");
        }

        [Test]
        public void LightWeightOrAMissingModelKeepsTheBuiltInLook()
        {
            MeshyLibrary.Enabled = false;
            Assert.IsNull(MeshyLibrary.Cargo("test_block", Color.red, parent.transform, 1, new Vector3[5], 0.62f), "Light weight never uses Meshy");
            MeshyLibrary.Enabled = true;
            Assert.IsNull(MeshyLibrary.Cargo("no_such_item", Color.red, parent.transform, 1, new Vector3[5], 0.62f));
            var builtIn = ModelLibrary.Cargo("no_such_item", Color.red, parent.transform);
            Assert.IsNotNull(builtIn.GetComponent<MeshRenderer>(), "the built-in model stands in");
        }

        [Test]
        public void ABuildingFitsItsFootprintOnAPlinthInTheOwnersColour()
        {
            MeshyLibrary.Enabled = true;
            var model = MeshyLibrary.Building("test_tower", FactionId.Player, Color.yellow, parent.transform, 1);
            Assert.IsNotNull(model, "the test building loads by its type id");
            Assert.AreEqual(2, model.Renderers.Count, "plinth and body");
            var body = model.Renderers[1].GetComponent<MeshFilter>().sharedMesh.bounds;
            Assert.AreEqual(0.9f, Mathf.Max(body.size.x, body.size.z), 0.001f, "90% of a one-tile footprint");
            Assert.AreEqual(0f, body.min.y, 0.001f, "standing on its plinth");
            Assert.AreEqual(ModelLibrary.FactionPaint(FactionId.Player), model.Renderers[0].sharedMaterial.GetColor("_BaseColor"), "the plinth shows the owner");
            var paints = System.Array.ConvertAll(model.Renderers[1].sharedMaterials, m => m.GetColor("_BaseColor"));
            Assert.Greater(paints.Length, 1, "an untextured model is painted in several parts");
            Assert.Contains(Palette.Ivory, paints, "its sides are ivory");
            Assert.Contains(Palette.Gunmetal, paints, "its underside is gunmetal");

            // The test tower is twice as tall as it is wide. A big footprint spans 97% of its square, so on two tiles
            // it would be 1.94 wide and 3.88 tall: the taller cap for two tiles (3.8) stops it at 1.9 by 3.8.
            var wide = MeshyLibrary.Building("test_tower", FactionId.Ai, Color.yellow, parent.transform, 2).Renderers[1].GetComponent<MeshFilter>().sharedMesh.bounds;
            Assert.AreEqual(1.9f, Mathf.Max(wide.size.x, wide.size.z), 0.02f, "nearly twice as wide as on one tile");
            Assert.AreEqual(3.8f, wide.size.y, 0.02f, "and as tall as the taller cap for bigger footprints allows");
            // On three tiles (the headquarters) the cap is 5.2: the same tower stands 5.2 tall, not squashed to 2.4.
            var big = MeshyLibrary.Building("test_tower", FactionId.Ai, Color.yellow, parent.transform, 3).Renderers[1].GetComponent<MeshFilter>().sharedMesh.bounds;
            Assert.AreEqual(5.2f, big.size.y, 0.02f);

            model.SetState(Model.Faded);
            Assert.AreNotEqual(model.Renderers[1].sharedMaterial, MeshyLibrary.Building("test_tower", FactionId.Player, Color.yellow, parent.transform, 1).Renderers[1].sharedMaterial, "Belt View fades it like any building");
        }

        [Test]
        public void BuildingsWithoutAModelOrInLightWeightUseTheBuiltInOne()
        {
            MeshyLibrary.Enabled = false;
            Assert.IsNull(MeshyLibrary.Building("test_tower", FactionId.Player, Color.yellow, parent.transform, 1));
            MeshyLibrary.Enabled = true;
            Assert.IsNull(MeshyLibrary.Building("no_such_building", FactionId.Player, Color.yellow, parent.transform, 1), "no Meshy model for this type");
            Assert.IsNotNull(ModelLibrary.Building("refinery", FactionId.Player, parent.transform), "the built-in refinery stands in");
        }

        static int Triangles(Mesh m)
        {
            int n = 0;
            for (int s = 0; s < m.subMeshCount; s++) n += (int)m.GetIndexCount(s) / 3;
            return n;
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
