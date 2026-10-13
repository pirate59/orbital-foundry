# Orbital Foundry: verification and user acceptance

There are two sign-off sections. Automated results never count as user acceptance.

## 1. Build verification (automated or by the implementer)

| ✓ | Check | Date | Notes |
| --- | --- | --- | --- |
| [ ] | Unity Editor compiles with no errors (runtime, editor and test assemblies) | | |
| [ ] | All EditMode tests pass (`CombatResolverTests`, `BeltNetworkTests`, `OwnershipTests`, `AIPlannerTests`, `SaveRoundTripTests`) | | |
| [ ] | AI construction gate: on map_a and map_b the AI builds a selling factory from starting resources, using only ordinary commands and no authored coordinates | | |
| [ ] | AI recovery: after a transit cell is captured, the AI replans its route and sells again | | |
| [ ] | Web build completes; every file is under 25 MiB; `_headers` is copied into `Build/Web` | | |
| [ ] | Served over HTTP: `.wasm.br`, `.js.br` and `.data.br` arrive with `Content-Encoding: br` and the correct MIME types | | |
| [ ] | Reload the same URL: the save resumes (IndexedDB), and so does a save made mid-battle | | |
| [ ] | Browser console shows no errors during a 5-minute session | | |

## 2. User acceptance (played by the user in a desktop browser)

| ✓ | Scenario | Date | Notes |
| --- | --- | --- | --- |
| [ ] | Play a 20-minute session in which factory building is most of the activity | | |
| [ ] | Hand-draw a belt network with at least one split and one merge; see ore, crystal, alloy and relay items move | | |
| [ ] | Cause a bottleneck (remove a source or fill a belt) and read the explanation on the stalled machine | | |
| [ ] | Buy the Brokerage Contract upgrade and at least one craft | | |
| [ ] | Get a raid warning, assign or move a defender, and fight the battle; production visibly pauses | | |
| [ ] | Use ground, ranged (artillery) and air (strike craft) attacks; check that the previewed damage and counterattacks match what happens | | |
| [ ] | Lose a cell and recapture it before the 3-minute grace period ends; the machines and items resume | | |
| [ ] | Capture the AI HQ **before** reaching the sales target; the AI withdraws, and its remaining cells are claimable without combat | | |
| [ ] | In another game, reach the sales target **before** capturing the AI HQ; the AI concedes when the HQ falls | | |
| [ ] | Keep building after the AI concedes | | |
| [ ] | Deliberately lose your HQ; the defeat screen is clear | | |
| [ ] | Close the tab, reopen the same URL, and choose Resume | | |

### Browser-specific failures

Record each one with the browser, its version, and steps to reproduce.

| Browser / version | Steps to reproduce | Observed | Expected |
| --- | --- | --- | --- |
| | | | |
