using System.IO;
using UnityEditor;
using UnityEngine;

namespace OrbitalFoundry.EditorTools
{
    /// <summary>Repeatable model renders of the shipyard's slow scan, quick turn, and nested radar.</summary>
    public static class ShipyardAnimationPreview
    {
        const string Folder = "docs/meshy-shipyard";

        [MenuItem("Orbital Foundry/Preview shipyard animation")]
        public static void Render()
        {
            Directory.CreateDirectory(Folder);
            bool previous = MeshyLibrary.Enabled;
            MeshyLibrary.Enabled = true;
            var def = CatalogRepository.GetBuilding(CatalogRepository.LoadContent(), "shipyard");
            Color colour;
            if (def == null || !ColorUtility.TryParseHtmlString(def.colorHex, out colour)) colour = Color.white;
            var model = MeshyLibrary.Building("shipyard", FactionId.Player, colour, null, 1);
            if (model == null) throw new System.InvalidOperationException("Meshy shipyard did not load");
            try
            {
                model.SetState(Model.Normal);
                Shot("shipyard-rest.png");
                model.Animate(4.2f);
                foreach (var effect in model.Effects)
                {
                    effect.Play();
                    effect.Simulate(0.6f, true, true);
                }
                Shot("shipyard-traverse.png");
                model.Animate(4.5f);
                Shot("shipyard-return.png");

                var claw = model.Root.transform.Find("Body/ShipyardClaw");
                if (claw == null) throw new System.InvalidOperationException("Shipyard claw mesh was not isolated");
                var clawMark = Mark(claw, Vector3.zero, 0.14f, Color.cyan);
                var swings = model.Root.transform.Find("Body").GetComponentsInChildren<Transform>();
                var armMarks = new System.Collections.Generic.List<GameObject>();
                foreach (var arm in swings)
                    if (arm.name == "Swing") armMarks.Add(Mark(arm, Vector3.up * 0.25f, 0.08f, armMarks.Count == 0 ? Color.red : Color.yellow));
                try { Shot("shipyard-marked.png"); }
                finally { Object.DestroyImmediate(clawMark); foreach (var mark in armMarks) Object.DestroyImmediate(mark); }

                void Shot(string name, float yaw = 205f)
                {
                    var light = new GameObject("Preview light").AddComponent<Light>();
                    var camera = new GameObject("Preview camera").AddComponent<Camera>();
                    var target = new RenderTexture(1000, 1000, 24);
                    try
                    {
                        light.type = LightType.Directional;
                        light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
                        camera.clearFlags = CameraClearFlags.SolidColor;
                        camera.backgroundColor = new Color(0.15f, 0.17f, 0.23f);
                        camera.orthographic = true;
                        camera.orthographicSize = 0.78f;
                        var focus = new Vector3(0f, model.Height * 0.45f, 0f);
                        camera.transform.position = focus + Quaternion.Euler(24f, yaw, 0f) * new Vector3(0f, 0f, -3.5f);
                        camera.transform.LookAt(focus);
                        camera.targetTexture = target;
                        camera.Render();
                        camera.Render();
                        RenderTexture.active = target;
                        var picture = new Texture2D(1000, 1000, TextureFormat.RGB24, false);
                        picture.ReadPixels(new Rect(0, 0, 1000, 1000), 0, 0);
                        File.WriteAllBytes(Path.Combine(Folder, name), picture.EncodeToPNG());
                        Object.DestroyImmediate(picture);
                        Debug.Log("FORT PREVIEW " + name);
                    }
                    finally
                    {
                        RenderTexture.active = null;
                        Object.DestroyImmediate(target);
                        Object.DestroyImmediate(camera.gameObject);
                        Object.DestroyImmediate(light.gameObject);
                    }
                }
            }
            finally
            {
                Object.DestroyImmediate(model.Root);
                MeshyLibrary.Enabled = previous;
            }
        }

        static GameObject Mark(Transform parent, Vector3 at, float radius, Color colour)
        {
            var kit = new MeshKit();
            kit.Torus(Vector3.zero, radius, radius * 0.035f, 48, 6, Palette.Flat(colour));
            var marker = kit.Instantiate("Animation zone", parent, false);
            marker.transform.localPosition = at;
            return marker;
        }
    }
}
