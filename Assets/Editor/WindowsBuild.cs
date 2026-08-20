using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UmaDesktopPet.Standalone.Runtime;

namespace UmaDesktopPet.Standalone.Editor
{
    public static class WindowsBuild
    {
        private const string GeneratedDirectory = "Assets/Generated";
        private const string ScenePath = GeneratedDirectory + "/Bootstrap.unity";
        private const string RendererPath = GeneratedDirectory + "/PetRenderer.asset";
        private const string PipelinePath = GeneratedDirectory + "/PetPipeline.asset";
        private const string RecordingToolsDefine = "UMA_RECORDING_TOOLS";
        private const string RecordingOnlyMarker =
            "RECORDING_ONLY_DO_NOT_SHIP.txt";
        private const string RecordingLauncher =
            "Launch-Recording-Tools.cmd";

        public static void Build()
        {
            BuildPlayer(BuildOptions.StrictMode, null, false);
        }

        public static void BuildDevelopment()
        {
            BuildPlayer(
                BuildOptions.StrictMode | BuildOptions.Development,
                null,
                false);
        }

        public static void BuildRecording()
        {
            BuildPlayer(
                BuildOptions.StrictMode,
                new[] { RecordingToolsDefine },
                true);
        }

        private static void BuildPlayer(
            BuildOptions buildOptions,
            string[] extraScriptingDefines,
            bool recordingOnly)
        {
            string outputPath = ReadArgument("-umaOutputPath");
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                throw new ArgumentException("Missing required -umaOutputPath argument.");
            }

            outputPath = Path.GetFullPath(outputPath);
            string outputDirectory = Path.GetDirectoryName(outputPath);
            Directory.CreateDirectory(outputDirectory);
            if (recordingOnly)
            {
                WriteRecordingOnlyMarker(outputDirectory);
            }
            EnsureRecordingDefineIsNotGlobal();
            ConfigurePlayer();
            ConfigureRenderPipeline();
            CreateBootstrapScene();

            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = outputPath,
                target = BuildTarget.StandaloneWindows64,
                options = buildOptions,
                extraScriptingDefines = extraScriptingDefines ??
                    new string[0]
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new InvalidOperationException(
                    "Windows player build failed: " + report.summary.result +
                    " (" + report.summary.totalErrors + " errors, " +
                    report.summary.totalWarnings + " warnings).");
            }

