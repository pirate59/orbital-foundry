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
        public void ShipyardStandsOnTwelveCornerLegsClearOfEveryBeltLane()
        {
            MeshyLibrary.Enabled = true;
            var model = MeshyLibrary.Building("shipyard", FactionId.Player, Color.white, parent.transform, 3);
            Assert.IsNotNull(model);
            // Stretched to 3x2 as the world view does: unrotated, centred on the footprint, long side along x.
            model.Root.transform.localScale = new Vector3(1f, 1f, 1.6f);
            MeshyLibrary.PlaceLegs(model, 3, 2);
            var legs = new System.Collections.Generic.List<Bounds>();
            foreach (var r in model.Renderers) if (r.name == "Leg") legs.Add(r.bounds);
            Assert.AreEqual(12, legs.Count, "a leg on every corner of a 3x2 footprint: 4 by 3");
            // A belt runs along the middle of a tile with its rails 0.23 either side, so a leg must stay within
            // 0.268 of a tile edge on both axes (here edges lie on whole and half numbers from the centre).
            foreach (var leg in legs)
            {
                float edgeX = Mathf.Round(leg.center.x + 0.5f) - 0.5f, edgeZ = Mathf.Round(leg.center.z);
                Assert.LessOrEqual(Mathf.Max(Mathf.Abs(leg.min.x - edgeX), Mathf.Abs(leg.max.x - edgeX)), 0.268f, "leg at " + leg.center + " reaches a lane across x");
                Assert.LessOrEqual(Mathf.Max(Mathf.Abs(leg.min.z - edgeZ), Mathf.Abs(leg.max.z - edgeZ)), 0.268f, "leg at " + leg.center + " reaches a lane across z");
                Assert.LessOrEqual(Mathf.Abs(leg.center.x), 1.5f, "inside the footprint");
                Assert.LessOrEqual(Mathf.Abs(leg.center.z), 1f, "inside the footprint");
            }
        }

        [Test]
        public void ShipyardHoistCarriesItsHullSectionAwayAndBackWhileFiveArmsWeld()
        {
            MeshyLibrary.Enabled = true;
            var model = MeshyLibrary.Building("shipyard", FactionId.Player, Color.white, parent.transform, 3);
            var load = Find(model.Root.transform, "Hoist load");
            var carriage = Find(model.Root.transform, "Hoist carriage");
            Assert.IsNotNull(load, "the claw and its hull section are cut out");
            Assert.IsNotNull(carriage);
            Assert.IsNotNull(Find(model.Root.transform, "Hoist cables"));
            Assert.AreEqual(5, CountNamed(model.Root.transform, "Welding arm"), "five welding arms");
            var rest = load.localPosition;
            var carriageRest = carriage.localPosition;
            var arm = Find(model.Root.transform, "Welding arm");
            var armRest = arm.localRotation;
            for (int i = 0; i < 180; i++) model.Animate(0.05f); // 9 s: half way through the 18 s cycle
            Assert.Greater(load.localPosition.y, rest.y + 0.1f, "hoisted");
            Assert.Less(load.localPosition.x, rest.x - 0.3f, "carried toward the nose");
            Assert.AreEqual(carriage.localPosition.x - carriageRest.x, load.localPosition.x - rest.x, 0.001f, "the carriage carries it");
            for (int i = 0; i < 180; i++) model.Animate(0.05f); // a whole cycle
            Assert.AreEqual(rest.y, load.localPosition.y, 0.001f, "set back in place");
            Assert.AreEqual(rest.x, load.localPosition.x, 0.001f);
            bool leaned = false;
            for (int i = 0; i < 120 && !leaned; i++) { model.Animate(0.05f); leaned = Quaternion.Angle(arm.localRotation, armRest) > 5f; }
            Assert.IsTrue(leaned, "the arm leans in to weld");
        }

        [Test]
        public void SolarDishTurnsSlowlyAndTiltsUpAndDown()
        {
            MeshyLibrary.Enabled = true;
            var model = MeshyLibrary.Building("solar_array", FactionId.Player, Color.white, parent.transform, 1);
            var turn = Find(model.Root.transform, "Tracker turn");
            var tilt = Find(model.Root.transform, "Tracker tilt");
            Assert.IsNotNull(turn, "the dish's mount turns");
            Assert.IsNotNull(tilt, "the dish tilts");
            Assert.AreSame(turn, tilt.parent, "the dish rides on the turning mount");
            for (int i = 0; i < 150; i++) model.Animate(0.05f); // 7.5 s: a twelfth of a turn, a quarter of a tilt
            Assert.AreEqual(30f, turn.localEulerAngles.y, 0.5f, "one turn every 90 s");
            Assert.AreEqual(10f, tilt.localEulerAngles.x, 0.5f, "tilted to its full 10 degrees");
        }

        static Transform Find(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
            return null;
        }

        [Test]
        public void NeedIconsAndBeltIconsAreSavedSettings()
        {
            var fresh = new GameSettings();
            fresh.Sanitise();
            Assert.AreEqual(GameSettings.NeedsAlways, fresh.needIcons, "needs show always by default");
            Assert.IsTrue(fresh.beltItemIcons, "belt items show their icon by default");
            var old = JsonUtility.FromJson<GameSettings>("{\"showNeedIcons\":false}"); // saved before the three modes
            old.Sanitise();
            Assert.AreEqual(GameSettings.NeedsNever, old.needIcons, "an old 'off' becomes Never");
            var copy = JsonUtility.FromJson<GameSettings>(JsonUtility.ToJson(new GameSettings { needIcons = GameSettings.NeedsOnHover, beltItemIcons = false }));
            copy.Sanitise();
            Assert.AreEqual(GameSettings.NeedsOnHover, copy.needIcons);
            Assert.IsFalse(copy.beltItemIcons);
        }

        [Test]
        public void TheSettingsMenuOffersNeedAndBeltIconChoices()
        {
            var tree = Resources.Load<UnityEngine.UIElements.VisualTreeAsset>("UI/Main").CloneTree();
            foreach (var name in new[] { "needs-0", "needs-1", "needs-2", "belt-icons-show", "belt-icons-hide" })
                Assert.IsNotNull(UnityEngine.UIElements.UQueryExtensions.Q<UnityEngine.UIElements.Button>(tree, name), name);
            Assert.IsNull(UnityEngine.UIElements.UQueryExtensions.Q(tree, "need-icons-toggle"), "the old on/off switch is gone");
            var settings = new GameSettings();
            settings.Sanitise();
            var menu = new MenuController(settings);
            UnityEngine.TestTools.LogAssert.NoUnexpectedReceived();
            menu.Initialize(tree);
            Assert.AreEqual(GameSettings.NeedIconNames.Length, 3);
        }

        [Test]
        public void TouchScreensGetABiggerInterfaceAndTheFpsReadoutIsASetting()
        {
            var settings = new GameSettings();
            settings.Sanitise();
            Assert.AreEqual(1f, settings.InterfaceScale(false, 1080f, 96f), "a mouse keeps 100%");
            Assert.AreEqual(1.75f, settings.InterfaceScale(true, 1170f, 460f), "a phone (2.5 in short side) gets the biggest");
            Assert.AreEqual(1.25f, settings.InterfaceScale(true, 1536f, 220f), "an iPad less so");
            settings.uiScale = 3;
            Assert.AreEqual(1.5f, settings.InterfaceScale(false, 1080f, 96f), "a chosen size always wins");
            Assert.AreEqual(GameSettings.PerfRates, new GameSettings().perfOverlay);
            var copy = JsonUtility.FromJson<GameSettings>(JsonUtility.ToJson(new GameSettings { perfOverlay = GameSettings.PerfOff }));
            copy.Sanitise();
            Assert.AreEqual(GameSettings.PerfOff, copy.perfOverlay, "the FPS readout can be switched off and stays off");
            Assert.AreEqual("Right-click a cell.", Pointer.Words("Right-click a cell."), "mouse wording is unchanged without touch");
            var auto = new GameSettings();
            auto.Sanitise();
            Assert.IsTrue(auto.LowGraphics(true), "phones (the compact layout) get Low graphics automatically");
            Assert.IsFalse(auto.LowGraphics(false), "everything else High");
            Fleet.MaxShown = 1;
            Assert.AreEqual(1, Fleet.ShownShips(CatalogRepository.GetCraft(CatalogRepository.LoadContent(), "guard"), 10), "Low draws one ship a fleet");
            Assert.AreEqual(10, Fleet.Ships(CatalogRepository.GetCraft(CatalogRepository.LoadContent(), "guard"), 10), "but the fleet itself is unchanged");
            Fleet.MaxShown = 0;
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
