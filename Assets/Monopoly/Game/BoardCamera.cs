using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

namespace Monopoly.Game
{
    /// <summary>
    /// Cinematic camera director. Every mode describes a rig (pivot point, yaw, pitch, distance); the camera
    /// eases between rigs so cuts become smooth moves. When following a token the camera sits outside the
    /// board edge the token is on, so that side's tiles read the right way up.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class BoardCamera : MonoBehaviour
    {
        private enum Mode { Orbit, Showcase, Overview, Follow, Focus }

        [Tooltip("Viewport area the whole board must fit in for the overview shot (leaves room for the HUD).")]
        [SerializeField] private Rect overviewSafeArea = new Rect(0.04f, 0.17f, 0.92f, 0.68f);
        [SerializeField] private float smoothTime = 0.55f;
        [SerializeField] private float followDistance = 6.4f;
        [SerializeField] private float followPitch = 40f;

        private Camera cam;
        private Mode mode = Mode.Orbit;
        private Transform target;

        // Desired rig.
        private Vector3 goalPivot;
        private float goalYaw, goalPitch = 38f, goalDistance = 18f;

        // Current (smoothed) rig.
        private Vector3 pivot, pivotVelocity;
        private float yaw, pitch = 38f, distance = 18f, yawVelocity, pitchVelocity, distanceVelocity;

        private float shake;

        // Manual control: two-finger pan / pinch / twist on touch, right-drag / scroll wheel / Q-E on desktop.
        // While manual, the director keeps tracking the action in the background but the camera stays where the
        // player put it, until they tap Focus (or, if the setting allows, the next turn starts).
        private Vector3 manualPivot;
        private float manualYaw, manualDistance;

        /// <summary>The player has taken the camera; the HUD shows a Focus button while this is true.</summary>
        public static bool IsManual { get; private set; }

        /// <summary>Gestures only move the camera during a game (not in menus or the lobby showcase).</summary>
        public bool AllowManualControl { get; set; }

        /// <summary>Returns true if a screen position is over UI that should swallow camera gestures.</summary>
        public static Func<Vector2, bool> IsInputBlocked = _ => false;

        /// <summary>True while the player is moving the camera, so taps don't fire.</summary>
        public static bool IsGesturing { get; private set; }
        private float overviewCacheKey = float.NaN;
        private float overviewCacheDistance;

        public float Distance => distance;

        private void OnEnable() => EnhancedTouchSupport.Enable();

        /// <summary>Give the camera back to the director: the Focus button.</summary>
        public void ReturnToDirector() => IsManual = false;

        /// <summary>
        /// A new beat of the game (turn start, dice throw). Hands the camera back only if the player has opted to
        /// have manual moves reset each turn.
        /// </summary>
        public void OnDirectorCue()
        {
            if (GameSettings.CameraAutoReturn) IsManual = false;
        }

        private void TakeManualControl()
        {
            if (IsManual) return;
            IsManual = true;
            manualPivot = pivot;
            manualYaw = yaw;
            manualDistance = distance;
        }

        private void HandleGestures()
        {
            bool wasGesturing = IsGesturing;
            IsGesturing = false;

            var touches = Touch.activeTouches;
            if (touches.Count >= 2)
            {
                var a = touches[0];
                var b = touches[1];
                if (!wasGesturing)
                {
                    if (IsInputBlocked(a.startScreenPosition) || IsInputBlocked(b.startScreenPosition)) return;
                    gestureTwist = 0f;
                    gesturePinch = 1f;
                    twistUnlocked = pinchUnlocked = false;
                }
                IsGesturing = true;
                Vector2 pa = a.screenPosition, pb = b.screenPosition;
                Vector2 qa = pa - a.delta, qb = pb - b.delta;

                // Grab the board: the point under the fingers' midpoint stays under it.
                if (GroundPoint((qa + qb) * 0.5f, out var before) && GroundPoint((pa + pb) * 0.5f, out var after))
                    Pan(before - after);

                // Pinch and twist only engage after a deliberate motion, so panning doesn't wobble the view.
                float d0 = (qa - qb).magnitude, d1 = (pa - pb).magnitude;
                if (d0 > 20f && d1 > 20f)
                {
                    float factor = d0 / d1;
                    gesturePinch *= factor;
                    if (!pinchUnlocked && Mathf.Abs(gesturePinch - 1f) > 0.08f) pinchUnlocked = true;
                    if (pinchUnlocked) Zoom(factor);

                    float twist = Vector2.SignedAngle(qb - qa, pb - pa);
                    gestureTwist += twist;
                    if (!twistUnlocked && Mathf.Abs(gestureTwist) > 12f) twistUnlocked = true;
                    if (twistUnlocked) Rotate(-twist);
                }
                return;
            }

            var mouse = Mouse.current;
            if (mouse == null) return;
            Vector2 pos = mouse.position.ReadValue();
            if (IsInputBlocked(pos)) return;
            if (mouse.rightButton.isPressed)
            {
                IsGesturing = true;
                Vector2 d = mouse.delta.ReadValue();
                if (GroundPoint(pos - d, out var before) && GroundPoint(pos, out var after)) Pan(before - after);
            }
            float scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f) Zoom(scroll > 0 ? 0.9f : 1.1f);
            var keys = Keyboard.current;
            if (keys != null)
            {
                if (keys.qKey.isPressed) Rotate(-90f * Time.unscaledDeltaTime);
                if (keys.eKey.isPressed) Rotate(90f * Time.unscaledDeltaTime);
            }
        }

