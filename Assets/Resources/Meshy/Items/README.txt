Meshy item models for the "Meshy" visual style.

Name each model after the item's id in catalog.json, e.g. iron_bar.fbx, ore.fbx, copper_wire.fbx.
FBX or OBJ (with its texture beside it) both work. Any size or orientation: the game scales each model to a
cargo slot and stands it on the belt. Items without a model keep the built-in one.

Keep models under about 1,500 triangles; bigger ones still work but the Console warns on import. A model with no texture is painted in the
game's palette using the item's catalog colour. Turns for models that do not stand right go in ../orientation.json
(ids like "Items/iron_bar").
