using System.Collections;
using UnityEngine;

namespace Monopoly.Game
{
    /// <summary>
    /// Two 3D dice thrown onto the board. The engine has already decided the result, so the throw is a
    /// scripted tumble that always comes to rest with the right faces up.
    /// </summary>
    public sealed class DiceView : MonoBehaviour
    {
        private const float Size = 0.42f;

        // Face normal (in die space) showing each value. Opposite faces sum to 7.
        private static readonly Vector3[] FaceNormals =
        {
            Vector3.zero, Vector3.up, Vector3.forward, Vector3.right, Vector3.left, Vector3.back, Vector3.down,
        };

        private readonly Transform[] dice = new Transform[2];
        private Coroutine hideRoutine;

        public static DiceView Create(Transform parent)
        {
            var view = new GameObject("Dice").AddComponent<DiceView>();
            view.transform.SetParent(parent, false);
            var lit = Resources.Load<Material>("Monopoly/Lit");
            var mat = new Material(lit != null ? lit : new Material(Shader.Find("Standard"))) { mainTexture = BuildAtlas(), color = Color.white };
            mat.SetFloat("_Glossiness", 0.55f);
            var mesh = BuildMesh();
            for (int i = 0; i < 2; i++)
            {
                var die = new GameObject($"Die {i + 1}");
                die.transform.SetParent(view.transform, false);
                die.transform.localScale = Vector3.one * Size;
                die.AddComponent<MeshFilter>().sharedMesh = mesh;
                die.AddComponent<MeshRenderer>().sharedMaterial = mat;
                die.SetActive(false);
                view.dice[i] = die.transform;
            }
            return view;
        }

        /// <summary>Throws both dice from <paramref name="from"/> to land around <paramref name="landing"/>.</summary>
        public IEnumerator Throw(int a, int b, Vector3 from, Vector3 landing, System.Action onImpact)
        {
            if (hideRoutine != null) StopCoroutine(hideRoutine);
            Vector3 dir = (landing - from);
            dir.y = 0;
            dir.Normalize();
            Vector3 side = Vector3.Cross(Vector3.up, dir);

            Sfx.Play(SfxKind.DiceShake);
            var r1 = StartCoroutine(Tumble(dice[0], a, from - side * 0.3f, landing - side * 0.38f + dir * 0.1f, dir, 0f, onImpact));
            var r2 = StartCoroutine(Tumble(dice[1], b, from + side * 0.3f, landing + side * 0.38f - dir * 0.1f, dir, 0.06f, null));
            yield return r1;
            yield return r2;
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
                foreach (var d in dice) d.localScale = Vector3.one * Size * (1f - t);
                yield return null;
            }
            foreach (var d in dice) d.gameObject.SetActive(false);
        }

        private IEnumerator Tumble(Transform die, int value, Vector3 start, Vector3 end, Vector3 dir, float delay, System.Action onImpact)
        {
            yield return new WaitForSeconds(delay);
            die.gameObject.SetActive(true);
            die.localScale = Vector3.one * Size;

            // Final pose: the value's face points up, with a random yaw.
            Quaternion final = Quaternion.AngleAxis(Random.Range(0f, 360f), Vector3.up) * Quaternion.FromToRotation(FaceNormals[value], Vector3.up);
            Vector3 axis = Vector3.Cross(Vector3.up, dir) + Random.insideUnitSphere * 0.4f;
            float spin = Random.Range(900f, 1300f);

            // Main arc then two shrinking bounces.
            Vector3 p1 = Vector3.Lerp(start, end, 0.72f), p2 = Vector3.Lerp(start, end, 0.92f);
            float half = Size / 2f;
            var arcs = new[]
            {
                (from: start, to: p1, height: 1.6f, time: 0.55f),
                (from: p1, to: p2, height: 0.35f, time: 0.22f),
                (from: p2, to: end, height: 0.1f, time: 0.14f),
            };
            float total = 0.91f, elapsed = 0f;
            for (int i = 0; i < arcs.Length; i++)
            {
                var arc = arcs[i];
                for (float t = 0; t < 1f; t += Time.deltaTime / arc.time)
                {
                    elapsed += Time.deltaTime;
                    float k = Mathf.Clamp01(elapsed / total);
                    Vector3 p = Vector3.Lerp(arc.from, arc.to, t) + Vector3.up * (half + Mathf.Sin(t * Mathf.PI) * arc.height);
                    die.localPosition = p;
                    // Spin unwinds to exactly the final pose as the throw ends.
                    float remaining = spin * (1f - k) * (1f - k);
                    die.localRotation = Quaternion.AngleAxis(remaining, axis) * final;
                    yield return null;
                }
                Sfx.Play(SfxKind.DiceHit, 1f - i * 0.3f, Random.Range(0.9f, 1.15f));
                if (i == 0) onImpact?.Invoke();
            }
            die.localPosition = end + Vector3.up * half;
            die.localRotation = final;
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
                        float d = Vector2.Distance(new Vector2(u, w), new Vector2(cx, cy));
                        if (d < 0.085f) c = Color32.Lerp(pip, c, Mathf.Clamp01((d - 0.075f) / 0.01f));
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
            var verts = new System.Collections.Generic.List<Vector3>();
            var uvs = new System.Collections.Generic.List<Vector2>();
            var tris = new System.Collections.Generic.List<int>();
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
