# Orbital Foundry

A solo, browser-playable Unity game: design connected factories, sell goods, and claim territory from an AI opponent that builds its own factories by the same rules. Battles are occasional, turn-based, and pause the factory economy.

The design baseline is `RTS_Automation_Game_Design_Working_Draft.md`; the implementation blueprint is `TODO_ARCHITECTURE.md`. The prototype checklist is in [docs/UAT.md](docs/UAT.md).

## Requirements

- **Unity 6000.6.4f1** (Unity 6), installed through Unity Hub with **Web Build Support**. `ProjectSettings/ProjectVersion.txt` pins this exact version.
- Built-in Render Pipeline, UI Toolkit, Unity Test Framework 1.8.0. No networking packages and no server.
- A modern 64-bit desktop browser with WebGL 2 and WebAssembly.

## Opening the project

1. In Unity Hub choose **Add project from disk** and select this folder.
2. Open it with 6000.6.4f1.
3. Run **Tools → Orbital Foundry → Create Prototype Scene**. This creates `Assets/Resources/UI/GamePanelSettings.asset`, creates `Assets/Scenes/Main.unity` containing one `GameBootstrap`, and registers that scene as the only build scene. Running it again is safe.
4. Open `Assets/Scenes/Main.unity` and press Play.

You can also run these steps headless:

```bash
Unity -batchmode -projectPath . -executeMethod OrbitalFoundry.EditorTools.ProjectSetup.CreatePrototypeSceneBatch -logFile Logs/setup.log
```

## Project layout

| Path | Contents |
| --- | --- |
| `Assets/Scripts/Domain` | Contracts (`GameContracts.cs`), catalog loading, validation, the command router, the game loop, and world creation |
| `Assets/Scripts/Factory` | Grid rules, the directed belt graph, pull routing, and the factory tick |
| `Assets/Scripts/Economy`, `Territory`, `Combat` | Ledger and purchases, ownership and capture, combat, movement, encounters, and raids |
| `Assets/Scripts/AI` | Procedural factory and belt planning, tactics, and the AI controller |
| `Assets/Scripts/Persistence` | Single-slot local save with backup |
| `Assets/Scripts/Presentation` | Runtime sprites, isometric world view, camera, input, HUD, and bootstrap |
| `Assets/Resources` | `catalog.json`, `maps/map_a.json` and `map_b.json`, plus UI assets |
| `Assets/Tests/EditMode` | EditMode tests |
| `deploy/_headers` | Cloudflare cache headers for the WebGL player |
| `deploy/site` | Committed WebGL player for Cloudflare Workers or Pages |
| `wrangler.jsonc` | Points Cloudflare Workers at the committed player |
| `tools/cloudflare_pages.py` | Stages the Unity build and rejects stale or incomplete deployments |
| `tools/serve_web.py` | Local HTTP server for `Build/Web` that sends matching headers |

All rules run in plain C# services against `WorldState`. MonoBehaviours and sprites hold no game state. Player UI and the AI submit the same `GameCommand`s through `CommandRouter`.

## Maps

- `map_a` (Frontier Line) is used for every new game.
- `map_b` (Broken Frontier) is used only by `AIPlannerTests`. It shows that the AI plans around obstacles without an authored layout.

Balance values live in `Assets/Resources/catalog.json`. Change them there, not in code.

## Controls (desktop mouse and keyboard)

| Input | Action |
| --- | --- |
| Left-click | Select a cell, resource node, machine, or craft. The selection panel shows that item's output, capacity, recipe, and available stock where relevant. |
| Right-click with your craft selected | Normal play: send it to a cell. Arriving in a clear cell you don't own claims it automatically (10s hold); a cell with enemy craft is attacked. Hovering shows what the click will do. In battle: attack, move, or hold. |
| Right-click otherwise, or Esc | Cancel the active tool or clear the selection |
| 1 – 9 | Extractor, Refinery, Assembler, Sale Depot, Shipyard, Fort Construction Zone, Storage, Water Purifier (once researched), or Solar Array tool |
| B, then left-drag | Draw belts. Each tile step places one one-way belt. Start a drag on an existing belt to branch it or merge into it. |
| X, then left-click or left-drag | Dismantle: click one of your machines, or drag across belts. Half the cost is refunded; items inside are lost. |
| WASD / arrow keys, right- or middle-drag | Pan |
| Mouse wheel, Q / E | Zoom |
| Space | End your turn in battle |
| Esc | Cancel the current tool or selection; with nothing selected, open the menu |

