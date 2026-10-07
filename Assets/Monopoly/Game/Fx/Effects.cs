using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Monopoly.Game
{
    /// <summary>Board effects: confetti, flying and raining coins, sparkles and tile glows.</summary>
    public sealed class Effects : MonoBehaviour
    {
        private Material particleMaterial;
        private Material coinMaterial;
        private Material glowMaterial;
        private Mesh coinMesh;
        private readonly Stack<Transform> coinPool = new Stack<Transform>();

        public static Effects Create(Transform parent)
        {
            var fx = new GameObject("Effects").AddComponent<Effects>();
            fx.transform.SetParent(parent, false);

            var particle = Resources.Load<Material>("Monopoly/Particle");
            fx.particleMaterial = particle != null ? particle : new Material(Shader.Find("Sprites/Default"));
            var glow = Resources.Load<Material>("Monopoly/Glow");
            fx.glowMaterial = new Material(glow != null ? glow : fx.particleMaterial) { mainTexture = SoftRect(64) };

            var lit = Resources.Load<Material>("Monopoly/Lit");
            fx.coinMaterial = new Material(lit != null ? lit : new Material(Shader.Find("Standard"))) { color = new Color(1f, 0.78f, 0.25f) };
            fx.coinMaterial.SetFloat("_Metallic", 0.9f);
            fx.coinMaterial.SetFloat("_Glossiness", 0.8f);
            fx.coinMaterial.EnableKeyword("_EMISSION");
            fx.coinMaterial.SetColor("_EmissionColor", new Color(0.35f, 0.22f, 0.02f));

            var tmp = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            fx.coinMesh = tmp.GetComponent<MeshFilter>().sharedMesh;
            Destroy(tmp);
            return fx;
        }

        // ---------------------------------------------------------------- particles

        public void Confetti(Vector3 position, int count = 90)
        {
            var ps = NewSystem("Confetti", position, 3f);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.6f, 2.4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(3f, 6f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.11f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
            main.gravityModifier = 0.9f;
            var palette = new Gradient();
            palette.SetKeys(new[]
            {
                new GradientColorKey(new Color(1f, 0.3f, 0.35f), 0f), new GradientColorKey(new Color(1f, 0.85f, 0.2f), 0.25f),
                new GradientColorKey(new Color(0.3f, 0.85f, 0.45f), 0.5f), new GradientColorKey(new Color(0.3f, 0.6f, 1f), 0.75f),
                new GradientColorKey(new Color(0.8f, 0.4f, 1f), 1f),
            }, new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, 1) });
            main.startColor = new ParticleSystem.MinMaxGradient(palette) { mode = ParticleSystemGradientMode.RandomColor };

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 28f;
            shape.radius = 0.15f;
            shape.rotation = new Vector3(-90, 0, 0);

            var rot = ps.rotationOverLifetime;
            rot.enabled = true;
            rot.z = new ParticleSystem.MinMaxCurve(-8f, 8f);
            var drag = ps.limitVelocityOverLifetime;
            drag.enabled = true;
            drag.drag = 1.6f;
            FadeOut(ps);
            ps.Emit(count);
        }

        public void Sparkle(Vector3 position, Color color, int count = 30)
        {
            var ps = NewSystem("Sparkle", position, 1.5f);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.9f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1f, 2.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.09f);
            main.startColor = Color.Lerp(color, Color.white, 0.4f);
            main.gravityModifier = -0.1f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.25f;
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0, 1, 1, 0));
            ps.Emit(count);
        }

        private ParticleSystem NewSystem(string name, Vector3 position, float lifetime)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = position;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.maxParticles = 300;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var emission = ps.emission;
            emission.rateOverTime = 0;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = particleMaterial;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            Destroy(go, lifetime);
            return ps;
        }

        private static void FadeOut(ParticleSystem ps)
        {
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                      new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, 0.75f), new GradientAlphaKey(0, 1) });
            col.color = g;
        }

        // ---------------------------------------------------------------- coins

        /// <summary>Coins arc from one point to another (rent, payments).</summary>
        public IEnumerator CoinStream(Vector3 from, Vector3 to, int coins = 8)
        {
            for (int i = 0; i < coins; i++)
            {
                StartCoroutine(FlyCoin(from + Random.insideUnitSphere * 0.1f, to, 0.55f));
                yield return new WaitForSeconds(0.05f);
            }
            yield return new WaitForSeconds(0.55f);
        }

        /// <summary>Coins drop from above and spin into a spot (passing GO, collecting money).</summary>
        public IEnumerator CoinRain(Vector3 target, int coins = 10)
        {
            for (int i = 0; i < coins; i++)
            {
                Vector3 start = target + new Vector3(Random.Range(-0.5f, 0.5f), 2.5f + Random.value, Random.Range(-0.5f, 0.5f));
                StartCoroutine(FlyCoin(start, target + Vector3.up * 0.2f, 0.5f, arc: 0.2f));
                yield return new WaitForSeconds(0.04f);
            }
            yield return new WaitForSeconds(0.5f);
        }

        private IEnumerator FlyCoin(Vector3 from, Vector3 to, float seconds, float arc = 1.2f)
        {
            var coin = coinPool.Count > 0 ? coinPool.Pop() : NewCoin();
            coin.gameObject.SetActive(true);
            Vector3 spinAxis = Random.onUnitSphere;
            for (float t = 0; t < 1f; t += Time.deltaTime / seconds)
            {
                coin.localPosition = Vector3.Lerp(from, to, t) + Vector3.up * (Mathf.Sin(t * Mathf.PI) * arc);
                coin.Rotate(spinAxis, 900f * Time.deltaTime, Space.World);
                coin.localScale = new Vector3(0.16f, 0.012f, 0.16f) * Mathf.Min(1f, (1f - t) * 6f + 0.2f);
                yield return null;
            }
            Sfx.Play(SfxKind.Coin, 0.35f, Random.Range(0.95f, 1.15f));
            coin.gameObject.SetActive(false);
            coinPool.Push(coin);
        }

        private Transform NewCoin()
        {
            var go = new GameObject("Coin");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = coinMesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = coinMaterial;
            return go.transform;
        }

        // ---------------------------------------------------------------- tile glow

        /// <summary>White rectangle that fades out towards its edges.</summary>
        private static Texture2D SoftRect(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size, v = (y + 0.5f) / size;
                float edge = Mathf.Min(Mathf.Min(u, 1 - u), Mathf.Min(v, 1 - v));
                tex.SetPixel(x, y, new Color(1, 1, 1, Mathf.SmoothStep(0, 1, edge / 0.3f)));
            }
            tex.Apply();
            return tex;
        }

        /// <summary>A soft pulse of light on a tile when a token lands there.</summary>
        public void TileGlow(Vector3 center, Quaternion rotation, Vector2 size, Color color)
        {
            StartCoroutine(Glow(center, rotation, size, color));
        }

        private IEnumerator Glow(Vector3 center, Quaternion rotation, Vector2 size, Color color)
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(quad.GetComponent<Collider>());
            quad.transform.SetParent(transform, false);
            quad.transform.localPosition = center + Vector3.up * 0.08f;
            quad.transform.localRotation = rotation * Quaternion.Euler(90, 0, 0);
            quad.transform.localScale = new Vector3(size.x * 1.15f, size.y * 1.1f, 1);
            var mat = new Material(glowMaterial);
            var r = quad.GetComponent<Renderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            for (float t = 0; t < 1f; t += Time.deltaTime / 1.1f)
            {
                float a = Mathf.Sin(t * Mathf.PI) * 0.9f;
                mat.color = new Color(color.r, color.g, color.b, a);
                yield return null;
            }
            Destroy(quad);
            Destroy(mat);
        }
    }
}
