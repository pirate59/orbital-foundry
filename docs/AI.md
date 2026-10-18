# Orbital Foundry AI

How the AI plays, how its difficulties are packaged, what each skill costs, and how to stress-test it headlessly.

## How it fits together

- **Rules:** the AI plays by the player's rules. Every action is a `GameCommand` through `CommandRouter`, with the same costs, queue cap (5), research goods, prices and Storage lists as a person.
- **Any seat:** the AI code says "me" and "my opponent" (`AiSide.Me` / `AiSide.Foe`) rather than naming a faction.
  - Every seat set up as AI-played (`FactionState.aiControlled`) gets an `AIController` from `GameLoop`. The game's AI plays `FactionId.Ai`; the arena adds a second AI for `FactionId.Player`.
  - Each seat's planning state (`FactionState.ai`) is saved with the game.
  - **Enemies and allies:** `AiSide.IsEnemy` / `IsAlly` follow the seats' teams.
    - Threats, attack targets, defences and fleet counters count every enemy.
    - Economy and fleet comparisons are against the **rival** (`AiSide.RivalOf`): the enemy still in the match with the most cells. In a two-seat match that is simply the opponent.
    - The trade counter watches whichever enemy is closest to a trade win.
    - It never targets an ally, defends allied land in battle, and weighs a target more when an ally's raid or attack is already going there.
- **Parts:**
  - `AIController`: one per side. It carries out queued orders, runs the brain, plans and takes military steps.
  - `AiBrain`: the skills in `Assets/Scripts/AI/Brain/Skills`, run in order on think ticks (every 3 ticks = 0.3 s). Each skill has its own cadence, and some also run on events.
  - `AIFactoryPlanner`: chains, sites, belts, power, Forts, research and the goods supply. It works within a work budget per think.
  - `AITactics` with the battle skills: plays tactical turns.
- **Cost:** about 0.08 ms of AI time per game tick on the grown-factory benchmark (field-424242 with raids). The worst tick is about 2.7 ms, and most of that is the planner's site search.

## Difficulty packaging: AI profiles

A difficulty is one **profile**. The presets are in `Assets/Resources/AI/ai_profiles.json` (`easy`, `medium`, `hard`). `AiProfile.BuiltIn` mirrors them in code in case the file is missing.

**Which profile a side plays**, first match wins:
1. The side's own profile (the arena gives each side one).
2. The seat's `FactionState.aiProfileId`, if the match set it up with a named profile. It is saved with the game.
3. The match difficulty's preset.

| Field | Easy | Medium | Hard | What it does |
|---|---|---|---|---|
| `Level` | 0 | 1 | 2 | **Which skills run.** 0 is the basics; 1 is the standard set; 2 is everything, at its sharpest. Kept apart from the weights, so tuning a weight never switches a skill on or off. |
| `Aggression` | 0.15 | 0.45 | 0.75 | Strike timing (raid interval × (1.25 − A)), claims at once (1 + 2A), danger it accepts when picking a cell to expand to, and the military lead it waits for. |
| `RiskTolerance` | 0.2 | 0.4 | 0.6 | An attack needs strength ≥ (1.6 − R) × defence. |
| `ReactionTicks` | 300 | 150 | 50 | How long a new situation must last before the stance changes. Defend always reacts at once. |
| `WorkScale` | 0.5 | 1.0 | 1.5 | Planner work per think. |
| `AssessEveryThinks` | 6 | 3 | 2 | Cadence of threat assessment, strategy, budget, power and belt upgrades. |
| `MaxCraft` | 3 | 6 | 9 | Strike fleet in a military game, on top of one guard per cell held. Without rules for expanding or raiding, it is the whole fleet. |
| `SightCells` | 1 | 2 | 99 | How far from its own cells it notices enemy fleets. |
| `SeesPlayerEconomy` | no | yes | yes | Reads the opponent's sales and trade-win progress. |
| `CounterAt` | 2.0 | 0.9 | 0.7 | Share of the opponent's trade target at which it counters. Above 1 means never. |
| `EarliestAttackTick` | never | 6000 (10 min) | 3000 (5 min) | Grace period before its first attack. |
| `ResearchQueue` | 1 | 2 | 3 | Levels it chooses to keep queued (the rule allows 5). |
| `RetreatPatience` | 2.0 | 1.25 | 1.0 | Leaves an attack when its ships would fall before the defence, allowing this factor. The rate comes from both sides' health lost so far. |
| `RetreatFromRound` | 8 | 3 | 2 | The battle round from which it judges whether to retreat. |
| `RecoupMinutes` | 3 | 5 | 8 | Industry gate: it attacks only if it could lose the attack fleet and recover within this time. Income must pay for the fleet again and its shipyards must rebuild it. |
| `CadenceScale` | 1 | 1 | 1 | Multiplies every skill interval: 2 runs everything half as often. For testing cost against strength. |
| `DisabledSkills` | — | — | — | Skill names to leave out, for testing what each is worth. |
| `Variety` | 0.1 | 0.1 | 0.1 | How much its personality wobbles from match to match: Aggression and RiskTolerance by up to ± this, its first attack by up to ± twice this share, and near-equal attack and expansion targets are picked between by chance. 0 plays exactly to the numbers. |

