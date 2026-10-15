Meshy building models for the "Meshy" visual style.

Name each model after the building's id in catalog.json: extractor, refinery, purifier, assembler, sale_depot,
shipyard, headquarters, fort, fort_site, storage, solar_array, drone_pad (e.g. refinery.fbx).
FBX or OBJ (with its texture beside it) both work. Any size: the game centres each model on its tile, scales it to
90% of the building's footprint (at most 2.4 tiles tall) and stands it on a thin plinth in the owner's colour.

Keep models under about 8,000 triangles; bigger ones still work but the Console warns on import
(Assets/Editor/MeshyImport.cs), since every copy on the board draws all of them.
A model with no texture is painted in the game's palette: gunmetal base, cream on upward faces and the
building's catalog colour elsewhere. A textured model keeps its texture.

Per-model settings go in ../models.json (ids like "Buildings/refinery"):
- rotate: a turn in degrees about X, Y and Z for a model that does not stand or face the right way.
- part: a spinning, plunging part around the upright centre line (the extractor's drill), with a drive rod and dust.
- swings: parts cut out by boxes that swing about an upright pivot (the refinery's crane).
- beacons: a lamp with sweeping beams and a glow.
- steam: vents that puff steam.
Positions and boxes are fractions of the model's size (0 to 1 along X, Y, Z: left, bottom, front to right, top, back).
Editor menu Orbital Foundry > Meshy blueprints renders gridded plan, front and side views to Logs/ to read them off,
and Preview Meshy buildings renders the model with its moving parts tinted red. Everything moves only while the
building is working.
