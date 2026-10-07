using System.IO;
using System.Linq;
using Monopoly.Game;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.PostProcessing;

namespace Monopoly.Editor
{
    /// <summary>
    /// One-click project setup: generates shared materials, the hero animator and asset references, the
    /// post-processing profile, and the game scene (registered in Build Settings). Everything else is built at
    /// runtime by <see cref="GameBootstrap"/>.
    /// </summary>
    public static class MonopolySetup
    {
        public const string ScenePath = "Assets/Scenes/Monopoly.unity";
        public const string BundleId = "com.arlind12344.monopoly";
        /// <summary>Arlind Ismalaja (Personal Team), arlind12344@gmail.com. Override with -teamId.</summary>
        private const string DefaultTeamId = "7F6CU26478";
        private const string ResourceFolder = "Assets/Resources/Monopoly";
        private const string SettingsFolder = "Assets/Monopoly/Settings";
        private const string HeroFolder = "Assets/RPG Tiny Hero Duo";
        private const string AnimFolder = HeroFolder + "/Animation/SwordAndShield";

        [MenuItem("Monopoly/Rebuild Game Scene")]
        public static void BuildScene()
        {
            Directory.CreateDirectory(ResourceFolder);
            Directory.CreateDirectory(SettingsFolder);
            CreateMaterials();
            CreateHeroAssets();
            var profile = CreatePostProfile();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var camGo = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener), typeof(BoardCamera), typeof(BlurCapture));
            camGo.tag = "MainCamera";
            var layer = camGo.AddComponent<PostProcessLayer>();
            layer.Init(AssetDatabase.LoadAssetAtPath<PostProcessResources>("Packages/com.unity.postprocessing/PostProcessing/PostProcessResources.asset"));
            layer.volumeTrigger = camGo.transform;
            layer.volumeLayer = ~0;
            layer.antialiasingMode = PostProcessLayer.Antialiasing.SubpixelMorphologicalAntialiasing;

            var volumeGo = new GameObject("Post FX");
            var volume = volumeGo.AddComponent<PostProcessVolume>();
            volume.isGlobal = true;
            volume.sharedProfile = profile;
            var postFx = volumeGo.AddComponent<PostFx>();
            var so = new SerializedObject(postFx);
            so.FindProperty("volume").objectReferenceValue = volume;
            so.ApplyModifiedPropertiesWithoutUndo();

            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(52f, -38f, 0f);