**Variety and chance:**
- Every roll comes from `AiRandom`, fixed by the match's saved seed, the seat and the purpose.
- So every machine in a multiplayer game, and every reload, rolls the same, while another match rolls differently.
- The AI must never take chance from anywhere else (`UnityEngine.Random`, the clock), or machines would disagree.
- `AiProfile.Varied` gives the profile as a seat plays it in this match.

**New presets:** add an entry to the file. An entry can name a `Base` preset and give only the values it changes:

```json
{ "Id": "hard_bold", "Base": "hard", "Name": "Hard, bolder", "Aggression": 0.95, "RiskTolerance": 0.8 }
```

**Not decided by the profile:** whether the AI may expand, raid or defend at all is set by the catalog rules `aiExpands`, `aiRaids` and `aiDefends`. All three are on in the shipped catalog (since battles stopped freezing the game), as they are in the arena. Tests of a purely defensive AI turn the first two off themselves.

## Combat readiness and the front line

- **Roles:** each ship has an `aiRole` in the catalog. Guard is defence, Bomber and Lancer are attack, and Fortress is support.
- **Whole-battle simulation:** before a strike, `AiEngagement` plays the battle out with the real rules:
  - every ship acts once a turn;
  - Forts and the assigned defender fall first;
  - damage is reduced by armour, never below 1, and scales with health;
  - survivors return fire;
  - the Fortress's shield protects the ships beside it;
  - Lancers fire from range, then recharge.
- **Readiness:** a strike goes only if it wins within the round limit and keeps the profile's share of its health, which is set by `RiskTolerance`.
- **Price:** each way to win is priced by the cost of the ships it needs plus the value of the expected losses. The AI builds the cheapest option, so a few Bombers beat a crowd of Guards.
- **Front-line movement (a game rule):** ships cannot fly through enemy-held cells, only into the one at the edge. Attacks start from cells that are not enemy-held. Ships withdrawing after a battle may cross enemy cells.
- **Start and headquarters:** every map starts each side on its HQ cell alone. The HQ is never left without a ship.

## Final skills review

**How costs were measured:**
- The grown-factory benchmark: `PerfBenchmark.GrownFactoryWithRaidsOnAField`, a Medium AI over 12,000 ticks.
- "ms/tick" is the skill's average share of every game tick.
- Battle skills are timed separately in `PerfBenchmark.BattleSkills`.
- **Level** is the profile's skill level: 0 for Easy, 1 for Medium, 2 for Hard.

### Strategy and military

