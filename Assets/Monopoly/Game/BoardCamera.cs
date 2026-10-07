using UnityEngine;

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
        private float overviewCacheKey = float.NaN;
        private float overviewCacheDistance;

        public float Distance => distance;

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

        public void Shake(float amount) => shake = Mathf.Max(shake, amount);

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

            float smooth = mode == Mode.Orbit ? smoothTime * 2f : smoothTime;
            pivot = Vector3.SmoothDamp(pivot, goalPivot, ref pivotVelocity, smooth * 0.6f, Mathf.Infinity, dt);
            yaw = Mathf.SmoothDampAngle(yaw, goalYaw, ref yawVelocity, smooth, Mathf.Infinity, dt);
            pitch = Mathf.SmoothDamp(pitch, goalPitch, ref pitchVelocity, smooth, Mathf.Infinity, dt);
            distance = Mathf.SmoothDamp(distance, goalDistance, ref distanceVelocity, smooth, Mathf.Infinity, dt);

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
