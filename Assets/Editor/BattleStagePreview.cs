using System.IO;
using UnityEditor;
using UnityEngine;

namespace OrbitalFoundry.EditorTools
{
    /// <summary>
    /// Films sample battle-scene matchups on the 3D battle stage (Meshy models) over a two-tone backdrop like the
    /// scene's, to Logs/battle-stage-*.png: -executeMethod OrbitalFoundry.EditorTools.BattleStagePreview.RenderBatch
    /// </summary>
    public static class BattleStagePreview
    {
        public static void RenderBatch()
        {
            try { Render(); EditorApplication.Exit(0); }
            catch (System.Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }

        [MenuItem("Orbital Foundry/Preview battle stage")]
        public static void Render()
        {
            bool previous = MeshyLibrary.Enabled;
            MeshyLibrary.Enabled = true;
            var catalog = CatalogRepository.LoadContent();
            var stage = new BattleStage();
            try
            {
                var matchups = new[] { new[] { "guard", "bomber" }, new[] { "lancer", "fortress" }, new[] { "bomber", "fort" } };
                for (int frame = 0; frame < matchups.Length * 2; frame++)
                {
                    int m = frame / 2;
                    bool impact = frame % 2 == 1; // the moment the shots land: blasts, a wreck falling, smoke
                    stage.Open();
                    Side(stage, catalog, matchups[m][0], FactionId.Player, true, false);
                    Side(stage, catalog, matchups[m][1], FactionId.Ai, false, impact);
                    if (impact) stage.Simulate(0.12f);
                    stage.RenderNow();
                    stage.RenderNow();
                    var rt = stage.Texture;
                    RenderTexture.active = rt;
                    var shot = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
                    shot.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                    RenderTexture.active = null;
                    // Composite over the scene's halves: mint-tinted for you on the left, peach for the enemy.
                    var px = shot.GetPixels();
                    for (int i = 0; i < px.Length; i++)
                    {
                        int x = i % rt.width;
                        var back = x < rt.width / 2 ? new Color(0.09f, 0.16f, 0.17f) : new Color(0.2f, 0.11f, 0.12f);
                        px[i] = Color.Lerp(back, new Color(px[i].r, px[i].g, px[i].b), px[i].a);
                    }
                    shot.SetPixels(px);
                    File.WriteAllBytes("Logs/battle-stage-" + matchups[m][0] + "-vs-" + matchups[m][1] + (impact ? "-impact" : "") + ".png", shot.EncodeToJPG(88));
                    Object.DestroyImmediate(shot);
                    Debug.Log("BATTLE STAGE " + matchups[m][0] + " vs " + matchups[m][1]);
                }
            }
            finally
            {
                stage.Close();
                MeshyLibrary.Enabled = previous;
            }
        }

        /// <summary>A side's full fleet in the scene's wedge, laid out as the battle scene lays it out.</summary>
        static void Side(BattleStage stage, ContentCatalog c, string type, FactionId owner, bool left, bool impact)
        {
            var craft = CatalogRepository.GetCraft(c, type);
            float size = craft == null ? 96f * 3f : 96f * Fleet.Scale(craft); // as the battle scene: a guard ship is 96 px
            var wedge = Fleet.Wedge(Fleet.FullFleet(craft));
            var raw = new Vector2[wedge.Length];
            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);
            for (int i = 0; i < wedge.Length; i++)
            {
                raw[i] = new Vector2(-wedge[i].y * size * 1.05f, wedge[i].x * size * 0.72f + wedge[i].y * size * 0.1f);
                min = Vector2.Min(min, raw[i]);
                max = Vector2.Max(max, raw[i]);
            }
            var span = max - min;
            const float boxLeft = 16f, boxWidth = 448f, boxBottom = 64f, boxHeight = 340f;
            float k = Mathf.Min(1f, boxWidth / (span.x + size), boxHeight / (span.y + size));
            size *= k;
            var origin = new Vector2(boxLeft + (boxWidth - (span.x + size / k) * k) * 0.5f, boxBottom + (boxHeight - (span.y + size / k) * k) * 0.5f);
            for (int i = 0; i < raw.Length; i++)
            {
                var at = origin + (raw[i] - min) * k;
                float x = at.x + size / 2f;
                if (!left) x = BattleStage.StageWidth - x;
                float y = BattleStage.StageHeight - (at.y + size / 2f);
                var model = stage.Add(c, type, owner, size);
                var spot = new Vector2(x, y);
                // At impact the last ship of the fleet is knocked out and tumbling; the rest are hit and smoking.
                bool wreck = impact && raw.Length > 1 && i == raw.Length - 1;
                float fall = wreck ? 0.45f : 0f;
                stage.Pose(model, spot + new Vector2((left ? -40f : 40f) * fall, 90f * fall * fall), left, 1f - 0.35f * fall, true, fall);
                if (impact)
                {
                    stage.Burst(spot, size * (wreck ? 1.1f : 0.75f), wreck);
                    if (!wreck) stage.Smoke(model);
                }
            }
        }
    }
}