| Skill | What it decides | Easy (0) | Medium (1) | Hard (2) | Runs every | ms/tick | ms a run | Frequency importance |
|---|---|---|---|---|---|---|---|---|
| Situation assessment (threats) | Fleet strength, the threat to each cell, the most threatened cell | Sees 1 cell out | 2 cells out | Sees everything | `AssessEveryThinks` (1.8 / 0.9 / 0.6 s) | 0.0017 | 0.015 | High: threats move in seconds |
| Situation assessment (economy) | Sales, machines, the opponent's trade progress, Fort defence | Machine counts only | Reads the opponent's economy | Same as Medium | 3 s | 0.0011 | 0.032 | Medium |
| Grand strategy | Stance: Build, Defend, Expand, Attack, Counter | Never attacks | Attacks with a lead, after 10 min | Smaller lead, after 5 min, reacts fastest | assessment | 0.0008 | 0.007 | High |
| Budget | Credits for factory, research or ships; ship reserve | Same rule at every level; the stance drives it | Same | Same | assessment | ~0 | ~0 | High |
| Expansion target | Which cell to claim: ores it lacks, lost cells, danger | Danger matters most | Balanced | Danger matters least | 5 s | 0.0004 | 0.023 | Medium |
| Expansion pacing | When to claim, how many claims at once | 1 claim, only with Fort money ×2 | 1 claim | 2 claims | every think | ~0 | ~0 | High (cheap) |
| Attack target | Which enemy cell to hit (ease, value, supply lines) | Off | Military targets | Also economic raids on supply links | 2.5 s | 0.0008 | 0.019 | Medium |
| Attack fleet & timing | Strike group, rally cell, launch | Off | On | On, with a shorter raid interval | every think | 0.0001 | 0.000 | High (cheap) |
| Fleet composition | Which ship next (counters to Forts and Fortresses) | Same rule at every level | Same | Same | 5 s + when a yard goes idle | 0.0001 | 0.004 | Medium |
| Defence posture | Guard order, reserve cell | On | On | On | 0.6–1.8 s defending / 5 s | 0.0010 | 0.052 | High under threat |
| Battle targeting | Which enemy to shoot | Any hit | Scored by kill and threat | Sharper scoring | per action | battle bench | — | High |
| Battle reinforcement | Pulling spare ships into a battle | None | Defending only | Attacking too | per turn | battle bench | — | Medium |
| Retreat judgement | When to leave a losing attack | From round 8, below 50% | Round 4, below 80% | Round 2, below 100% | per turn | battle bench | — | High |
| Formation / lancer play | Close on the shield; lancers keep their range | Off | Formation | Formation and lancer kiting | per action | battle bench | — | Medium |

### Economy

| Skill | What it decides | Easy (0) | Medium (1) | Hard (2) | Runs every | ms/tick | ms a run | Frequency importance |
|---|---|---|---|---|---|---|---|---|
| Factory planner | Chains, sites, belts, power, Forts, research, research-goods supply | Half work budget | Full budget | 1.5× budget | every think, budgeted | 0.0509 | 0.707 (worst 2.67) | High; most of the AI's cost |
| Needs & gaps | Ship-part shortfall, Ion wanted, stalls | On | On | On | 3 s | 0.0017 | 0.050 | Medium |
| Product selection | Products by credits per MW | Simplest first | Value-ranked once earning | Also unlocks for ship parts | 20 s + when research finishes | 0.0004 | 0.073 | Low |
| Research goals | Target node: products, ship parts, drones, boosts | Cheapest first | Value target, drones | Also ship parts and economy boosts | 15 s + when the queue empties | 0.0005 | 0.067 | Low |
| Bottleneck repair | Replans for starved machines | Off | On | On | 10 s | 0.0020 | 0.202 | Medium |
| Power planned ahead | Builds power before machines, holds new machines when short | No margin | 3 MW margin | 6 MW margin | assessment | 0.0001 | 0.001 | High (cheap) |
| Solar supply chain | Hurries Fort zones and array goods when short of power | Off | On | On | 5 s | 0.0001 | 0.003 | Medium |
| Throughput balancing | Builds more beside saturated machines | Off | Uptime ≥ 95%, up to 3 per recipe | Uptime ≥ 85%, up to 5 per recipe | 12 s | 0.0002 | 0.024 | Low |
| Upgrade vs expand | Which asteroid a new chain starts on | Home | Picks by ores and room | Same as Medium | 20 s | 0.0007 | 0.146 | Low |
| Cross-platform supply | Refining outposts; moves raw-ore refineries out to the ore | Flies raw ore | Outposts need 40 free tiles | 20 free tiles | 20 s + when drones unlock | 0.0004 | 0.081 | Low |

