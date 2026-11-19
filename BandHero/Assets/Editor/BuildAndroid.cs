using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using BandHero.Core;

namespace BandHero.EditorTools
{
    /// <summary>
    /// Configures the project for Android and produces the APK.
    ///
    /// Everything is applied through the PlayerSettings API rather than committed .asset YAML,
    /// so the build is reproducible from a clean checkout:
    ///   Unity -batchmode -quit -projectPath BandHero \
    ///         -executeMethod BandHero.EditorTools.BuildAndroid.BuildFromCommandLine
    /// </summary>
    public static class BuildAndroid
    {
        const string ScenePath = "Assets/Scenes/Main.unity";
        const string OutputPath = "Builds/BandHero.apk";

        [MenuItem("Band Hero/Create Game Scene")]
        public static void CreateGameScene()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath) ?? "Assets/Scenes");

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 6.4f;
            camera.transform.position = new Vector3(0f, 0f, -10f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.05f, 0.05f, 0.08f);
            cameraObject.AddComponent<AudioListener>();

            var appObject = new GameObject("BandHero");
            appObject.AddComponent<BandHeroApp>();

            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.Refresh();

            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            Debug.Log("Created " + ScenePath);
        }

        [MenuItem("Band Hero/Build Android APK")]
        public static void BuildFromMenu() => Build(BuildOptions.None);

        /// <summary>Entry point for -executeMethod in batch mode.</summary>
        public static void BuildFromCommandLine()
        {
            try
            {
                Build(BuildOptions.None);
                EditorApplication.Exit(0);
            }
            catch (Exception ex)
            {
                Debug.LogError("Build failed: " + ex);
                EditorApplication.Exit(1);
            }
        }

        static void Build(BuildOptions options)
        {
            if (!File.Exists(ScenePath)) CreateGameScene();
            else EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };

            ApplyPlayerSettings();

            Directory.CreateDirectory("Builds");

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = OutputPath,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = options
            });

            BuildSummary summary = report.summary;

            if (summary.result != BuildResult.Succeeded)
            {
                throw new Exception("Android build failed: " + summary.result +
                                    " (" + summary.totalErrors + " errors)");
            }

            Debug.Log("APK built: " + Path.GetFullPath(OutputPath) +
                      " (" + (summary.totalSize / (1024f * 1024f)).ToString("F1") + " MB)");
        }

        static void ApplyPlayerSettings()
        {
            PlayerSettings.companyName = "Band Hero";
            PlayerSettings.productName = "Band Hero";
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, "com.bandhero.game");
            PlayerSettings.bundleVersion = "1.0.0";
            PlayerSettings.Android.bundleVersionCode = 1;

            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            PlayerSettings.allowedAutorotateToPortrait = true;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = false;
            PlayerSettings.allowedAutorotateToLandscapeRight = false;

            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel24;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;

            // Mono with both ABIs keeps the build fast and runs on every current device.
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.Mono2x);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARMv7 | AndroidArchitecture.ARM64;

            PlayerSettings.SetApiCompatibilityLevel(BuildTargetGroup.Android, ApiCompatibilityLevel.NET_Standard_2_0);

            PlayerSettings.Android.useCustomKeystore = false;
            PlayerSettings.Android.forceInternetPermission = true;
            PlayerSettings.Android.forceSDCardPermission = false;

            PlayerSettings.colorSpace = ColorSpace.Gamma;
            PlayerSettings.gpuSkinning = false;
            PlayerSettings.MTRendering = true;

            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[]
            {
                UnityEngine.Rendering.GraphicsDeviceType.OpenGLES3,
                UnityEngine.Rendering.GraphicsDeviceType.OpenGLES2
            });

            ApplyIcon();
        }

        static void ApplyIcon()
        {
            const string iconPath = "Assets/Art/app_icon.png";
            if (!File.Exists(iconPath)) return;

            var importer = AssetImporter.GetAtPath(iconPath) as TextureImporter;
            if (importer == null) return;

            importer.textureType = TextureImporterType.Default;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.SaveAndReimport();

            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(iconPath);
            if (icon != null) PlayerSettings.SetIconsForTargetGroup(BuildTargetGroup.Android, new[] { icon });
        }
    }
}
