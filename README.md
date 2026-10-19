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
| V | Belt View: fade buildings so belts behind them can be selected, upgraded or removed |
| X, then left-click or left-drag | Dismantle: click one of your machines, or drag across belts. Half the cost is refunded; items inside are lost. |
| WASD / arrow keys, right-drag | Pan (right-drag grabs the board) |
| Q / E, middle-drag or Shift + right-drag | Rotate the camera around the view (drags also tilt) |
| R / F | Tilt the camera, from straight down to skimming the rock |
| T / G | Jump to a top-down view / drop to a low view across the field |
| Mouse wheel, + / - | Zoom (the wheel zooms toward the cursor) |
| Home | Reset the camera angle and zoom |
| Space | End your turn in battle |
| Esc | Cancel the current tool or selection; with nothing selected, open the menu |

Craft are built at a **Shipyard**. Select one of your shipyards, then order a craft from its **Working on** list in the selection panel. The order costs credits; the shipyard then pulls the craft's parts along belts and launches it when they have arrived and the build time has passed. Parts are role-themed and climb the tiers, so better craft need research and territory:

| Craft | Credits | Parts | Raw needs |
| --- | --- | --- | --- |
| Mobile Guard | 60 | 4 Iron Plate + 2 Copper Wire | Iron, Copper |
| Tank | 120 | 2 Reinforced Plate + 1 Electronics | Iron, Copper, Silicon (Framing + Electronics research) |
| Artillery | 150 | 2 Explosives + 1 Reinforced Plate + 1 Electronics | adds Sulphur and Complex Carbon |
| Strike Craft | 220 | 1 Hull Section + 1 Field Emitter + 1 Coolant Cell | Titanium, Unobtanium, Copper, Ice |

