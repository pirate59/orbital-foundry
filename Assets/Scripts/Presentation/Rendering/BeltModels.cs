using System.Collections.Generic;
using UnityEngine;

namespace OrbitalFoundry
{
    /// <summary>
    /// Conveyors and cargo. A belt is a raised conveyor (dark belt bed between two rails on short legs) whose rails
    /// show its speed tier and whose side strip lights in the owner's colour; glowing arrows ride it in the direction of
    /// travel. Junctions are turntable hubs. Cargo is a small 3D good shaped by what it is: ore chunks, ingots,
    /// canisters, spools, plates, crystals and crates, in the item's colour.
    /// </summary>
    public static partial class ModelLibrary
    {
        /// <summary>Height of the belt surface above the deck.</summary>
        public const float BeltSurface = 0.11f;

        static Mesh conveyorMesh, arrowMesh, hubMesh;
        static Material[] hubMaterials;
        static readonly Dictionary<string, Mesh> cargoMeshes = new Dictionary<string, Mesh>();
        static readonly Dictionary<string, Material[]> cargoMaterials = new Dictionary<string, Material[]>();

        static readonly Color[] TierRail = { Palette.Chrome, Palette.Hex(0x5CC8E0), Palette.Hex(0xE8C04A) };

        /// <summary>One straight conveyor tile along +Z, centred, slightly over 1 long so neighbours meet.</summary>
        public static Mesh ConveyorMesh()
        {
            if (conveyorMesh != null) return conveyorMesh;
            // Placeholder materials fix the submesh order: 0 bed, 1 rails, 2 side strips.
            var bed = Palette.Get(Palette.Hex(0x2B2A30), Palette.Finish.Matte);
            var rail = Palette.Get(Palette.Hex(0x808080), Palette.Finish.Chrome);
            var strip = Palette.Get(Palette.Hex(0x404040), Palette.Finish.Matte);
            var k = new MeshKit();
            const float len = 1.04f;
            k.Box(new Vector3(0, BeltSurface - 0.02f, 0), new Vector3(0.34f, 0.04f, len), bed);
            foreach (float x in new[] { -0.2f, 0.2f })
            {
                k.Box(new Vector3(x, BeltSurface, 0), new Vector3(0.05f, 0.08f, len), rail);
                k.Box(new Vector3(x * 1.13f, BeltSurface - 0.01f, 0), new Vector3(0.012f, 0.03f, len * 0.92f), strip);
                k.Box(new Vector3(x, BeltSurface * 0.4f, 0), new Vector3(0.04f, BeltSurface * 0.8f, 0.06f), rail);
            }
            // Rollers showing under the bed's edge.
            for (int i = -1; i <= 1; i++)
                k.Push(new Vector3(0, BeltSurface - 0.045f, i * 0.33f), Quaternion.Euler(0, 0, 90))
                    .Cylinder(new Vector3(0, -0.17f, 0), 0.025f, 0.34f, 8, rail).Pop();
            conveyorMesh = k.Build("Conveyor");
            return conveyorMesh;
        }

        /// <summary>The conveyor's materials for a belt's state: bed, tier rails, and the owner strip (lit when running).</summary>
        public static Material[] ConveyorMaterials(FactionId owner, int tier, bool active, bool highlighted)
        {
            var bed = Palette.Get(highlighted ? Palette.Hex(0x45434C) : Palette.Hex(0x2B2A30), Palette.Finish.Matte);
            var rail = Palette.Get(TierRail[Mathf.Clamp(tier, 0, TierRail.Length - 1)], Palette.Finish.Chrome);
            var paint = FactionPaint(owner);
            var strip = !active ? Palette.Get(Palette.Gunmetal, Palette.Finish.Matte)
                : Palette.Get(highlighted ? Color.Lerp(paint, Color.white, 0.55f) : paint, Palette.Finish.Glow);
            return new[] { bed, active ? rail : Palette.Dimmed(rail), strip };
        }

        /// <summary>A flat chevron arrow pointing along +Z, sitting on the belt surface.</summary>
        public static Mesh ArrowMesh()
        {
            if (arrowMesh != null) return arrowMesh;
            var k = new MeshKit();
            var m = Palette.Get(Color.white);
            k.Push(new Vector3(0, BeltSurface + 0.008f, 0), Quaternion.Euler(90, 0, 0))
                .Fin(new[] { new Vector2(0, 0.1f), new Vector2(0.11f, -0.02f), new Vector2(0.06f, -0.06f), new Vector2(0, 0.03f), new Vector2(-0.06f, -0.06f), new Vector2(-0.11f, -0.02f) }, 0.012f, m)
                .Pop();
            arrowMesh = k.Build("Belt Arrow");
            return arrowMesh;
        }