        private float gestureTwist, gesturePinch = 1f;
        private bool twistUnlocked, pinchUnlocked;

        /// <summary>Where a screen point hits the board's plane.</summary>
        private bool GroundPoint(Vector2 screen, out Vector3 point)
        {
            var ray = cam.ScreenPointToRay(screen);
            var plane = new Plane(Vector3.up, Vector3.zero);
            if (plane.Raycast(ray, out float enter) && enter < 500f)
            {
                point = ray.GetPoint(enter);
                return true;
            }
            point = default;
            return false;
        }

        private void Pan(Vector3 delta)
        {
            TakeManualControl();
            // Don't let the player scroll off into the forest.
            Vector3 world = manualPivot + new Vector3(delta.x, 0, delta.z);
            float limit = BoardView.HalfBoard + 1f;
            world.x = Mathf.Clamp(world.x, -limit, limit);
            world.z = Mathf.Clamp(world.z, -limit, limit);
            manualPivot = world;
            pivot = manualPivot;
        }

        private void Zoom(float factor)
        {
            TakeManualControl();
            manualDistance = Mathf.Clamp(manualDistance * factor, 3f, 32f);
            distance = manualDistance;
        }

        private void Rotate(float degrees)
        {
            TakeManualControl();
            manualYaw += degrees;
            yaw = manualYaw;
        }

        private void Awake()
        {
            cam = GetComponent<Camera>();
            cam.fieldOfView = 34f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 300f;
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.allowMSAA = true;
            cam.allowHDR = true;
            Orbit(snap: true);
        }

        /// <summary>Camera yaw that looks at board side containing <paramref name="space"/> from outside.</summary>
        public static float SideYaw(int space) => (space / 10) * 90f;

        // ---------------------------------------------------------------- modes

        /// <summary>Slow turntable around the board, for menus.</summary>
        public void Orbit(bool snap = false)
        {
            mode = Mode.Orbit;
            goalPivot = new Vector3(0, -0.6f, 0);
            goalPitch = 34f;
            goalDistance = 19f;
            if (snap) Snap();
        }

        /// <summary>Close-up on the token line-up during the lobby.</summary>
        public void Showcase(Vector3 center, float viewDistance)
        {
            mode = Mode.Showcase;
            goalPivot = center;
            goalYaw = 0f;
            goalPitch = 20f;
            goalDistance = viewDistance;
        }

        /// <summary>The whole board, seen from the given side.</summary>
        public void Overview(float fromYaw)
        {
            mode = Mode.Overview;
            goalPivot = Vector3.zero;
            goalYaw = fromYaw;
            goalPitch = 56f;
        }

        /// <summary>Chase a token, looking from outside the board side <paramref name="fromYaw"/>.</summary>
        public void Follow(Transform token, float fromYaw, float distanceScale = 1f)
        {
            mode = Mode.Follow;
            target = token;
            goalYaw = fromYaw;
            goalPitch = followPitch;
            goalDistance = followDistance * distanceScale;
        }

        /// <summary>Frame a fixed point, e.g. where the dice will land.</summary>
        public void Focus(Vector3 point, float fromYaw, float viewDistance, float viewPitch = 48f)
        {
            mode = Mode.Focus;
            goalPivot = point;
            goalYaw = fromYaw;
            goalPitch = viewPitch;
            goalDistance = viewDistance;
        }