### Logistics and upkeep

| Skill | What it decides | Easy (0) | Medium (1) | Hard (2) | Runs every | ms/tick | ms a run | Frequency importance |
|---|---|---|---|---|---|---|---|---|
| Belt upgrades | Upgrades lines with items queuing mid-route | Off | Slow trigger | Fast trigger | assessment | 0.0065 | 0.059 | Medium (could move to 3 s) |
| Belt refactoring | Second lanes for held-back pieces, detours | Off | Every 45 s | Every 30 s, with detours | 45 / 30 s | 0.0004 | 0.160 | Low |
| Sale depot outlets | A depot for machines stuck with goods | Off | On | On | 25 s | 0.0008 | 0.202 | Low |
| Belt style | Dedicated: untangles mixed lines; sushi: overflow depots on shared lines (profile `BeltStyle`) | Off | Once earning | Once earning | 30 s | - | - | Low |
| Rebuild after loss | Remembers lost cells and their worth | Off | On | On | 5 s | 0.0005 | 0.027 | Medium |
| Delivery priority | High priority for building shipyards and Fort zones | Off | On | On | 5 s | 0.0005 | 0.023 | Medium |
| Storage hub use | A Storage for busy asteroids, once it has a shipyard | Off | On | On | 30 s | 0.0010 | 0.296 | Low |
| Storage management | Each Storage's list: Solar Array goods and next research goods | On | On | On | 10 s | 0.0011 | 0.105 | Medium |
| Relayout / dismantle | Removes dead-end belts and orphan machines | Off | On | On | 30 s | 0.0005 | 0.136 | Low |

**Totals:** the brain's skills come to about 0.025 ms per tick. All AI work, including the planner and orders, is about 0.08 ms per tick, roughly 2% of the 3.4 ms the whole simulation spends per tick on that field.

**Fixed with the arena's first runs.** AI v AI matches showed the AI hardly ever attacking. There were four causes:
1. It picked targets deep in enemy land, with no free cell beside them to gather in, and then waited forever. Targets now need a gathering cell.
2. Retreat counted the Forts in every battle cell, but the target was chosen on the target cell's defence alone. A strike planned at 50× the odds withdrew in round 2. Retreat now uses the same measure.
3. The fleet cap equalled its garrison, one ship per cell held, so no ship was ever free. `MaxCraft` is now the strike fleet on top of the garrison.
4. The strike group was re-picked from scratch each think and never finished gathering. Members now stay in the group.

**Where to look next:**
1. The planner's site search spikes, up to about 2.7 ms in one think.
2. Belt upgrades running on the assessment cadence; it could move to 3 s.
3. The Storage hub skill, whose surplus-buffering role needs rethinking now that Storages only collect what they are told to.

## The headless arena (AI v AI)

The arena plays AI v AI matches with no visuals, as fast as the simulation runs. An 8-minute early game takes about 3 seconds of wall time; late games are slower as factories grow.

```bash
tools/ai-arena.sh tools/arena/example.json
```

- **The editor:** it must not have the project open, because batch mode needs the project to itself.
- **Results:** they go to `Logs/arena/<config>-<time>/`, or to a second argument giving the directory.
- **Other Unity versions:** set `UNITY=/path/to/Unity`.

**Sides:**
- Side **A** plays the player's faction and side **B** the AI's faction.
- On the hand-made maps the AI's faction starts with more cells, so `swapSides` (on by default) plays every match both ways.
- Read win rates per profile, not per side.

**Configs** live in `tools/arena/`:
- `smoke.json`: one short match, to check it runs.
- `ladder.json`: Easy, Medium and Hard against each other on three fields.
- `example.json`: custom profiles (bolder, no hubs, half-cadence).

