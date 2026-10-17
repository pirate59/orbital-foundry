using System.Text;
using NUnit.Framework;

namespace OrbitalFoundry.Tests
{
    /// <summary>
    /// An example of tracing one arena match with <see cref="AiArena.Probe"/>: side A's stance, target, rally cell and
    /// strike group every 30 game seconds, written to Logs/strike_trace.txt. Explicit: run it by name when a profile
    /// does something unexpected, and change the probe to trace whatever is in question.
    /// </summary>
    public class AiArenaTrace
    {
        [Test, Explicit]
        public void TraceStrike()
        {
            var sb = new StringBuilder();
            AiArena.Probe = loop =>
            {
                var w = loop.World;
                var d = loop.PlayerAi.Brain.Decisions;
                sb.Append("t=").Append(w.tick / 600).Append("m stance ").Append(d.Stance).Append(" target ").Append(d.AttackCell).Append(" rally ").Append(d.RallyCell)
                  .Append(" short ").Append(d.StrikeShortfall).Append(" assembled ").Append(d.StrikeAssembled).Append(" launch ").Append(d.LaunchNow).Append(" group:");
                foreach (int id in d.StrikeGroup)
                {
                    var k = WorldLookup.GetCraft(w, id);
                    sb.Append(' ').Append(k.typeId).Append(id).Append('@').Append(k.cellId).Append(k.worldPathCellIds.Count > 0 ? "->" + k.worldDestinationCellId : "")
                      .Append(WorldLookup.IsDefender(w, k) ? "(def)" : "").Append(" hp").Append(k.currentHealth);
                }
                sb.Append(" | all A ships:");
                foreach (var k in w.crafts)
                    if (k.owner == FactionId.Player && WorldLookup.IsCraftLive(k))
                        sb.Append(' ').Append(k.id).Append('@').Append(k.cellId).Append(k.worldPathCellIds.Count > 0 ? "->" + k.worldDestinationCellId : "").Append(WorldLookup.IsDefender(w, k) ? "d" : "");
                sb.Append(" | last: ").Append(loop.PlayerAi.LastAction).Append('\n');
            };
            var config = new ArenaConfig { maxMinutes = 30f, sampleEverySeconds = 30f };
            AiArena.PlayMatch(config, 0, "field-424242", 1234, AiProfile.For(Difficulty.Hard), AiProfile.For(Difficulty.Easy), new StringBuilder(), new StringBuilder());
            AiArena.Probe = null;
            TestContext.WriteLine(sb.ToString());
            System.IO.File.WriteAllText("Logs/strike_trace.txt", sb.ToString());
        }
    }
}
