using UnityEngine;
using UnityEngine.Rendering;

namespace Monopoly.Game
{
    /// <summary>
    /// The world around the board: sky, lighting, fog, a grassy ground with low-poly trees and rocks, and a
    /// reflection probe so the metal tokens have something to reflect.
    /// </summary>
    public static class Scenery
    {
        public static void Build(Transform parent)
        {
            var root = new GameObject("Scenery").transform;
            root.SetParent(parent, false);

            var lit = Resources.Load<Material>("Monopoly/Lit");
            var template = lit != null ? lit : new Material(Shader.Find("Standard"));
            Material Mat(Color c, float gloss = 0.1f)
            {
                var m = new Material(template) { color = c };
                m.SetFloat("_Glossiness", gloss);
                return m;
            }

            // Lighting.
            var sun = Object.FindFirstObjectByType<Light>();
            if (sun == null) sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.94f, 0.84f);
            sun.intensity = 1.25f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.75f;
            sun.shadowBias = 0.02f;
            sun.shadowNormalBias = 0.2f;
            sun.transform.rotation = Quaternion.Euler(52f, -38f, 0f);

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.72f, 0.9f);
            RenderSettings.ambientEquatorColor = new Color(0.55f, 0.58f, 0.6f);
            RenderSettings.ambientGroundColor = new Color(0.3f, 0.32f, 0.26f);
            var sky = Resources.Load<Material>("Monopoly/Sky");
            if (sky != null) RenderSettings.skybox = sky;
            RenderSettings.sun = sun;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(0.7f, 0.82f, 0.92f);
            RenderSettings.fogDensity = 0.012f;

            // Shadow and anti-aliasing quality come from GameSettings.
            QualitySettings.anisotropicFiltering = AnisotropicFiltering.ForceEnable;

            // Ground and plinth.
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ground.name = "Ground";
            Object.Destroy(ground.GetComponent<Collider>());
            ground.transform.SetParent(root, false);
            ground.transform.localPosition = new Vector3(0, -0.8f, 0);
            ground.transform.localScale = new Vector3(160f, 0.05f, 160f);
            ground.GetComponent<Renderer>().sharedMaterial = Mat(new Color32(118, 176, 92, 255));

            var plinth = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            plinth.name = "Plinth";
            Object.Destroy(plinth.GetComponent<Collider>());
            plinth.transform.SetParent(root, false);
            plinth.transform.localPosition = new Vector3(0, -0.76f, 0);
            plinth.transform.localScale = new Vector3(20f, 0.04f, 20f);
            plinth.GetComponent<Renderer>().sharedMaterial = Mat(new Color32(226, 210, 170, 255));

            // Low-poly trees and rocks in a ring around the board.
            var trunk = Mat(new Color32(120, 82, 52, 255));
            var leaves = new[] { Mat(new Color32(62, 140, 70, 255)), Mat(new Color32(84, 160, 64, 255)), Mat(new Color32(46, 118, 76, 255)) };
            var rock = Mat(new Color32(150, 150, 140, 255), 0.2f);
            var cone = MeshKit.Lathe(new[] { new Vector2(0.9f, 0), new Vector2(0.6f, 0.5f), new Vector2(0, 1.6f) }, 7);
            var rng = new System.Random(11);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);

            for (int i = 0; i < 70; i++)
            {
                float angle = R(0, Mathf.PI * 2);
                float radius = R(13f, 42f);
                var pos = new Vector3(Mathf.Cos(angle) * radius, -0.78f, Mathf.Sin(angle) * radius);
                if (rng.NextDouble() < 0.75)
                {
                    var tree = new GameObject("Tree").transform;
                    tree.SetParent(root, false);
                    tree.localPosition = pos;
                    tree.localScale = Vector3.one * R(0.9f, 1.8f);
                    tree.localRotation = Quaternion.Euler(0, R(0, 360), 0);
                    var t = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    Object.Destroy(t.GetComponent<Collider>());
                    t.transform.SetParent(tree, false);
                    t.transform.localPosition = new Vector3(0, 0.4f, 0);
                    t.transform.localScale = new Vector3(0.25f, 0.4f, 0.25f);
                    t.GetComponent<Renderer>().sharedMaterial = trunk;
                    var mat = leaves[rng.Next(leaves.Length)];
                    for (int layer = 0; layer < 2; layer++)
                    {
                        var foliage = new GameObject("Leaves");
                        foliage.transform.SetParent(tree, false);
                        foliage.transform.localPosition = new Vector3(0, 0.7f + layer * 0.7f, 0);
                        foliage.transform.localScale = Vector3.one * (1f - layer * 0.25f);
                        foliage.AddComponent<MeshFilter>().sharedMesh = cone;
                        foliage.AddComponent<MeshRenderer>().sharedMaterial = mat;
                    }
                }
                else
                {
                    var r = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    Object.Destroy(r.GetComponent<Collider>());
                    r.transform.SetParent(root, false);
                    r.transform.localPosition = pos;
                    r.transform.localScale = new Vector3(R(0.6f, 1.6f), R(0.4f, 0.9f), R(0.6f, 1.6f));
                    r.transform.localRotation = Quaternion.Euler(R(-10, 10), R(0, 360), 0);
                    r.GetComponent<Renderer>().sharedMaterial = rock;
                }
            }

            // Reflections for the metal tokens: render the surroundings once.
            var probe = new GameObject("Reflections").AddComponent<ReflectionProbe>();
            probe.transform.SetParent(root, false);
            probe.transform.localPosition = new Vector3(0, 1.5f, 0);
            probe.mode = ReflectionProbeMode.Realtime;
            probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
            probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.NoTimeSlicing;
            probe.size = new Vector3(120f, 40f, 120f);
            probe.resolution = 256;
            probe.hdr = true;
            probe.clearFlags = ReflectionProbeClearFlags.Skybox;
            probe.RenderProbe();
        }
    }
}
