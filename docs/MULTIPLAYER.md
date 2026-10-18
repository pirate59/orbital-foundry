# Orbital Foundry multiplayer

The plan for up to four seats on a 4×4 map, any mix of people and AIs, with optional teams. It records where the work stands and what the code audit found.

## Model

- **Seat:** one of up to four factions, each with a headquarters in a corner of the 4×4 grid.
- **Controller:** what drives a seat: a local person, a remote person, or an AI. It can change mid-game, for example an AI taking over a seat whose player dropped out.
- **Team:** optional. Seats on one team are allies; with no teams, every seat is on its own.

| Example | Seat 1 | Seat 2 | Seat 3 | Seat 4 |
|---|---|---|---|---|
| 2 people v 1 AI | Person, team 1 | Person, team 1 | AI, team 2 | empty |
| Person + AI v person + AI | Person, team 1 | AI, team 1 | Person, team 2 | AI, team 2 |
| Free-for-all | Person | AI | AI | Person |

**Networking: deterministic lockstep.**
- Every machine runs the whole simulation. Only people's commands are sent, each stamped with the tick it applies on.
- AI seats run inside every machine's simulation and are never sent: the AI decides purely from the world, so every machine computes the same moves.
- A Cloudflare Durable Object per room relays commands, hands out room codes and keeps the command log for reconnecting by replay. It never runs the game.
- Machines compare world hashes every few seconds to catch any drift at once.

## Phases

| # | Phase | Status |
|---|---|---|
| 1 | Determinism harness | **Done** (see below) |
| 2a | Seats in the simulation: factions, teams, per-seat AI state and profile, outcomes, battles, alerts | **Done** (see below) |
| 2b | AI with several enemies and allies; arena seats and teams; seeded AI randomness | **Done** (see below) |
| 2c | Presentation: local seat, four seat colours, HUD and camera | **Done** (see below) |
| 3 | Performance for four factories and four AIs on every machine | **First pass done** (see below) |
| 4 | Ship offline multi-seat play (you plus AIs, teams, per-seat difficulty) | **Mostly done**: the new-game seat setup (below) |
| 5 | Battles alongside play, several at once, with a turn timer | **Done** (see below) |
| 6 | Lockstep loop: tick waits for people's inputs, seat handover as a logged command | **Done** (see below; the network transport is phase 7) |
| 7 | Relay and lobby: rooms, seat and team picking, reconnect | |

## Phase 1: determinism harness (done)

**What it is:**
- `ISideController` (`Assets/Scripts/Domain`): anything that plays a side through commands. `AIController` is one; `GameLoop.PlayerSide` takes any.
- `CommandRouter.Executed`: an event raised for every command applied, in order.
- `CommandJournal.cs` (`Assets/Scripts/Simulation`):
  - `RecordingSide` writes down every command a side issues, by slot (the how-many-th time the loop called on that side) and tick.
  - `ReplaySide` plays a side from that journal alone.
  - `WorldHash` fingerprints the saved world.
- `DeterminismHarness`: records and replays AI v AI matches, with world hashes every 100 ticks.

**What the tests prove** (`DeterminismTests`):

| Test | Result |
|---|---|
| The same match played twice | Identical, hash for hash |
| Side A replayed from its commands alone, side B's AI live (the lockstep model) | Identical. map_a, Hard v Hard: 194 commands and a battle, decided at tick 5,280. field-424242, Medium v Hard: 381 commands over 12,000 ticks |
| One command dropped from the replay | Caught within one checkpoint of where it went missing |
| Two machines loading the same save and playing on | Identical |

**Finding: AI memory makes reloads diverge.**
- A reloaded copy parts from the game that carried on within 100 ticks (10 s), measured by the explicit `ProbeReloadAgainstCarryingOn` test.
- The cause: the AI's working memory (`AiMemory`, skills' own tables, planner scratch) is not saved, so a reload thinks afresh.
- **Fine for lockstep** as long as every machine reloads together; the save test above shows those copies agree.
- **Rule for later phases:** any mid-game re-sync, such as sending one machine a fresh copy of the world, must be applied on every machine at the same tick. The alternative is saving the AI's memory.