| Config field | Meaning |
|---|---|
| `maps` | Map ids: `map_a`, `field-424242`, ... |
| `randomFields`, `fieldSeedStart` | Also play this many generated fields, `field-<seed>` onwards |
| `pairings` | `{ "a": profile, "b": profile }` pairs |
| `repeats`, `worldSeed` | Each pairing on each map this many times, seeds `worldSeed`, `worldSeed + 1`, ... |
| `swapSides` | Also play each match with the sides swapped |
| `maxMinutes` | Game minutes before a match is a draw. The leader at the limit is recorded: more cells, then more sales. |
| `maxBattleTurns` | A battle longer than this is called off (counted as stalled) |
| `tradeWin` | Off by default. Only the player's faction can win by trade, so leaving it on makes matches one-sided. |
| `difficulty` | The trade target, and the preset for any side without a profile |
| `rules` | `aiExpands`, `aiRaids`, `aiDefends` for both sides (all on by default) |
| `sampleEverySeconds` | Interval of the timeline in `samples.csv` |
| `profiles` | Profiles for this run, the same form as the presets file, `Base` included |

**Seat matches** (three or four seats, teams or free-for-all) on maps with a headquarters per seat, such as `map_d` (Four Corners):

```json
{
  "seatMatches": [ { "map": "map_d", "seats": [ { "profile": "hard", "team": 1 }, { "profile": "medium", "team": 2 },
                                               { "profile": "medium", "team": 1 }, { "profile": "hard", "team": 2 } ] } ],
  "rotateSeats": true
}
```

- Seats are listed in seat order. `team` 0 means a seat plays on its own.
- `rotateSeats` (on by default) plays each match once per rotation of who sits where.
- `repeats` and `worldSeed` work as for pairings.
- Results:
  - `seats.csv`: one row per seat per match, with its team, whether it won or was knocked out, cells, sales, ships, machines, research and AI ms per tick.
  - `seats_summary.txt`: per profile, seats played, wins, win rate, knock-outs and averages.
- Configs: `tools/arena/seats_ffa.json` (Hard, Hard, Medium, Medium each on its own) and `tools/arena/seats_teams.json` (Hard + Medium against Hard + Medium).

**Output files:**
- `matches.csv`: one row per match. It holds the winner and reason, the leader at the limit, minutes, battles, and each side's sales, credits, cells, ships, machines, research levels and AI ms per tick. It also counts each side's attacks and how they ended: won, withdrew, fatigued or repelled.
- `reports.txt`: each side's final brain report, with stance, budget, target, strike and economy reasons. This is the first place to look when a profile does something unexpected.
- `skills.csv`: per match and side, each skill's ms per tick, ms per run, runs and worst run.
- `samples.csv`: each side's economy over time: credits, sales, sales per minute, cells, ships, power.
- `summary.txt` / `summary.json`: per profile, the matches, wins, losses, draws (and how many it led), win rate, average sales and cells, minutes to win, and AI ms per tick.

**Stress-testing weights:**
- Give a preset one changed value, such as `{ "Id": "h_a90", "Base": "hard", "Aggression": 0.9 }`, and pair it against the plain preset.
- Use `DisabledSkills` to measure what one skill is worth.
- Use `CadenceScale` to trade AI cost against strength.
- Use several `randomFields` and `repeats` so one map's quirks do not decide the result.

**Tracing:**
- `AiArena.Probe` is called at every sample with the match's `GameLoop`; side B is `loop.Services.Ai` and side A is `loop.PlayerSide` (an `AIController`).
- `AiArenaTrace.TraceStrike`, an explicit test, is a worked example that writes `Logs/strike_trace.txt`.

**Limits:**
- Matches run one after another. Two Unity processes cannot open the same project, so for parallel runs use a copy of the project.
- The arena is also callable from code: `AiArena.Run`, `AiArena.PlayMatch`. `AiArenaTests` uses it.

## Belt style

Each profile has a `BeltStyle`. **Sushi** (Easy, Medium, Hard) shares lines freely: cheaper, and the Belt style skill sells
surplus that backs up on a mixed line through an overflow Sale Depot. **Dedicated** (the cautious variants) keeps one item
to a line: once earning, the factory planner routes each link on belts carrying only its item (sharing where a line of its
own would be over half as long again, plus four belts), the skill gives producers on mixed lines a line of their own, and a
machine added beside a saturated one on the same recipe is placed in a row with it (a manifold: the input line branches into
each, their outputs rejoin on the far side). `tools/arena/belt_style.json` pits the two: in October 2026 dedicated sold
about 8% less on average, with wide swings by map.