        public static Material ArrowMaterial(FactionId owner, int tier, bool active)
        {
            if (!active) return Palette.Get(Palette.Hex(0x55555C), Palette.Finish.Matte);
            var c = tier > 0 ? TierRail[Mathf.Clamp(tier, 0, TierRail.Length - 1)] : Color.Lerp(FactionPaint(owner), Color.white, 0.35f);
            return Palette.Get(c, Palette.Finish.Glow);
        }

        /// <summary>A turntable hub for corners, splits and merges.</summary>
        public static GameObject Hub(Transform parent)
        {
            if (hubMesh == null)
            {
                var k = new MeshKit();
                k.Cylinder(Vector3.zero, 0.27f, BeltSurface, 18, Palette.Get(Palette.Gunmetal, Palette.Finish.Chrome));
                k.Cylinder(new Vector3(0, BeltSurface, 0), 0.2f, 0.012f, 18, Palette.Get(Palette.Hex(0x2B2A30), Palette.Finish.Matte));
                k.Torus(new Vector3(0, BeltSurface, 0), 0.24f, 0.022f, 20, 4, Palette.Get(Palette.Hex(0x404040), Palette.Finish.Matte));
                hubMesh = k.Build("Belt Hub");
                hubMaterials = k.Materials;
            }
            var go = new GameObject("Belt Hub");
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = hubMesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterials = hubMaterials;
            return go;
        }

        /// <summary>Hub materials with its ring lit in the owner's colour, or dark when the belt is stopped.</summary>
        public static Material[] HubMaterials(FactionId owner, bool active)
        {
            if (hubMaterials == null) return null;
            return new[]
            {
                hubMaterials[0], hubMaterials[1],
                active ? Palette.Get(FactionPaint(owner), Palette.Finish.Glow) : Palette.Get(Palette.Gunmetal, Palette.Finish.Matte)
            };
        }

        // ---------------------------------------------------------------- cargo

        enum CargoShape { Chunk, Ingot, Canister, Spool, Plate, Crystal, Crate, Frame, Circuit }

        static CargoShape ShapeOf(string itemId)
        {
            if (itemId == "crystal" || itemId == "quantum_core" || itemId == "field_emitter") return CargoShape.Crystal;
            if (itemId == "ice" || itemId.EndsWith("ore") || itemId == "sulphur" || itemId == "complex_carbon" || itemId == "graphite") return CargoShape.Chunk;
            if (itemId.EndsWith("_bar") || itemId.EndsWith("_ingot")) return CargoShape.Ingot;
            if (itemId.Contains("water") || itemId == "coolant_cell" || itemId == "refined_sulphur" || itemId == "sulfur_powder") return CargoShape.Canister;
            if (itemId.EndsWith("_wire")) return CargoShape.Spool;
            if (itemId.EndsWith("_plate") || itemId.EndsWith("_sheet") || itemId == "silicon_wafer" || itemId == "hull_section") return CargoShape.Plate;
            if (itemId.EndsWith("_frame") || itemId.EndsWith("_strut")) return CargoShape.Frame;
            if (itemId == "electronics" || itemId == "precision_circuit" || itemId == "relay") return CargoShape.Circuit;
            return CargoShape.Crate;
        }

        /// <summary>Where each unit sits in a stacked load: a ring of up to four on the belt and one on top, a little smaller.</summary>
        static readonly Vector3[] PileSpots =
        {
            new Vector3(-0.08f, 0f, -0.07f), new Vector3(0.08f, 0f, -0.07f), new Vector3(-0.08f, 0f, 0.08f),
            new Vector3(0.08f, 0f, 0.08f), new Vector3(0f, 0.13f, 0f)
        };

