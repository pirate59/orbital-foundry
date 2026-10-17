using UnityEngine;

namespace OrbitalFoundry
{
    /// <summary>
    /// One pointer for the map, from a mouse or a touch screen. The world input reads clicks and drags from here
    /// and the camera reads gestures, so both work the same either way.
    ///
    /// With a mouse it is the mouse: the left button is the primary click; the right button and the camera's drags
    /// are read as before.
    ///
    /// On a touch screen (iPad, phone):
    /// - a tap is a left click, at the tap;
    /// - a long press (half a second, held still) is a right click: order the selected craft there, rotate the
    ///   shipyard ghost, or cancel the tool;
    /// - one finger dragging pans the board, unless a drawing tool (belts, dismantle) is active, when it draws;
    /// - two fingers pan, pinch to zoom and twist to turn the view;
    /// - three fingers dragged up or down tilt the view.
    /// A touch that starts over the interface is the interface's, never the map's.
    /// </summary>
    public static class Pointer
    {
        public const float TapSlop = 14f, LongPressSeconds = 0.5f;

        /// <summary>True once a touch has been seen: the game is being played by touch.</summary>
        public static bool TouchMode { get; private set; }
        /// <summary>Where the pointer is (a touch keeps its last place once lifted, so a placement ghost stays put).</summary>
        public static Vector3 Position { get; private set; }
        /// <summary>The primary (left) press began, is held, or ended this frame.</summary>
        public static bool PrimaryDown { get; private set; }
        public static bool PrimaryHeld { get; private set; }
        public static bool PrimaryUp { get; private set; }
        /// <summary>A right click (or a touch long-press) this frame.</summary>
        public static bool Secondary { get; private set; }
        /// <summary>How far a long press has got (0 to 1), for drawing its ring; 0 when none is under way.</summary>
        public static float LongPressProgress { get; private set; }

        /// <summary>Touch camera gestures this frame: zoom factor (above 1 moves closer), twist, three-finger tilt.</summary>
        public static float Zoom { get; private set; }
        public static float Twist { get; private set; }
        public static float Tilt { get; private set; }
        /// <summary>A touch pan this frame: the screen point the board should move from and to.</summary>
        public static bool Panning { get; private set; }
        public static Vector3 PanFrom { get; private set; }
        public static Vector3 PanTo { get; private set; }
        /// <summary>Where a pinch is centred, to zoom toward.</summary>
        public static Vector3 PinchCentre { get; private set; }

        /// <summary>
        /// Rewrites mouse and keyboard wording for touch: "right-click" becomes "long-press", "click" becomes "tap",
        /// Esc becomes the Cancel button and Z the Rotate button. Unchanged with a mouse.
        /// </summary>
        public static string Words(string text)
        {
            if (!TouchMode || string.IsNullOrEmpty(text)) return text;
            return text.Replace("Right-click or Esc", "Long-press or Cancel").Replace("right-click or Esc", "long-press or Cancel")
                .Replace("Z or right-click", "Rotate or a long press").Replace("Shift+Z turns it back. ", "")
                .Replace("Right-click", "Long-press").Replace("right-click", "long-press")
                .Replace("Left-click", "Tap").Replace("left-click", "tap")
                .Replace("Click ", "Tap ").Replace("click ", "tap ").Replace("click.", "tap.")
                .Replace("Esc to cancel", "Cancel to stop").Replace(" or Esc", " or Cancel")
                .Replace("hover ", "tap ").Replace("Hover ", "Tap ");
        }

        /// <summary>Set by the world input: one finger draws (belts, dismantle) instead of panning.</summary>
        public static bool DrawMode;

        // The touch gesture in progress.
        static bool tracking, moved, longPressed, multi, overUi;
        static int fingers;
        static float startTime;
        static Vector2 start, last;
        static float lastSpread, lastAngle;
        static Vector2 lastCentre;

