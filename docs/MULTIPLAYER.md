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
| 2b | AI with several enemies and allies; arena seats and teams; seeded AI randomness | |
| 2c | Presentation: local seat, four seat colours, HUD and camera | |
| 3 | Performance for four factories and four AIs on every machine | |
| 4 | Ship offline multi-seat play (you plus AIs, teams, per-seat difficulty) | |
| 5 | Battle design for more than two sides (pausing, three-way cells) | Decision needed before 6 |
| 6 | Lockstep loop: tick waits for people's inputs, seat handover as a logged command | |
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
- **2b:** the AI still has one opponent (`AiSide.Foe` / `WorldLookup.Opponent`). Four AIs play without errors but seats 3 and 4 play poorly.
- **2c:** the HUD reads seat 1 as the local player; results, colours and the "you are out" state need the local seat. When the only person goes out while several AI teams remain, the match continues with nobody to watch it: the HUD should say "you are out".
- **4:** shipped maps still have two HQs; a four-corner map is built in tests only.
- **Shared alert history:** it keeps the last 30 alerts for every seat together. Fine with alerts dropped for AI seats; worth splitting per seat if four people play.
- **6:** a failed save adds an alert to the shared world. That is one machine's news and must leave the world before lockstep, or it would desync.

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

1. **Battles with several seats** (phase 5): pause everyone, pause only the two sides, or resolve without pausing; and what happens when three teams meet in one cell.
2. **What allies share:** building in each other's cells, belts and drone links across allied cells, research, vision.
3. **Seeded AI randomness** (phase 2b): per-match personality wobble, opening variety, tie-breaks. It must draw only from a seeded generator saved in the world.
4. **Maps:** four corner HQs (`map_c` is 4×4 with two today). With fewer than four players, empty corners start neutral.