        /// <summary>
        /// A cargo model for an item, about 0.25 across, resting on y = 0. A stacked load of raw goods (count above
        /// one) is drawn as a small pile, so a full belt slot reads differently from a single unit.
        /// </summary>
        public static GameObject Cargo(string itemId, Color itemColor, Transform parent, int count = 1)
        {
            count = Mathf.Clamp(count, 1, PileSpots.Length);
            string key = count > 1 ? itemId + "x" + count : itemId;
            Mesh mesh;
            Material[] mats;
            if (!cargoMeshes.TryGetValue(key, out mesh))
            {
                var k = new MeshKit();
                if (count == 1) AddCargo(k, itemId, itemColor);
                else
                    for (int i = 0; i < count; i++)
                    {
                        k.Push(PileSpots[i], Quaternion.Euler(0, i * 47f, 0), Vector3.one * 0.62f);
                        AddCargo(k, itemId, itemColor);
                        k.Pop();
                    }
                mesh = k.Build("Cargo " + key);
                mats = k.Materials;
                cargoMeshes[key] = mesh;
                cargoMaterials[key] = mats;
            }
            mats = cargoMaterials[key];
            var go = new GameObject("Cargo " + itemId);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterials = mats;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        static void AddCargo(MeshKit k, string itemId, Color itemColor)
        {
            var main = Palette.Get(itemColor, ShapeOf(itemId) == CargoShape.Ingot || ShapeOf(itemId) == CargoShape.Plate ? Palette.Finish.Chrome : Palette.Finish.Enamel);
            var glow = Palette.Get(itemColor, Palette.Finish.Glow);
            var band = Palette.Get(Palette.Gunmetal, Palette.Finish.Chrome);
            var cream = Palette.Get(Palette.Cream);
            switch (ShapeOf(itemId))
            {
                case CargoShape.Chunk:
                    k.Rock(new Vector3(0, 0.09f, 0), 0.11f, itemId.Length * 13, 0.3f, Palette.Get(itemColor, Palette.Finish.Matte), 0.8f);
                    break;
                case CargoShape.Ingot:
                    k.Lathe(new[] { new Vector2(0, 0), new Vector2(0.15f, 0), new Vector2(0.11f, 0.08f), new Vector2(0, 0.08f) }, 4, main, 45f);
                    k.Push(new Vector3(0, 0.08f, 0), Quaternion.Euler(0, 90, 0), new Vector3(0.85f, 1f, 0.85f)).Lathe(new[] { new Vector2(0, 0), new Vector2(0.15f, 0), new Vector2(0.11f, 0.08f), new Vector2(0, 0.08f) }, 4, main, 45f).Pop();
                    break;
                case CargoShape.Canister:
                    k.Cylinder(Vector3.zero, 0.08f, 0.2f, 14, cream);
                    k.Cylinder(new Vector3(0, 0.05f, 0), 0.083f, 0.1f, 14, glow);
                    k.Sphere(new Vector3(0, 0.2f, 0), 0.08f, 14, cream, true);
                    k.Cylinder(new Vector3(0, 0.27f, 0), 0.03f, 0.03f, 8, band);
                    break;
                case CargoShape.Spool:
                    k.Cylinder(Vector3.zero, 0.12f, 0.025f, 14, band);
                    k.Cylinder(new Vector3(0, 0.025f, 0), 0.1f, 0.12f, 16, main);
                    k.Cylinder(new Vector3(0, 0.145f, 0), 0.12f, 0.025f, 14, band);
                    break;
                case CargoShape.Plate:
                    for (int i = 0; i < 3; i++) k.Box(new Vector3(0, 0.02f + i * 0.04f, 0), new Vector3(0.26f - i * 0.02f, 0.03f, 0.2f - i * 0.02f), main);
                    break;
                case CargoShape.Frame:
                    foreach (float x in new[] { -0.1f, 0.1f })
                        foreach (float z in new[] { -0.1f, 0.1f })
                            k.Box(new Vector3(x, 0.1f, z), new Vector3(0.03f, 0.2f, 0.03f), main);
                    k.Box(new Vector3(0, 0.02f, 0), new Vector3(0.23f, 0.03f, 0.23f), main);
                    k.Box(new Vector3(0, 0.19f, 0), new Vector3(0.23f, 0.03f, 0.23f), main);
                    break;
                case CargoShape.Circuit:
                    k.Box(new Vector3(0, 0.05f, 0), new Vector3(0.22f, 0.1f, 0.22f), cream);
                    k.Box(new Vector3(0, 0.105f, 0), new Vector3(0.18f, 0.012f, 0.18f), glow);
                    k.Box(new Vector3(0.05f, 0.12f, 0.05f), new Vector3(0.06f, 0.03f, 0.06f), band);
                    break;
                case CargoShape.Crystal:
                    k.Cylinder(Vector3.zero, 0.1f, 0.04f, 6, band);
                    k.Lathe(new[] { new Vector2(0, 0.03f), new Vector2(0.07f, 0.1f), new Vector2(0.06f, 0.22f), new Vector2(0, 0.3f) }, 6, glow);
                    break;
                default:
                    k.Box(new Vector3(0, 0.1f, 0), new Vector3(0.22f, 0.2f, 0.22f), cream);
                    k.Box(new Vector3(0, 0.1f, 0), new Vector3(0.226f, 0.06f, 0.226f), main);
                    k.Box(new Vector3(0, 0.205f, 0), new Vector3(0.1f, 0.012f, 0.1f), glow);
                    break;
            }
        }
    }
}