            CopySupportFiles(outputDirectory);
            if (recordingOnly)
            {
                File.WriteAllText(
                    Path.Combine(outputDirectory, RecordingLauncher),
                    "@echo off\r\n" +
                    "setlocal\r\n" +
                    "start \"\" /D \"%~dp0\" \"%~dp0UmaDesktopPet.exe\" " +
                    "--recording-tools\r\n");
            }
            else
            {
                DeleteIfPresent(Path.Combine(outputDirectory, RecordingOnlyMarker));
                DeleteIfPresent(Path.Combine(outputDirectory, RecordingLauncher));
            }
            Debug.Log("Built standalone local prototype: " + outputPath);
        }

        private static void WriteRecordingOnlyMarker(string outputDirectory)
        {
            File.WriteAllText(
                Path.Combine(outputDirectory, RecordingOnlyMarker),
                "This local player contains temporary recording controls.\r\n" +
                "Do not package or publish this folder.\r\n");
        }

        private static void DeleteIfPresent(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        private static void EnsureRecordingDefineIsNotGlobal()
        {
            string defines = PlayerSettings.GetScriptingDefineSymbolsForGroup(
                BuildTargetGroup.Standalone);
            string[] values = (defines ?? string.Empty).Split(';');
            if (values.Any(value => string.Equals(
                value.Trim(),
                RecordingToolsDefine,
                StringComparison.Ordinal)))
            {
                throw new InvalidOperationException(
                    RecordingToolsDefine +
                    " must be supplied only through BuildPlayerOptions." +
                    " Remove it from the global Standalone defines.");
            }
        }

        private static void ConfigurePlayer()
        {
            PlayerSettings.companyName = "pqqqqqdev";
            PlayerSettings.productName = "Uma Desktop Pet";
            PlayerSettings.bundleVersion = "0.1.0";
            PlayerSettings.defaultScreenWidth =
                DesktopWindowController.NativeWindowWidth;
            PlayerSettings.defaultScreenHeight =
                DesktopWindowController.NativeWindowHeight;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.allowFullscreenSwitch = false;
            PlayerSettings.resizableWindow = false;
            PlayerSettings.runInBackground = true;
            PlayerSettings.useFlipModelSwapchain = false;
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.SetScriptingBackend(
                BuildTargetGroup.Standalone,
                ScriptingImplementation.Mono2x);
            PlayerSettings.SetApiCompatibilityLevel(
                BuildTargetGroup.Standalone,
                ApiCompatibilityLevel.NET_Standard_2_0);
            PlayerSettings.SetManagedStrippingLevel(
                BuildTargetGroup.Standalone,
                ManagedStrippingLevel.Low);
            PlayerSettings.SetGraphicsAPIs(
                BuildTarget.StandaloneWindows64,
                new[] { GraphicsDeviceType.Direct3D11 });
        }

        private static void ConfigureRenderPipeline()
        {
            EnsureAssetDirectory();

            UniversalRendererData renderer =
                AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            if (renderer == null)
            {
                renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(renderer, RendererPath);
            }

            UniversalRenderPipelineAsset pipeline =
                AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
            if (pipeline == null)
            {
                pipeline = UniversalRenderPipelineAsset.Create(renderer);
                pipeline.name = "Pet Pipeline";
                pipeline.msaaSampleCount = 1;
                pipeline.renderScale = 1.0f;
                pipeline.supportsHDR = false;
                pipeline.supportsCameraDepthTexture = false;
                pipeline.supportsCameraOpaqueTexture = false;
                AssetDatabase.CreateAsset(pipeline, PipelinePath);
            }

            GraphicsSettings.renderPipelineAsset = pipeline;
            QualitySettings.renderPipeline = pipeline;
            EditorUtility.SetDirty(pipeline);
            AssetDatabase.SaveAssets();
        }

        private static void CreateBootstrapScene()
        {
            EnsureAssetDirectory();
            Scene scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                NewSceneMode.Single);
            var bootstrap = new GameObject("Uma Desktop Pet");
            bootstrap.AddComponent<PetBootstrap>();
            EditorSceneManager.SaveScene(scene, ScenePath, true);
        }

        private static void EnsureAssetDirectory()
        {
            if (!AssetDatabase.IsValidFolder(GeneratedDirectory))
            {
                AssetDatabase.CreateFolder("Assets", "Generated");
            }
        }

        private static string ReadArgument(string name)
        {
            string[] arguments = Environment.GetCommandLineArgs();
            int index = Array.FindIndex(
                arguments,
                item => string.Equals(item, name, StringComparison.OrdinalIgnoreCase));
            return index >= 0 && index + 1 < arguments.Length
                ? arguments[index + 1]
                : null;
        }

        private static void CopySupportFiles(string outputDirectory)
        {
            string projectDirectory = Path.GetFullPath(
                Path.Combine(Application.dataPath, ".."));
            string[] fileNames =
            {
                "README.md",
                "THIRD_PARTY_NOTICES.txt"
            };
            foreach (string fileName in fileNames)
            {
                CopySupportFile(
                    Path.Combine(projectDirectory, fileName),
                    Path.Combine(outputDirectory, fileName));
            }

            CopySupportFile(
                Path.Combine(projectDirectory, "LICENSE"),
                Path.Combine(outputDirectory, "LICENSE.txt"));
            CopySupportFile(
                Path.Combine(projectDirectory, "docs", "TESTING.md"),
                Path.Combine(outputDirectory, "TESTING.md"));

            string licensesDirectory = Path.Combine(outputDirectory, "Licenses");
            Directory.CreateDirectory(licensesDirectory);
            CopySupportFile(
                Path.Combine(
                    projectDirectory,
                    "ThirdParty",
                    "SQLite3MultipleCiphers",
                    "LICENSE"),
                Path.Combine(
                    licensesDirectory,
                    "SQLite3MultipleCiphers-MIT.txt"));
            CopySupportFile(
                Path.Combine(
                    projectDirectory,
                    "ThirdParty",
                    "UniWindowController",
                    "LICENSE.md"),
                Path.Combine(
                    licensesDirectory,
                    "UniWindowController-MIT.txt"));
            CopySupportFile(
                Path.Combine(
                    projectDirectory,
                    "ThirdParty",
                    "BootstrapIcons",
                    "LICENSE"),
                Path.Combine(
                    licensesDirectory,
                    "BootstrapIcons-MIT.txt"));
        }

        private static void CopySupportFile(string source, string destination)
        {
            if (!File.Exists(source))
            {
                throw new FileNotFoundException(
                    "Required release support file is missing.",
                    source);
            }

            File.Copy(source, destination, true);
        }
    }
}