        public void Shake(float amount)
        {
            if (GameSettings.CameraShake) shake = Mathf.Max(shake, amount);
        }

        private void Snap()
        {
            pivot = goalPivot;
            yaw = goalYaw;
            pitch = goalPitch;
            distance = goalDistance;
        }

        // ---------------------------------------------------------------- update

        private void LateUpdate()
        {
            float dt = Time.unscaledDeltaTime * Mathf.Max(1f, Time.timeScale * 0.75f);

            switch (mode)
            {
                case Mode.Orbit:
                    goalYaw += dt * 5f;
                    break;
                case Mode.Overview:
                    goalDistance = OverviewDistance(goalYaw, goalPitch);
                    break;
                case Mode.Follow:
                    if (target != null)
                    {
                        // Look slightly past the token toward the board centre so the next tiles are visible.
                        Vector3 p = target.position;
                        Vector3 inward = -new Vector3(p.x, 0, p.z).normalized;
                        goalPivot = p + inward * 0.9f + Vector3.up * 0.2f;
                    }
                    break;
            }

            // In manual mode the camera holds the player's framing; otherwise it eases toward the director's.
            Vector3 wantPivot = IsManual ? manualPivot : goalPivot;
            float wantYaw = IsManual ? manualYaw : goalYaw;
            float wantDistance = IsManual ? manualDistance : goalDistance;
            float smooth = mode == Mode.Orbit ? smoothTime * 2f : smoothTime;
            pivot = Vector3.SmoothDamp(pivot, wantPivot, ref pivotVelocity, smooth * 0.6f, Mathf.Infinity, dt);
            yaw = Mathf.SmoothDampAngle(yaw, wantYaw, ref yawVelocity, smooth, Mathf.Infinity, dt);
            pitch = Mathf.SmoothDamp(pitch, goalPitch, ref pitchVelocity, smooth, Mathf.Infinity, dt);
            distance = Mathf.SmoothDamp(distance, wantDistance, ref distanceVelocity, smooth, Mathf.Infinity, dt);

            if (AllowManualControl) HandleGestures();
            else IsManual = false;
            Apply(pivot, yaw, pitch, distance);

            if (shake > 0.001f)
            {
                float t = Time.unscaledTime * 32f;
                transform.position += transform.right * (Mathf.PerlinNoise(t, 0f) - 0.5f) * shake
                                    + transform.up * (Mathf.PerlinNoise(0f, t) - 0.5f) * shake;
                shake = Mathf.MoveTowards(shake, 0f, dt * 1.5f);
            }

            PostFx.SetFocusDistance(distance);
        }

        private void Apply(Vector3 p, float y, float x, float d)
        {
            var rot = Quaternion.Euler(x, y, 0);
            transform.SetPositionAndRotation(p - rot * Vector3.forward * d, rot);
        }

        /// <summary>Smallest distance at which the whole board fits in the safe area (cached per yaw and screen).</summary>
        private float OverviewDistance(float atYaw, float atPitch)
        {
            float key = Mathf.Round(atYaw) * 100000f + Screen.width * 10f + Screen.height * 0.001f + atPitch;
            if (key == overviewCacheKey) return overviewCacheDistance;

            var saved = (transform.position, transform.rotation);
            float half = BoardView.HalfBoard + BoardView.Rim + 0.3f;
            var corners = new[]
            {
                new Vector3(-half, 0, -half), new Vector3(half, 0, -half), new Vector3(-half, 0, half), new Vector3(half, 0, half),
                new Vector3(-half, 0.8f, -half), new Vector3(half, 0.8f, -half), new Vector3(-half, 0.8f, half), new Vector3(half, 0.8f, half),
            };
            float lo = 4f, hi = 120f;
            for (int i = 0; i < 28; i++)
            {
                float mid = (lo + hi) / 2f;
                Apply(Vector3.zero, atYaw, atPitch, mid);
                bool fits = true;
                foreach (var c in corners)
                {
                    Vector3 v = cam.WorldToViewportPoint(c);
                    if (v.z <= 0 || !overviewSafeArea.Contains(new Vector2(v.x, v.y))) { fits = false; break; }
                }
                if (fits) hi = mid;
                else lo = mid;
            }
            transform.SetPositionAndRotation(saved.position, saved.rotation);
            overviewCacheKey = key;
            overviewCacheDistance = hi;
            return hi;
        }
    }
}
