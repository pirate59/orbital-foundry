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
| Right-click with your craft selected | Normal play: move it, attack a defended cell, or hold the control point of the cell it stands in. In battle: attack, move, or hold. |
| Right-click otherwise, or Esc | Cancel the active tool or clear the selection |
| 1 / 2 / 3 / 4 | Extractor, Refinery, Assembler, or Sale Depot tool |
| B, then left-drag | Draw belts. Each tile step places one one-way belt. Start a drag on an existing belt to branch it or merge into it. |
| WASD / arrow keys, right- or middle-drag | Pan |
| Mouse wheel, Q / E | Zoom |
| Space | End your turn in battle |

New craft launch from the selected cell if you own it; otherwise they launch from your HQ.

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

## Settings notes

- **Input handling** is set to the legacy Input Manager. Choosing "Both" requires adding the Input System package, which the blueprint does not include. Mouse and keyboard work fully with the legacy setting.
- Company and product name (Orbital Foundry) and the 1600×900 default web canvas are set by the setup menu.
- WebGL 2 is the Unity 6 default for Web builds.

## Status

The simulation, AI, and saving are covered by passing EditMode tests. The browser UAT checklist in `docs/UAT.md` is **not** signed off. Do not treat deployment, mobile, multiplayer, or account sync as done.
