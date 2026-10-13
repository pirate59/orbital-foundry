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
| `deploy/_headers` | Cloudflare Pages response headers |
| `tools/serve_web.py` | Local HTTP server for `Build/Web` that sends matching headers |

All rules run in plain C# services against `WorldState`. MonoBehaviours and sprites hold no game state. Player UI and the AI submit the same `GameCommand`s through `CommandRouter`.

## Maps

- `map_a` (Frontier Line) is used for every new game.
- `map_b` (Broken Frontier) is used only by `AIPlannerTests`. It shows that the AI plans around obstacles without an authored layout.

Balance values live in `Assets/Resources/catalog.json`. Change them there, not in code.

## Controls (desktop mouse and keyboard)

| Input | Action |
| --- | --- |
| Left-click | Select a cell, machine, or craft |
| Right-click with your craft selected | Normal play: send it to a cell. Arriving in a clear cell you don't own claims it automatically (10s hold); a cell with enemy craft is attacked. Hovering shows what the click will do. In battle: attack, move, or hold. |
| Right-click otherwise, or Esc | Cancel the active tool or clear the selection |
| 1 / 2 / 3 / 4 / 5 | Extractor, Refinery, Assembler, Sale Depot, or Shipyard tool |
| B, then left-drag | Draw belts. Each tile step places one one-way belt. Start a drag on an existing belt to branch it or merge into it. |
| X, then left-click or left-drag | Dismantle: click one of your machines, or drag across belts. Half the cost is refunded; items inside are lost. |
| WASD / arrow keys, right- or middle-drag | Pan |
| Mouse wheel, Q / E | Zoom |
| Space | End your turn in battle |
| Esc | Cancel the current tool or selection; with nothing selected, open the menu |

Craft are built at a **Shipyard**. Select one of your shipyards, then order a craft from the left panel. The order costs a small credit fee. The shipyard then pulls the craft's parts (Alloy Plates, plus Signal Relays for most types) along belts and launches the craft when the parts have arrived and the build time has passed.

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

The build uses Brotli compression with the decompression fallback disabled. It writes to `Build/Web`, copies `deploy/_headers` into it, and fails if any single file is over Cloudflare Pages' 25 MiB limit.

### Local HTTP test

Do not open `index.html` as a file. Serve the build over HTTP with the same headers `deploy/_headers` gives Cloudflare. The bundled server does this:

```bash
python3 tools/serve_web.py 8080
```

Then open http://localhost:8080. In the browser network panel, check that `.wasm.br` arrives as `application/wasm` with `Content-Encoding: br`.

### Cloudflare Pages

- Static output directory: `Build/Web`.
- Use a stable URL. Saves live in the browser's IndexedDB, keyed by the page URL, so moving the game to a new URL starts with no save.
- After the first deploy, check the response headers again. If Pages does not honour the `Content-Encoding` rules for pre-compressed files, either rebuild with `decompressionFallback = true` or host the `.br` files in R2 with explicit headers.

## Design changes since the blueprint

These changes were requested after the first playable build. They supersede the matching parts of `TODO_ARCHITECTURE.md`:

- **Dismantling your own structures.** Players (and the AI) can dismantle their own buildings and belts in cells they hold, for a 50% refund. Clearing inactive AI structures after the AI HQ falls still works as before.
- **Craft are manufactured, not bought.** A new `Shipyard` building (`BuildingKind.Shipyard = 4`) builds craft from factory goods. These are listed in `CraftDefinition.components` and take `buildTicks` to build. `BuyCraft` now places an order at the shipyard named by `buildingId`. Credit fees are lower: guard 40, tank 60, artillery 70, strike 80. The AI follows the same rule and only builds a shipyard after 150 lifetime sales, so early raids and expansion come much later.
- **Production starts from the HQ alone.** Iron Ore and Ion Crystal now sell for 1 and Alloy Plate for 4. An extractor and a depot in the HQ earn money immediately, so spending your starting credits can never leave you with no income. Depots are served after machines and shipyards, so they only sell surplus.
- **Craft are drawn on free tiles.** They no longer hide resource nodes or machines.
- **Passive AI for factory testing.** `rules.aiExpands` and `rules.aiRaids` in `catalog.json` are both `false`. In this mode the AI builds and runs its own factory but never claims cells, raids, builds a shipyard, or orders craft. Set either to `true` to bring the military AI back.
- **Belts show direction.** Each belt is drawn as a conveyor track with arrowheads that travel the way items move. Junction pads join corners, splits and merges. Stopped belts turn grey and their arrows stop.
- **Building choices and needs.** Selecting a building shows **Working on** (recipes for refineries and assemblers, craft orders for shipyards, a sell list for depots via the new `ToggleDepotItem` command, which uses `BuildingState.blockedItemIds`) and a **Needs** list. Machines also show their ingredients as floating icons: green = in stock, amber = on the way, pulsing red = missing, blue = paused because storage is full.
- **Belts join automatically.** Drawing into the middle of a line merges; dragging out of one splits; drawing over your own belt the other way reverses it for free.
- **Claiming cells.** Units claim a clear cell automatically when they arrive. The selection panel has a Unit Orders section and a "Claim this cell" button.
- **Bigger cells.** Every cell is 15×15 tiles (`CatalogRepository.CellGridSize`). Each HQ starts with 2 ore and 2 crystal nodes, and other cells hold 2–3 nodes. **Rich** nodes (gold ring) produce twice as fast. Saves made on the old 6×6 layout are refused with a message; start a new game.
- **Menu.** The Menu button in the top bar, or Esc when nothing is selected, pauses the game. It offers Resume, Save now, and Restart (asks for confirmation). Settings are kept in PlayerPrefs: effects volume and mute, factory speed (1×/2×/4×), camera pan speed, and showing or hiding ingredient icons.
- **Sound.** Short effects are generated at runtime (no audio files) for placing, belts, dismantling, sales, errors, alerts, claims and combat hits.
- **Web builds report every exception.** `WebBuild` sets full exception support with stack traces, so errors show in the browser console instead of failing silently.
- **Versions.** Content is now catalog version 2 and saves are schema 2. Older saves still load.

## Settings notes

- **Input handling** is set to the legacy Input Manager. Choosing "Both" requires adding the Input System package, which the blueprint does not include. Mouse and keyboard work fully with the legacy setting.
- Company and product name (Orbital Foundry) and the 1600×900 default web canvas are set by the setup menu.
- WebGL 2 is the Unity 6 default for Web builds.

## Status

The simulation, AI, and saving are covered by passing EditMode tests. The browser UAT checklist in `docs/UAT.md` is **not** signed off. Do not treat deployment, mobile, multiplayer, or account sync as done.