Craft are built at a **Shipyard**. Select one of your shipyards, then order a craft from its **Working on** list in the selection panel. The order costs a small credit fee. The shipyard then pulls the craft's parts (Iron Bars, plus Signal Relays for most types) along belts and launches the craft when the parts have arrived and the build time has passed.

## Tests

```bash
Unity -batchmode -projectPath . -runTests -testPlatform EditMode -testResults Logs/editmode-results.xml -logFile Logs/tests.log
```

There are five required test files plus a shared harness, `TestWorld.cs`. They cover combat (including the tank profile you supplied), belts and logistics, ownership and grace expiry, the AI construction gate on both maps (including replanning after a transit cell is captured), and save round trips.

## Web build

From the menu: **Tools → Orbital Foundry → Build Web**. Headless:

```bash
Unity -batchmode -projectPath . -executeMethod OrbitalFoundry.EditorTools.WebBuild.BuildWebBatch -logFile Logs/webbuild.log
```

The build uses Brotli compression with Unity's decompression fallback enabled. Its `.unityweb` files stay below Cloudflare's 25 MiB static asset limit, and Unity decodes them after the browser handles any transport compression Cloudflare applies. The build writes to `Build/Web` and copies `deploy/_headers` into it. Do not add `Content-Encoding: br` rules for `.unityweb` files.

### Local HTTP test

Do not open `index.html` as a file. Serve the build over HTTP with the bundled server:

```bash
python3 tools/serve_web.py 8080
```

Then open http://localhost:8080 and confirm the game reaches its playable screen.

### Staging the WebGL player for Cloudflare

Cloudflare checks out the GitHub repository but does not run this project's Unity build. `Build/Web` is ignored by Git, so **stage the built player** after each Unity build:

```bash
Unity -batchmode -quit -projectPath . -executeMethod OrbitalFoundry.EditorTools.WebBuild.BuildWebBatch -logFile Logs/webbuild.log
python3 tools/cloudflare_pages.py stage
python3 tools/cloudflare_pages.py verify
git add Assets Packages ProjectSettings deploy docs tools README.md wrangler.jsonc
git commit -m "Update Orbital Foundry WebGL player"
git push
```

The staging command copies the Unity player into tracked `deploy/site` and records hashes of the Unity source and output. The verification command fails a build if source files changed without a fresh Unity build, if an output file is missing or changed, if `_headers` is stale, or if a file exceeds Cloudflare's 25 MiB static asset limit. Unity itself is needed only on the machine that stages the player.

### Cloudflare Workers Builds from GitHub

The default Workers deploy command, `npx wrangler deploy`, uses the repository-root `wrangler.jsonc`. Its `assets.directory` points to `./deploy/site`, which contains the committed WebGL player. In **Workers & Pages → your Worker → Settings → Build → Build Configuration**, use:

| Setting | Value |
| --- | --- |
| Git branch | `codex/comet-garden-graphics` for this update; switch to `main` after merging it |
| Root directory | Repository root (leave blank) |
| Build command | `python3 tools/cloudflare_pages.py verify` |
| Deploy command | `npx wrangler deploy` |

The Worker name in Cloudflare must match `name` in `wrangler.jsonc` (`orbital-foundry`), unless your Workers Builds setup overrides it. Cloudflare's Workers Builds does not run a Wrangler `build.command`, so set the verification command in Build Configuration. `deploy/site/_headers` supplies the cache rules; Unity decodes its `.unityweb` assets in the browser.

### Cloudflare Pages from GitHub

In **Cloudflare → Workers & Pages → Create application → Pages → Import an existing Git repository**, select `pirate59/orbital-foundry` and use:

| Setting | Value |
| --- | --- |
| Framework preset | None |
| Root directory | Repository root (leave blank) |
| Production branch | `codex/comet-garden-graphics` for this pushed update; switch to `main` after merging it |
| Build command | `python3 tools/cloudflare_pages.py verify` |
| Build output directory | `deploy/site` |

Pages hosts the committed WebGL files after the verification command succeeds. Load the game in a browser to verify the `.unityweb` assets. Keep the final game URL stable: saves live in that browser's IndexedDB and do not transfer automatically to a new domain.

## Design changes since the blueprint

These changes were requested after the first playable build. They supersede the matching parts of `TODO_ARCHITECTURE.md`:

