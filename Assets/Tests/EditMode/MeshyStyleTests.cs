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
        public void ABuildingFitsItsFootprintStandingOnTheDeck()
        {
            MeshyLibrary.Enabled = true;
            var model = MeshyLibrary.Building("test_tower", FactionId.Player, Color.yellow, parent.transform, 1);
            Assert.IsNotNull(model, "the test building loads by its type id");
            Assert.AreEqual(1, model.Renderers.Count, "just the body: no owner plinth (the platform shows the owner)");
            var body = model.Renderers[0].GetComponent<MeshFilter>().sharedMesh.bounds;
            Assert.AreEqual(0.9f, Mathf.Max(body.size.x, body.size.z), 0.001f, "90% of a one-tile footprint");
            Assert.AreEqual(0f, body.min.y, 0.001f, "standing on the deck");
            var paints = System.Array.ConvertAll(model.Renderers[0].sharedMaterials, m => m.GetColor("_BaseColor"));
            Assert.Greater(paints.Length, 1, "an untextured model is painted in several parts");
            Assert.Contains(Palette.Ivory, paints, "its sides are ivory");
            Assert.Contains(Palette.Gunmetal, paints, "its underside is gunmetal");

            // The test tower is twice as tall as it is wide. A big footprint spans 97% of its square, so on two tiles
            // it would be 1.94 wide and 3.88 tall: the taller cap for two tiles (3.8) stops it at 1.9 by 3.8.
            var wide = MeshyLibrary.Building("test_tower", FactionId.Ai, Color.yellow, parent.transform, 2).Renderers[0].GetComponent<MeshFilter>().sharedMesh.bounds;
            Assert.AreEqual(1.9f, Mathf.Max(wide.size.x, wide.size.z), 0.02f, "nearly twice as wide as on one tile");
            Assert.AreEqual(3.8f, wide.size.y, 0.02f, "and as tall as the taller cap for bigger footprints allows");
            // On three tiles (the headquarters) the cap is 5.2: the same tower stands 5.2 tall, not squashed to 2.4.
            var big = MeshyLibrary.Building("test_tower", FactionId.Ai, Color.yellow, parent.transform, 3).Renderers[0].GetComponent<MeshFilter>().sharedMesh.bounds;
            Assert.AreEqual(5.2f, big.size.y, 0.02f);

            model.SetState(Model.Faded);
            Assert.AreNotEqual(model.Renderers[0].sharedMaterial, MeshyLibrary.Building("test_tower", FactionId.Player, Color.yellow, parent.transform, 1).Renderers[0].sharedMaterial, "Belt View fades it like any building");
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

        [Test]
        public void HeadquartersAntennaTurnsAndFlashesWithoutMovingTheDome()
        {
            MeshyLibrary.Enabled = true;
            var model = MeshyLibrary.Building("headquarters", FactionId.Player, Color.white, parent.transform, 3);
            Assert.IsNotNull(model);
            var body = model.Root.transform.Find("Body");
            var antenna = body.Find("Spinner");
            var glow = body.Find("Glow");
            Assert.IsNotNull(antenna, "the antenna geometry is cut out for rotation");
            Assert.Greater(Triangles(antenna.GetComponent<MeshFilter>().sharedMesh), 0);
            Assert.IsNotNull(glow, "the antenna orb has a signal light");

            var domeRotation = body.localRotation;
            model.SetState(Model.Normal);
            model.Animate(0.05f);
            Assert.Greater(Quaternion.Angle(Quaternion.identity, antenna.localRotation), 0f);
            Assert.AreEqual(domeRotation, body.localRotation, "the HQ body stays still");
            Assert.Greater(glow.localScale.x, 0f, "the first flash is lit");
            model.Animate(0.10f);
            Assert.AreEqual(0f, glow.localScale.x, "the two flashes have a dark gap");
            model.Animate(0.15f);
            Assert.Greater(glow.localScale.x, 0f, "the second flash is lit");
        }

        [Test]
        public void PurifierHasMovingFanTwoLitBubbleTanksAndThreeOrbitRings()
        {
            MeshyLibrary.Enabled = true;
            var model = MeshyLibrary.Building("purifier", FactionId.Player, Color.white, parent.transform, 1);
            Assert.IsNotNull(model, "the imported purifier loads as its Meshy building");
            var body = model.Root.transform.Find("Body");
            var fan = body.Find("Spinner");
            Assert.IsNotNull(fan, "fan blades are cut out for rotation");
            Assert.Greater(Triangles(fan.GetComponent<MeshFilter>().sharedMesh), 0);
            Assert.AreEqual(2, CountNamed(body, "TankPulse"));
            Assert.AreEqual(22, CountNamed(body, "Bubble"));
            Assert.AreEqual(3, CountNamed(body, "OrbitRing"));
            Assert.AreEqual(2, CountNamed(body, "Glass"), "both tank windows are transparent");
            model.SetState(Model.Normal);
            model.Animate(1f);
            Assert.Greater(Quaternion.Angle(Quaternion.identity, fan.localRotation), 0f);
            foreach (Transform child in body)
                if (child.name == "OrbitRing")
                    Assert.Greater(Quaternion.Angle(Quaternion.identity, child.localRotation), 0f);
        }

        [Test]
        public void FortTurretScansChecksTargetAndCarriesItsMovingRadar()
        {
            MeshyLibrary.Enabled = true;
            var model = MeshyLibrary.Building("fort", FactionId.Player, Color.white, parent.transform, 1);
            Assert.IsNotNull(model);
            var body = model.Root.transform.Find("Body");
            var turret = body.Find("Spinner");
            var yaw = turret != null ? turret.Find("RadarYaw") : null;
            var tilt = yaw != null ? yaw.Find("RadarTilt") : null;
            Assert.IsNotNull(turret, "turret is separated from the fixed fort base");
            Assert.IsNotNull(yaw, "radar turns as a turret child");
            Assert.IsNotNull(tilt, "radar dish tilts within its turning mount");
            Assert.Greater(Triangles(turret.GetComponent<MeshFilter>().sharedMesh), 0);
            Assert.Greater(Triangles(tilt.GetComponent<MeshFilter>().sharedMesh), 0);

            var baseRotation = body.localRotation;
            model.Animate(1f);
            Assert.Greater(Quaternion.Angle(Quaternion.identity, turret.localRotation), 0f);
            Assert.Greater(Quaternion.Angle(Quaternion.identity, yaw.localRotation), 0f);
            Assert.Greater(Quaternion.Angle(Quaternion.identity, tilt.localRotation), 0f);
            Assert.AreEqual(baseRotation, body.localRotation, "fort base stays still");

            model.Animate(7.4f); // 8.4 seconds: just before a target check
            var beforeCheck = turret.localRotation;
            model.Animate(0.35f);
            Assert.Greater(Quaternion.Angle(beforeCheck, turret.localRotation), 40f,
                "brief target check turns much faster than the normal scan");
            Assert.AreEqual(turret, yaw.parent, "radar continues riding the turret during the quick turn");
        }

        static int CountNamed(Transform parent, string name)
        {
            int count = 0;
            foreach (var t in parent.GetComponentsInChildren<Transform>()) if (t.name == name) count++;
            return count;
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
