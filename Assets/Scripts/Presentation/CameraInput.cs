using UnityEngine;

namespace OrbitalFoundry
{
    /// <summary>
    /// Perspective orbit camera around a focus point on the board, eased toward its targets for a smooth, cinematic
    /// feel. WASD/arrows pan, Q/E rotate, R/F tilt (from straight down to skimming the rock), the wheel (or +/-)
    /// zooms toward the cursor, right-drag grabs and pans the board, middle-drag (or Shift + right-drag) orbits,
    /// T jumps to a top-down view, G drops to a low view across the field, and Home resets the view.
    /// In Tactical mode it frames the encounter's battle cells and returns to the previous view afterwards.
    /// </summary>
    public sealed class CameraInput
    {
        const float FieldOfView = 38f;
        const float MinDistance = 5f;
        const float MaxDistance = 120f;
        const float MinPitch = 3f;     // nearly level, looking across the field
        const float MaxPitch = 89.5f;  // straight down, short of the pole
        /// <summary>The camera never drops closer than this to the deck under the focus.</summary>
        const float MinHeight = 0.7f;
        const float TopDownPitch = 89.5f, TopDownDistance = 45f;
        const float GroundPitch = 7f, GroundDistance = 16f;
        const float DefaultYaw = 45f;
        const float DefaultPitch = 52f;
        const float DefaultDistance = 34f;
        const float PanSpeed = 1.1f;      // board units per second per unit of distance
        const float RotateSpeed = 95f;    // degrees per second
        const float OrbitPerPixel = 0.25f;
        const float Ease = 10f;

        public readonly Camera Camera;

        /// <summary>The map being shown, for finding the deck under the cursor.</summary>
        public MapDefinition Map;

        /// <summary>Keyboard pan speed multiplier from settings.</summary>
        public float PanMultiplier = 1f;
        Bounds bounds;

        // Targets the controls change, and the eased values the camera shows.
        Vector3 focus, shownFocus;
        float yaw = DefaultYaw, shownYaw = DefaultYaw;
        float pitch = DefaultPitch, shownPitch = DefaultPitch;
        float distance = DefaultDistance, shownDistance = DefaultDistance;

        Vector3 savedFocus;
        float savedDistance;
        bool framingBattle;

        enum Drag { None, Pan, Orbit }
        Drag drag;
        Vector3 pressMouse, lastMouse;
        Vector3 grabPoint;

        /// <summary>True while a drag gesture moved the camera this press (so the release is not treated as a click).</summary>
        public bool DragMoved { get; private set; }

        public CameraInput(Camera camera, Bounds mapBounds, Vector3 initialFocus)
        {
            Camera = camera;
            bounds = mapBounds;
            focus = shownFocus = initialFocus;
            camera.orthographic = false;
            camera.fieldOfView = FieldOfView;
            camera.nearClipPlane = 0.3f;
            camera.farClipPlane = 800f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.02f, 0.03f, 0.06f);
            Snap();
        }

