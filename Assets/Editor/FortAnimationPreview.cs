using System.IO;
using UnityEditor;
using UnityEngine;

namespace OrbitalFoundry.EditorTools
{
    /// <summary>Repeatable model renders of the fort's slow scan, quick turn, and nested radar.</summary>
    public static class FortAnimationPreview
    {
        const string Folder = "docs/meshy-fort";

        [MenuItem("Orbital Foundry/Preview fort animation")]
        public static void Render()
        {
            Directory.CreateDirectory(Folder);
            bool previous = MeshyLibrary.Enabled;
            MeshyLibrary.Enabled = true;
            var def = CatalogRepository.GetBuilding(CatalogRepository.LoadContent(), "fort");
            Color colour;
            if (def == null || !ColorUtility.TryParseHtmlString(def.colorHex, out colour)) colour = Color.white;
            var model = MeshyLibrary.Building("fort", FactionId.Player, colour, null, 1);
            if (model == null) throw new System.InvalidOperationException("Meshy fort did not load");
            try
            {
                model.SetState(Model.Normal);
                Shot("fort-rest.png");
                model.Animate(4f);
                Shot("fort-scan.png");
                model.Animate(4.8f);
                Shot("fort-quick-turn.png");

                var turret = model.Root.transform.Find("Body/Spinner");
                var dish = turret.Find("RadarYaw/RadarTilt");
                float width = model.Renderers[0].bounds.size.x;
                var turretMark = Mark(turret, new Vector3(0f, model.Height * 0.5f, 0f), width * 0.28f, Color.red);
                var radarMark = Mark(dish, new Vector3(0f, model.Height * 0.08f, 0f), width * 0.12f, Color.cyan);
                try
                {
                    Shot("fort-marked.png");
                    Shot("fort-marked-reverse.png", 30f);
                }
                finally
                {
                    Object.DestroyImmediate(turretMark);
                    Object.DestroyImmediate(radarMark);
                }

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
                        var focus = new Vector3(0f, model.Height * 0.52f, 0f);
                        camera.transform.position = focus + Quaternion.Euler(24f, yaw, 0f) * new Vector3(0f, 0f, -1.3f);
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
