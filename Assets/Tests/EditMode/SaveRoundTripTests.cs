using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace OrbitalFoundry.Tests
{
    public class SaveRoundTripTests
    {
        TestWorld t;
        const FactionId P = FactionId.Player;

        [SetUp]
        public void SetUp() { t = new TestWorld(); }

        [TearDown]
        public void TearDown() { t.Dispose(); }

        /// <summary>A partially moved token, a frozen captured belt, queued AI commands and a pending defender choice.</summary>
        void BuildRichState()
        {
            t.BuildPlayerRelayChain();
            t.Player.currency += 500; // test setup: fund a second site
            t.SetOwner(1, P);
            t.Build(P, "extractor", 1, 2, 3);
            t.Build(P, "refinery", 1, 5, 3);
            t.Line(P, 1, 2, 3, 1, 3, 3, 1, 4, 3, 1, 5, 3);
            System.Predicate<ItemTokenState> inCell1 = k => WorldLookup.GetBelt(t.W, k.routeSegmentIds[k.segmentIndex]).toCellId == 1;
            for (int i = 0; i < 400 && !t.W.tokens.Exists(inCell1); i++) t.FactoryTick();
            Assert.IsTrue(t.W.tokens.Exists(inCell1), "an ore token travelling in cell 1");
            t.S.Ownership.TransferCell(t.W, t.Catalog, 1, FactionId.Ai);
            Assert.IsTrue(t.W.tokens.Exists(k => k.status == TokenStatus.Frozen), "a stranded token");

            t.W.ai.queuedCommands.Add(new GameCommand { kind = CommandKind.PlaceBelt, actor = FactionId.Ai, fromCellId = 7, fromX = 0, fromY = 0, toCellId = 7, toX = 1, toY = 0 });
            t.W.ai.queuedCommands.Add(new GameCommand { kind = CommandKind.Research, actor = FactionId.Ai, contentId = "charter" });
            t.Player.research.Add(new ResearchProgress { nodeId = "process_optimisation", level = 2 });

            // Encounter: AI attacks cell 4, killing the defender while two player reserves survive.
            t.SetOwner(4, P);
            var defender = t.AddCraft(P, "tank", 4, true);
            t.AddCraft(P, "guard", 4, false);
            t.AddCraft(P, "artillery", 4, false);
            var raider = t.AddCraft(FactionId.Ai, "artillery", 6, false);
            defender.currentHealth = 1;
            Assert.IsTrue(t.S.Encounters.Start(t.W, t.Catalog, t.Map, 4, FactionId.Ai).success);
            t.MustRun(new GameCommand { kind = CommandKind.EndTurn, actor = P });
            t.MustRun(new GameCommand { kind = CommandKind.TacticalAttack, actor = FactionId.Ai, craftId = raider.id, targetCellId = 4 });
            Assert.AreEqual(4, t.W.encounter.pendingDefenderCellId);
        }

        [Test]
        public void RoundTripPreservesEveryField()
        {
            BuildRichState();
            string before = JsonUtility.ToJson(t.W);
            var saved = t.Loop.Saves.Save(t.W, t.Catalog);
            Assert.IsTrue(saved.success, saved.message);

            WorldState loaded;
            var r = t.Loop.Saves.TryLoad(t.Catalog, t.Map, out loaded);
            Assert.IsTrue(r.success, r.code + " " + r.message);
            Assert.AreEqual(before, JsonUtility.ToJson(loaded));
            Assert.AreEqual(MatchMode.Tactical, loaded.mode);
            Assert.AreEqual(4, loaded.encounter.pendingDefenderCellId);
            Assert.AreEqual(2, loaded.ai.queuedCommands.Count);
            Assert.AreEqual(t.W.tick, loaded.tick);

            // The restored world plays on: the player answers the pending choice.
            var loop = new GameLoop();
            loop.Initialize(t.Catalog, t.Map, loaded, new SaveService(t.SaveDir));
            var reserve = WorldLookup.CraftsInCell(loaded, 4, P)[0];
            Assert.IsTrue(loop.Submit(new GameCommand { kind = CommandKind.AssignDefender, actor = P, craftId = reserve.id, targetCellId = 4 }).success);
        }

        [Test]
        public void CorruptPrimaryFallsBackToBackup()
        {
            Assert.IsTrue(t.Loop.Saves.Save(t.W, t.Catalog).success);
            t.FactoryTick();
            Assert.IsTrue(t.Loop.Saves.Save(t.W, t.Catalog).success); // first save becomes slot1.bak
            File.WriteAllText(t.Loop.Saves.SlotPath, "{ this is not json");

            WorldState loaded;
            var r = t.Loop.Saves.TryLoad(t.Catalog, t.Map, out loaded);
            Assert.IsTrue(r.success);
            Assert.AreEqual("RECOVERED_BACKUP", r.code);
            Assert.AreEqual(0, loaded.tick);
            Assert.IsTrue(File.Exists(t.Loop.Saves.SlotPath), "the damaged file is kept for inspection");
        }

        [Test]
        public void CorruptSaveWithoutBackupIsReportedAndKept()
        {
            Directory.CreateDirectory(t.SaveDir);
            File.WriteAllText(t.Loop.Saves.SlotPath, "garbage");
            WorldState loaded;
            var r = t.Loop.Saves.TryLoad(t.Catalog, t.Map, out loaded);
            Assert.IsFalse(r.success);
            Assert.AreEqual("CORRUPT_SAVE", r.code);
            Assert.IsNull(loaded);
            Assert.IsTrue(File.Exists(t.Loop.Saves.SlotPath));
        }

        [Test]
        public void NewerSchemaIsRejectedAndProtectedFromOverwrite()
        {
            var envelope = new SaveEnvelope { schemaVersion = SaveService.SchemaVersion + 1, catalogVersion = 1, world = t.W };
            Directory.CreateDirectory(t.SaveDir);
            string json = JsonUtility.ToJson(envelope);
            File.WriteAllText(t.Loop.Saves.SlotPath, json);

            WorldState loaded;
            var r = t.Loop.Saves.TryLoad(t.Catalog, t.Map, out loaded);
            Assert.IsFalse(r.success);
            Assert.AreEqual("NEWER_SAVE", r.code);
            var blocked = t.Loop.Saves.Save(t.W, t.Catalog);
            Assert.AreEqual("SAVE_BLOCKED", blocked.code);
            Assert.AreEqual(json, File.ReadAllText(t.Loop.Saves.SlotPath), "incompatible save untouched");
        }

        [Test]
        public void TabSuspensionAddsNoFactoryTime()
        {
            int before = t.W.tick;
            t.Loop.Update(600f); // ten minutes in one frame
            Assert.LessOrEqual(t.W.tick - before, t.Catalog.rules.maxCatchUpTicksPerFrame);
            t.Loop.Update(float.NaN);
            t.Loop.Update(-3f);
            Assert.LessOrEqual(t.W.tick - before, t.Catalog.rules.maxCatchUpTicksPerFrame);
        }
    }
}
