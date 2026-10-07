using System.Collections.Generic;
using UnityEngine;

namespace Monopoly.Game
{
    /// <summary>
    /// Builds token models. The classic pieces are generated procedurally in polished metal; heroes come from
    /// the RPG Tiny Hero Duo pack. Every model faces +Z, stands on y = 0 and fits a ~0.5 unit footprint.
    /// </summary>
    public static class TokenModels
    {
        private static Material silver, darkMetal;

        public sealed class Result
        {
            public GameObject Root;
            public Animator Animator;
            public readonly List<Transform> Wheels = new List<Transform>();
        }

        public static Result Build(TokenDef def, Transform parent)
        {
            EnsureMaterials();
            var result = new Result { Root = new GameObject(def.Name) };
            result.Root.transform.SetParent(parent, false);
            var t = result.Root.transform;

            switch (def.Shape)
            {
                case TokenShape.TopHat: TopHat(t); break;
                case TokenShape.Thimble: Thimble(t); break;
                case TokenShape.Boot: Boot(t); break;
                case TokenShape.Iron: Iron(t); break;
                case TokenShape.RaceCar: RaceCar(t, result.Wheels); break;
                case TokenShape.Battleship: Battleship(t); break;
                case TokenShape.Wheelbarrow: Wheelbarrow(t, result.Wheels); break;
                case TokenShape.Duck: Duck(t); break;
                case TokenShape.Knight: result.Animator = Hero(t, female: false); break;
                case TokenShape.Valkyrie: result.Animator = Hero(t, female: true); break;
            }
            foreach (var r in result.Root.GetComponentsInChildren<Renderer>())
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                r.receiveShadows = true;
            }
            return result;
        }

        private static void EnsureMaterials()
        {
            if (silver != null) return;
            var lit = Resources.Load<Material>("Monopoly/Lit");
            var template = lit != null ? lit : new Material(Shader.Find("Standard"));
            silver = new Material(template) { name = "Token Silver", color = new Color(0.82f, 0.84f, 0.88f) };
            silver.SetFloat("_Metallic", 0.92f);
            silver.SetFloat("_Glossiness", 0.82f);
            darkMetal = new Material(template) { name = "Token Dark", color = new Color(0.22f, 0.23f, 0.26f) };
            darkMetal.SetFloat("_Metallic", 0.8f);
            darkMetal.SetFloat("_Glossiness", 0.6f);
        }

        // ---------------------------------------------------------------- building blocks

