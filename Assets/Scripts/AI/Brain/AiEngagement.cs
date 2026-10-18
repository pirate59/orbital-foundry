using System.Collections.Generic;

namespace OrbitalFoundry
{
    /// <summary>
    /// Combat readiness: whether a strike could win a whole battle, not just its first round. It plays the battle out
    /// in miniature with the real combat rules: every ship acts once a turn; the cell's Forts and assigned
    /// defender protect the rest and must fall first; damage after armour (at least 1), scaled by health; return fire
    /// from whatever survives a hit; the shield flagship's armour for the ships beside it; a Rail Lancer's long shot
    /// that nothing answers, then its recharge. The result says whether it wins within the round limit, in how many
    /// rounds, and how much of the fleet's health it would lose. Only the AI uses it; it changes nothing.
    /// </summary>
    public static class AiEngagement
    {
        public struct Result
        {
            public bool Won;
            public int Rounds, HpStart, HpLost;
            /// <summary>What the losses are worth: each ship's price times the share of its health it loses.</summary>
            public float LossValue;
            /// <summary>Share of the fleet's health left after the battle (0 when it loses).</summary>
            public float Kept { get { return Won && HpStart > 0 ? (HpStart - HpLost) / (float)HpStart : 0f; } }
        }

        /// <summary>The ship's role for the AI: "attack", "support" or "defence" (from its catalog aiRole, else its stats).</summary>
        public static string Role(CraftDefinition d)
        {
            if (d == null) return "defence";
            if (!string.IsNullOrEmpty(d.aiRole)) return d.aiRole;
            int attack = System.Math.Max(d.attackDamage, d.rangedAttackDamage);
            return attack > d.defenceDamage ? "attack" : "defence";
        }

        /// <summary>A ship that can be the core of a strike: an attack or support ship (not one built to hold cells).</summary>
        public static bool StrikeCore(ContentCatalog c, CraftState k)
        {
            return Role(CatalogRepository.GetCraft(c, k.typeId)) != "defence";
        }

        sealed class Unit
        {
            public int Hp, Max, Armour, Attack, Ranged, ReturnFire, Cooldown, Wait, Start, Price;
            public bool Fort, Protector, Lancer;
            public bool Alive { get { return Hp > 0; } }
        }

        static readonly List<Unit> attackers = new List<Unit>();
        static readonly List<Unit> defenders = new List<Unit>();
        static readonly HashSet<Unit> acted = new HashSet<Unit>();

