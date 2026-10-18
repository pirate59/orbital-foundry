using System.Collections.Generic;
using NUnit.Framework;

namespace OrbitalFoundry.Tests
{
    /// <summary>The AI profile presets, profiles built on them, and AI v AI matches in the headless arena.</summary>
    public class AiArenaTests
    {
        [Test]
        public void ThePresetFileHoldsTheThreeDifficulties()
        {
            foreach (Difficulty d in new[] { Difficulty.Easy, Difficulty.Medium, Difficulty.Hard })
            {
                var preset = AiProfile.For(d);
                var builtIn = AiProfile.BuiltIn(d);
                Assert.AreEqual(builtIn.Id, preset.Id);
                Assert.AreEqual(builtIn.Level, preset.Level, d + " skill level");
                Assert.AreEqual(builtIn.Aggression, preset.Aggression, 1e-6, d + " aggression");
                Assert.AreEqual(builtIn.MaxCraft, preset.MaxCraft);
                Assert.AreEqual(builtIn.EarliestAttackTick, preset.EarliestAttackTick);
            }
            Assert.AreEqual(0, AiProfile.For(Difficulty.Easy).Level);
            Assert.AreEqual(2, AiProfile.For(Difficulty.Hard).Level);
        }

        [Test]
        public void AProfileCanChangeJustSomeOfAPresetsValues()
        {
            var parsed = AiProfiles.Parse("{\"profiles\":[{\"Id\":\"bold\",\"Base\":\"hard\",\"Aggression\":0.95,\"DisabledSkills\":[\"Storage hub use\"]},"
                + "{\"Id\":\"bolder\",\"Base\":\"bold\",\"RiskTolerance\":0.9}]}", null);
            Assert.AreEqual(2, parsed.Count);
            var bold = parsed[0];
            Assert.AreEqual(0.95f, bold.Aggression, 1e-6);
            Assert.AreEqual(AiProfile.For(Difficulty.Hard).MaxCraft, bold.MaxCraft, "the rest is Hard's");
            CollectionAssert.AreEqual(new[] { "Storage hub use" }, bold.DisabledSkills);
            Assert.AreEqual(0.95f, parsed[1].Aggression, 1e-6, "a profile can build on one defined before it");
            Assert.AreEqual(0.9f, parsed[1].RiskTolerance, 1e-6);
            Assert.AreEqual(0.75f, AiProfile.For(Difficulty.Hard).Aggression, 1e-6, "the preset itself is untouched");
        }

        [Test]
        public void BothSidesPlayInAnAiVersusAiMatch()
        {
            var config = new ArenaConfig { maxMinutes = 6f, swapSides = false };
            var result = AiArena.PlayMatch(config, 0, "map_a", 1234, AiProfile.For(Difficulty.Medium), AiProfile.For(Difficulty.Hard),
                new System.Text.StringBuilder(), new System.Text.StringBuilder());
            Assert.AreNotEqual("error", result.Winner, result.Reason);
            Assert.Greater(result.A.Sales, 0, "the player's side, played by an AI, built a factory and sold");
            Assert.Greater(result.B.Sales, 0, "and so did the game's AI");
            Assert.Greater(result.A.AiMs, 0, "side A was timed apart");
            Assert.Greater(result.B.AiMs, 0, "side B was timed apart");
            Assert.AreEqual(FactionId.Ai, AiSide.Me, "the AI code is back to playing the game's side");
        }

        [Test]
        public void ADisabledSkillDoesNotRun()
        {
            using (var t = new TestWorld())
            {
                var brain = new AiBrain();
                var profile = AiProfile.For(Difficulty.Medium).Clone();
                profile.DisabledSkills.Add("Research goals");
                using (AiSide.Enter(FactionId.Ai, null, null, profile, "[T] "))
                    for (int i = 0; i < 100; i++) { t.W.tick += 10; brain.Think(t.W, t.Catalog, t.Map, t.S, new AiMemory()); }
                Assert.IsFalse(AiTiming.Lines("[T] ").Exists(l => l.Name == "Research goals"), "left out");
                Assert.IsTrue(AiTiming.Lines("[T] ").Exists(l => l.Name == "Needs & gaps analysis"), "the others still think");
            }
        }
    
        [Test]
        public void AMatchCanStartWithAnyPresetWhateverItsDifficulty()
        {
            using (var t = new TestWorld())
            {
                t.W.difficulty = Difficulty.Easy;
                Assert.AreEqual("easy", AiProfile.Current(t.W).Id);
                t.W.aiProfileId = "hard";
                Assert.AreEqual("hard", AiProfile.Current(t.W).Id, "the profile it started with");
                t.W.aiProfileId = "no_such_profile";
                Assert.AreEqual("easy", AiProfile.Current(t.W).Id, "an unknown id falls back to the difficulty");
            }
        }
    
        [Test]
        public void NeitherSideLeavesItsHeadquartersEmpty()
        {
            // Hard v Hard on the field where side A's empty headquarters used to fall to a strike at 9 minutes.
            var bare = new System.Collections.Generic.List<string>();
            AiArena.Probe = loop =>
            {
                var w = loop.World;
                foreach (var side in new[] { FactionId.Player, FactionId.Ai })
                {
                    var hq = w.cells.Find(cell => cell.headquartersFor == side);
                    if (hq == null || hq.owner != side) continue;
                    bool guarded = w.crafts.Exists(k => k.owner == side && WorldLookup.IsCraftLive(k) && k.cellId == hq.id && k.worldPathCellIds.Count == 0);
                    bool coming = w.crafts.Exists(k => k.owner == side && WorldLookup.IsCraftLive(k) && k.worldPathCellIds.Count > 0 && k.worldDestinationCellId == hq.id);
                    bool anyShips = w.crafts.Exists(k => k.owner == side && WorldLookup.IsCraftLive(k));
                    if (anyShips && !guarded && !coming) bare.Add(side + " at minute " + (w.tick / 600));
                }
            };
            try
            {
                var config = new ArenaConfig { maxMinutes = 15f, sampleEverySeconds = 10f, swapSides = false };
                var result = AiArena.PlayMatch(config, 0, "field-1001", 1234, AiProfile.For(Difficulty.Hard), AiProfile.For(Difficulty.Hard),
                    new System.Text.StringBuilder(), new System.Text.StringBuilder());
                Assert.AreNotEqual("error", result.Winner, result.Reason);
                Assert.IsEmpty(bare, "a headquarters was left with no ship: " + string.Join(", ", bare) + " (" + result.Reason + ")");
                if (result.Reason.Contains("headquarters")) Assert.Greater(result.Battles, 0, "a headquarters is only taken in battle, not walked into");
            }
            finally { AiArena.Probe = null; }
        }
    }
}