**Not yet covered:**
- Cross-browser runs of the WebGL build. Every peer runs the same build, and WebAssembly arithmetic is standard, but this is untested until phase 6.
- Battles in the replay test: only one so far. A longer battle-heavy match would strengthen it.

## Phase 2a: seats in the simulation (done)

The rules now play up to four seats in any mix of people and AIs, with optional teams. Two-seat play is unchanged: map_a Hard v Hard in the arena is decided on the same tick as before.

**Data (save schema 4):**
- `FactionId` adds `Seat3` and `Seat4`. Seats 1 and 2 keep their old names, `Player` and `Ai`, until phase 2c.
- Each `FactionState` now holds:
  - `team` (0 means a team of its own)
  - `aiControlled`
  - `aiProfileId`
  - its own saved AI state (`ai`)
  - its own trade ledger (`trade`)
- `MatchOutcome.Decided` with `WorldState.winners` replaces `PlayerDefeat` / `AiConceded`.
- Alerts carry a recipient, `AlertState.to`; Neutral means everyone.
- A battle keeps its turn order in `EncounterState.sides`.
- `SaveService.UpgradeSeats` converts older saves: the AI's state, profile and the player's trade move to their seats, and old outcomes become winners.

**Setup:**
- A map offers one seat per HQ. `GameFactory.CreateNew(catalog, map, seed, seats)` takes a `SeatSetup` per seat (who plays it, with which profile, on which team).
- With no setup, seat 1 is a person and every other seat an AI, each on its own: the old game.
- Maps need an HQ for seat 1 and at least one other seat, at most one per seat.
- `GameLoop` gives every AI seat its `AIController`. `SetController` hands any seat to an AI or a command stream (`PlayerSide` is seat 1's).

**Rules:**
- **Allies:** `WorldLookup.AreAllies` / `AreEnemies`. Allies cannot attack or claim each other's cells, their ships do not block each other, and they are never targets.
- **Out of the match:** a seat whose HQ falls is out. Its craft withdraw, its factories stop and its AI stops; whoever holds its cells may clear its structures.
- **Winning:** the last team with a seat still in wins, as does a team whose seat holds the trade rate. Every seat of the winning team wins, including any already out; the rest concede. The match goes on if a person is among the winners, and ends otherwise.
- **Battles:** every seat with ships in the battle, plus the target's owner, takes turns in seat order, and a round ends back at the first. `EncounterService.LastResult` records how it ended; the arena no longer reads the message text.
- **Alerts:**
  - Each is written for its recipient ("you", "your ally", "the AI", "the enemy"), so alerts read the same on every machine.
  - Alerts for AI-played seats are dropped.
  - The battle status line and order replies are worded for this machine's `Perspective.Viewer`.
- **Per-seat arrays:** per-faction power and production stats are sized for every seat (`WorldLookup.FactionSlots`). This was found by the four-AI test, which crashed on its first tick before the fix.

**Tests:** `SeatsTests` covers:
- four seats from a four-corner `map_c`
- allies
- one seat going out while play goes on
- last team standing, including fallen teammates
- no person winning ending the match
- a team trade win
- alert recipients
- battle turns over three seats
- four-seat save and load
- upgrading an old save
- four AIs playing 1,500 ticks without errors

The determinism tests still pass, and leave the AI state of the seat standing in for a person out of the world hash.

**Left for later phases:**
- **4:** shipped maps still have two HQs; a four-corner map is built in tests only.
- **Shared alert history:** it keeps the last 30 alerts for every seat together. Fine with alerts dropped for AI seats; worth splitting per seat if four people play.
- **6:** a failed save adds an alert to the shared world. That is one machine's news and must leave the world before lockstep, or it would desync.

## Phase 2b: AI with several enemies and allies (done)

**Enemies and allies:**
- `AiSide.Foe` is gone. `AiSide.IsEnemy` / `IsAlly` follow the teams, and `AiSide.RivalOf` picks the enemy still in the match with the most cells.
- **Every enemy counts** for threats (ships out of their own side's land, or bound for the AI's), attack targets, defences, fleet counters, expansion danger and battle chasing.
- **Comparisons use the rival:** economy (sales, machines, cells, credits) and the military balance. The balance uses the strongest single enemy fleet, not the sum of all enemies.
- **Trade counter:** watches whichever enemy is closest to a trade win.
- In a two-seat match all of this is exactly the old behaviour.

**Teammates:**
- An AI never targets an ally (its cells, ships or Forts).
- In battle it chases invaders of allied land as well as its own.
- It weighs an attack target more when an ally's raid is bound there or an ally's battle is already being fought there.

**Seeded variety:**
- `AiRandom`'s rolls are fixed by the saved match seed, the seat and the purpose: the same on every machine and every reload, different between matches.
- The profile's new `Variety` (0.1 in the presets) wobbles Aggression and RiskTolerance by up to ±0.1 and the first attack by up to ±20%, and breaks near-ties between attack and expansion targets.

**Arena:**
- Seat matches (`seatMatches`, `rotateSeats`) play 3–4 AI seats, in teams or free-for-all, with `seats.csv` and `seats_summary.txt` as output.
- `map_d` (Four Corners) ships as a four-HQ map, built from `map_c`. The game does not offer it yet (phase 4).

**Tests:**
- `AiSeatsTests`: the rival, threats from every enemy but not allies, no attacks on allies, variety fixed per match and seat, and a four-seat arena match.
- `DeterminismTests` replays seat 1 of a four-seat map_d match (three AIs live) hash for hash.
- `SeatsTests` now uses `map_d`.

**First arena results** (40-minute games, map_d):

| Config | Games | Decided | Battles a game | Knock-outs | AI cost per seat |
|---|---|---|---|---|---|
| Free-for-all, Hard ×2 + Medium ×2 | 4 | 0 | 0–5 | 3 | ~0.09–0.10 ms/tick |
| 2v2, Hard + Medium a team | 4 | 0 | 1–6 | 1 | ~0.10 ms/tick |

The AI plays four-seat games without errors and fights, but is cautious: no game was won inside 40 minutes. Tuning it for several enemies, for example pressing an attack on a weakened neighbour or ganging up with an ally, is arena work for later.

## Phase 2c: presentation (done)

**The local seat:**
- `Perspective.Viewer` is the seat played at this machine (seat 1 until a lobby or setup screen picks another).
- HUD, input, world view, research, resource bar, sounds and battle scenes all read it.
- Orders are issued as that seat. Its HQ is where the camera starts. Its buildings get the need and stall markers, its alerts are shown, and its trade drives the goals line.

**Allies in the UI:**
- Right-click and hover treat an ally's cell as friendly: move, not claim or attack. Ships and Forts are attack targets only when they are an enemy's.
- Names read "Your", "Allied" or "Enemy".
- Cells and turns name other seats "AI 2", "Ally 3" or "Enemy 4" when there are more than two seats, and just "AI" or "Enemy" in a two-seat game.
- The goals line counts enemy HQs taken ("Enemy HQs 1 / 3 taken").

**Seat colours**, in every art style:

| Seat | Platform lights | Enamel trim | Sprites | Pixel stations | Comet Garden |
|---|---|---|---|---|---|
| 1 | blue | seafoam teal | mint | blue | mint |
| 2 | red | cherry | coral | red | peach |
| 3 | lime | atomic lime | lime | lime | lime |
| 4 | violet | violet | violet | violet | lilac |

- Amber stays the unguarded-territory warning, and neutral keeps mustard and lavender.
- Each seat's idle ships face from its corner toward the middle of the map.

**Out of the match:**
- When the local seat's HQ falls while the match goes on, the result overlay says "Out of the match", says whether an ally fights on, and offers Watch or New game.
- If the local team later wins, the victory screen follows.

**Checking it:**
- `SeatsMockup` (Orbital Foundry → Preview four seats) plays four AIs on map_d for fifteen minutes and renders it to `Logs/seats-*.png`.
- `SeatsTests.TheBattleLogSpeaksToWhoeverIsWatching` covers the viewer-relative battle log.

## Phase 5: live battles and the turn timer (done)

Decided: battles no longer freeze the game, in single-player and multiplayer alike, and turns run on a Hearthstone-style burning timer.

**How battles work now:**
- **The world keeps running.** Factories, belts, research, travel and other battles all carry on. `MatchMode.Tactical` is gone from play (kept only so old saves load).
- **Siege:** only the cell under attack stops producing (`cell.factoryOnline` is off while it is a battle's target); its HQ or Fort still defends.
- **Several battles at once,** in `WorldState.encounters`, each with an id, never two over the same cells. A ship fights in at most one.
  - Ships in a battle take battle orders only. Map orders are refused with `IN_BATTLE`.
  - Battle orders may name their battle (`GameCommand.battleId`); otherwise the ship's battle, or the one where it is the actor's turn.
- **The turn timer** (catalog rules):

  | Rule | Ticks | Time |
  |---|---|---|
  | `battleTurnTicks` | 300 | 30 s |
  | `battleShortTurnTicks` (after one time-out) | 150 | 15 s |
  | `battleShortestTurnTicks` (after two or more) | 100 | 10 s |
  | `battleFuseTicks` (the fuse lights) | 100 | 10 s left |

  - A turn that runs out is ended for its seat with a strike, and a defender choice left waiting goes to the lowest-numbered ship.
  - Ending a turn in time clears the seat's strikes.
  - It is all counted in ticks, so every machine agrees.
- **AI battle turns** are part of the AI's tick: one action every `aiBattleStepTicks` (6 = 0.6 s), counted from the turn's start, so nothing about pacing is stored. Single-player's AI turns setting maps to 6 (Watch), 2 (Quick) or 0 (Instant: a whole turn in one tick).
- **Screen:**
  - The HUD shows the focused battle (`Perspective.FocusBattle`): the selected ship's, else one where it is your turn, else any you are in.
  - It shows the clock, "FUSE" in orange for the last 10 s, your strikes, and how many other battles are on.
  - Battle scenes play over the running game. Space ends your turn when it is yours, else pauses (single-player).
- **Old saves (schema 5):** a frozen battle becomes the first live one with a fresh turn.
- **Other seats' battles:** you are never pulled in.
  - A "Battles elsewhere" list shows each: cell, sides, whose turn and their clock. Clicking one watches it (camera and panel, read-only); clicking again stops.
  - Your own turn always takes the panel first.
  - Battle scenes play only for battles you fight in or watch (a spectator sees all).
  - Third parties cannot join a battle in progress (decided for the first multiplayer version).

**Tests:** `LiveBattleTests` covers:
- the world running on, with only the besieged cell stopped
- two battles with their own turns, and no battle over another's cells
- no map orders for ships in battle
- the timer passing a turn, shortening the next to 15 s then 10 s, and acting in time restoring 30 s
- the AI's watchable pace
- the old-save upgrade

**Arena:** battles are counted as they start (`EncounterService.BattleStarted`). map_a Hard v Hard is still decided at 8.9 minutes. 2v2 games on map_d now see up to 8 battles in 40 minutes.

**Caught by the determinism test:** a first version kept the AI's next-action tick in the shared battle. A replayed person's seat never wrote it, so worlds parted; pacing from the turn clock alone fixed it.

**Still to do:**
- Play-testing of the battle panel.
- In multiplayer, the menu, research screen and single-player pause must not stop a shared game (phase 6).

## Phase 6: the lockstep loop (done)

`Simulation/Lockstep.cs`. The game plays in lockstep once `GameLoop.UseLockstep(session)` is called; alone, nothing changes.

**How a tick runs:**
- A person's command is not carried out when given. `Submit` stamps it for a tick `InputDelay` ticks ahead (3, about 300 ms) and sends it to every machine. It returns `SENT`; the real result arrives through `LockstepSession.LocalCommandRan` (and `GameLoop.CommandSubmitted`) when it runs.
- Every machine sends one `Inputs` message per tick for its seat, empty or not. A tick runs only once every person's inputs for it are in; otherwise the loop waits (`GameLoop.WaitingFor`, shown in the HUD) and keeps one tick in hand.
- Each person's seat is played by a `LockstepSide`, which carries out that seat's commands in its turn of the tick's thinking half, in seat order: the same place the replay test played seat 1. AIs run on every machine and send nothing.
- The game speed is held at 1x in lockstep. Pausing on one machine stalls the others: shared speed and pause are for phase 7.
- A machine only ever gives orders for its own seat (the session overwrites the actor).

**Drift:** every `HashEvery` ticks (50, five seconds) each machine sends `WorldHash.Of` its world. A mismatch sets `DesyncTick` and `DesyncWith` and raises `Desynced`; the HUD says so. A failed save no longer adds an alert to the shared world in lockstep.

**Seat handover:** a `Handover` message hands a person's seat to an AI from a given tick. Every machine applies it in that seat's turn of that tick: the seat becomes AI-played with the given profile, and its inputs are no longer waited for. The tick must still be ahead of every machine. In phase 7 the relay picks it: the tick after the last inputs it passed on from that seat, which every machine is sure to have.

**Transport:** `ILockstepTransport` (send; receive in order). `LoopbackHub` joins machines in one process, with optional latency, for tests. Phase 7 adds the Cloudflare relay behind the same interface.

**Tests (`LockstepTests`):** each machine is a whole game, joined in memory. On a three-seat map (two people, one Hard AI):
- two machines play 2,400 ticks with latency, seat 1 giving a recorded AI's orders, and end identical with no drift seen
- a tick waits for missing inputs, and says whose
- a command runs on every machine on the same tick, and not at once
- a changed world is caught at the next fingerprint, by both machines
- a dropped person's seat is handed to an AI, and the remaining machines stay identical

## Phase 3: performance, first pass

**The benchmark:** `PerfBenchmark.FourSeatsOnFourCorners` (explicit; run with `-testFilter FourSeatsOnFourCorners`).
- Four AIs on map_d with expansion, raids and defence, grown for 30 minutes (about 175 buildings, 650 belts).
- Then 2,000 ticks timed one by one: median, 95th and 99th percentile, worst, and ticks over 4 and 8 ms.
- Also each seat's AI, and per stage both time and bytes allocated (`Perf.TrackAllocations`, off in the game).
- Output in `Logs/perf_four_seats.txt`.

**Fixes:**
- **AIs no longer all think on the same tick.** Each seat's think tick is offset by its seat number, so their planners stop stacking up in one frame. Worst AI tick: 7.3 → 4.9 ms.
- **Route search allocates nothing per search.** It reuses its scratch collections and a binary heap (`LongMinHeap`) instead of a fresh `SortedSet` each time. Routes are unchanged: same pop order, same end state.
  - Search garbage: 40 → 0.1 KB a tick.
  - Search time: 0.19 → 0.10 ms a tick.
  - Garbage collections: 44 → 19 per 2,000 ticks.

**Editor numbers, four seats:**

| | Before | After |
|---|---|---|
| Mean tick | 3.48 ms | 3.25 ms |
| 99th percentile | 7.14 ms | 5.95 ms |
| Worst tick | 10.2 ms | 7.7–9.2 ms (the worst is a garbage collection) |
| Ticks over 8 ms | 6 | 0–2 |
| GC collections / 2,000 ticks | 44 | 19 |

**Still to do:**
- **Logistics is the steady cost.** Requests plus Dispatch take about 2 ms of the 3.25.
- **Remaining garbage is spread thinly**, about 9 KB a tick in the AI, 8 in Dispatch and 6 in Construction. Finding it needs proper allocation profiling.
- **Browser numbers are untested.** The WebGL build runs C# several times slower than the editor; the F3 overlay in an exhibition game gives the real per-frame figures.

## New-game seat setup and exhibitions

**The title screen:**
- It sets up each of the four seats as Off, You or AI.
- Each AI has a difficulty (Easy / Medium / Hard) and an aggressiveness (Cautious / Normal / Bold): the `*_cautious` / `*_bold` presets in `ai_profiles.json`.
- Each seat can be on Team A, Team B or no team.
- At least two seats must play, at most one can be You, and they cannot all be one team.

**The map:**
- A new game is a generated Four Corners field, `seats-MASK-SEED` (for example `seats-1100-123`). A seat that is off leaves its corner an ordinary neutral cell.
- The map ID carries the seats and seed, so saves rebuild it.
- The default setup (You and one AI on opposite corners) plays like the old game.

**Exhibition** (no seat is You):
- The person watches: `Perspective.Spectating` is set, the view follows the first seat, and no orders can be given.
- Sides are named by seat, every alert and every battle scene is shown, and the result names the winners.

**Return to menu** in the in-game menu saves and goes back to the title screen, where the game can be resumed.

## Audit for phase 2

Counts are references in `Assets/Scripts`.

| Pattern | Rules | AI | Presentation | Arena |
|---|---|---|---|---|
| `FactionId.Player` | 46 | 1 | 164 | 7 |
| `FactionId.Ai` | 27 | 8 | 11 | 3 |
| `AiSide.Foe` | | 23 | | |
| `w.ai` (the one saved AI state) | 24 | 1 | 1 | |

### Rules (phase 2a), by kind of change

1. **Faction set and setup.** Mechanical: becomes a list of seats.
   - `GameFactory` creates exactly Player and Ai.
   - `StateValidator` requires exactly two factions.
   - `CatalogRepository` requires one Player HQ and one Ai HQ per map.
   - `CommandRouter` only accepts those two as actors.
2. **Battle turns.**
   - `EncounterService` alternates turns with `Opponent()` and starts each round on Player.
   - `GameLoop` dispatches battle turns by Player/Ai.
   - These become the encounter's own two sides (attacker and defender), which also suits battles between any two seats.
3. **Win and loss.** This is a design change.
   - `MatchOutcome` has only `PlayerDefeat` and `AiConceded`.
   - `OwnershipService` decides the outcome from the two HQs and withdraws the AI.
   - The trade win counts only the player (`TradeService`, `EconomyService`, one `TradeLedger`).
   - It becomes: a seat is out when its HQ falls; a team is out when all its seats are; the last team standing wins. Each seat gets its own trade ledger.
4. **One AI state and raids.**
   - `w.ai` holds one AI's queue, goal and raid.
   - `RaidService` tracks raids only for the AI and warns only the player.
   - It becomes one saved AI state per AI seat, with raid warnings going to the seat being raided.
5. **Alerts written for one viewer.**
   - Rules code writes "You captured…", "your shipyard", "The AI attacks" and so on, and only raises some alerts when the player is involved: `FactorySimulation`, `CaptureService`, `ResearchService`, `EncounterService`, `CombatResolver`, `OwnershipService`.
   - `AlertState` has no recipient.
   - It becomes: an alert names its seat(s), and the HUD words it for the local player. This is the most spread-out rules change.

### AI (phase 2b)

- `AiSide.Foe` appears 23 times: situation assessment (5), attack target (5), attack fleet (3), fleet composition (3), tactics (3), battle skills (2), brain (1), expansion target (1).
- Each use becomes "my enemies" (threats and targets across several seats) and "my allies" (never target; help defend).
- `AiSide.StateOf` already gives each side its own state; that state needs saving per seat instead of only for `FactionId.Ai`.

### Presentation (phase 2c)

- 164 of the 218 `Player` references, mostly `HudController` (77) and `WorldInput` (59), with `WorldView` at 11.
- Nearly all mean "the local player" and become a local seat id. Mechanical and low-risk.
- Two-colour assumptions (blue / red, including the platform safety lights) become four seat colours.

## Open decisions

1. **Battles with several seats** (phase 5): decided. Battles run alongside play with a turn timer. Every seat with ships in the battle area takes turns, in seat order.
2. **What allies share:** building in each other's cells, belts and drone links across allied cells, research, vision.
3. **Seeded AI randomness** (phase 2b): per-match personality wobble, opening variety, tie-breaks. It must draw only from a seeded generator saved in the world.
4. **Maps:** four corner HQs (`map_c` is 4×4 with two today). With fewer than four players, empty corners start neutral.