        /// <summary>Reads the pointer for this frame. <paramref name="isOverUi"/> tells whether a screen point is on the interface.</summary>
        public static void Update(System.Func<Vector2, bool> isOverUi)
        {
            PrimaryDown = PrimaryUp = Secondary = Panning = false;
            Zoom = 1f;
            Twist = Tilt = 0f;
            LongPressProgress = 0f;
            if (Input.touchCount > 0 && !TouchMode)
            {
                TouchMode = true;
                // From now on the map listens to touches directly; a touch no longer also pretends to be a mouse.
                Input.simulateMouseWithTouches = false;
            }
            if (!TouchMode)
            {
                Position = Input.mousePosition;
                PrimaryDown = Input.GetMouseButtonDown(0);
                PrimaryHeld = Input.GetMouseButton(0);
                PrimaryUp = Input.GetMouseButtonUp(0);
                return;
            }
            ReadTouches(isOverUi);
        }

        static void ReadTouches(System.Func<Vector2, bool> isOverUi)
        {
            int count = Input.touchCount;
            if (count == 0)
            {
                if (tracking && !multi && !overUi)
                {
                    if (DrawMode) { if (PrimaryHeld) PrimaryUp = true; }
                    else if (!moved && !longPressed) { PrimaryDown = true; PrimaryUp = true; } // a tap
                }
                tracking = multi = false;
                PrimaryHeld = false;
                fingers = 0;
                return;
            }

            var first = Input.GetTouch(0);
            if (!tracking)
            {
                tracking = true;
                moved = longPressed = multi = false;
                start = last = first.position;
                startTime = Time.unscaledTime;
                overUi = isOverUi != null && isOverUi(first.position);
                fingers = count;
                Position = first.position;
                if (!overUi && DrawMode && count == 1) { PrimaryDown = true; PrimaryHeld = true; }
            }
            if (overUi) return; // the interface has this touch

            if (count >= 2)
            {
                // Several fingers: camera only. A drawing press started by the first finger is let go.
                if (!multi)
                {
                    multi = true;
                    if (PrimaryHeld) { PrimaryUp = true; PrimaryHeld = false; }
                    BeginMulti(count);
                }
                else if (count != fingers) BeginMulti(count);
                Gesture(count);
                return;
            }
            if (multi) return; // one finger left after a gesture: wait for it to lift

            Position = first.position;
            if (!moved && (first.position - start).magnitude > TapSlop) moved = true;
            if (DrawMode)
            {
                PrimaryHeld = true;
                last = first.position;
                return;
            }
            if (moved)
            {
                Panning = true;
                PanFrom = last;
                PanTo = first.position;
            }
            else if (!longPressed)
            {
                float held = Time.unscaledTime - startTime;
                LongPressProgress = Mathf.Clamp01(held / LongPressSeconds);
                if (held >= LongPressSeconds) { longPressed = true; Secondary = true; }
            }
            last = first.position;
        }

        static void BeginMulti(int count)
        {
            fingers = count;
            lastCentre = Centre(count);
            lastSpread = Spread(count, lastCentre);
            lastAngle = count >= 2 ? Angle() : 0f;
        }

        static void Gesture(int count)
        {
            var centre = Centre(count);
            if (count == 2)
            {
                float spread = Spread(count, centre);
                if (lastSpread > 1f && spread > 1f) Zoom = spread / lastSpread;
                float angle = Angle();
                Twist = Mathf.DeltaAngle(lastAngle, angle);
                lastSpread = spread;
                lastAngle = angle;
                Panning = true;
                PanFrom = lastCentre;
                PanTo = centre;
                PinchCentre = centre;
            }
            else Tilt = centre.y - lastCentre.y; // three or more fingers dragged up or down
            lastCentre = centre;
        }

        static Vector2 Centre(int count)
        {
            var sum = Vector2.zero;
            for (int i = 0; i < count; i++) sum += Input.GetTouch(i).position;
            return sum / count;
        }

        static float Spread(int count, Vector2 centre)
        {
            float total = 0f;
            for (int i = 0; i < count; i++) total += (Input.GetTouch(i).position - centre).magnitude;
            return total / count;
        }

        static float Angle()
        {
            var d = Input.GetTouch(1).position - Input.GetTouch(0).position;
            return Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
        }
    }
}
