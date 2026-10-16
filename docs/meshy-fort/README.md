# Meshy fort animation preview

The supplied fort is loaded as `Buildings/fort` by the Meshy building pipeline. Its FBX and colour texture are in `Assets/Resources/Meshy/Buildings/`. The metallic, normal, and roughness PNGs are preserved in `Assets/MeshySource/Buildings/fort/`; the current Meshy renderer uses the colour texture.

The **red ring** in the marked views follows the turret. The base stays fixed while the turret scans at 7° per second. Every 13 seconds it makes a brief 62° target check, pauses, and returns to the slow scan. Checks alternate left and right.

The **cyan ring** follows the radar dish, which is nested on the turret. The dish rotates at 31° per second and nods 13° up and down on a 4.6 second cycle.

`fort-rest.png`, `fort-scan.png`, and `fort-quick-turn.png` show the same camera at 0, 4, and 8.8 seconds. `fort-marked.png` and `fort-marked-reverse.png` show the animated parts at the quick turn. These are Unity renders of the current implementation; the local WebGL build shows the continuous motion.

Regenerate the previews with **Orbital Foundry → Preview fort animation** in Unity, or run `OrbitalFoundry.EditorTools.FortAnimationPreview.Render` in batch mode.