            new GameObject("Monopoly").AddComponent<GameBootstrap>();

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            Debug.Log($"Monopoly scene saved to {ScenePath}.");
        }

        // ---------------------------------------------------------------- materials

        private static void CreateMaterials()
        {
            CreateMaterial("Lit", "Standard", m => m.SetFloat("_Glossiness", 0.25f));
            CreateMaterial("Unlit", "Unlit/Texture", null);
            CreateMaterial("Glow", "Sprites/Default", null);
            CreateMaterial("Blur", "Hidden/Monopoly/Blur", null);
            CreateMaterial("Particle", "Particles/Standard Unlit", m =>
            {
                // Fade mode so particles can fade out over their lifetime.
                m.SetFloat("_Mode", 2);
                m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_ZWrite", 0);
                m.EnableKeyword("_ALPHABLEND_ON");
                m.renderQueue = 3000;
            });
            CreateMaterial("Sky", "Skybox/Procedural", m =>
            {
                m.SetFloat("_SunSize", 0.045f);
                m.SetFloat("_AtmosphereThickness", 0.75f);
                m.SetColor("_SkyTint", new Color(0.45f, 0.62f, 0.9f));
                m.SetColor("_GroundColor", new Color(0.42f, 0.58f, 0.36f));
                m.SetFloat("_Exposure", 1.3f);
            });
            AssetDatabase.SaveAssets();
        }

        private static void CreateMaterial(string name, string shader, System.Action<Material> configure)
        {
            string path = $"{ResourceFolder}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool isNew = mat == null;
            if (isNew) mat = new Material(Shader.Find(shader));
            else mat.shader = Shader.Find(shader);
            configure?.Invoke(mat);
            if (isNew) AssetDatabase.CreateAsset(mat, path);
            else EditorUtility.SetDirty(mat);
        }

        // ---------------------------------------------------------------- heroes

        private static void CreateHeroAssets()
        {
            string controllerPath = $"{ResourceFolder}/TokenAnimator.controller";
            AssetDatabase.DeleteAsset(controllerPath);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            var sm = controller.layers[0].stateMachine;

            AnimatorState State(string name, string clipFile, float speed = 1f)
            {
                var state = sm.AddState(name);
                state.motion = AssetDatabase.LoadAllAssetsAtPath($"{AnimFolder}/{clipFile}.fbx")
                                            .OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview"));
                state.speed = speed;
                if (state.motion == null) Debug.LogWarning($"Missing animation clip {clipFile}");
                return state;
            }

            var idle = State("Idle", "Idle_Normal_SwordAndShield");
            sm.defaultState = idle;
            State("Run", "InPlace/SprintFWD_Battle_InPlace_SwordAndShield", 1.2f);
            foreach (var (name, clip) in new[]
                     {
                         ("Victory", "Victory_Battle_SwordAndShield"), ("LevelUp", "LevelUp_Battle_SwordAndShield"),
                         ("Dizzy", "Dizzy_SwordAndShield"), ("Hit", "GetHit01_SwordAndShield"),
                     })
            {
                var oneShot = State(name, clip);
                var back = oneShot.AddTransition(idle);
                back.hasExitTime = true;
                back.exitTime = 0.92f;
                back.duration = 0.2f;
            }
            var die = State("Die", "Die01_SwordAndShield");
            var dead = State("Dead", "Die01_Stay_SwordAndShield");
            var toDead = die.AddTransition(dead);
            toDead.hasExitTime = true;
            toDead.exitTime = 0.95f;
            toDead.duration = 0.1f;

            string heroPath = $"{ResourceFolder}/HeroAssets.asset";
            var assets = AssetDatabase.LoadAssetAtPath<HeroAssets>(heroPath);
            if (assets == null)
            {
                assets = ScriptableObject.CreateInstance<HeroAssets>();
                AssetDatabase.CreateAsset(assets, heroPath);
            }
            GameObject Prefab(string name) => AssetDatabase.LoadAssetAtPath<GameObject>($"{HeroFolder}/Prefab/{name}.prefab");
            assets.maleHero = Prefab("MaleCharacterPolyart");
            assets.femaleHero = Prefab("FemaleCharacterPolyart");
            assets.sword = Prefab("OHS03Polyart");
            assets.shield = Prefab("Shield05Polyart");
            assets.femaleSword = Prefab("OHS06Polyart");
            assets.femaleShield = Prefab("Shield08Polyart");
            assets.controller = controller;
            EditorUtility.SetDirty(assets);
            AssetDatabase.SaveAssets();
        }

        // ---------------------------------------------------------------- post-processing

        private static PostProcessProfile CreatePostProfile()
        {
            string path = $"{SettingsFolder}/PostFxProfile.asset";
            AssetDatabase.DeleteAsset(path);
            var profile = ScriptableObject.CreateInstance<PostProcessProfile>();
            AssetDatabase.CreateAsset(profile, path);

            T Add<T>() where T : PostProcessEffectSettings
            {
                var s = profile.AddSettings<T>();
                s.enabled.Override(true);
                s.name = typeof(T).Name;
                AssetDatabase.AddObjectToAsset(s, profile);
                return s;
            }

            var bloom = Add<Bloom>();
            bloom.intensity.Override(1.4f);
            bloom.threshold.Override(1.05f);
            bloom.softKnee.Override(0.6f);
            bloom.diffusion.Override(7f);

            var grading = Add<ColorGrading>();
            grading.tonemapper.Override(Tonemapper.Neutral);
            grading.postExposure.Override(0.15f);
            grading.saturation.Override(14f);
            grading.contrast.Override(12f);
            grading.temperature.Override(4f);

            var vignette = Add<Vignette>();
            vignette.intensity.Override(0.3f);
            vignette.smoothness.Override(0.45f);

            var ao = Add<AmbientOcclusion>();
            ao.mode.Override(AmbientOcclusionMode.MultiScaleVolumetricObscurance);
            ao.intensity.Override(0.6f);

            var dof = Add<DepthOfField>();
            dof.focusDistance.Override(7f);
            dof.aperture.Override(8f);
            dof.focalLength.Override(42f);
            dof.kernelSize.Override(KernelSize.Medium);

            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return profile;
        }

        // ---------------------------------------------------------------- builds

        /// <summary>Command line: -executeMethod Monopoly.Editor.MonopolySetup.BuildMac -buildPath &lt;path.app&gt;</summary>
        public static void BuildMac() => Build(BuildTarget.StandaloneOSX, "Builds/Monopoly.app");

        /// <summary>
        /// Generates the Xcode project. Command line:
        /// -buildTarget iOS -executeMethod Monopoly.Editor.MonopolySetup.BuildIOS -buildPath &lt;folder&gt; [-teamId &lt;id&gt;]
        /// </summary>
        [MenuItem("Monopoly/Build iOS (Xcode project)")]
        public static void BuildIOS()
        {
            ConfigureMobile();
            Build(BuildTarget.iOS, "Builds/iOS");
        }

        /// <summary>
        /// Builds an installable APK (debug-signed, for sideloading). Command line:
        /// -buildTarget Android -executeMethod Monopoly.Editor.MonopolySetup.BuildAndroid -buildPath &lt;file.apk&gt;
        /// </summary>
        [MenuItem("Monopoly/Build Android (APK)")]
        public static void BuildAndroid()
        {
            ConfigureMobile();
            var android = NamedBuildTarget.Android;
            PlayerSettings.SetApplicationIdentifier(android, BundleId);
            PlayerSettings.Android.bundleVersionCode = 1;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
            PlayerSettings.SetScriptingBackend(android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.SetManagedStrippingLevel(android, ManagedStrippingLevel.Minimal);
            PlayerSettings.Android.forceInternetPermission = true;
            EditorUserBuildSettings.buildAppBundle = false;
            AssetDatabase.SaveAssets();
            Build(BuildTarget.Android, "Builds/Monopoly.apk");
        }

        /// <summary>
        /// Builds the browser version into a folder you can upload to any static host. Command line:
        /// -buildTarget WebGL -executeMethod Monopoly.Editor.MonopolySetup.BuildWebGL -buildPath &lt;folder&gt;
        /// </summary>
        [MenuItem("Monopoly/Build Web (WebGL)")]
        public static void BuildWebGL()
        {
            var web = NamedBuildTarget.WebGL;
            PlayerSettings.companyName = "Arlind Ismalaja";
            PlayerSettings.productName = "Monopoly";
            PlayerSettings.WebGL.template = "PROJECT:Monopoly";
            // Gzip plus the JavaScript fallback works on hosts that don't send Content-Encoding headers.
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
            PlayerSettings.WebGL.decompressionFallback = true;
            PlayerSettings.WebGL.dataCaching = true;
            PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly;
            PlayerSettings.SetManagedStrippingLevel(web, ManagedStrippingLevel.Minimal);
            PlayerSettings.runInBackground = true;
            AssetDatabase.SaveAssets();
            Build(BuildTarget.WebGL, "Builds/Web");
        }

        /// <summary>Player settings for phones: landscape only, readable on small screens, signed for your team.</summary>
        public static void ConfigureMobile()
        {
            var ios = NamedBuildTarget.iOS;
            PlayerSettings.companyName = "Arlind Ismalaja";
            PlayerSettings.productName = "Monopoly";
            PlayerSettings.SetApplicationIdentifier(ios, BundleId);
            PlayerSettings.bundleVersion = "1.0";
            PlayerSettings.iOS.buildNumber = "1";
            PlayerSettings.iOS.targetOSVersionString = "15.0";
            PlayerSettings.iOS.targetDevice = iOSTargetDevice.iPhoneAndiPad;
            PlayerSettings.iOS.appleEnableAutomaticSigning = true;
            PlayerSettings.iOS.appleDeveloperTeamID = Arg("-teamId") ?? DefaultTeamId;
            PlayerSettings.SetManagedStrippingLevel(ios, ManagedStrippingLevel.Minimal);
            PlayerSettings.statusBarHidden = true;

            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            AssetDatabase.SaveAssets();
        }

        private static string Arg(string name)
        {
            string[] args = System.Environment.GetCommandLineArgs();
            int i = System.Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        private static void Build(BuildTarget target, string defaultOutput)
        {
            if (!File.Exists(ScenePath)) BuildScene();
            string output = Arg("-buildPath") ?? defaultOutput;

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = output,
                target = target,
                options = BuildOptions.None,
            });
            Debug.Log($"Build {report.summary.result}: {output} ({report.summary.totalErrors} errors)");
            if (report.summary.result != BuildResult.Succeeded) EditorApplication.Exit(1);
        }
    }
}
