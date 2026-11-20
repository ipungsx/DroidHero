using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using DroidHero.Core;

namespace DroidHero.EditorTools
{
    /// <summary>
    /// Configures the project for Android and produces the APK.
    ///
    /// Everything is applied through the PlayerSettings API rather than committed .asset YAML,
    /// so the build is reproducible from a clean checkout:
    ///   Unity -batchmode -quit -projectPath DroidHero \
    ///         -executeMethod DroidHero.EditorTools.BuildAndroid.BuildFromCommandLine
    /// </summary>
    public static class BuildAndroid
    {
        const string ScenePath = "Assets/Scenes/Main.unity";
        const string OutputPath = "Builds/DroidHero.apk";

        /// <summary>Fallback for CI runs that cannot pass -customBuildPath.</summary>
        const string OutputPathVariable = "DROIDHERO_APK_PATH";

        [MenuItem("Droid Hero/Create Game Scene")]
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

            var appObject = new GameObject("DroidHero");
            appObject.AddComponent<DroidHeroApp>();

            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.Refresh();

            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            Debug.Log("Created " + ScenePath);
        }

        [MenuItem("Droid Hero/Build Android APK")]
        public static void BuildFromMenu() => Build(BuildOptions.None, OutputPath);

        /// <summary>Entry point for -executeMethod in batch mode.</summary>
        public static void BuildFromCommandLine()
        {
            try
            {
                Build(BuildOptions.None, OutputPath);
                EditorApplication.Exit(0);
            }
            catch (Exception ex)
            {
                Debug.LogError("Build failed: " + ex);
                EditorApplication.Exit(1);
            }
        }

        /// <summary>
        /// Build entry point for game-ci, which supplies the output path and owns the editor
        /// lifecycle. Unlike BuildFromCommandLine this must not exit the editor: the caller
        /// still has to collect logs and the build report.
        /// </summary>
        public static void BuildForCi()
        {
            Build(BuildOptions.None, ResolveCiOutputPath());
        }

        /// <summary>
        /// game-ci passes the destination as -customBuildPath; the environment variable is a
        /// fallback so the workflow can also set it directly.
        /// </summary>
        static string ResolveCiOutputPath()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-customBuildPath" && !string.IsNullOrEmpty(args[i + 1]))
                {
                    string supplied = args[i + 1];
                    // game-ci supplies a directory plus file name; keep it if it names a file.
                    if (Path.GetExtension(supplied).Equals(".apk", StringComparison.OrdinalIgnoreCase))
                        return supplied;
                    return Path.Combine(supplied, Path.GetFileName(OutputPath));
                }
            }

            string fromEnvironment = Environment.GetEnvironmentVariable(OutputPathVariable);
            return string.IsNullOrEmpty(fromEnvironment) ? OutputPath : fromEnvironment;
        }

        static void Build(BuildOptions options, string outputPath)
        {
            // The scene is generated rather than committed, so a fresh checkout always builds
            // a playable player instead of an empty scene list.
            if (!File.Exists(ScenePath)) CreateGameScene();
            else EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };

            ApplyExternalTools();
            ApplyPlayerSettings();

            string directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = outputPath,
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

            Debug.Log("APK built: " + Path.GetFullPath(outputPath) +
                      " (" + (summary.totalSize / (1024f * 1024f)).ToString("F1") + " MB)");
        }

        /// <summary>
        /// Points the editor at the Android SDK, JDK and NDK when the environment supplies them.
        ///
        /// These normally live in the editor preferences, which are per-machine and absent in
        /// batch mode, so a headless build finds no JDK at all. The variables let CI (and these
        /// scratch machines) describe their toolchain without touching a GUI. Unset variables
        /// are left alone so an interactive editor keeps whatever it already has.
        /// </summary>
        static void ApplyExternalTools()
        {
            ApplyToolPath("ANDROID_SDK_ROOT", "AndroidSdkRoot");
            ApplyToolPath("JAVA_HOME", "JdkPath");
            ApplyToolPath("ANDROID_NDK_ROOT", "AndroidNdkRoot");
        }

        static void ApplyToolPath(string variable, string preference)
        {
            string path = Environment.GetEnvironmentVariable(variable);
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return;
            EditorPrefs.SetString(preference, path);
            Debug.Log("Using " + preference + " from " + variable + ": " + path);
        }

        static void ApplyPlayerSettings()
        {
            PlayerSettings.companyName = "Droid Hero";
            PlayerSettings.productName = "Droid Hero";
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, "com.droidhero.game");
            PlayerSettings.bundleVersion = "1.5.0";
            PlayerSettings.Android.bundleVersionCode = 9;

            // Portrait only, and OS autorotation off: the highway is a tall shape and the
            // menus are laid out top to bottom, so a landscape rotation has nowhere to put
            // anything. Locking it here keeps the manifest and the runtime in agreement.
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            PlayerSettings.allowedAutorotateToPortrait = true;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = false;
            PlayerSettings.allowedAutorotateToLandscapeRight = false;
            PlayerSettings.useOSAutorotation = false;

            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel24;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;

            // Mono ships 32-bit libraries only in Unity 6, so IL2CPP is the only way to
            // reach the 64-bit-only devices that most current phones are.
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARMv7 | AndroidArchitecture.ARM64;

            PlayerSettings.SetApiCompatibilityLevel(BuildTargetGroup.Android, ApiCompatibilityLevel.NET_Standard_2_0);

            PlayerSettings.Android.useCustomKeystore = false;
            PlayerSettings.Android.forceInternetPermission = true;
            PlayerSettings.Android.forceSDCardPermission = false;

            PlayerSettings.colorSpace = ColorSpace.Gamma;
            PlayerSettings.gpuSkinning = false;
            PlayerSettings.MTRendering = true;

            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            // Only ES 3 is offered: Unity 6 rejects the build outright when ES 2 is requested.
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[]
            {
                UnityEngine.Rendering.GraphicsDeviceType.OpenGLES3
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