        /// <summary>Plays out an attack by <paramref name="fleet"/> on <paramref name="targetCell"/> against what defends it now.</summary>
        public static Result Simulate(WorldState w, ContentCatalog c, IList<CraftState> fleet, int targetCell)
        {
            var r = new Result();
            attackers.Clear();
            defenders.Clear();
            var cell = WorldLookup.GetCell(w, targetCell);
            if (cell == null || fleet.Count == 0) return r;
            FactionId me = fleet[0].owner, foe = cell.owner;

            int ourShield = 0;
            foreach (var k in fleet)
            {
                var d = CatalogRepository.GetCraft(c, k.typeId);
                if (d != null && d.shieldArmour > ourShield) ourShield = d.shieldArmour;
            }
            foreach (var k in fleet)
            {
                var d = CatalogRepository.GetCraft(c, k.typeId);
                if (d == null || !WorldLookup.IsCraftLive(k)) continue;
                attackers.Add(new Unit
                {
                    Hp = k.currentHealth, Max = d.maxHealth, Armour = d.armour + (d.shieldArmour > 0 ? 0 : ourShield),
                    Attack = d.attackDamage, Ranged = d.range > 1 ? d.rangedAttackDamage : 0, ReturnFire = d.defenceDamage,
                    Cooldown = ResearchService.CooldownTurns(c, WorldLookup.GetFaction(w, me), d), Lancer = d.range > 1,
                    Start = k.currentHealth, Price = Price(d)
                });
                r.HpStart += k.currentHealth;
            }

            // The defence: enemy ships standing in the cell and its working Forts.
            int theirShield = 0;
            foreach (var k in w.crafts)
            {
                if (k.owner != foe || k.cellId != targetCell || !WorldLookup.IsCraftLive(k) || k.worldPathCellIds.Count > 0) continue;
                var d = CatalogRepository.GetCraft(c, k.typeId);
                if (d != null && d.shieldArmour > theirShield) theirShield = d.shieldArmour;
            }
            foreach (var k in w.crafts)
            {
                if (k.owner != foe || k.cellId != targetCell || !WorldLookup.IsCraftLive(k) || k.worldPathCellIds.Count > 0) continue;
                var d = CatalogRepository.GetCraft(c, k.typeId);
                if (d == null) continue;
                bool assigned = cell.defenderCraftId == k.id;
                defenders.Add(new Unit
                {
                    Hp = k.currentHealth, Max = d.maxHealth, Protector = assigned,
                    Armour = d.armour + (assigned ? d.defenderArmourBonus : 0) + (d.shieldArmour > 0 ? 0 : theirShield),
                    Attack = d.attackDamage, ReturnFire = d.defenceDamage > 0 && assigned ? d.defenceDamage + d.defenderCounterBonus : d.defenceDamage
                });
            }
            foreach (var b in w.buildings)
            {
                if (b.owner != foe || b.cellId != targetCell || !WorldLookup.IsBuildingActive(w, b)) continue;
                var d = CatalogRepository.GetBuilding(c, b.typeId);
                if (d == null || d.combatHealth <= 0 || d.combatHealth - b.damage <= 0) continue;
                defenders.Add(new Unit { Hp = d.combatHealth - b.damage, Max = d.combatHealth, Armour = d.combatArmour, ReturnFire = d.combatReturnFire, Fort = true, Protector = true });
            }
            if (defenders.Count == 0) { r.Won = true; return r; }

            int limit = c.rules.battleRoundLimit > 0 ? c.rules.battleRoundLimit : 10;
            for (int round = 1; round <= limit; round++)
            {
                r.Rounds = round;
                // Its turn: every ship acts once, each making the strongest hit it can on a target it may choose.
                acted.Clear();
                for (int a = 0; a < attackers.Count; a++)
                {
                    bool protectorsLeft = defenders.Exists(u => u.Alive && u.Protector);
                    Unit by = null, on = null;
                    int bestDamage = 0;
                    foreach (var u in attackers)
                    {
                        if (!u.Alive || acted.Contains(u) || u.Wait > 0) continue;
                        int stat = Scale(c, u.Lancer ? u.Ranged : u.Attack, u.Hp, u.Max);
                        foreach (var t in defenders)
                        {
                            if (!t.Alive || (protectorsLeft && !t.Protector)) continue;
                            int dmg = CombatResolver.DamageAfterArmour(stat, t.Armour);
                            int score = dmg >= t.Hp ? dmg + 100 : dmg; // a kill first
                            if (score > bestDamage) { bestDamage = score; by = u; on = t; }
                        }
                    }
                    if (by == null) break;
                    acted.Add(by);
                    int hit = CombatResolver.DamageAfterArmour(Scale(c, by.Lancer ? by.Ranged : by.Attack, by.Hp, by.Max), on.Armour);
                    on.Hp -= hit;
                    if (by.Lancer) by.Wait = by.Cooldown;
                    // What survives a hit fires back, unless it was a lancer's long shot.
                    if (on.Alive && !by.Lancer)
                    {
                        int back = CombatResolver.DamageAfterArmour(Scale(c, on.ReturnFire, on.Hp, on.Max), by.Armour);
                        r.HpLost += System.Math.Min(back, by.Hp);
                        by.Hp -= back;
                    }
                }
                if (!defenders.Exists(u => u.Alive)) { r.Won = true; break; }

                // Their turn: each of their ships hits its weakest ship (Forts only return fire).
                acted.Clear();
                for (int a = 0; a < defenders.Count; a++)
                {
                    Unit by = null, on = null;
                    foreach (var u in defenders) if (u.Alive && !u.Fort && !acted.Contains(u) && u.Attack > 0) { by = u; break; }
                    if (by == null) break;
                    foreach (var t in attackers) if (t.Alive && (on == null || t.Hp < on.Hp)) on = t;
                    if (on == null) break;
                    acted.Add(by);
                    int hit = CombatResolver.DamageAfterArmour(Scale(c, by.Attack, by.Hp, by.Max), on.Armour);
                    int dealt = System.Math.Min(hit, on.Hp);
                    on.Hp -= hit;
                    r.HpLost += dealt;
                    if (on.Alive)
                    {
                        int back = CombatResolver.DamageAfterArmour(Scale(c, on.ReturnFire, on.Hp, on.Max), by.Armour);
                        by.Hp -= back;
                    }
                }
                foreach (var u in attackers) if (u.Wait > 0) u.Wait--;
                if (!attackers.Exists(u => u.Alive)) { r.Won = false; break; }
            }
            if (r.HpLost > r.HpStart) r.HpLost = r.HpStart;
            foreach (var u in attackers)
                if (u.Max > 0) r.LossValue += u.Price * (u.Start - System.Math.Max(0, u.Hp)) / (float)u.Max;
            return r;
        }

        static int Scale(ContentCatalog c, int stat, int hp, int max)
        {
            return CombatResolver.ScaleByHealth(c, stat, hp, max);
        }