A craft whose parts are not yet researched cannot be ordered (`NEEDS_RESEARCH`). The AI plans a supply line per craft and only orders craft it can actually source.

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
- **Menu.** The Menu button in the top bar, or Esc when nothing is selected, pauses the game. It offers Resume, Save now, and Restart (asks for confirmation). Settings are kept in PlayerPrefs: effects and music volume, mute, factory speed (1×/2×/4×), camera pan speed, and showing or hiding ingredient icons.
- **Sound and celebrations.** Short effects are generated at runtime for placing, belts, dismantling, sales, errors, alerts, claims, combat hits and milestone fireworks. `Assets/Resources/Audio/OpeningTheme.mp3` loops continuously on the title screen and during gameplay. Extractors, refineries, assemblers and shipyards have low-volume working loops while they are active and powered; the three most audible machines play at once, with a selected machine prioritised. The first Iron Bar, Signal Relay and Relay sale launch one-time fireworks from the earning building; the latter two also launch from HQ. A separate random generator varies the fireworks' colours, counts, heights and timing without changing factory rules. Settings has separate effects and music volume sliders plus Mute all sound. Browsers start the music on the first key press or pointer interaction.
- **Headquarters and Forts (holding territory).** Each HQ cell has a pre-placed Headquarters building (`BuildingKind.Headquarters`). It can't be built or dismantled and changes hands when its cell is captured. Any other owned cell must be held by an active **Fort** (150, one per cell, hotkey 6) or by one of your units stationed there. Otherwise it is *unguarded*: you get a warning, the grid pulses orange, and after `rules.abandonTicks` (600 = 60s of normal play) it reverts to neutral. Its machines then stop under the usual 3-minute recapture grace. Expanding therefore means claim with a unit, then fortify before moving the unit on. Starting AI outer cells begin fortified. When expansion is enabled, the AI fortifies its claims and only then moves units on. Catalog v3; older saves are refused with a message.
- **Resource tree and Ion power (catalog v4).** Ten node types in three rarities: common Iron Ore, Copper Ore, Ice and Ion Crystal in every cell; rare Titanium Ore, Silicon Ore and Sulphur in contested neutral cells; extra-rare Unobtanium Ore, Complex Carbon and Gold Ore in one cell each. Recipes run in tiers: **tier 1** refining in a Refinery (ore to bar, ice to water, and so on), **tier 2** components in an Assembler (Iron Plate, Iron Frame, Copper Wire, Copper Sheet, Titanium Strut, Titanium Sheet, Silicon Wafer, Sulfur Powder) plus Purified Water in the new **Water Purifier** (90, hotkey 8), and **tier 3** assemblies made from tier 1 and tier 2 goods (Electronics, Explosives, Reinforced Plate, Coolant Cell, Hull Section, Precision Circuit, Field Emitter). Signal Relays are Iron Bar + Copper Bar; craft parts are Iron Bars. **Ion** (`rules.ionItemId`) is a faction-wide power stockpile and the only resource that is never belted. An extractor on an Ion node adds `rules.ionPerExtraction` (10) per cycle. Every recipe cycle (`ionCost`), building placement and craft launch spends Ion, and a machine holds a finished cycle until the stockpile covers it. Starting credits are 600 and starting Ion is 150. The AI taps an Ion node before building anything else and builds its simplest product chain first. Older saves are refused with a message.
- **Research web (catalog v5).** Select your Headquarters and press **Research** to open the web; the game pauses while it is open. It replaces the old Invest panel: Brokerage Contract and Quantum License are now nodes. 22 nodes in six branches (Metallurgy, Electrics, Chemistry, Industry, Power, Commerce) grow from the Foundry Charter root. As in Orc Horde, owned and buyable nodes show their names, a node one step further out is an "Undiscovered" outline, and anything beyond is not drawn. Each level costs credits and Ion. Levels unlock recipes and buildings (`unlock_recipe`, `unlock_building`) or add percentage bonuses (`machine_speed_percent`, `ion_cost_percent`, `extraction_speed_percent`, `ion_yield_percent`, `sale_price_percent`) that stack across levels. Tier 1 refining, Iron Plate, Copper Wire and Signal Relay need no research; every other component and assembly does. The **Solar Satellite** (Power branch, hotkey 9) adds 2 Ion every 1.5 s without an Ion node, about a fifth of a tapped node. Each side researches separately from zero every match. The AI buys the cheapest level in reach once its factory is complete and it has spare funds, so its products grow with its research. Machine choice lists show unlocked recipes, then locked ones whose research is in sight, then a count of recipes still to discover. Data: `ContentCatalog.research`, `FactionState.research`, `CommandKind.Research`, `ResearchService`, `ResearchController`.
- **Research screen.** Branches run left to right in coloured lanes from the Foundry Charter. Drag anywhere (on a node too) to move the web; a press only becomes a drag after it moves a few pixels, so clicks still select. Scroll zooms about the pointer; the corner buttons zoom and Fit frames everything (F). Arrow keys or WASD pan and Enter researches the selected node. Links into and out of the selected node are highlighted, and the detail panel lists what the node leads to. The web opens fitted to what you can see.
- **Performance overlay and logistics speed-ups.** Bottom left shows FPS, ticks per second (10 is full speed) and simulation milliseconds per tick; F3 adds the five costliest stages in milliseconds of CPU per second of play, then hides it (`Perf`). Dispatch was the bottleneck: with one request per unit, a grown AI factory held ~1,900 open requests and each one searched every source, costing 77 ms of a 100 ms tick in the editor (more in the browser). Requests now count outstanding goods once a tick, sources are indexed by the items they hold, a building's requests for an item stop searching once one has failed that tick, sources whose outgoing belts are full are skipped, reachability is found once per source per tick, and the request sort uses packed keys. An idle AI planner looks again after 10 ticks rather than every tick. The same world now costs 2.6 ms a tick. `PerfBenchmark` (explicit, run with `-testFilter OrbitalFoundry.Tests.PerfBenchmark.GrownFactoryOnMapA`) prints the per-stage costs.
- **Resource bar.** A bar along the bottom lists the core resources (Iron Ore, Copper Ore, Ice, Iron Bar, Copper Bar, Water) and then any other item your network holds: the total across every building you own plus goods on your belts and drones, with how many are moving (`ResourceBarController`).
- **Visual style: Light weight or Meshy.** Settings (now also on the title screen) has a Visual style choice. Light weight is the built-in look. Meshy uses a Meshy model for an item when there is one in `Assets/Resources/Meshy/Items/<item id>` (FBX or OBJ, any size or orientation) and the built-in model otherwise. Each model is merged into one mesh, scaled to a cargo slot, stood on the belt and drawn with the game's lit material and its own texture; stacks become a pile of the model. An importer (`Assets/Editor/MeshyImport.cs`) makes those meshes readable. Switching style rebuilds goods and ingredient icons straight away (`MeshyLibrary`).
- **Belt junctions and chevrons.** Where belts meet at a hub (corners, splits, merges, crossings, line ends) each conveyor's rails stop at the hub's rim instead of crossing it (`ModelLibrary.ConveyorMesh(trimStart, trimEnd)`). Chevrons drift at 40% of item speed and only shrink near belt ends, so they no longer pulse constantly.
- **Mining research.** Extraction research raises yield, not speed: Foundry Charter and each Rapid Extraction level add 10 ore per cycle (`extraction_yield`), and an extractor's buffer grows to hold two cycles. **Bulk Crates** (Industry, 3 levels) grows raw loads per belt slot from 5 to 8, 14 and 20 (`raw_stack_size`), the lever that raises what reaches the factory, since one deposit out-mines a belt. **Multi-Chute Extractors** (Industry, expensive, 2 levels) lets an extractor feed 2, then 3 exit belts; with one chute an extractor feeds only its oldest exit belt, so split one exit line to reach several buildings (`extractor_chutes`, `ResearchService.Chutes`). Any speed bonus now carries its leftover fraction of a tick into the next cycle instead of rounding it away (`NextExtractionTicks`), and the extractor panel shows researched output, time, buffer and chutes.
- **Loads that lose their way.** When a load's route is cut (a belt ahead removed or its destination dismantled) it first looks for a new route to the same building, then for the nearest reachable building of its owner's that can take it (a machine whose recipe uses it, a shipyard or construction zone that needs it, a depot that sells it, a storage that accepts it). With nowhere to go it is stranded: a red "!" over it, one alert, no building counts it as on its way (so its old destination orders again), it retries every second, and it is lost after 30 seconds unless a route opens (`ItemTokenState.strandedSinceTick`, `FactorySimulation.StrandedTicks`). Goods on a dismantled belt are still lost at once; nothing is refunded.
- **Meshy buildings and animation.** Building models go in `Assets/Resources/Meshy/Buildings/<type id>` (FBX, Z-up as Meshy exports them; a same-named PNG is the colour map). Each is fitted to its footprint on a plinth in the owner's colour. Models are imported as made, compressed, with a Console warning over 8,000 triangles; textures are capped at 1024. Untextured models are painted in the palette (gunmetal base, cream tops, ivory sides, the building's colour as a trim band) or by paint regions. `Assets/Resources/Meshy/models.json` sets per model: `rotate`, `paint` regions, `landingPads`, a `part` that spins and plunges on a drive rod with dust (the extractor's drill), `swings`, `beacons` (a lamp with sweeping beams), `glass` (a see-through glowing dome) and `steam`/smoke vents, all moving only while the building works. Editor menu Orbital Foundry: Report Meshy models, Meshy blueprints (gridded views for reading positions), Preview Meshy buildings, Build dust material. `loader` builds a jointed arm (shoulder, elbow) with a claw on a cable whose fingers open and close. Current models: extractor (drill, dust), refinery (glass dome with a turning light, smoke stack, landing pads) and drone pad (loading arm and claw).
- **Title, help and HUD clean-up.** A themed HTML loading page (orbit animation, progress and status) fades into an in-game title screen with runtime art, Resume, New game and How to play; the Unity splash is turned off in `WebBuild`. Small **?** buttons beside every build tool, the selection panel (it follows what is selected), the Ion counter, the research screen and the battle panel open short help popups loaded from `Assets/Resources/help.json` (`HelpController`). New games show a six-step Signal Relay guide that follows actual production and sale; Guide in the top bar hides or reopens it, and its progress survives a save and resume. The menu has How to play. The always-visible Order Craft panel is gone: craft are ordered from the selected shipyard's Working on list. The top bar shows spare power next to the Power readout, e.g. `Power: 12 / 20 MW (+8)`, or `(-6 · 77% speed)` when overloaded.
- **Combat on four stats, with battle scenes.** Every craft fights on Attack, Reactive (counter damage, stored as `defenceDamage`), Armour and HP: Tank 5/3/3/12, Artillery 8/1/1/7, Mobile Guard 2/5/3/11 (+1 armour and reactive as a defender), Strike Craft 6/2/1/8. Air and ranged stats stay in the data but match the main stats, and every craft has range 0 (neighbouring cells only); Strike Craft still fly over defended cells. With `rules.combatScalesWithHealth` (on), the attacker's Attack scales with its HP and the counter with the defender's HP after the hit, rounded up. Each resolved attack is recorded as a `CombatEvent` and replayed by `BattleSceneController` as an Advance Wars-style split scene: formations of up to five ships (one per fifth of HP), bolts across the divider, HP counting down, then the counter. Play pauses while a scene runs; click or Esc skips; the menu's Battle scenes setting is Off, Fast (default) or Full. Mined asteroids now sit in front of their extractor so you can see what each rig is mining.
- **Solar power, Ion for parts, and Forts by construction (catalog v6).** Power is capacity in MW: your Headquarters has 20 MW of built-in panels and each **Solar Array** adds 6 MW (`powerSupply`; Photovoltaics research +25%). Working machines draw `powerDraw` (Extractor 1, Refinery and Water Purifier 2, Assembler 3, Shipyard 4); idle machines, depots, storage, Forts and belts draw nothing. When demand exceeds supply, every machine of that faction advances supply / demand of a step per tick (`BuildingState.powerCharge`), so nothing stops outright; the top bar shows `Power: demand / supply MW (spare)`. Solar Arrays (80 credits, hotkey 9) take 4 Copper Bars and 2 Silicon Ingots from your Storage when placed (each HQ cell has one Silicon Ore asteroid in place of its second Ion Crystal) (`goodsCost`) and need a slot: 3 in the HQ cell, 2 per Fort cell (`arraySlots`). Ion is spent only on research (each level costs credits and Ion; Ion Efficiency research cuts the Ion price 10% per level). Recipes, buildings and craft launches never use Ion. Outside the HQ cell you can build only where a completed Fort stands (`CellState.factoryOnline`), and a cell's machines stop if its Fort is lost. Forts are never placed directly: a **Fort Construction Zone** (150 credits, hotkey 6) goes on an edge tile of a cell you hold, facing the cell to expand into, is fed 6 Iron Plates and 4 Copper Wire by local belts (`materials`), and once full and the neighbour is claimed it deploys a Fort there and disappears. The AI places and feeds zones the same way when expansion is on. Older saves are refused with a message.
- **Random maps.** Every new game generates a 4x4 field (`MapGenerator`, ID `field-SEED`) from the `map_c` layout. Your HQ is in one corner, and the AI's HQ is in the opposite corner with two fortified outposts beside it. Saves on the earlier 4x2 maps (`random-SEED`, from `map_a`) still rebuild their own layout. Generation works the same way for both: same cells, owners, Headquarters and starting craft, with asteroids placed from the seed. Both HQ cells get the same kit (2 Iron, 1 Copper, 1 Ice, 1 Ion, 1 Silicon); the AI's fortified outer cells get Iron, Copper, Ion and Ice; neutral cells get commons (sometimes rich), each rare resource twice and each extra-rare resource once. Asteroids keep off cell borders and the central build area. Saves store only the map ID and rebuild the map from its seed. `map_a` and `map_b` remain as fixed test maps.
- **Ten-to-one raw economy and stacked cargo.** Every refining recipe takes 10 raw units (10 Iron Ore make 1 Iron Bar, 10 Ice make 1 Water, and so on); later tiers are unchanged, so every item costs ten times the raw goods. `rules.nodeOutputPerCycle` is 30 to match, and an extractor holds until the whole cycle fits its buffer (60). Ion is unchanged at `rules.ionPerExtraction` (30) per cycle. Raw goods (items no recipe makes) ride the belts in stacks of `rules.rawStackSize` (5) per belt slot, drawn as a small pile, while made goods still travel one per slot; set it to 2 or 3 for busier belts. A belt still holds one load per tile (`beltCapacityPerSegment`), and a drone's capacity counts loads. Buffers grew to fit: Refinery 40, Sale Depot 40, Storage 200, and depots and storages keep eight loads of each product on order. Tokens carry `quantity`; older saves read it as one.
- **Belt timers, lanes and tiers.** Each belt tile runs its own timer from its speed tier (`rules.beltTiers`: Conveyor 5 ticks, Fast 3, Express 2; every belt starts as a Conveyor, with per-belt upgrades to come). Items are routed by the quickest route once queued items are counted (`BeltNetwork.FindLeastLoadedRoute`), so parallel lanes from one producer to one consumer share the load; a belt still carries one item at a time. Select a belt to upgrade it, or its whole line (the run between buildings or junctions, highlighted), one tier at a time for the new tier's per-belt price (`CommandKind.UpgradeBelt`); faster tiers are tinted cyan and gold. Machines order ingredients to fill their storage (keeping room for output); each building's first cycle is a priority request and the rest are top-ups (`PullRequestState.fill`), so every machine on a split line gets its first batch before any fills up. Depots, Storages and top-ups order up to 8 of each good but hold off on routes with a queued item, so they never back a line up in front of a machine; at merges, items nearest their destination move first and waiting items go before new arrivals.
- **Collapsible side panels.** BUILD, SELECTION and ALERTS each have a Hide/Show toggle in their header (`HudController.MakeCollapsible`); a collapsed panel stops stretching so the map gets the space, and each choice is remembered in PlayerPrefs. The 1-9, B and X hotkeys work either way.
- **Belt View.** A top-bar toggle (or V) fades buildings, their shadows, icons and the asteroids under extractors to a faint outline and makes them ignore clicks: a click selects the belt on (or nearest) the tile and Dismantle removes belts only. Choosing a building tool turns it off (`UiState.beltView`).
- **3D atomic-age board (URP).** The game renders in 3D through the Universal Render Pipeline (`Tools/Orbital Foundry/Configure Rendering` creates the pipeline asset and the shared materials in `Resources/Materials`):
  - **Camera.** A perspective orbit camera (`CameraInput`) eases toward its targets. WASD or right-drag pans, Q/E or middle-drag (Shift + right-drag on a trackpad) rotates, R/F tilts from straight down to just above the rock (the camera never dips below the deck, and its focus follows each asteroid's height), T and G jump to top-down and ground-level views, and the wheel zooms toward the cursor.
  - **Look.** `RenderRig` sets a warm low sun with soft shadows, a cool rim light, the procedural `OrbitalFoundry/RetroSky` backdrop, bloom, ACES tonemapping, a warm grade and a vignette.
  - **Models.** All models are built in code by `MeshKit` (lathe, box, torus, fin and rock primitives) in a 1950s-futurism palette (`Palette`: cream, cherry, mint, chrome). `ModelLibrary` builds every building, craft and asteroid:
    - Buildings: a Googie needle HQ with a spinning atom, a tripod rocket drill, a smelting capsule, a water tower, a Quonset works with a radar dish, a trading post with a neon starburst, a rocket gantry, a ray-gun bunker, a scaffold site, domed silos, and a sunflower solar collector.
    - Craft: a saucer, a hover tank, a coil cannon and a finned rocket fighter.
    - Resource nodes: faceted outcrops with glowing crystals in their resource's colour.
    - Faction trim: seafoam for the player, cherry for the AI.
    - States: stopped buildings dim their lights, and Belt View swaps in faded materials.
  - **Belts and cargo.** Each belt is a raised conveyor (`BeltModels`): rails tinted by speed tier, a side strip lit in the owner's colour (brighter on the selected line, dark when stopped), and glowing arrows riding in the direction of travel. Turntable hubs mark corners, splits, merges and line ends. Cargo is a small 3D good in the item's colour, shaped by kind (ore chunk, ingot, canister, spool, plate stack, frame, circuit box, crystal or crate), with a small icon above it naming the item.
  - **Comic battle units.** The battle scenes and the title line-up show "enhanced comic" craft (`ComicCraft`). These are more detailed cartoon models with pilots in open cockpits, panel lines, rivets, antennas, pennants, roundels and exhaust flames. They are drawn with the `OrbitalFoundry/ComicToon` shader: three cel bands, halftone dots in the shadows, a rim highlight, a hard glint and ink outlines along smoothed normals. Each type and faction is rendered once off-screen into a cropped, transparent portrait (alpha is recovered by rendering on black and white). If the shader is unavailable, the pixel-art ships are used instead.
  - **Markers.** Need icons, alerts and craft HP bars are small 3D markers that turn to face the camera (`Marker`): a status ring (green in stock, amber on the way, pulsing red missing, blue backed up) around a slowly spinning model of the needed good with pips underneath, a bobbing warning sign, and a bar that shrinks toward its left end. Cargo grows as the camera pulls back so goods stay readable when zoomed out.
  - **Asteroid belt board.** Each cell is a flat-topped mini asteroid with a jagged underside and its own mineral tint (`BoardArt`). The asteroids are scattered: spread apart and nudged sideways and up or down by a seeded offset, while keeping their grid neighbours (`BoardLayout`). Only presentation moves; the simulation and saves are unchanged, and the camera picks the deck under the cursor. Buildings stand on the rock, resource nodes are outcrops in the ground (the Extractor tool snaps to the outcrop under the cursor), and beacon posts light in the owner's colour. Belts stop at an asteroid's edge. Goods cross between asteroids only through **Drone Pads** (`BuildingKind.DronePad`, 60 credits, hotkey 0), unlocked by **Drone Logistics** research in the new Logistics branch:
    - **Linking.** Select a pad to see its drone range as a ring. Press Link and click a tile on another asteroid you hold inside the ring (`CommandKind.LinkDronePad`). A receiving pad is placed there, or the link joins a pad already standing there. Each pad sends to one place; you can relink or unlink it, and a receiving pad can relay onward. Goods route through pads like junctions, and removing a pad removes its links.
    - **Timing.** A link's one-way flight is its length times `rules.droneTicksPerUnit` (2 ticks per unit). The drone loads at the pad, flies, drops off, and flies home before the next trip.
    - **Research.** Drone Logistics levels 2-3 add range (22, then 32, then 44 units, measured flat across the field; `rules.droneBaseRange`). Drone Engines adds speed (+25% per level), and Cargo Drones adds items per trip (2, then 3; `rules.droneBaseCapacity`). Belt tier upgrades on a link also speed its drone.
    - **Layout.** The layout lives in the domain (`MapLayout`) so the simulation can time each link. It is seeded from the map ID, so every new map scatters differently and a save rebuilds the same field. Gaps vary from 14 to 34 units, set separately for each row and column so asteroids stagger rather than line up. Each asteroid strays up to 12 units on both horizontal axes and is lifted or sunk up to 5, and a relaxation pass pushes apart any that come closer than 8 units between grids.
    - **The AI flies drones too.** Each production chain lives on one asteroid. When the chain's ore has run out there, the AI mines it on another of its asteroids in drone reach, places a sending pad facing home, links it to a receiving pad, and belts both legs (`AIFactoryPlanner.PlanDroneRoute`). If the ore exists only out of reach, it researches Drone Logistics (prerequisites first) and its range levels ahead of everything else. It moves a chain to another asteroid when it cannot be finished where it is.
    - **Selection panel.** Selecting a link or a pad shows its flight time.
  - **Scene mockups.** `-executeMethod OrbitalFoundry.EditorTools.SceneMockup.ExportBatch` stages a running factory with a cross-asteroid belt and renders it through the real `WorldView`.
  - **Model gallery.** Render every model to PNG with `-executeMethod OrbitalFoundry.EditorTools.ModelGallery.ExportBatch -galleryOut <dir>`.
- **Delivery priority.** Every receiving building has a priority (`BuildingState.priority`, Very low to Very high; `CommandKind.SetPriority`; `DeliveryPriority`). Requests are served in this order: every machine's first batch, then priority, then the old order (machines before Storage and depots, first batches before top-ups, oldest first). Order only matters where requests compete for the same stock, so a priority stays local: a High tank fills before a Low shop off the same junction, while the machines it feeds compete on their own priorities. A High building also waits on a busy belt rather than let a rival take its goods. Buildings set away from Normal show green up-chevrons or amber down-chevrons. A full Storage now drops its open requests, and it no longer counts goods on their way twice when judging its room.
- **Settings pop-up and copy/paste.** A building's settings (recipe, craft order, sell or accept list, delivery priority) live in a Settings pop-up, opened with the side bar's Settings button or C. The pop-up follows the selection while open, and Esc closes it. The right bar shows current state: what the building is doing and a one-line summary of its settings. Copy settings and Paste settings (buttons, or Ctrl/Cmd+C and Ctrl/Cmd+V) carry everything onto the same kind of building, or just the priority onto any other. Machines start with no recipe: placing one opens its pop-up, and until a recipe is chosen it shows a warning sign. The AI selects its recipes explicitly.
- **Storage hub.** The Storage building (`BuildingKind.Storage`, 90, hotkey 7) holds 40 items. It collects surplus of any item on its accept list from connected producers and supplies any connected machine, shipyard or depot, so several lines can be joined at one hub. Machines and shipyards are served before storages and depots; storages never pull from each other. Its accept list uses the same `ToggleDepotItem` command as a depot's sell list.
- **Web builds report every exception.** `WebBuild` sets full exception support with stack traces, so errors show in the browser console instead of failing silently.
- **Versions.** Content is now catalog version 2 and saves are schema 2. Older saves still load.

## Settings notes

- **Input handling** is set to the legacy Input Manager. Choosing "Both" requires adding the Input System package, which the blueprint does not include. Mouse and keyboard work fully with the legacy setting.
- Company and product name (Orbital Foundry) and the 1600×900 default web canvas are set by the setup menu.
- WebGL 2 is the Unity 6 default for Web builds.

## Status

The simulation, AI, and saving are covered by passing EditMode tests. The browser UAT checklist in `docs/UAT.md` is **not** signed off. Do not treat deployment, mobile, multiplayer, or account sync as done.
