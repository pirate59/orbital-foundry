using System.Collections.Generic;
using System.Text;

namespace OrbitalFoundry
{
    /// <summary>
    /// The AI's skills, run in order on its think ticks: assess the situation, choose a stance, split the budget,
    /// pick targets. Each skill writes to the shared <see cref="AiDecisions"/>, which the controller and the factory
    /// planner then act on. Runtime only: after a load it rebuilds everything from the world on its first think.
    /// New skills are added to <see cref="Skills"/>; their order is the order they think in.
    /// </summary>
    public sealed class AiBrain
    {
        public readonly List<IAiSkill> Skills = new List<IAiSkill>
        {
            new EconomyAssessmentSkill(),
            new SituationAssessmentSkill(),
            new GrandStrategySkill(),
            new BudgetSkill(),
            new ExpansionTargetSkill(),
            new AttackTargetSkill(),
            new AttackFleetSkill(),
            new FleetCompositionSkill(),
            new ExpansionPacingSkill(),
            new DefencePostureSkill(),
            new ProductSelectionSkill(),
            new ResearchGoalSkill(),
            new NeedsAnalysisSkill(),
            new BottleneckRepairSkill(),
            new PowerPlanningSkill(),
            new SolarSupplySkill(),
            new ThroughputBalancingSkill(),
            new UpgradeOrExpandSkill(),
            new CrossPlatformSupplySkill(),
            new BeltUpgradeSkill(),
            new BeltRefactorSkill(),
            new DepotOutletSkill(),
            new LostGroundSkill(),
            new DeliveryPrioritySkill(),
            new StorageHubSkill(),
            new StorageManagementSkill(),
            new RelayoutSkill(),
        };

        public AiProfile Profile = AiProfile.For(Difficulty.Medium);
        public AiWorldModel Model = new AiWorldModel();
        public AiDecisions Decisions = new AiDecisions();
        readonly AiContext ctx = new AiContext();
        WorldState world;
        int thinks;

        /// <summary>One think: every skill whose turn it is runs once, in order.</summary>
        public void Think(WorldState w, ContentCatalog c, MapDefinition map, GameServices services, AiMemory memory)
        {
            if (world != w)
            {
                // A different world (new game or load): start from nothing.
                world = w;
                thinks = 0;
                Model = new AiWorldModel();
                Decisions = new AiDecisions();
            }
            Profile = AiProfile.Current(w);
            ctx.World = w;
            ctx.Catalog = c;
            if (ctx.DistanceMap != map) { ctx.DistanceMap = map; ctx.Distances.Clear(); }
            ctx.Map = map;
            ctx.Services = services;
            ctx.Faction = WorldLookup.GetFaction(w, AiSide.Me);
            ctx.Player = WorldLookup.GetFaction(w, AiSide.Foe);
            ctx.Profile = Profile;
            ctx.Model = Model;
            ctx.Decisions = Decisions;
            ctx.Memory = memory;
            if (ctx.Faction == null || ctx.Player == null) return;
            Decisions.PriorityCommands.Clear(); // orders from the last think have been carried out (or dropped)
            Decisions.InstantCommands.Clear();
            foreach (var skill in Skills)
            {
                if (Profile.DisabledSkills.Count > 0 && Profile.DisabledSkills.Contains(skill.Name)) continue;
                int every = System.Math.Max(1, skill.EveryThinks(ctx));
                var onEvent = skill as IAiEventSkill;
                if (thinks % every != 0 && (onEvent == null || !onEvent.Triggered(ctx))) continue;
                long start = AiTiming.Begin();
                skill.Think(ctx);
                AiTiming.End(skill.Name, start);
            }
            thinks++;
        }

        /// <summary>One or two lines for the debug readout: stance, balances, budget and targets.</summary>
        public string Report()
        {
            var d = Decisions;
            var m = Model;
            var sb = new StringBuilder();
            sb.Append("AI ").Append(Profile.Name).Append(": ").Append(d.Stance).Append(" (").Append(d.StanceReason).Append(")")
              .Append("  ·  economy ").Append(m.EconomyBalance.ToString("0.0")).Append("x  military ").Append(m.MilitaryBalance.ToString("0.0")).Append('x');
            if (m.MostThreatenedCell >= 0) sb.Append("  ·  threat on cell ").Append(m.MostThreatenedCell);
            sb.Append("\n  ").Append(d.BudgetReason);
            if (d.ExpansionCell >= 0) sb.Append("  ·  expand: ").Append(d.ExpansionReason);
            if (d.AttackCell >= 0) sb.Append("  ·  attack: ").Append(d.AttackReason);
            if (d.StrikeReason.Length > 0) sb.Append("\n  strike: ").Append(d.StrikeReason);
            if (d.BuildOrder.Count > 0) sb.Append("  ·  build next: ").Append(d.BuildOrder[0]).Append(" (").Append(d.BuildReason).Append(')');
            if (d.ExpandReason.Length > 0) sb.Append("  ·  expansion: ").Append(d.ExpandReason);
            sb.Append("\n  economy: ").Append(d.ProductReason).Append("  ·  research: ").Append(d.ResearchReason)
              .Append("  ·  ").Append(d.NeedsReason).Append("  ·  ").Append(d.BottleneckReason).Append("  ·  ").Append(d.PowerReason).Append("  ·  solar: ").Append(d.SolarReason)
              .Append("\n  growth: ").Append(d.ThroughputReason).Append("  ·  ").Append(d.GrowthReason).Append("  ·  supply: ").Append(d.SupplyReason).Append("  ·  belts: ").Append(d.BeltReason)
              .Append("\n  layout: ").Append(d.RefactorReason).Append("  ·  outlets: ").Append(d.OutletReason).Append("  ·  ").Append(d.LostReason)
              .Append("\n  upkeep: ").Append(d.PriorityReason).Append("  ·  hub: ").Append(d.HubReason).Append("  ·  relayout: ").Append(d.RelayoutReason).Append("  ·  storage: ").Append(d.StorageReason);
            return sb.ToString();
        }
    }
}
