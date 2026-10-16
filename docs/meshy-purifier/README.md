# Meshy water purifier animation mockup

The Meshy purifier is loaded as `Buildings/purifier` by the same resource path as the other Meshy buildings. Its FBX and colour texture are in `Assets/Resources/Meshy/Buildings/`. The metallic, normal, and roughness PNGs from the supplied archive are retained in `Assets/MeshySource/Buildings/purifier/` as source maps. The current Meshy renderer uses the colour texture.

## Model markings

| Mark | Location | Motion |
| --- | --- | --- |
| Red circle | Fan blades above the top housing | Clockwise rotation at 24° per second (one turn in 15 seconds) |
| Cyan circles | Both tall water tanks, at the top and bottom of each window | 11 bubbles per tank rise and drift; the light column inside each tank pulses every 3.2 or 3.7 seconds |
| Gold circles | Three blue bands on the central tower | Four cyan lamps travel around each band; the middle ring reverses direction |

`purifier-rest.png` and `purifier-animated.png` show the same camera at the start and four seconds into the cycle. `purifier-animated-reverse.png` shows the other side. `purifier-marked.png`, `purifier-marked-reverse.png`, and `purifier-marked-top.png` add the coloured model-space marks. These are Unity renders of the current implementation. The stills capture one point in each loop; use the local WebGL build to see the motion.

To regenerate the screenshots, run the Unity editor menu item **Orbital Foundry → Preview purifier animation**, or execute `OrbitalFoundry.EditorTools.PurifierAnimationPreview.Render` in batch mode.