- **Dismantling your own structures.** Players (and the AI) can dismantle their own buildings and belts in cells they hold, for a 50% refund. Clearing inactive AI structures after the AI HQ falls still works as before.
- **Craft are manufactured, not bought.** A new `Shipyard` building (`BuildingKind.Shipyard = 4`) builds craft from factory goods. These are listed in `CraftDefinition.components` and take `buildTicks` to build. `BuyCraft` now places an order at the shipyard named by `buildingId`. Credit fees are lower: guard 40, tank 60, artillery 70, strike 80. The AI follows the same rule and only builds a shipyard after 150 lifetime sales, so early raids and expansion come much later.
- **Production starts from the HQ alone.** Iron Ore and Ion Crystal now sell for 1 and Alloy Plate for 4. An extractor and a depot in the HQ earn money immediately, so spending your starting credits can never leave you with no income. Depots are served after machines and shipyards, so they only sell surplus.
- **Craft are drawn on free tiles.** They no longer hide resource nodes or machines.
- **Defensive AI by default.** `rules.aiExpands` and `rules.aiRaids` in `catalog.json` are both `false`, and `rules.aiDefends` is `true`. The AI builds and runs its factory, adds power when its machines draw 90% of its supply (a Storage hub fed with Copper Bars and Silicon Ingots, then Solar Arrays in free slots), builds a Shipyard after 150 lifetime sales, and orders Guards and Tanks until every cell it holds has a defender, moving spare craft to undefended cells. It never leaves its own territory in this mode. Set `aiExpands` or `aiRaids` to `true` to bring back expansion (Fort zones included) and raids.
- **Belts show direction.** Each belt is drawn as a conveyor track with arrowheads that travel the way items move. Junction pads join corners, splits and merges. Stopped belts turn grey and their arrows stop.
- **Building choices and needs.** Selecting a building shows **Working on** (recipes for refineries and assemblers, craft orders for shipyards, a sell list for depots via the new `ToggleDepotItem` command, which uses `BuildingState.blockedItemIds`) and a **Needs** list. Machines also show their ingredients as floating icons: green = in stock, amber = on the way, pulsing red = missing, blue = paused because storage is full.
- **Contextual selection.** Clicking a visible resource node, building, or craft opens its own details. Nodes show resource, maximum rate, renewable reserve, tapped state, and extractor buffer. Production buildings and shipyards show recipes or craft options plus stock here, inbound, and available from active upstream belts. The panel scrolls when details grow long.
- **Comet Garden art.** The runtime station, craft, and asteroid sprites now follow the original Comet Garden visual theme documented in [docs/COMET_GARDEN.md](docs/COMET_GARDEN.md).
- **Belts join automatically.** Drawing into the middle of a line merges; dragging out of one splits; drawing over your own belt the other way reverses it for free.
- **Claiming cells.** Units claim a clear cell automatically when they arrive. The selection panel has a Unit Orders section and a "Claim this cell" button.
- **Bigger cells.** Every cell is 20×20 tiles (`CatalogRepository.CellGridSize`). Each HQ starts with 2 ore and 2 crystal nodes, and other cells hold 2–3 nodes. **Rich** nodes (gold ring) produce twice as fast. Saves made on the old 6×6 layout are refused with a message; start a new game.
- **Menu.** The Menu button in the top bar, or Esc when nothing is selected, pauses the game. It offers Resume, Save now, and Restart (asks for confirmation). Settings are kept in PlayerPrefs: effects volume and mute, factory speed (1×/2×/4×), camera pan speed, and showing or hiding ingredient icons.
- **Sound.** Short effects are generated at runtime (no audio files) for placing, belts, dismantling, sales, errors, alerts, claims and combat hits.
- **Headquarters and Forts (holding territory).** Each HQ cell has a pre-placed Headquarters building (`BuildingKind.Headquarters`). It can't be built or dismantled and changes hands when its cell is captured. Any other owned cell must be held by an active **Fort** (150, one per cell, hotkey 6) or by one of your units stationed there. Otherwise it is *unguarded*: you get a warning, the grid pulses orange, and after `rules.abandonTicks` (600 = 60s of normal play) it reverts to neutral. Its machines then stop under the usual 3-minute recapture grace. Expanding therefore means claim with a unit, then fortify before moving the unit on. Starting AI outer cells begin fortified. When expansion is enabled, the AI fortifies its claims and only then moves units on. Catalog v3; older saves are refused with a message.
- **Resource tree and Ion power (catalog v4).** Ten node types in three rarities: common Iron Ore, Copper Ore, Ice and Ion Crystal in every cell; rare Titanium Ore, Silicon Ore and Sulphur in contested neutral cells; extra-rare Unobtanium Ore, Complex Carbon and Gold Ore in one cell each. Recipes run in tiers: **tier 1** refining in a Refinery (ore to bar, ice to water, and so on), **tier 2** components in an Assembler (Iron Plate, Iron Frame, Copper Wire, Copper Sheet, Titanium Strut, Titanium Sheet, Silicon Wafer, Sulfur Powder) plus Purified Water in the new **Water Purifier** (90, hotkey 8), and **tier 3** assemblies made from tier 1 and tier 2 goods (Electronics, Explosives, Reinforced Plate, Coolant Cell, Hull Section, Precision Circuit, Field Emitter). Signal Relays are Iron Bar + Copper Bar; craft parts are Iron Bars. **Ion** (`rules.ionItemId`) is a faction-wide power stockpile and the only resource that is never belted. An extractor on an Ion node adds `rules.ionPerExtraction` (10) per cycle. Every recipe cycle (`ionCost`), building placement and craft launch spends Ion, and a machine holds a finished cycle until the stockpile covers it. Starting credits are 600 and starting Ion is 150. The AI taps an Ion node before building anything else and builds its simplest product chain first. Older saves are refused with a message.
- **Research web (catalog v5).** Select your Headquarters and press **Research** to open the web; the game pauses while it is open. It replaces the old Invest panel: Brokerage Contract and Quantum License are now nodes. 22 nodes in six branches (Metallurgy, Electrics, Chemistry, Industry, Power, Commerce) grow from the Foundry Charter root. As in Orc Horde, owned and buyable nodes show their names, a node one step further out is an "Undiscovered" outline, and anything beyond is not drawn. Each level costs credits and Ion. Levels unlock recipes and buildings (`unlock_recipe`, `unlock_building`) or add percentage bonuses (`machine_speed_percent`, `ion_cost_percent`, `extraction_speed_percent`, `ion_yield_percent`, `sale_price_percent`) that stack across levels. Tier 1 refining, Iron Plate, Copper Wire and Signal Relay need no research; every other component and assembly does. The **Solar Satellite** (Power branch, hotkey 9) adds 2 Ion every 1.5 s without an Ion node, about a fifth of a tapped node. Each side researches separately from zero every match. The AI buys the cheapest level in reach once its factory is complete and it has spare funds, so its products grow with its research. Machine choice lists show unlocked recipes, then locked ones whose research is in sight, then a count of recipes still to discover. Data: `ContentCatalog.research`, `FactionState.research`, `CommandKind.Research`, `ResearchService`, `ResearchController`.
- **Title, help and HUD clean-up.** A themed HTML loading page (orbit animation, progress and status) fades into an in-game title screen with runtime art, Resume, New game and How to play; the Unity splash is turned off in `WebBuild`. Small **?** buttons beside every build tool, the selection panel (it follows what is selected), the Ion counter, the research screen and the battle panel open short help popups loaded from `Assets/Resources/help.json` (`HelpController`); "Getting started" opens automatically on the first new game in a browser, and the menu has How to play. The always-visible Order Craft panel is gone: craft are ordered from the selected shipyard's Working on list. The top bar shows spare power next to the Power readout, e.g. `Power: 12 / 20 MW (+8)`, or `(-6 · 77% speed)` when overloaded.
- **Combat on four stats, with battle scenes.** Every craft fights on Attack, Reactive (counter damage, stored as `defenceDamage`), Armour and HP: Tank 5/3/3/12, Artillery 8/1/1/7, Mobile Guard 2/5/3/11 (+1 armour and reactive as a defender), Strike Craft 6/2/1/8. Air and ranged stats stay in the data but match the main stats, and every craft has range 0 (neighbouring cells only); Strike Craft still fly over defended cells. With `rules.combatScalesWithHealth` (on), the attacker's Attack scales with its HP and the counter with the defender's HP after the hit, rounded up. Each resolved attack is recorded as a `CombatEvent` and replayed by `BattleSceneController` as an Advance Wars-style split scene: formations of up to five ships (one per fifth of HP), bolts across the divider, HP counting down, then the counter. Play pauses while a scene runs; click or Esc skips; the menu's Battle scenes setting is Off, Fast (default) or Full. Mined asteroids now sit in front of their extractor so you can see what each rig is mining.
- **Solar power, Ion for parts, and Forts by construction (catalog v6).** Power is capacity in MW: your Headquarters has 20 MW of built-in panels and each **Solar Array** adds 6 MW (`powerSupply`; Photovoltaics research +25%). Working machines draw `powerDraw` (Extractor 1, Refinery and Water Purifier 2, Assembler 3, Shipyard 4); idle machines, depots, storage, Forts and belts draw nothing. When demand exceeds supply, every machine of that faction advances supply / demand of a step per tick (`BuildingState.powerCharge`), so nothing stops outright; the top bar shows `Power: demand / supply MW (spare)`. Solar Arrays (80 credits, hotkey 9) take 4 Copper Bars and 2 Silicon Ingots from your Storage when placed (each HQ cell has one Silicon Ore asteroid in place of its second Ion Crystal) (`goodsCost`) and need a slot: 3 in the HQ cell, 2 per Fort cell (`arraySlots`). Ion is spent only on research (each level costs credits and Ion; Ion Efficiency research cuts the Ion price 10% per level). Recipes, buildings and craft launches never use Ion. Outside the HQ cell you can build only where a completed Fort stands (`CellState.factoryOnline`), and a cell's machines stop if its Fort is lost. Forts are never placed directly: a **Fort Construction Zone** (150 credits, hotkey 6) goes on an edge tile of a cell you hold, facing the cell to expand into, is fed 6 Iron Plates and 4 Copper Wire by local belts (`materials`), and once full and the neighbour is claimed it deploys a Fort there and disappears. The AI places and feeds zones the same way when expansion is on. Older saves are refused with a message.
- **Random maps.** Every new game generates a map (`MapGenerator`, ID `random-SEED`) from the `map_a` layout: same cells, owners, Headquarters and starting craft, with asteroids placed from the seed. Both HQ cells get the same kit (2 Iron, 1 Copper, 1 Ice, 1 Ion, 1 Silicon); the AI's fortified outer cells get Iron, Copper, Ion and Ice; neutral cells get commons (sometimes rich), each rare resource twice and each extra-rare resource once. Asteroids keep off cell borders and the central build area. Saves store only the map ID and rebuild the map from its seed. `map_a` and `map_b` remain as fixed test maps.
- **Triple node output.** `rules.nodeOutputPerCycle` is 3: every resource node produces three items per extraction cycle (Ion nodes add 3 × `ionPerExtraction`), and an extractor holds until the whole cycle's output fits its buffer. A belt still moves at most one item per tile every `beltTravelTicksPerSegment` ticks (2 items/s), so a busy node can outpace one belt line.
- **Belt timers, lanes and tiers.** Each belt tile runs its own timer from its speed tier (`rules.beltTiers`: Conveyor 5 ticks, Fast 3, Express 2; every belt starts as a Conveyor, with per-belt upgrades to come). Items are routed by the quickest route once queued items are counted (`BeltNetwork.FindLeastLoadedRoute`), so parallel lanes from one producer to one consumer share the load; a belt still carries one item at a time. Depots and Storages order up to 8 of each good but hold off on routes with a queued item, so they never back a line up in front of a machine; at merges, items nearest their destination move first and waiting items go before new arrivals.
- **Space-station visuals.** All art is generated in code as point-filtered pixel art (`PixelCanvas`, `StationArt`):
  - Resource nodes are floating asteroids. Ore ones have metal seams; crystal ones have glowing shards; rich ones are bigger with a glow. A mined asteroid sits under its rig.
  - Buildings are mini stations in their owner's colours: mining rig, smelter, fabricator, trade dock, drydock and bastion. The HQ is a large station with solar wings.
  - Belts are lit lanes with pulses running in the flow direction.
  - Cargo rides hover platforms.
  - Craft are pixel-art ships: hover tank, artillery platform, shield drone and strike fighter.
  - The camera zooms in to about 1.6 world units to show the detail.
- **Storage hub.** The Storage building (`BuildingKind.Storage`, 90, hotkey 7) holds 40 items. It collects surplus of any item on its accept list from connected producers and supplies any connected machine, shipyard or depot, so several lines can be joined at one hub. Machines and shipyards are served before storages and depots; storages never pull from each other. Its accept list uses the same `ToggleDepotItem` command as a depot's sell list.
- **Web builds report every exception.** `WebBuild` sets full exception support with stack traces, so errors show in the browser console instead of failing silently.
- **Versions.** Content is now catalog version 2 and saves are schema 2. Older saves still load.

## Settings notes

- **Input handling** is set to the legacy Input Manager. Choosing "Both" requires adding the Input System package, which the blueprint does not include. Mouse and keyboard work fully with the legacy setting.
- Company and product name (Orbital Foundry) and the 1600×900 default web canvas are set by the setup menu.
- WebGL 2 is the Unity 6 default for Web builds.

## Status

The simulation, AI, and saving are covered by passing EditMode tests. The browser UAT checklist in `docs/UAT.md` is **not** signed off. Do not treat deployment, mobile, multiplayer, or account sync as done.
