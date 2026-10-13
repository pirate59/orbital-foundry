using UnityEngine;

namespace OrbitalFoundry
{
    /// <summary>
    /// Orthographic isometric camera (45° yaw, 35° pitch). WASD/arrows pan, the wheel zooms, right/middle drag pans.
    /// In Tactical mode it frames the encounter's battle cells and returns to the previous view afterwards.
    /// </summary>
    public sealed class CameraInput
    {
        public const float Yaw = 45f;
        public const float Pitch = 35f;
        const float Distance = 60f;
        const float MinZoom = 1.6f;   // close enough to see station detail
        const float MaxZoom = 34f;
        const float PanSpeed = 14f;

        public readonly Camera Camera;

        /// <summary>Keyboard pan speed multiplier from settings.</summary>
        public float PanMultiplier = 1f;
        Bounds bounds;
        Vector3 focus;
        float zoom = 9f;
        Vector3 savedFocus;
        float savedZoom;
        bool framingBattle;
        Vector3 lastDragMouse;
        bool dragging;

        /// <summary>True while a drag gesture moved the camera this press (so the release is not treated as a click).</summary>
        public bool DragMoved { get; private set; }

        public CameraInput(Camera camera, Bounds mapBounds, Vector3 initialFocus)
        {
            Camera = camera;
            bounds = mapBounds;
            focus = initialFocus;
            camera.orthographic = true;
            camera.orthographicSize = zoom;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 200f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.02f, 0.025f, 0.06f);
            camera.transparencySortMode = TransparencySortMode.Orthographic;
            camera.transform.rotation = Quaternion.Euler(Pitch, Yaw, 0f);
            Apply();
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
                if (h != 0 || v != 0) Pan(new Vector2(h, v) * PanSpeed * PanMultiplier * dt * (zoom / 8f));
                if (Input.GetKey(KeyCode.Q)) zoom = Mathf.Clamp(zoom * (1f + 1.5f * dt), MinZoom, MaxZoom);
                if (Input.GetKey(KeyCode.E)) zoom = Mathf.Clamp(zoom / (1f + 1.5f * dt), MinZoom, MaxZoom);
            }

            if (!pointerOverUi)
            {
                float wheel = Input.mouseScrollDelta.y;
                if (Mathf.Abs(wheel) > 0.01f) zoom = Mathf.Clamp(zoom * Mathf.Pow(0.9f, wheel), MinZoom, MaxZoom);
                if (Input.GetMouseButtonDown(1) || Input.GetMouseButtonDown(2))
                {
                    dragging = true;
                    DragMoved = false;
                    lastDragMouse = Input.mousePosition;
                }
            }
            if (dragging && (Input.GetMouseButton(1) || Input.GetMouseButton(2)))
            {
                var delta = Input.mousePosition - lastDragMouse;
                if (delta.sqrMagnitude > 4f) DragMoved = true;
                if (DragMoved)
                {
                    float unitsPerPixel = zoom * 2f / Screen.height;
                    Pan(new Vector2(-delta.x, -delta.y) * unitsPerPixel);
                }
                lastDragMouse = Input.mousePosition;
            }
            else if (dragging && !Input.GetMouseButton(1) && !Input.GetMouseButton(2))
            {
                dragging = false;
            }
            Apply();
        }

        /// <summary>Pans in screen-aligned ground directions.</summary>
        void Pan(Vector2 screenDelta)
        {
            var right = Quaternion.Euler(0, Yaw, 0) * Vector3.right;
            var forward = Quaternion.Euler(0, Yaw, 0) * Vector3.forward;
            // Screen vertical maps to ground forward stretched by the pitch.
            focus += right * screenDelta.x + forward * (screenDelta.y / Mathf.Sin(Pitch * Mathf.Deg2Rad));
            framingBattle = false;
        }

        public void FrameBattle(MapDefinition map, EncounterState e)
        {
            if (!e.active || e.battleCellIds.Count == 0) return;
            if (!framingBattle)
            {
                savedFocus = focus;
                savedZoom = zoom;
            }
            var b = new Bounds(WorldView.CellCenter(map, e.targetCellId), Vector3.zero);
            foreach (int id in e.battleCellIds) b.Encapsulate(WorldView.CellCenter(map, id));
            focus = b.center;
            zoom = Mathf.Clamp(Mathf.Max(b.size.x, b.size.z) * 0.45f + 3f, MinZoom, MaxZoom);
            framingBattle = true;
            Apply();
        }

        public void ReturnFromBattle()
        {
            if (!framingBattle) return;
            focus = savedFocus;
            zoom = savedZoom;
            framingBattle = false;
            Apply();
        }

        public void FocusOn(Vector3 point)
        {
            focus = point;
            Apply();
        }

        void Apply()
        {
            focus.x = Mathf.Clamp(focus.x, bounds.min.x, bounds.max.x);
            focus.z = Mathf.Clamp(focus.z, bounds.min.z, bounds.max.z);
            focus.y = 0;
            Camera.orthographicSize = zoom;
            Camera.transform.rotation = Quaternion.Euler(Pitch, Yaw, 0f);
            Camera.transform.position = focus - Camera.transform.forward * Distance;
        }

        /// <summary>Intersects the mouse ray with the ground plane and returns the global tile it hits.</summary>
        public bool TryGetGroundTile(Vector3 screenPoint, out int gx, out int gy, out Vector3 hit)
        {
            gx = gy = 0;
            hit = Vector3.zero;
            var ray = Camera.ScreenPointToRay(screenPoint);
            var plane = new Plane(Vector3.up, Vector3.zero);
            float enter;
            if (!plane.Raycast(ray, out enter)) return false;
            hit = ray.GetPoint(enter);
            gx = Mathf.FloorToInt(hit.x);
            gy = Mathf.FloorToInt(hit.z);
            return true;
        }
    }
}
