using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace OrbitalFoundry
{
    /// <summary>
    /// The title screen's backdrop: the detailed Mobile Guard (Resources/Title: its full-size colour, normal and
    /// metallic maps, uncompressed mesh), large and whole, a third in from the right of the screen (or at the top of
    /// a phone held upright), turning slowly. A camera of its own shoots it far below the map, with the main camera's starry sky, the scene's
    /// lights and the post effects, into a texture the size of the screen that the title overlay shows as its
    /// background. It only runs while the title is up. Without the model there is no texture, and the title keeps
    /// its plain starfield.
    /// </summary>
    public sealed class TitleShowcase
    {
        /// <summary>Far below the map, out of sight of play.</summary>
        static readonly Vector3 Stage = new Vector3(0f, -3000f, 0f);
        /// <summary>The ship's longest side, in world units.</summary>
        const float ShipSize = 10f;
        /// <summary>A layer of its own, so its camera sees the ship alone.</summary>
        const int Layer = 29;
        /// <summary>Seconds for one whole turn.</summary>
        const float TurnSeconds = 90f;

        readonly Camera main;
        Camera camera;
        GameObject root;
        Transform ship;
        RenderTexture texture;
        /// <summary>
        /// The ship's reach from its centre as it turns about its upright axis: across (its length or wingspan, the
        /// diagonal of its plan) and up (its height, plus a little for the bank), in world units.
        /// </summary>
        float reachAcross = ShipSize * 0.5f, reachUp = ShipSize * 0.25f;
        bool built;
        float time;

        /// <summary>Raised when the texture is made again (the window changed size): show the new one.</summary>
        public System.Action<RenderTexture> TextureChanged;

        public TitleShowcase(Camera main) { this.main = main; }

        /// <summary>The ship on show; the texture to put behind the title, or null without a ship.</summary>
        public RenderTexture Show()
        {
            if (!built) Build();
            if (ship == null) return null;
            root.SetActive(true);
            camera.enabled = true;
            Fit();
            return texture;
        }

        /// <summary>Puts the ship and its camera away (play has started).</summary>
        public void Hide()
        {
            if (camera != null) camera.enabled = false;
            if (root != null) root.SetActive(false);
        }

        /// <summary>The slow turn; the texture follows the window's size.</summary>
        public void Tick(float deltaSeconds)
        {
            if (ship == null || camera == null || !camera.enabled) return;
            time += deltaSeconds;
            // A steady turn about its own axis, banked a little, bobbing gently.
            ship.localRotation = Quaternion.Euler(-6f + Mathf.Sin(time * 0.2f) * 1.5f, -150f + time * 360f / TurnSeconds, 9f);
            ship.localPosition = new Vector3(0f, Mathf.Sin(time * 0.31f) * 0.1f, 0f);
            Fit();
        }

        void Build()
        {
            built = true;
            if (main == null) return;
            var prefab = Resources.Load<GameObject>("Title/guard");
            var material = Resources.Load<Material>("Title/guard");
            if (prefab == null || material == null) return;
            root = new GameObject("Title Showcase");
            root.transform.position = Stage;
            var model = Object.Instantiate(prefab, root.transform);
            model.name = "Mobile Guard";
            var renderers = model.GetComponentsInChildren<Renderer>(true);
            var bounds = new Bounds();
            bool any = false;
            foreach (var r in renderers)
            {
                var slots = new Material[Mathf.Max(1, r.sharedMaterials.Length)];
                for (int i = 0; i < slots.Length; i++) slots[i] = material;
                r.sharedMaterials = slots;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                r.gameObject.layer = Layer;
                if (!any) { bounds = r.bounds; any = true; }
                else bounds.Encapsulate(r.bounds);
            }
            if (!any) { Object.Destroy(root); root = null; return; }
            // Scaled whole to the stage, centred on its middle, so it turns about itself.
            float longest = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
            float scale = ShipSize / Mathf.Max(0.001f, longest);
            var pivot = new GameObject("Turn").transform;
            pivot.SetParent(root.transform, false);
            model.transform.SetParent(pivot, true);
            model.transform.localScale *= scale;
            // Measured again at its new size, then moved so its middle is the pivot.
            var scaled = renderers[0].bounds;
            foreach (var r in renderers) scaled.Encapsulate(r.bounds);
            model.transform.position += pivot.position - scaled.center;
            reachAcross = new Vector2(scaled.extents.x, scaled.extents.z).magnitude;
            reachUp = scaled.extents.y + reachAcross * 0.12f; // the bank and bob lift an end a little
            ship = pivot;
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = Layer;

            var camGo = new GameObject("Title Camera");
            camera = camGo.AddComponent<Camera>();
            camera.CopyFrom(main);
            camera.cullingMask = 1 << Layer;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 200f;
            camera.depth = main.depth - 1;
            var data = camera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            data.renderShadows = false;
            camera.enabled = false;
            root.SetActive(false);
        }

        /// <summary>
        /// The texture at the screen's size (never scaled down), and the camera placed so the whole ship is in view: its
        /// centre a third in from the right on a wide screen (the menu on the left), or in the top part of an upright
        /// one (the menu below).
        /// </summary>
        void Fit()
        {
            int w = Mathf.Max(64, Screen.width), h = Mathf.Max(64, Screen.height);
            if (texture == null || texture.width != w || texture.height != h)
            {
                if (texture != null) { camera.targetTexture = null; texture.Release(); Object.Destroy(texture); }
                texture = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { name = "Title Showcase", antiAliasing = 4 };
                camera.targetTexture = texture;
                if (TextureChanged != null) TextureChanged(texture);
            }
            camera.aspect = (float)w / h;
            float aspect = camera.aspect;
            float halfHeight;
            Vector3 offset;
            if (aspect >= 1f)
            {
                // The ship's centre a third in from the right, the whole ship on screen however it turns: its reach fits
                // the two thirds of a half-width to the right edge, and the height.
                halfHeight = Mathf.Max(reachAcross / (aspect * 2f / 3f * 0.94f), reachUp / 0.9f);
                offset = new Vector3(aspect * halfHeight / 3f, 0f, 0f);
            }
            else
            {
                // Upright (a phone): the ship in the top part of the screen, centred across; the menu is below it.
                halfHeight = Mathf.Max(reachAcross / (aspect * 0.94f), reachUp / 0.6f);
                offset = new Vector3(0f, halfHeight * 0.36f, 0f); // the camera below the ship: it shows high
            }
            float distance = halfHeight / Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            camera.transform.SetPositionAndRotation(Stage - offset - Vector3.forward * distance, Quaternion.identity);
        }
    }
}
