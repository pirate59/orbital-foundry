using System.IO;
using UnityEditor;
using UnityEngine;

namespace OrbitalFoundry.EditorTools
{
    /// <summary>
    /// A contact sheet of the Meshy shipyard's work cycle (the hoist carrying the hull section away and back, the
    /// welding arms leaning in) at six moments, to Logs/shipyard-animation.png:
    /// -executeMethod OrbitalFoundry.EditorTools.ShipyardAnimationPreview.RenderBatch
    /// MESHY_MODEL="solar_array" previews another building (to Logs/&lt;id&gt;-animation.png).
    /// SHIPYARD_VIEW="yaw pitch distance" changes the camera (default 210 28 4.2), SHIPYARD_FOCUS="x y z" aims it
    /// at a point given as shares of the model's size, and SHIPYARD_TIMES="s s s s s s" picks the six moments.
    /// </summary>
    public static class ShipyardAnimationPreview
    {
        const int Cell = 600, Columns = 3, Rows = 2;
        static readonly float[] Seconds = { 0f, 3.2f, 6.5f, 9f, 12f, 15.8f };

        public static void RenderBatch()
        {
            try { Render(); EditorApplication.Exit(0); }
            catch (System.Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }

        [MenuItem("Orbital Foundry/Preview shipyard animation")]
        public static void Render()
        {
            bool previous = MeshyLibrary.Enabled;
            MeshyLibrary.Enabled = true;
            // MESHY_MODEL previews another building's animation the same way.
            string id = System.Environment.GetEnvironmentVariable("MESHY_MODEL");
            if (string.IsNullOrEmpty(id)) id = "shipyard";
            var buildingDef = CatalogRepository.GetBuilding(CatalogRepository.LoadContent(), id);
            int tiles = buildingDef != null ? Mathf.Max(buildingDef.width, buildingDef.footprint) : 1;
            var model = MeshyLibrary.Building(id, FactionId.Player, Color.white, null, tiles);
            if (model == null) throw new System.InvalidOperationException("No Meshy model for " + id);
            var sheet = new Texture2D(Cell * Columns, Cell * Rows, TextureFormat.RGB24, false);
            var light = new GameObject("Light").AddComponent<Light>();
            var cam = new GameObject("Camera").AddComponent<Camera>();
            var target = new RenderTexture(Cell, Cell, 24) { antiAliasing = 4 };
            try
            {
                model.SetState(Model.Normal);
                light.type = LightType.Directional;
                light.shadows = LightShadows.Soft;
                light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.14f, 0.15f, 0.2f);
                cam.fieldOfView = 34f;
                cam.targetTexture = target;
                float yaw = 210f, pitch = 28f, distance = 4.2f;
                var view = System.Environment.GetEnvironmentVariable("SHIPYARD_VIEW");
                if (!string.IsNullOrEmpty(view))
                {
                    var n = System.Array.ConvertAll(view.Split(' '), float.Parse);
                    yaw = n[0]; pitch = n[1]; distance = n[2];
                }
                var focus = new Vector3(0f, model.Height * 0.5f, 0f);
                var aim = System.Environment.GetEnvironmentVariable("SHIPYARD_FOCUS");
                if (!string.IsNullOrEmpty(aim))
                {
                    var n = System.Array.ConvertAll(aim.Split(' '), float.Parse);
                    var frame = model.Renderers[0].bounds;
                    focus = frame.min + Vector3.Scale(new Vector3(n[0], n[1], n[2]), frame.size);
                }
                var seconds = Seconds;
                var times = System.Environment.GetEnvironmentVariable("SHIPYARD_TIMES");
                if (!string.IsNullOrEmpty(times)) seconds = System.Array.ConvertAll(times.Split(' '), float.Parse);
                var rot = Quaternion.Euler(pitch, yaw, 0f);
                cam.transform.rotation = rot;
                cam.transform.position = focus - rot * Vector3.forward * distance;
                float clock = 0f;
                for (int i = 0; i < seconds.Length && i < Columns * Rows; i++)
                {
                    // Step the motions in small frames up to this moment, as the game would.
                    while (clock < seconds[i]) { model.Animate(0.05f); clock += 0.05f; }
                    foreach (var effect in model.Effects) effect.Simulate(0.3f, true, true);
                    cam.Render();
                    RenderTexture.active = target;
                    var shot = new Texture2D(Cell, Cell, TextureFormat.RGB24, false);
                    shot.ReadPixels(new Rect(0, 0, Cell, Cell), 0, 0);
                    sheet.SetPixels(i % Columns * Cell, (Rows - 1 - i / Columns) * Cell, Cell, Cell, shot.GetPixels());
                    Object.DestroyImmediate(shot);
                    RenderTexture.active = null;
                }
                File.WriteAllBytes("Logs/" + id + "-animation.png", sheet.EncodeToPNG());
                Debug.Log("MESHY ANIMATION Logs/" + id + "-animation.png");
            }
            finally
            {
                RenderTexture.active = null;
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(cam.gameObject);
                Object.DestroyImmediate(light.gameObject);
                Object.DestroyImmediate(sheet);
                Object.DestroyImmediate(model.Root);
                MeshyLibrary.Enabled = previous;
            }
        }
    }
}
