using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Monopoly.Game
{
    /// <summary>
    /// Two physical dice. A throw is simulated with real physics first (bouncing off the board, houses, card
    /// decks and tokens), recorded, then replayed. The engine has already decided the result, so each die's
    /// visual is rotated by a cube symmetry so that whichever face physics landed on shows the right number.
    /// </summary>
    public sealed class DiceView : MonoBehaviour
    {
        private const float Size = 0.42f;
        private const int MaxSteps = 300;
        /// <summary>Replay faster than real time: a snappy throw reads better than a physically exact one.</summary>
        private const float ReplaySpeed = 1.35f;

        // Face normal (in die space) showing each value. Opposite faces sum to 7.
        private static readonly Vector3[] FaceNormals =
        {
            Vector3.zero, Vector3.up, Vector3.forward, Vector3.right, Vector3.left, Vector3.back, Vector3.down,
        };

        private sealed class Die
        {
            public Transform Body;
            public Transform Visual;
            public Rigidbody Rigidbody;
            public readonly List<Vector3> Positions = new List<Vector3>();
            public readonly List<Quaternion> Rotations = new List<Quaternion>();
            public readonly List<int> Impacts = new List<int>();
        }

        private readonly Die[] dice = new Die[2];
        private Coroutine hideRoutine;

        public static DiceView Create(Transform parent)
        {
            var view = new GameObject("Dice").AddComponent<DiceView>();
            view.transform.SetParent(parent, false);
            var lit = Resources.Load<Material>("Monopoly/Lit");
            var mat = new Material(lit != null ? lit : new Material(Shader.Find("Standard"))) { mainTexture = BuildAtlas(), color = Color.white };
            mat.SetFloat("_Glossiness", 0.55f);
            var mesh = BuildMesh();
            var bounce = new PhysicsMaterial("Die") { bounciness = 0.35f, dynamicFriction = 0.35f, staticFriction = 0.4f, bounceCombine = PhysicsMaterialCombine.Maximum };

            for (int i = 0; i < 2; i++)
            {
                var body = new GameObject($"Die {i + 1}");
                body.transform.SetParent(view.transform, false);
                body.transform.localScale = Vector3.one * Size;
                var collider = body.AddComponent<BoxCollider>();
                collider.sharedMaterial = bounce;
                var rb = body.AddComponent<Rigidbody>();
                rb.mass = 0.1f;
                rb.linearDamping = 0.05f;
                rb.angularDamping = 0.25f;
                rb.isKinematic = true;
                rb.interpolation = RigidbodyInterpolation.None;

                var visual = new GameObject("Visual");
                visual.transform.SetParent(body.transform, false);
                visual.AddComponent<MeshFilter>().sharedMesh = mesh;
                visual.AddComponent<MeshRenderer>().sharedMaterial = mat;

                body.SetActive(false);
                view.dice[i] = new Die { Body = body.transform, Visual = visual.transform, Rigidbody = rb };
            }
            return view;
        }

        /// <summary>Throws both dice from <paramref name="from"/> toward <paramref name="landing"/>, showing <paramref name="a"/> and <paramref name="b"/>.</summary>
        public IEnumerator Throw(int a, int b, Vector3 from, Vector3 landing, System.Action onImpact)
        {
            if (hideRoutine != null) StopCoroutine(hideRoutine);
            Sfx.Play(SfxKind.DiceShake);

            Simulate(from, landing);
            AlignFace(dice[0], a);
            AlignFace(dice[1], b);

            foreach (var d in dice)
            {
                d.Body.gameObject.SetActive(true);
                d.Body.localScale = Vector3.one * Size;
                d.Rigidbody.isKinematic = true;
            }

            // Replay, interpolating between recorded physics steps; the barely-moving tail is cut off.
            int frames = Mathf.Max(SettledFrame(dice[0]), SettledFrame(dice[1])) + 1;
            float step = Time.fixedDeltaTime;
            bool firstImpact = true;
            int lastFrame = -1;
            for (float t = 0; ; t += Time.deltaTime * ReplaySpeed)
            {
                float f = t / step;
                int i0 = Mathf.Min((int)f, frames - 1);
                if (i0 >= frames - 1) break;
                float k = f - i0;
                foreach (var d in dice)
                {
                    int a0 = Mathf.Min(i0, d.Positions.Count - 1), a1 = Mathf.Min(i0 + 1, d.Positions.Count - 1);
                    d.Body.position = Vector3.Lerp(d.Positions[a0], d.Positions[a1], k);
                    d.Body.rotation = Quaternion.Slerp(d.Rotations[a0], d.Rotations[a1], k);
                }
                for (int frame = lastFrame + 1; frame <= i0; frame++)
                    foreach (var d in dice)
                        if (d.Impacts.Contains(frame))
                        {
                            Sfx.Play(SfxKind.DiceHit, firstImpact ? 1f : 0.5f, Random.Range(0.9f, 1.15f));
                            if (firstImpact) onImpact?.Invoke();
                            firstImpact = false;
                        }
                lastFrame = i0;
                yield return null;
            }
            foreach (var d in dice)
            {
                d.Body.position = d.Positions[d.Positions.Count - 1];
                d.Body.rotation = d.Rotations[d.Rotations.Count - 1];
            }
        }

        /// <summary>First recorded frame after which the die no longer visibly moves.</summary>
        private static int SettledFrame(Die d)
        {
            int last = d.Positions.Count - 1;
            for (int i = last; i > 0; i--)
                if ((d.Positions[i] - d.Positions[last]).sqrMagnitude > 0.0004f || Quaternion.Angle(d.Rotations[i], d.Rotations[last]) > 1.5f)
                    return Mathf.Min(last, i + 4);
            return 0;
        }

        public void HideSoon(float delay = 2.5f)
        {
            if (hideRoutine != null) StopCoroutine(hideRoutine);
            hideRoutine = StartCoroutine(Hide(delay));
        }

        private IEnumerator Hide(float delay)
        {
            yield return new WaitForSeconds(delay);
            for (float t = 0; t < 1f; t += Time.deltaTime / 0.3f)
            {
                foreach (var d in dice) d.Body.localScale = Vector3.one * Size * (1f - t);
                yield return null;
            }
            foreach (var d in dice) d.Body.gameObject.SetActive(false);
        }

        // ---------------------------------------------------------------- physics

        /// <summary>Runs the throw through PhysX and records every step. Retries if the dice end up somewhere silly.</summary>
        private void Simulate(Vector3 from, Vector3 landing)
        {
            Physics.SyncTransforms();
            float inner = BoardView.HalfBoard - 0.3f;
            for (int attempt = 0; attempt < 8; attempt++)
            {
                Launch(from, landing, attempt);
                bool settled = Record();
                bool onBoard = true;
                foreach (var d in dice)
                {
                    Vector3 p = d.Positions[d.Positions.Count - 1];
                    onBoard &= Mathf.Abs(p.x) < inner && Mathf.Abs(p.z) < inner && p.y < 1f;
                }
                if (settled && onBoard) break;
            }
            foreach (var d in dice)
            {
                d.Rigidbody.isKinematic = true;
                d.Body.gameObject.SetActive(false);
            }
        }

        private void Launch(Vector3 from, Vector3 landing, int attempt)
        {
            const float flight = 0.55f;
            Vector3 dir = landing - from;
            dir.y = 0;
            Vector3 side = Vector3.Cross(Vector3.up, dir.normalized);
            for (int i = 0; i < 2; i++)
            {
                var d = dice[i];
                d.Positions.Clear();
                d.Rotations.Clear();
                d.Impacts.Clear();
                d.Body.gameObject.SetActive(true);
                d.Body.localScale = Vector3.one * Size;
                d.Visual.localRotation = Quaternion.identity;
                Vector3 start = transform.TransformPoint(from + side * (i == 0 ? -0.28f : 0.28f));
                Vector3 target = transform.TransformPoint(landing + side * (i == 0 ? -0.35f : 0.35f) + Random.insideUnitSphere * 0.25f * (1 + attempt * 0.2f));
                d.Body.SetPositionAndRotation(start, Random.rotationUniform);
                d.Rigidbody.isKinematic = false;
                Vector3 delta = target - start;
                Vector3 v = new Vector3(delta.x, 0, delta.z) / flight;
                v.y = (delta.y - 0.5f * Physics.gravity.y * flight * flight) / flight;
                d.Rigidbody.linearVelocity = v;
                d.Rigidbody.angularVelocity = Random.onUnitSphere * Random.Range(12f, 22f);
            }
        }

        /// <summary>Steps the physics scene and stores the dice transforms. Returns true once both have come to rest.</summary>
        private bool Record()
        {
            int still = 0;
            var lastVelocity = new Vector3[2];
            for (int stepIndex = 0; stepIndex < MaxSteps; stepIndex++)
            {
                for (int i = 0; i < 2; i++)
                {
                    var d = dice[i];
                    d.Positions.Add(d.Body.position);
                    d.Rotations.Add(d.Body.rotation);
                    lastVelocity[i] = d.Rigidbody.linearVelocity;
                }
                Physics.Simulate(Time.fixedDeltaTime);

                bool resting = true;
                for (int i = 0; i < 2; i++)
                {
                    var d = dice[i];
                    // A sharp change in velocity is a bounce: play a click there during replay.
                    if ((d.Rigidbody.linearVelocity - lastVelocity[i]).magnitude > 1.2f) d.Impacts.Add(stepIndex);
                    resting &= d.Rigidbody.linearVelocity.sqrMagnitude < 0.0025f && d.Rigidbody.angularVelocity.sqrMagnitude < 0.01f;
                }
                still = resting ? still + 1 : 0;
                if (still > 12)
                {
                    foreach (var d in dice)
                    {
                        d.Positions.Add(d.Body.position);
                        d.Rotations.Add(d.Body.rotation);
                    }
                    return true;
                }
            }
            return false;
        }

        /// <summary>Rotates the die's visual (a cube symmetry, so it still matches the collider) so <paramref name="value"/> ends on top.</summary>
        private static void AlignFace(Die d, int value)
        {
            Quaternion final = d.Rotations[d.Rotations.Count - 1];
            Vector3 upInBody = Quaternion.Inverse(final) * Vector3.up;
            Vector3 landed = FaceNormals[1];
            float best = -2f;
            for (int v = 1; v <= 6; v++)
            {
                float dot = Vector3.Dot(FaceNormals[v], upInBody);
                if (dot > best) { best = dot; landed = FaceNormals[v]; }
            }

            Vector3 wanted = FaceNormals[value];
            Quaternion fix;
            if (Vector3.Dot(wanted, landed) < -0.5f)
            {
                // Opposite faces: half turn about an axis perpendicular to both.
                Vector3 axis = Mathf.Abs(wanted.y) > 0.5f ? Vector3.right : Vector3.up;
                fix = Quaternion.AngleAxis(180f, axis);
            }
            else fix = Quaternion.FromToRotation(wanted, landed);
            d.Visual.localRotation = fix;
        }

        // ---------------------------------------------------------------- generated assets

        /// <summary>A 3x2 atlas with faces 1-6, white with rounded corners and dark pips.</summary>
        private static Texture2D BuildAtlas()
        {
            const int face = 128;
            var tex = new Texture2D(face * 3, face * 2, TextureFormat.RGBA32, true) { name = "Dice Atlas", wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[face * 3 * face * 2];
            Color32 body = new Color32(250, 248, 242, 255), edge = new Color32(214, 210, 200, 255), pip = new Color32(28, 28, 34, 255);
            int[][] layouts =
            {
                new[] { 4 }, new[] { 0, 8 }, new[] { 0, 4, 8 }, new[] { 0, 2, 6, 8 }, new[] { 0, 2, 4, 6, 8 }, new[] { 0, 2, 3, 5, 6, 8 },
            };
            for (int v = 0; v < 6; v++)
            {
                int ox = (v % 3) * face, oy = (v / 3) * face;
                for (int y = 0; y < face; y++)
                for (int x = 0; x < face; x++)
                {
                    float u = (x + 0.5f) / face, w = (y + 0.5f) / face;
                    float border = Mathf.Min(Mathf.Min(u, 1 - u), Mathf.Min(w, 1 - w));
                    Color32 c = border < 0.06f ? Color32.Lerp(edge, body, border / 0.06f) : body;
                    foreach (int p in layouts[v])
                    {
                        float cx = 0.25f + (p % 3) * 0.25f, cy = 0.25f + (p / 3) * 0.25f;
                        float dist = Vector2.Distance(new Vector2(u, w), new Vector2(cx, cy));
                        if (dist < 0.085f) c = Color32.Lerp(pip, c, Mathf.Clamp01((dist - 0.075f) / 0.01f));
                    }
                    px[(oy + y) * face * 3 + ox + x] = c;
                }
            }
            tex.SetPixels32(px);
            tex.Apply(true, true);
            return tex;
        }

        /// <summary>Unit cube whose faces map to atlas cells so that face N shows value N.</summary>
        private static Mesh BuildMesh()
        {
            var mesh = new Mesh { name = "Die" };
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            for (int value = 1; value <= 6; value++)
            {
                Vector3 n = FaceNormals[value];
                Vector3 up = Mathf.Abs(n.y) > 0.5f ? Vector3.forward : Vector3.up;
                Vector3 right = Vector3.Cross(up, n);
                Vector3 c = n * 0.5f;
                int v0 = verts.Count;
                verts.Add(c - right * 0.5f - up * 0.5f);
                verts.Add(c + right * 0.5f - up * 0.5f);
                verts.Add(c + right * 0.5f + up * 0.5f);
                verts.Add(c - right * 0.5f + up * 0.5f);
                int cell = value - 1;
                float u0 = (cell % 3) / 3f, v0f = (cell / 3) / 2f;
                uvs.Add(new Vector2(u0, v0f));
                uvs.Add(new Vector2(u0 + 1f / 3f, v0f));
                uvs.Add(new Vector2(u0 + 1f / 3f, v0f + 0.5f));
                uvs.Add(new Vector2(u0, v0f + 0.5f));
                tris.AddRange(new[] { v0, v0 + 1, v0 + 2, v0, v0 + 2, v0 + 3 });
            }
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