        /// <summary>
        /// Whether a fleet is ready to strike a cell: played out, it wins the whole battle within the round limit and
        /// keeps at least the share of its health the profile insists on (Easy keeps more, Hard accepts heavy losses).
        /// How many ships that takes comes from what each one trades against this defence (damage after armour, the
        /// return fire it takes), not a fixed count: if a Guard takes 1 HP a hit off a Fort, it takes many Guards. The
        /// strike builder adds attack and support ships first (more trading value each), then defence ships, until it
        /// is ready; defence ships alone go only when sized like that to win.
        /// </summary>
        public static bool Ready(ContentCatalog c, IList<CraftState> fleet, Result sim, AiProfile p, bool emergency)
        {
            return sim.Won && (emergency || sim.Kept >= KeepShare(p));
        }

        static readonly List<CraftState> planned = new List<CraftState>();

        /// <summary>The ship types this side could build and supply the parts for now.</summary>
        public static List<string> Buildable(AiContext ctx)
        {
            var list = new List<string>();
            var planner = AiSide.Planner ?? ctx.Services.AiPlanner;
            foreach (var def in ctx.Catalog.crafts)
                if (planner.CanSupply(ctx.World, ctx.Catalog, ctx.Faction, def)) list.Add(def.id);
            return list;
        }

        /// <summary>A ship's price for weighing options: its credits plus its Ion at twice the weight (Ion is scarcer).</summary>
        public static int Price(CraftDefinition d)
        {
            return d == null ? 0 : d.cost + 2 * d.ionCost;
        }

        /// <summary>One way to win a battle: build <see cref="Ships"/> more of <see cref="TypeId"/> (none: go as it is).</summary>
        public struct Option
        {
            public string TypeId;
            public int Ships;
            /// <summary>Building them plus the expected losses: the activity's whole price.</summary>
            public float Price;
            public bool Found;
        }

        /// <summary>
        /// The cheapest way for the fleet to win a full battle for the cell: as it is (if ready, priced by its expected
        /// losses), or with more of one of <paramref name="buildable"/> types, priced by what they cost to build plus
        /// the expected losses. Ten Guards that would be shot to pieces against a Fort lose to a few Bombers this way.
        /// </summary>
        public static Option Cheapest(WorldState w, ContentCatalog c, IList<CraftState> fleet, int cellId, AiProfile p, bool emergency,
            IList<string> buildable, int most)
        {
            var best = new Option();
            if (fleet.Count > 0)
            {
                var now = Simulate(w, c, fleet, cellId);
                if (Ready(c, fleet, now, p, emergency)) best = new Option { TypeId = "", Ships = 0, Price = now.LossValue, Found = true };
            }
            foreach (var type in buildable)
            {
                var def = CatalogRepository.GetCraft(c, type);
                if (def == null) continue;
                planned.Clear();
                planned.AddRange(fleet);
                for (int n = 1; n <= most; n++)
                {
                    planned.Add(new CraftState { id = -n, owner = AiSide.Me, typeId = type, currentHealth = def.maxHealth, cellId = -1 });
                    float built = n * Price(def);
                    if (best.Found && built >= best.Price) break; // already dearer than the best option
                    var sim = Simulate(w, c, planned, cellId);
                    if (!Ready(c, planned, sim, p, emergency)) continue;
                    float total = built + sim.LossValue;
                    if (!best.Found || total < best.Price) best = new Option { TypeId = type, Ships = n, Price = total, Found = true };
                    break;
                }
            }
            return best;
        }

        /// <summary>
        /// How many more ships of <paramref name="typeId"/> the fleet needs to be ready to strike the cell: 0 when it is
        /// ready now, -1 when even <paramref name="most"/> more would not do. The extra ships are imagined at full
        /// health; nothing is added to the world.
        /// </summary>
        public static int ShipsNeeded(WorldState w, ContentCatalog c, IList<CraftState> fleet, int cellId, AiProfile p, bool emergency, string typeId, int most)
        {
            planned.Clear();
            planned.AddRange(fleet);
            if (planned.Count > 0 && Ready(c, planned, Simulate(w, c, planned, cellId), p, emergency)) return 0;
            var def = CatalogRepository.GetCraft(c, typeId);
            if (def == null) return -1;
            for (int n = 1; n <= most; n++)
            {
                planned.Add(new CraftState { id = -n, owner = AiSide.Me, typeId = typeId, currentHealth = def.maxHealth, cellId = -1 });
                if (Ready(c, planned, Simulate(w, c, planned, cellId), p, emergency)) return n;
            }
            return -1;
        }

        /// <summary>Share of the fleet's health a strike must expect to keep: 0.6 cautious down to 0 bold (from RiskTolerance).</summary>
        public static float KeepShare(AiProfile p)
        {
            return System.Math.Max(0f, System.Math.Min(0.9f, 0.6f - p.RiskTolerance));
        }
    }
}
