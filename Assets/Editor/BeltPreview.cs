using System.IO;
using UnityEditor;
using UnityEngine;

namespace OrbitalFoundry.EditorTools
{
    /// <summary>Renders a belt crossing and a corner, with hubs, to Logs/belt-junctions.png, to check rails stop at the hub rim.</summary>
    public static class BeltPreview
    {
        /// <summary>Three parallel belts at tiers 1, 2 and 3, with their arrows, to Logs/belt-tiers.png.</summary>
        [MenuItem("Orbital Foundry/Preview belt tiers")]
        public static void Tiers()
        {
            var root = new GameObject("Tiers");
            for (int tier = 0; tier < 3; tier++)
            {
                var mats = ModelLibrary.ConveyorMaterials(FactionId.Player, tier, true, false);
                for (int i = 0; i < 3; i++)
                    Belt(root, new Vector3(i - 1.5f, 0, tier * 0.7f), new Vector3(i - 0.5f, 0, tier * 0.7f), false, false, mats);
                var arrow = new GameObject("Arrow");
                arrow.transform.SetParent(root.transform, false);
                arrow.transform.SetPositionAndRotation(new Vector3(0f, 0f, tier * 0.7f), Quaternion.LookRotation(Vector3.right));
                arrow.AddComponent<MeshFilter>().sharedMesh = ModelLibrary.ArrowMesh();
                arrow.AddComponent<MeshRenderer>().sharedMaterial = ModelLibrary.ArrowMaterial(FactionId.Player, tier, true);
            }
            var light = new GameObject("Light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(55, -30, 0);
            var cam = new GameObject("Cam").AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.35f, 0.32f, 0.3f);
            cam.transform.position = new Vector3(0f, 2.2f, -1.8f);
            cam.transform.LookAt(new Vector3(0f, 0f, 0.7f));
            var rt = new RenderTexture(900, 500, 24);
            cam.targetTexture = rt;
            cam.Render();
            cam.Render();
            RenderTexture.active = rt;
            var shot = new Texture2D(900, 500, TextureFormat.RGB24, false);
            shot.ReadPixels(new Rect(0, 0, 900, 500), 0, 0);
            File.WriteAllBytes("Logs/belt-tiers.png", shot.EncodeToPNG());
            RenderTexture.active = null;
            Object.DestroyImmediate(cam.gameObject);
            Object.DestroyImmediate(light.gameObject);
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(rt);
        }

        [MenuItem("Orbital Foundry/Preview belt junctions")]
        public static void Run()
        {
            var root = new GameObject("Junctions");
            var mats = ModelLibrary.ConveyorMaterials(FactionId.Player, 0, true, false);
            // A crossing at (0,0): four belts meet there. A corner at (3,0): in from the west, out to the north.
            Belt(root, new Vector3(-1, 0, 0), Vector3.zero, false, true, mats);
            Belt(root, Vector3.zero, new Vector3(1, 0, 0), true, false, mats);
            Belt(root, new Vector3(0, 0, -1), Vector3.zero, false, true, mats);
            Belt(root, Vector3.zero, new Vector3(0, 0, 1), true, false, mats);
            ModelLibrary.Hub(root.transform).transform.position = Vector3.zero;
            Belt(root, new Vector3(2, 0, 0), new Vector3(3, 0, 0), false, true, mats);
            Belt(root, new Vector3(3, 0, 0), new Vector3(3, 0, 1), true, false, mats);
            ModelLibrary.Hub(root.transform).transform.position = new Vector3(3, 0, 0);

            var light = new GameObject("Light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(55, -30, 0);
            var cam = new GameObject("Cam").AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.35f, 0.32f, 0.3f);
            cam.transform.position = new Vector3(1.5f, 3.2f, -2.4f);
            cam.transform.LookAt(new Vector3(1.5f, 0f, 0.2f));
            var rt = new RenderTexture(1024, 512, 24);
            cam.targetTexture = rt;
            cam.Render();
            cam.Render();
            RenderTexture.active = rt;
            var shot = new Texture2D(1024, 512, TextureFormat.RGB24, false);
            shot.ReadPixels(new Rect(0, 0, 1024, 512), 0, 0);
            File.WriteAllBytes("Logs/belt-junctions.png", shot.EncodeToPNG());
            RenderTexture.active = null;
            Object.DestroyImmediate(cam.gameObject);
            Object.DestroyImmediate(light.gameObject);
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(rt);
        }

        static void Belt(GameObject root, Vector3 from, Vector3 to, bool trimStart, bool trimEnd, Material[] mats)
        {
            var go = new GameObject("Belt");
            go.transform.SetParent(root.transform, false);
            go.transform.SetPositionAndRotation((from + to) * 0.5f, Quaternion.LookRotation(to - from, Vector3.up));
            go.AddComponent<MeshFilter>().sharedMesh = ModelLibrary.ConveyorMesh(trimStart, trimEnd);
            go.AddComponent<MeshRenderer>().sharedMaterials = mats;
        }
    }
}