        public void Update(float dt, bool pointerOverUi, bool keyboardFree)
        {
            if (keyboardFree)
            {
                float h = 0, v = 0;
                if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) h -= 1;
                if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) h += 1;
                if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) v -= 1;
                if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) v += 1;
                if (h != 0 || v != 0) Pan(new Vector2(h, v).normalized * PanSpeed * PanMultiplier * distance * dt);
                if (Input.GetKey(KeyCode.Q)) yaw += RotateSpeed * dt;
                if (Input.GetKey(KeyCode.E)) yaw -= RotateSpeed * dt;
                if (Input.GetKey(KeyCode.R)) pitch = Mathf.Clamp(pitch + RotateSpeed * 0.6f * dt, MinPitch, MaxPitch);
                if (Input.GetKey(KeyCode.F)) pitch = Mathf.Clamp(pitch - RotateSpeed * 0.6f * dt, MinPitch, MaxPitch);
                if (Input.GetKey(KeyCode.Equals) || Input.GetKey(KeyCode.KeypadPlus)) Zoom(1f + 1.6f * dt, null);
                if (Input.GetKey(KeyCode.Minus) || Input.GetKey(KeyCode.KeypadMinus)) Zoom(1f / (1f + 1.6f * dt), null);
                if (Input.GetKeyDown(KeyCode.Home)) { yaw = DefaultYaw; pitch = DefaultPitch; distance = DefaultDistance; }
                if (Input.GetKeyDown(KeyCode.T)) { pitch = TopDownPitch; distance = Mathf.Max(distance, TopDownDistance); }
                if (Input.GetKeyDown(KeyCode.G)) { pitch = GroundPitch; distance = GroundDistance; }
            }

            if (!pointerOverUi)
            {
                float wheel = Input.mouseScrollDelta.y;
                if (Mathf.Abs(wheel) > 0.01f) Zoom(Mathf.Pow(1.12f, wheel), Input.mousePosition);
                bool orbitPress = Input.GetMouseButtonDown(2) || (Input.GetMouseButtonDown(1) && ShiftHeld());
                if (orbitPress) BeginDrag(Drag.Orbit);
                else if (Input.GetMouseButtonDown(1)) BeginDrag(Drag.Pan);
            }

            bool held = Input.GetMouseButton(1) || Input.GetMouseButton(2);
            if (drag != Drag.None && held)
            {
                var mouse = Input.mousePosition;
                if ((mouse - pressMouse).sqrMagnitude > 16f) DragMoved = true;
                if (DragMoved)
                {
                    if (drag == Drag.Orbit)
                    {
                        var d = mouse - lastMouse;
                        yaw += d.x * OrbitPerPixel;
                        pitch = Mathf.Clamp(pitch - d.y * OrbitPerPixel, MinPitch, MaxPitch);
                    }
                    else
                    {
                        // Keep the grabbed board point under the cursor, measured against the eased camera.
                        Vector3 now;
                        if (GroundPoint(mouse, out now))
                        {
                            focus += grabPoint - now;
                            shownFocus += grabPoint - now;
                            framingBattle = false;
                        }
                    }
                }
                lastMouse = mouse;
            }
            else if (drag != Drag.None && !held) drag = Drag.None;

            Clamp();
            float k = 1f - Mathf.Exp(-Ease * Mathf.Max(0f, dt));
            shownFocus = Vector3.Lerp(shownFocus, focus, k);
            shownYaw = Mathf.LerpAngle(shownYaw, yaw, k);
            shownPitch = Mathf.Lerp(shownPitch, pitch, k);
            shownDistance = Mathf.Lerp(shownDistance, distance, k);
            Place();
        }

        static bool ShiftHeld() { return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift); }

        void BeginDrag(Drag kind)
        {
            drag = kind;
            DragMoved = false;
            pressMouse = lastMouse = Input.mousePosition;
            if (kind == Drag.Pan && !GroundPoint(pressMouse, out grabPoint)) drag = Drag.None;
        }

        /// <summary>Pans along the ground in the camera's facing.</summary>
        void Pan(Vector2 delta)
        {
            var rot = Quaternion.Euler(0, yaw, 0);
            focus += rot * Vector3.right * delta.x + rot * Vector3.forward * delta.y;
            framingBattle = false;
        }

        /// <summary>Zooms by <paramref name="factor"/> (above 1 moves closer), pulling the focus toward the cursor's board point.</summary>
        void Zoom(float factor, Vector3? screenPoint)
        {
            float next = Mathf.Clamp(distance / factor, MinDistance, MaxDistance);
            Vector3 target;
            if (screenPoint.HasValue && GroundPoint(screenPoint.Value, out target))
            {
                float pull = 1f - next / distance;
                focus += (target - focus) * pull;
            }
            distance = next;
        }

        public void FrameBattle(MapDefinition map, EncounterState e)
        {
            if (!e.active || e.battleCellIds.Count == 0) return;
            if (!framingBattle)
            {
                savedFocus = focus;
                savedDistance = distance;
            }
            var b = new Bounds(WorldView.CellCenter(map, e.targetCellId), Vector3.zero);
            foreach (int id in e.battleCellIds) b.Encapsulate(WorldView.CellCenter(map, id));
            focus = b.center;
            distance = Mathf.Clamp(Mathf.Max(b.size.x, b.size.z) * 1.3f + 24f, MinDistance, MaxDistance);
            framingBattle = true;
        }

        public void ReturnFromBattle()
        {
            if (!framingBattle) return;
            focus = savedFocus;
            distance = savedDistance;
            framingBattle = false;
        }

        /// <summary>Jumps straight to a point without easing (used when a match starts).</summary>
        public void FocusOn(Vector3 point)
        {
            focus = point;
            Clamp();
            Snap();
        }

        void Snap()
        {
            shownFocus = focus;
            shownYaw = yaw;
            shownPitch = pitch;
            shownDistance = distance;
            Place();
        }

        void Clamp()
        {
            focus.x = Mathf.Clamp(focus.x, bounds.min.x, bounds.max.x);
            focus.z = Mathf.Clamp(focus.z, bounds.min.z, bounds.max.z);
            // Rest on the deck under the focus (asteroids sit at different heights); keep the last height over gaps.
            float deck;
            if (BoardLayout.DeckHeightAt(Map, focus.x, focus.z, out deck)) focus.y = deck;
            distance = Mathf.Clamp(distance, MinDistance, MaxDistance);
            pitch = Mathf.Clamp(pitch, LowestPitch(distance), MaxPitch);
        }

        /// <summary>The lowest tilt that keeps the camera <see cref="MinHeight"/> above the deck at this distance.</summary>
        static float LowestPitch(float dist)
        {
            return Mathf.Max(MinPitch, Mathf.Asin(Mathf.Clamp01(MinHeight / Mathf.Max(dist, 0.01f))) * Mathf.Rad2Deg);
        }

        void Place()
        {
            var rot = Quaternion.Euler(shownPitch, shownYaw, 0f);
            Camera.transform.rotation = rot;
            Camera.transform.position = shownFocus - rot * Vector3.forward * shownDistance;
        }

        bool GroundPoint(Vector3 screenPoint, out Vector3 hit)
        {
            hit = Vector3.zero;
            var ray = Camera.ScreenPointToRay(screenPoint);
            var plane = new Plane(Vector3.up, new Vector3(0f, shownFocus.y, 0f));
            float enter;
            if (!plane.Raycast(ray, out enter) || enter > Camera.farClipPlane) return false;
            hit = ray.GetPoint(enter);
            return true;
        }

        /// <summary>Finds the deck tile under the cursor (decks may sit at different heights) as a global grid tile.</summary>
        public bool TryGetGroundTile(Vector3 screenPoint, out int gx, out int gy, out Vector3 hit)
        {
            return BoardLayout.Raycast(Map, Camera.ScreenPointToRay(screenPoint), Camera.farClipPlane, out gx, out gy, out hit);
        }

        /// <summary>The map's extent changed (a new map or layout): re-clamp the camera to it.</summary>
        public void SetBounds(Bounds mapBounds)
        {
            bounds = mapBounds;
        }
    }
}