        private static Transform Part(Transform parent, Mesh mesh, Material mat, Vector3 pos, Vector3 euler = default, Vector3? scale = null)
        {
            var go = new GameObject(mesh.name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localEulerAngles = euler;
            go.transform.localScale = scale ?? Vector3.one;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            return go.transform;
        }

        private static Transform Prim(Transform parent, PrimitiveType type, Material mat, Vector3 pos, Vector3 scale, Vector3 euler = default)
        {
            var go = GameObject.CreatePrimitive(type);
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localEulerAngles = euler;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            return go.transform;
        }

        private static Vector2[] V(params float[] xy)
        {
            var pts = new Vector2[xy.Length / 2];
            for (int i = 0; i < pts.Length; i++) pts[i] = new Vector2(xy[i * 2], xy[i * 2 + 1]);
            return pts;
        }

        // ---------------------------------------------------------------- classic pieces

        private static void TopHat(Transform t)
        {
            Part(t, MeshKit.Lathe(V(0, 0, 0.29f, 0, 0.31f, 0.012f, 0.3f, 0.03f, 0.2f, 0.038f, 0.19f, 0.05f,
                                   0.2f, 0.4f, 0.215f, 0.44f, 0.21f, 0.46f, 0, 0.46f)), silver, Vector3.zero);
            Part(t, MeshKit.Lathe(V(0.198f, 0.06f, 0.206f, 0.065f, 0.207f, 0.125f, 0.199f, 0.13f)), darkMetal, Vector3.zero);
        }

        private static void Thimble(Transform t)
        {
            var profile = new List<Vector2> { new Vector2(0.17f, 0), new Vector2(0.185f, 0.015f), new Vector2(0.18f, 0.045f) };
            // Ridged sides like a real thimble.
            for (int i = 0; i < 6; i++)
            {
                float y = 0.06f + i * 0.045f;
                float r = Mathf.Lerp(0.172f, 0.15f, i / 6f);
                profile.Add(new Vector2(r, y));
                profile.Add(new Vector2(r - 0.006f, y + 0.022f));
            }
            profile.AddRange(V(0.14f, 0.34f, 0.12f, 0.4f, 0.07f, 0.44f, 0, 0.455f));
            Part(t, MeshKit.Lathe(profile), silver, Vector3.zero);
        }

        private static void Boot(Transform t)
        {
            // Side profile with the toe pointing along +X, extruded across the width, then turned to face +Z.
            var outline = V(-0.2f, 0.03f, 0.24f, 0.03f, 0.3f, 0.06f, 0.31f, 0.1f, 0.28f, 0.14f, 0.1f, 0.18f, 0.03f, 0.22f,
                            0.0f, 0.44f, 0.02f, 0.48f, -0.21f, 0.48f, -0.19f, 0.44f, -0.19f, 0.15f, -0.21f, 0.07f);
            var boot = new GameObject("Boot").transform;
            boot.SetParent(t, false);
            boot.localEulerAngles = new Vector3(0, -90, 0);
            Part(boot, MeshKit.Extrude(outline, 0.18f), silver, Vector3.zero);
            Prim(boot, PrimitiveType.Cube, darkMetal, new Vector3(0.06f, 0.015f, 0), new Vector3(0.5f, 0.03f, 0.19f));
            Prim(boot, PrimitiveType.Cube, darkMetal, new Vector3(-0.15f, 0.045f, 0), new Vector3(0.11f, 0.04f, 0.185f));
            for (int i = 0; i < 3; i++)
                Prim(boot, PrimitiveType.Cylinder, darkMetal, new Vector3(0.09f - i * 0.05f, 0.2f + i * 0.04f, 0),
                     new Vector3(0.015f, 0.095f, 0.015f), new Vector3(90, 0, 0));
        }

        private static void Iron(Transform t)
        {
            var sole = V(0, 0.36f, 0.12f, 0.2f, 0.19f, 0f, 0.2f, -0.18f, 0.17f, -0.22f, -0.17f, -0.22f, -0.2f, -0.18f, -0.19f, 0f, -0.12f, 0.2f);
            Part(t, MeshKit.Extrude(sole, 0.05f), darkMetal, new Vector3(0, 0.025f, 0), new Vector3(90, 0, 0));
            Part(t, MeshKit.Extrude(sole, 0.12f), silver, new Vector3(0, 0.11f, -0.02f), new Vector3(90, 0, 0), new Vector3(0.86f, 0.86f, 1f));
            Prim(t, PrimitiveType.Cube, silver, new Vector3(0, 0.31f, -0.03f), new Vector3(0.06f, 0.05f, 0.3f));
            Prim(t, PrimitiveType.Cube, silver, new Vector3(0, 0.24f, 0.09f), new Vector3(0.045f, 0.12f, 0.045f), new Vector3(-20, 0, 0));
            Prim(t, PrimitiveType.Cube, silver, new Vector3(0, 0.24f, -0.15f), new Vector3(0.045f, 0.12f, 0.045f));
        }

        private static void RaceCar(Transform t, List<Transform> wheels)
        {
            Prim(t, PrimitiveType.Cube, silver, new Vector3(0, 0.09f, -0.02f), new Vector3(0.2f, 0.08f, 0.44f));
            Prim(t, PrimitiveType.Cube, silver, new Vector3(0, 0.075f, 0.26f), new Vector3(0.11f, 0.05f, 0.2f));
            Prim(t, PrimitiveType.Cube, silver, new Vector3(0, 0.06f, 0.36f), new Vector3(0.28f, 0.015f, 0.06f));
            Prim(t, PrimitiveType.Sphere, silver, new Vector3(0, 0.14f, -0.04f), new Vector3(0.14f, 0.09f, 0.2f));
            Prim(t, PrimitiveType.Sphere, darkMetal, new Vector3(0, 0.19f, -0.06f), Vector3.one * 0.07f);
            Prim(t, PrimitiveType.Cube, silver, new Vector3(0, 0.21f, -0.25f), new Vector3(0.28f, 0.02f, 0.07f));
            Prim(t, PrimitiveType.Cube, silver, new Vector3(0.08f, 0.16f, -0.24f), new Vector3(0.02f, 0.08f, 0.03f));
            Prim(t, PrimitiveType.Cube, silver, new Vector3(-0.08f, 0.16f, -0.24f), new Vector3(0.02f, 0.08f, 0.03f));
            foreach (var p in new[] { new Vector3(0.15f, 0.065f, 0.17f), new Vector3(-0.15f, 0.065f, 0.17f),
                                      new Vector3(0.155f, 0.07f, -0.17f), new Vector3(-0.155f, 0.07f, -0.17f) })
                wheels.Add(Wheel(t, p, p.z < 0 ? 0.14f : 0.13f, 0.05f));
        }

        private static Transform Wheel(Transform parent, Vector3 pos, float diameter, float width)
        {
            var axle = new GameObject("Wheel").transform;
            axle.SetParent(parent, false);
            axle.localPosition = pos;
            Prim(axle, PrimitiveType.Cylinder, darkMetal, Vector3.zero, new Vector3(diameter, width / 2f, diameter), new Vector3(0, 0, 90));
            Prim(axle, PrimitiveType.Cube, silver, Vector3.zero, new Vector3(width * 1.05f, diameter * 0.25f, diameter * 0.7f));
            return axle;
        }

        private static void Battleship(Transform t)
        {
            var hull = V(0, 0.42f, 0.09f, 0.24f, 0.12f, 0f, 0.11f, -0.3f, 0.06f, -0.37f, -0.06f, -0.37f, -0.11f, -0.3f, -0.12f, 0f, -0.09f, 0.24f);
            Part(t, MeshKit.Extrude(hull, 0.1f), silver, new Vector3(0, 0.05f, 0), new Vector3(90, 0, 0));
            Prim(t, PrimitiveType.Cube, silver, new Vector3(0, 0.14f, -0.05f), new Vector3(0.14f, 0.08f, 0.24f));
            Prim(t, PrimitiveType.Cube, silver, new Vector3(0, 0.22f, -0.01f), new Vector3(0.1f, 0.08f, 0.1f));
            Prim(t, PrimitiveType.Cylinder, darkMetal, new Vector3(0, 0.27f, -0.1f), new Vector3(0.06f, 0.06f, 0.06f));
            Prim(t, PrimitiveType.Cylinder, silver, new Vector3(0, 0.33f, 0.0f), new Vector3(0.015f, 0.08f, 0.015f));
            foreach (float z in new[] { 0.2f, -0.26f })
            {
                Prim(t, PrimitiveType.Cylinder, silver, new Vector3(0, 0.12f, z), new Vector3(0.1f, 0.02f, 0.1f));
                float dir = z > 0 ? 1 : -1;
                for (int b = -1; b <= 1; b += 2)
                    Prim(t, PrimitiveType.Cylinder, darkMetal, new Vector3(b * 0.02f, 0.135f, z + dir * 0.08f),
                         new Vector3(0.018f, 0.06f, 0.018f), new Vector3(90, 0, 0));
            }
        }

        private static void Wheelbarrow(Transform t, List<Transform> wheels)
        {
            Part(t, MeshKit.Tray(new Vector2(0.18f, 0.24f), new Vector2(0.3f, 0.38f), 0.14f), silver, new Vector3(0, 0.12f, -0.02f), new Vector3(-6, 0, 0));
            wheels.Add(Wheel(t, new Vector3(0, 0.08f, 0.2f), 0.16f, 0.05f));
            for (int s = -1; s <= 1; s += 2)
            {
                Prim(t, PrimitiveType.Cube, silver, new Vector3(s * 0.11f, 0.16f, -0.1f), new Vector3(0.025f, 0.025f, 0.46f), new Vector3(-8, 0, 0));
                Prim(t, PrimitiveType.Cube, darkMetal, new Vector3(s * 0.09f, 0.06f, -0.12f), new Vector3(0.02f, 0.12f, 0.02f));
                Prim(t, PrimitiveType.Cube, darkMetal, new Vector3(s * 0.04f, 0.1f, 0.15f), new Vector3(0.015f, 0.08f, 0.015f), new Vector3(30, 0, 0));
            }
        }

        private static void Duck(Transform t)
        {
            Prim(t, PrimitiveType.Sphere, silver, new Vector3(0, 0.13f, -0.02f), new Vector3(0.3f, 0.22f, 0.38f));
            Prim(t, PrimitiveType.Sphere, silver, new Vector3(0, 0.32f, 0.09f), Vector3.one * 0.19f);
            Prim(t, PrimitiveType.Sphere, darkMetal, new Vector3(0, 0.31f, 0.2f), new Vector3(0.1f, 0.035f, 0.11f));
            Prim(t, PrimitiveType.Sphere, silver, new Vector3(0, 0.22f, -0.18f), new Vector3(0.1f, 0.12f, 0.1f));
            for (int s = -1; s <= 1; s += 2)
            {
                Prim(t, PrimitiveType.Sphere, darkMetal, new Vector3(s * 0.055f, 0.36f, 0.16f), Vector3.one * 0.03f);
                Prim(t, PrimitiveType.Sphere, silver, new Vector3(s * 0.14f, 0.15f, -0.03f), new Vector3(0.05f, 0.12f, 0.2f), new Vector3(-10, 0, 0));
            }
        }

        // ---------------------------------------------------------------- animated heroes

        private static Animator Hero(Transform t, bool female)
        {
            var assets = HeroAssets.Load();
            var prefab = assets != null ? (female ? assets.femaleHero : assets.maleHero) : null;
            if (prefab == null)
            {
                // Hero assets not generated yet: fall back to a classic piece so the game still works.
                TopHat(t);
                return null;
            }

            var hero = Object.Instantiate(prefab, t);
            hero.name = female ? "Valkyrie" : "Knight";
            foreach (var c in hero.GetComponentsInChildren<Collider>()) Object.Destroy(c);
            AttachWeapon(hero.transform, "weapon_r", female ? assets.femaleSword : assets.sword);
            AttachWeapon(hero.transform, "weapon_l", female ? assets.femaleShield : assets.shield);

            // Normalise height so heroes stand a little taller than the metal pieces.
            var renderers = hero.GetComponentsInChildren<Renderer>();
            Bounds b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);
            float scale = b.size.y > 0 ? 0.62f / b.size.y : 1f;
            hero.transform.localScale = Vector3.one * scale;
            hero.transform.localPosition = Vector3.zero;

            var animator = hero.GetComponent<Animator>();
            animator.runtimeAnimatorController = assets.controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            return animator;
        }

        private static void AttachWeapon(Transform hero, string boneName, GameObject weapon)
        {
            if (weapon == null) return;
            var bone = FindDeep(hero, boneName);
            if (bone == null) return;
            var w = Object.Instantiate(weapon, bone);
            w.transform.localPosition = Vector3.zero;
            w.transform.localRotation = Quaternion.identity;
            foreach (var c in w.GetComponentsInChildren<Collider>()) Object.Destroy(c);
        }

        private static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name) return root;
            foreach (Transform child in root)
            {
                var found = FindDeep(child, name);
                if (found != null) return found;
            }
            return null;
        }
    }
}
