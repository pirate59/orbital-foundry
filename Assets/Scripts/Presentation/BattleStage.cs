using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace OrbitalFoundry
{
    /// <summary>
    /// A small 3D set far below the map where the battle scene films its fleets: the craft's own models (Meshy where
    /// there are some, else the built-in ones) and the Fort, posed in stage pixels so they sit exactly where the scene's
    /// overlay (bolts, numbers, plates) expects them. An orthographic camera renders the set into <see cref="Texture"/>
    /// with a clear background, shown behind the overlay. Only its own layer is lit and filmed.
    /// </summary>
    public sealed class BattleStage
    {
        public const float StageWidth = 960f, StageHeight = 540f, PixelsPerUnit = 100f;
        /// <summary>The layer the set lives on, so its camera and light see nothing else and the map's camera ignores it.</summary>
        public const int Layer = 31;
        /// <summary>How far the ships lean toward the camera and turn toward it, so they read in three quarters.</summary>
        const float Lean = 18f, TurnToCamera = 22f;
        static readonly Vector3 Origin = new Vector3(0f, -5000f, 0f);

        GameObject root;
        Camera camera;
        RenderTexture texture;
        readonly List<Model> models = new List<Model>();
        readonly Dictionary<Model, Vector3> fitted = new Dictionary<Model, Vector3>();

        public RenderTexture Texture { get { return texture; } }

        /// <summary>Builds the set on first use and starts filming.</summary>
        public void Open()
        {
            if (root == null)
            {
                root = new GameObject("Battle stage");
                root.transform.position = Origin;
                texture = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4, name = "Battle stage" };
                camera = new GameObject("Battle stage camera").AddComponent<Camera>();
                camera.transform.SetParent(root.transform, false);
                camera.transform.localPosition = new Vector3(StageWidth * 0.5f / PixelsPerUnit, -StageHeight * 0.5f / PixelsPerUnit, -30f);
                camera.orthographic = true;
                camera.orthographicSize = StageHeight * 0.5f / PixelsPerUnit;
                camera.nearClipPlane = 1f;
                camera.farClipPlane = 60f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0f, 0f, 0f, 0f); // clear: the scene's backdrop shows through
                camera.cullingMask = 1 << Layer;
                camera.targetTexture = texture;
                var data = camera.GetUniversalAdditionalCameraData();
                data.renderPostProcessing = false; // keeps the background's alpha
                data.renderShadows = false;
                var key = new GameObject("Battle stage key light").AddComponent<Light>();
                key.transform.SetParent(root.transform, false);
                key.type = LightType.Directional;
                key.intensity = 1.25f;
                key.cullingMask = 1 << Layer;
                key.transform.rotation = Quaternion.Euler(35f, -30f, 0f);
                var rim = new GameObject("Battle stage rim light").AddComponent<Light>();
                rim.transform.SetParent(root.transform, false);
                rim.type = LightType.Directional;
                rim.intensity = 0.55f;
                rim.color = new Color(0.6f, 0.75f, 1f);
                rim.cullingMask = 1 << Layer;
                rim.transform.rotation = Quaternion.Euler(20f, 160f, 0f);
            }
            Clear();
            camera.enabled = true;
        }

        /// <summary>Renders the set now (for previews; in play the camera films every frame).</summary>
        public void RenderNow()
        {
            if (camera != null) camera.Render();
        }

        /// <summary>Removes the fleets and stops filming.</summary>
        public void Close()
        {
            Clear();
            if (camera != null) camera.enabled = false;
        }

        void Clear()
        {
            foreach (var m in models)
                if (m != null && m.Root != null)
                {
                    if (Application.isPlaying) Object.Destroy(m.Root); else Object.DestroyImmediate(m.Root); // previews run outside play
                }
            models.Clear();
            fitted.Clear();
            foreach (var e in effects)
                if (e != null)
                {
                    if (Application.isPlaying) Object.Destroy(e.gameObject); else Object.DestroyImmediate(e.gameObject);
                }
            effects.Clear();
        }

        /// <summary>
        /// One ship of a craft's fleet (or a Fort, for a building type), its longest side <paramref name="sizePixels"/>
        /// of stage, or null when there is no model for it.
        /// </summary>
        public Model Add(ContentCatalog c, string type, FactionId owner, float sizePixels)
        {
            if (root == null) return null;
            var craft = CatalogRepository.GetCraft(c, type);
            var building = craft == null ? CatalogRepository.GetBuilding(c, type) : null;
            Color colour;
            string hex = craft != null ? craft.colorHex : building != null ? building.colorHex : "";
            if (!ColorUtility.TryParseHtmlString(hex, out colour)) colour = Palette.Cream;
            float size = sizePixels / PixelsPerUnit;
            Model model = null;
            if (craft != null) model = MeshyLibrary.Craft(type, owner, colour, root.transform, size) ?? ModelLibrary.Craft(type, owner, root.transform);
            else if (building != null) model = MeshyLibrary.Building(type, owner, colour, root.transform, 1) ?? ModelLibrary.Building(type, owner, root.transform);
            if (model == null) return null;
            // Fit the longest side to the ship's size, keeping a craft's extra girth.
            var extent = model.Bounds.size;
            float fit = size / Mathf.Max(0.01f, Mathf.Max(extent.x, Mathf.Max(extent.y, extent.z)));
            float girth = Fleet.Girth(craft);
            model.Root.transform.localScale = new Vector3(fit * girth, fit * girth, fit);
            fitted[model] = model.Root.transform.localScale;
            SetLayer(model.Root.transform);
            model.SetState(Model.Normal);
            models.Add(model);
            return model;
        }

        /// <summary>
        /// Poses a model with its middle at a stage point (pixels from the stage's top left), facing its enemy,
        /// at <paramref name="scale"/> of its fitted size; hidden when <paramref name="visible"/> is false.
        /// </summary>
        public void Pose(Model model, Vector2 stagePoint, bool facingRight, float scale, bool visible, float tumble = 0f)
        {
            Vector3 size;
            if (model == null || model.Root == null || !fitted.TryGetValue(model, out size)) return;
            var t = model.Root.transform;
            model.Root.SetActive(visible && scale > 0.02f);
            float yaw = facingRight ? 90f + TurnToCamera : -90f - TurnToCamera;
            // A knocked-out ship rolls over and noses down as it falls away.
            t.localRotation = Quaternion.Euler(-Lean, 0f, 0f) * Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(tumble * 35f, 0f, tumble * 140f);
            t.localScale = size * scale;
            // Models stand on their base; lift so the middle of the model is on the point.
            var target = new Vector3(stagePoint.x / PixelsPerUnit, -stagePoint.y / PixelsPerUnit, 0f);
            t.localPosition = target;
            var middle = model.Bounds.center - t.position;
            t.localPosition = target - middle;
        }

        // ---------------------------------------------------------------- effects

        Material dust;
        bool lookedForDust;
        readonly List<ParticleSystem> effects = new List<ParticleSystem>();

        Material Dust()
        {
            if (!lookedForDust) { lookedForDust = true; dust = Resources.Load<Material>("Materials/Dust"); }
            return dust;
        }

        static Vector3 Local(Vector2 stagePoint, float towardCamera)
        {
            return new Vector3(stagePoint.x / PixelsPerUnit, -stagePoint.y / PixelsPerUnit, -towardCamera);
        }

        /// <summary>
        /// An explosion at a stage point, about <paramref name="sizePixels"/> across: a white-hot flash, a spray of
        /// sparks, fire and a rolling cloud of smoke. A knock-out (<paramref name="big"/>) is larger, with debris.
        /// </summary>
        public void Burst(Vector2 stagePoint, float sizePixels, bool big)
        {
            if (root == null || Dust() == null) return;
            float u = sizePixels / PixelsPerUnit;
            var at = Local(stagePoint, 3f);
            Emit("Flash", at, big ? 3 : 1, new Vector2(0.06f, 0.12f), new Vector2(0f, 0.2f) * u, new Vector2(1.2f, 1.8f) * u * (big ? 1.4f : 1f),
                new Color(1f, 1f, 0.92f, 1f), new Color(1f, 0.8f, 0.45f, 1f), 0f);
            Emit("Sparks", at, big ? 70 : 28, new Vector2(0.25f, 0.6f), new Vector2(2f, 6f) * u, new Vector2(0.04f, 0.1f) * u,
                new Color(1f, 0.95f, 0.65f, 1f), new Color(1f, 0.45f, 0.1f, 1f), 1.2f);
            Emit("Fire", at, big ? 22 : 9, new Vector2(0.3f, 0.6f), new Vector2(0.3f, 1.4f) * u, new Vector2(0.35f, 0.7f) * u * (big ? 1.3f : 1f),
                new Color(1f, 0.6f, 0.15f, 0.9f), new Color(0.9f, 0.25f, 0.05f, 0.8f), -0.15f);
            Emit("Smoke", at, big ? 16 : 6, new Vector2(0.8f, 1.6f), new Vector2(0.2f, 0.8f) * u, new Vector2(0.5f, 1f) * u * (big ? 1.4f : 1f),
                new Color(0.32f, 0.3f, 0.3f, 0.7f), new Color(0.15f, 0.14f, 0.15f, 0.6f), -0.3f);
            if (big)
                Emit("Debris", at, 18, new Vector2(0.6f, 1.2f), new Vector2(1.5f, 4f) * u, new Vector2(0.06f, 0.14f) * u,
                    new Color(0.55f, 0.55f, 0.6f, 1f), new Color(0.3f, 0.3f, 0.34f, 1f), 2.2f);
        }

        /// <summary>A muzzle flash where a ship fires.</summary>
        public void MuzzleFlash(Vector2 stagePoint, float sizePixels, Color colour)
        {
            if (root == null || Dust() == null) return;
            float u = sizePixels / PixelsPerUnit;
            Emit("Muzzle", Local(stagePoint, 3f), 2, new Vector2(0.05f, 0.1f), Vector2.zero, new Vector2(0.4f, 0.6f) * u,
                Color.Lerp(Color.white, colour, 0.4f), colour, 0f);
        }

        /// <summary>A damaged ship trails smoke for the rest of the scene.</summary>
        public void Smoke(Model model)
        {
            if (model == null || model.Root == null || Dust() == null) return;
            var smoke = WorkDust.Steam(model.Root.transform, Vector3.zero, 0.3f, true);
            if (smoke == null) return;
            var main = smoke.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            SetLayer(smoke.transform);
            smoke.Play();
            effects.Add(smoke);
        }

        void Emit(string name, Vector3 at, int count, Vector2 life, Vector2 speed, Vector2 size, Color a, Color b, float gravity)
        {
            var go = new GameObject(name);
            go.layer = Layer;
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = at;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.duration = 0.2f;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life.x, life.y);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed.x, speed.y);
            main.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y);
            main.startColor = new ParticleSystem.MinMaxGradient(a, b);
            main.gravityModifier = gravity;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = count + 4;
            main.stopAction = Application.isPlaying ? ParticleSystemStopAction.Destroy : ParticleSystemStopAction.None;
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = Mathf.Max(0.01f, size.x);
            var fade = ps.colorOverLifetime;
            fade.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.4f), new GradientAlphaKey(0f, 1f) });
            fade.color = new ParticleSystem.MinMaxGradient(g);
            var grow = ps.sizeOverLifetime;
            grow.enabled = true;
            grow.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(1f, 1.4f)));
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = dust;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            ps.Play();
            effects.Add(ps);
        }

        /// <summary>Steps every effect by hand (previews render outside play, where particles do not advance).</summary>
        public void Simulate(float seconds)
        {
            foreach (var e in effects) if (e != null) e.Simulate(seconds, true, false);
        }

        static void SetLayer(Transform t)
        {
            t.gameObject.layer = Layer;
            foreach (Transform child in t) SetLayer(child);
        }
    }
}
