using System;
using System.Collections;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UmaDesktopPet.Standalone.Core;

namespace UmaDesktopPet.Standalone.Runtime
{
    public sealed class PetBootstrap : MonoBehaviour
    {
        private const string NativeLibraryName = "sqlite3mc_x64.dll";
        private static string _oneShotSelectedRoot;
        private static bool _renderPipelinesCaptured;
        private static RenderPipelineAsset _defaultRenderPipeline;
        private static RenderPipelineAsset _qualityRenderPipeline;

        private GameDataCatalog _catalog;
        private BundleRepository _bundles;
        private InstalledCareUiAssets _careUiAssets;
        private MiniCharacterInstance _character;
        private DesktopWindowController _windowController;
        private OguriPetAnimationController _animationController;
        private PetInteractionController _interactionController;
        private PetNeedsState _needsState;
        private PetAutonomyController _autonomyController;
        private GameInstallPreferences _installPreferences;
        private DesktopPetPreferences _petPreferences;
        private PetCharacterProfile _selectedCharacter;
        private GameInstallSetupPanel _setupPanel;
        private Camera _camera;
        private Camera _clearCamera;
        private string _setupSelectedRoot;
        private bool _waitingForInitialSetup;
        private bool _showFirstRunHint;
        private bool _reloadRequested;
        private string _status = "Reading the installed game data...";
        private string _failure;
        private float _hideStatusAt = float.PositiveInfinity;

        private IEnumerator Start()
        {
            Application.runInBackground = true;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 60;
            Screen.SetResolution(
                DesktopWindowController.NativeWindowWidth,
                DesktopWindowController.NativeWindowHeight,
                FullScreenMode.Windowed);
            yield return null;

            string sqliteLibrary = null;
            try
            {
                sqliteLibrary = ResolveNativeLibraryPath();
                _installPreferences = new GameInstallPreferences();
                _petPreferences = new DesktopPetPreferences();
                _selectedCharacter = LoadSelectedCharacter(
                    _petPreferences,
                    out _showFirstRunHint);
                EnsureBootstrapWindow();
                EnsureSetupPanel(sqliteLibrary);
            }
            catch (Exception exception)
            {
                RecordStartupFailure(exception);
            }

            if (!string.IsNullOrEmpty(_failure))
            {
                yield return SmokeFrameCapture.CaptureIfRequested(_camera, true);
                yield break;
            }

            yield return null;
            if (HasCommandLineArgument("--smoke-setup"))
            {
                _setupPanel.Show(
                    false,
                    "Choose where Umamusume is installed. You can also drag " +
                    "the game folder into this window.");
                yield return WaitForDesktopWindow();
                if (HasCommandLineArgument("--smoke-browse"))
                {
                    _setupPanel.BeginFolderBrowse();
                    yield return null;
                }
                yield return SmokeFrameCapture.CaptureIfRequested(_camera, false);
                yield break;
            }

            string gameRoot = null;
            try
            {
                gameRoot = ResolveInitialGameRoot(sqliteLibrary);
            }
            catch (Exception exception)
            {
                RecordStartupFailure(exception);
            }

            if (string.IsNullOrEmpty(_failure) && string.IsNullOrEmpty(gameRoot))
            {
                _waitingForInitialSetup = true;
                if (!_setupPanel.IsVisible)
                {
                    _setupPanel.Show(
                        false,
                        "Choose where Umamusume is installed. You can also drag " +
                        "the game folder into this window.");
                }
                while (string.IsNullOrEmpty(_setupSelectedRoot) &&
                    _setupPanel != null && _setupPanel.IsVisible)
                {
                    yield return null;
                }
                _waitingForInitialSetup = false;
                gameRoot = _setupSelectedRoot;
            }

            if (!string.IsNullOrEmpty(_failure))
            {
                yield return SmokeFrameCapture.CaptureIfRequested(_camera, true);
                yield break;
            }
            if (string.IsNullOrEmpty(gameRoot))
            {
                yield break;
            }

            try
            {
                LoadPet(gameRoot, sqliteLibrary);
            }
            catch (Exception exception)
            {
                RecordStartupFailure(exception);
            }

            if (string.IsNullOrEmpty(_failure))
            {
                yield return WaitForDesktopWindow();
                bool showWelcome = _showFirstRunHint &&
                    !HasSmokeCommandLineArgument();
                if (showWelcome)
                {
                    _status = "Oguri is here. Click her, hold to pat, drag " +
                        "to move her, or right-click for carrots and settings.";
                    float welcomeDeadline = Time.realtimeSinceStartup + 5.0f;
                    while (_animationController != null &&
                        _animationController.IsBusy &&
                        Time.realtimeSinceStartup < welcomeDeadline)
                    {
                        yield return null;
                    }
                    if (_animationController != null &&
                        !_animationController.IsBusy)
                    {
                        _animationController.TriggerAmbientGreeting();
                    }

                    string preferenceError;
                    if (!_petPreferences.TryMarkInteractionHintShown(
                        _selectedCharacter.Key,
                        out preferenceError))
                    {
                        Debug.LogWarning(preferenceError);
                    }
                }
                if (SmokeMenuBurstCapture.IsRequested)
                {
                    yield return SmokeMenuBurstCapture.CaptureIfRequested(
                        _interactionController,
                        !string.IsNullOrEmpty(_failure));
                    yield break;
                }
                yield return TriggerSmokeActionIfRequested();
                bool smokeCarrotEating =
                    HasCommandLineArgument("--smoke-carrot-eating");
                if ((smokeCarrotEating ||
                    HasCommandLineArgument("--smoke-carrot")) &&
                    _interactionController != null)
                {
                    float carrotTimeout = Time.realtimeSinceStartup + 5.0f;
                    while (_animationController != null &&
                        _animationController.IsBusy &&
                        Time.realtimeSinceStartup < carrotTimeout)
                    {
                        yield return null;
                    }
                    if (smokeCarrotEating)
                    {
                        _interactionController.OpenCarrotEatingForSmokeTest();
                    }
                    else
                    {
                        _interactionController.OpenCarrotFeedForSmokeTest();
                    }
                }
                else if (HasCommandLineArgument("--smoke-settings") &&
                    _interactionController != null)
                {
                    _interactionController.OpenSettingsForSmokeTest();
                    Debug.Log(
                        "Settings smoke: region=" + _catalog.Region +
                        ", root=" + _catalog.GameRoot +
                        ", character=" + _selectedCharacter.Key +
                        ", selectable=" + PetCharacterCatalog.Selectable.Count + ".");
                }
                else if (HasCommandLineArgument("--smoke-menu") &&
                    _interactionController != null)
                {
                    _interactionController.OpenMenuForSmokeTest();
                }
            }
            if (string.IsNullOrEmpty(_failure))
            {
                _hideStatusAt = Time.realtimeSinceStartup +
                    (_showFirstRunHint && !HasSmokeCommandLineArgument()
                        ? 6.0f
                        : 2.0f);
            }

            yield return SmokeFrameCapture.CaptureIfRequested(
                _camera,
                !string.IsNullOrEmpty(_failure));

        }

        private void LoadPet(string gameRoot, string sqliteLibrary)
        {
            _setupPanel.Hide();
            if (_selectedCharacter == null)
            {
                _selectedCharacter = PetCharacterCatalog.Oguri;
            }
            if (_selectedCharacter.GameCharacterId != MiniCharacterAssembler.OguriCharacterId)
            {
                throw new NotSupportedException(
                    _selectedCharacter.DisplayName +
                    " is not implemented by this desktop-pet build.");
            }
            _status = "Loading " + _selectedCharacter.ShortName +
                " from " + gameRoot + "...";

            _catalog = GameDataCatalog.Open(gameRoot, sqliteLibrary);
            ConfigureRenderPipeline(_catalog.Region);
            _bundles = new BundleRepository(_catalog);
            _careUiAssets = InstalledCareUiAssets.TryLoad(_bundles);

            var assembler = new MiniCharacterAssembler(_catalog, _bundles);
            _character = assembler.AssembleOguri(transform);
            GameObject visualFrame = CreateVisualFrame(_character.gameObject);
            ConfigureScene(_character.gameObject);

            _animationController =
                _character.gameObject.AddComponent<OguriPetAnimationController>();
            string diagnosticDragAsset =
                ReadCommandLineValue("--smoke-drag-asset");
            double? diagnosticDragTime = ReadOptionalNormalizedTime(
                "--smoke-drag-time");
            _animationController.Initialize(
                _character.Animator,
                _bundles,
                _character.FaceController,
                diagnosticDragAsset,
                diagnosticDragTime);

            PetCameraFramingController framing =
                gameObject.AddComponent<PetCameraFramingController>();
            framing.Initialize(
                _camera,
                visualFrame.transform,
                _character.gameObject,
                _animationController);

            string diagnosticFace = ReadCommandLineValue("--smoke-face");
            if (!string.IsNullOrWhiteSpace(diagnosticFace))
            {
                string faceError;
                if (!_character.FaceController.TryApplyDiagnostic(
                    diagnosticFace,
                    out faceError))
                {
                    throw new InvalidDataException(faceError);
                }
            }

            _needsState = gameObject.AddComponent<PetNeedsState>();
            if (HasCommandLineArgument("--smoke-reset-needs"))
            {
                _needsState.ResetNeeds();
            }
            _windowController.SetRenderCamera(_camera);

            _interactionController =
                gameObject.AddComponent<PetInteractionController>();
            _interactionController.Initialize(
                _windowController,
                _animationController,
                _needsState,
                _careUiAssets,
                _camera,
                _character.transform,
                _selectedCharacter,
                _catalog.Region,
                _catalog.GameRoot,
                RequestCharacterChange,
                RequestGameInstallChange,
                RequestGameFilesReload);

            _autonomyController =
                gameObject.AddComponent<PetAutonomyController>();
            _autonomyController.Initialize(
                _animationController,
                _needsState,
                _interactionController);

            _status = _selectedCharacter.ShortName +
                " is running. Click, hold to pat, drag, or right-click.";
            Debug.Log(
                "Uma Desktop Pet loaded " + _selectedCharacter.DisplayName +
                " without exporting game assets.");
        }

        private static PetCharacterProfile LoadSelectedCharacter(
            DesktopPetPreferences preferences,
            out bool showFirstRunHint)
        {
            showFirstRunHint = true;
            DesktopPetPreferenceSnapshot snapshot;
            string error;
            if (preferences.TryLoad(out snapshot, out error))
            {
                showFirstRunHint = !snapshot.HasSeenInteractionHint;
                return PetCharacterCatalog.ResolveOrDefault(
                    snapshot.SelectedCharacterKey);
            }
            if (preferences.Exists && !string.IsNullOrWhiteSpace(error))
            {
                Debug.LogWarning(error + " Falling back to Oguri Cap.");
            }
            return PetCharacterCatalog.Oguri;
        }

        private static void ConfigureRenderPipeline(GameRegion region)
        {
            if (!_renderPipelinesCaptured)
            {
                _defaultRenderPipeline = GraphicsSettings.defaultRenderPipeline;
                _qualityRenderPipeline = QualitySettings.renderPipeline;
                _renderPipelinesCaptured = true;
            }

            bool useBuiltInPipeline = region == GameRegion.Global;
            GraphicsSettings.defaultRenderPipeline = useBuiltInPipeline
                ? null
                : _defaultRenderPipeline;
            QualitySettings.renderPipeline = useBuiltInPipeline
                ? null
                : _qualityRenderPipeline;

            Debug.Log(
                "Uma Desktop Pet selected the " +
                (useBuiltInPipeline ? "Built-in" : "Universal") +
                " render pipeline for " + region + ".");
        }

        private void RecordStartupFailure(Exception exception)
        {
            _failure = DescribeStartupFailure(exception);
            _status = "Could not load " +
                (_selectedCharacter != null
                    ? _selectedCharacter.ShortName
                    : "the desktop pet") +
                ".";
            Debug.LogException(exception);
        }

        private IEnumerator WaitForDesktopWindow()
        {
            if (_windowController == null || !_windowController.IsSupported)
            {
                yield break;
            }

            float deadline = Time.realtimeSinceStartup + 6.0f;
            while (!_windowController.IsReady &&
                Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            if (!_windowController.IsReady)
            {
                _failure = "The native transparent desktop window was not acquired.";
                _status = "Could not create the desktop-pet window.";
                Debug.LogError(_failure);
            }
        }

        private IEnumerator TriggerSmokeActionIfRequested()
        {
            string action = ReadCommandLineValue("--smoke-action");
            if (string.IsNullOrWhiteSpace(action) &&
                HasCommandLineArgument("--smoke-tap"))
            {
                action = "tap";
            }
            if (string.IsNullOrWhiteSpace(action) ||
                _animationController == null)
            {
                yield break;
            }

            float deadline = Time.realtimeSinceStartup + 10.0f;
            while (_animationController.IsBusy &&
                Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            action = action.Trim().ToLowerInvariant();
            bool started;
            switch (action)
            {
                case "tap":
                    started = _animationController.TriggerTapReaction();
                    if (started && _needsState != null)
                    {
                        _needsState.RecordTapReaction();
                    }
                    break;
                case "pat":
                    started = _animationController.TriggerPatHappy();
                    if (started && _needsState != null)
                    {
                        _needsState.TryPat();
                    }
                    break;
                case "feed":
                    started = _animationController.TriggerFeedResponse();
                    if (started && _needsState != null)
                    {
                        _needsState.TryFeed();
                    }
                    break;
                case "ambient":
                    started = _animationController.TriggerAmbientGreeting();
                    break;
                case "ambient-happy":
                    started = _animationController.TriggerAmbientHappy();
                    break;
                case "drag":
                    started = _animationController.BeginDragReaction();
                    break;
                case "drop":
                    started = _animationController.BeginDragReaction();
                    if (started)
                    {
                        yield return new WaitForSecondsRealtime(0.35f);
                        _animationController.EndDragReaction();
                    }
                    break;
                default:
                    _failure = "Unknown smoke action: " + action + ".";
                    _status = "Could not run the requested smoke action.";
                    Debug.LogError(_failure);
                    yield break;
            }

            if (!started)
            {
                _failure =
                    "The Oguri " + action + " smoke action could not start.";
                _status = "Could not play Oguri's " + action + " action.";
                Debug.LogError(_failure);
                yield break;
            }

            Debug.Log("Oguri " + action + " smoke action started.");
            if (_needsState != null)
            {
                Debug.Log(
                    "Pet state after smoke action: mood=" +
                    _needsState.MoodLabel +
                    ", energy=" + _needsState.Energy.ToString("0.0") + ".");
            }
        }

        private static bool HasCommandLineArgument(string name)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            return Array.Exists(
                Environment.GetCommandLineArgs(),
                argument => string.Equals(
                    argument,
                    name,
                    StringComparison.OrdinalIgnoreCase));
#else
            return false;
#endif
        }

        private static bool HasSmokeCommandLineArgument()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            return Array.Exists(
                Environment.GetCommandLineArgs(),
                argument => argument != null &&
                    argument.StartsWith(
                        "--smoke-",
                        StringComparison.OrdinalIgnoreCase));
#else
            return false;
#endif
        }

        private static string ReadCommandLineValue(string name)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            string[] arguments = Environment.GetCommandLineArgs();
            for (int index = 0; index < arguments.Length - 1; index++)
            {
                if (string.Equals(
                    arguments[index],
                    name,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return arguments[index + 1];
                }
            }
#endif
            return null;
        }

        private static double? ReadOptionalNormalizedTime(string name)
        {
            string value = ReadCommandLineValue(name);
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            double parsed;
            if (!double.TryParse(
                value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out parsed) || parsed < 0.0d || parsed > 1.0d)
            {
                throw new InvalidDataException(
                    name + " must be a number between 0 and 1.");
            }
            return parsed;
        }

        private void EnsureBootstrapWindow()
        {
            _camera = Camera.main;
            if (_camera == null)
            {
                var cameraObject = new GameObject("Pet Camera");
                cameraObject.tag = "MainCamera";
                cameraObject.transform.SetParent(transform, false);
                _camera = cameraObject.AddComponent<Camera>();
            }
            _camera.orthographic = true;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = Color.clear;
            _camera.allowHDR = false;
            _camera.allowMSAA = false;
            ConfigureFixedCameraViewports();

            _windowController = GetComponent<DesktopWindowController>();
            if (_windowController == null)
            {
                _windowController = gameObject.AddComponent<DesktopWindowController>();
            }
            _windowController.SetRenderCamera(_camera);
        }

        private void EnsureSetupPanel(string sqliteLibrary)
        {
            _setupPanel = GetComponent<GameInstallSetupPanel>();
            if (_setupPanel == null)
            {
                _setupPanel = gameObject.AddComponent<GameInstallSetupPanel>();
            }
            _setupPanel.Initialize(
                _windowController,
                _installPreferences,
                sqliteLibrary);
            _setupPanel.InstallAccepted += HandleInstallAccepted;
            _setupPanel.Cancelled += HandleSetupCancelled;
        }

        private string ResolveInitialGameRoot(string sqliteLibrary)
        {
            GameInstallResolution resolution;
            if (!string.IsNullOrWhiteSpace(_oneShotSelectedRoot))
            {
                string selectedRoot = _oneShotSelectedRoot;
                _oneShotSelectedRoot = null;
                resolution = new GameInstallResolution(
                    selectedRoot,
                    GameInstallSource.Remembered);
            }
            else
            {
                resolution = GameInstallLocator.ResolvePreferred(
                    _installPreferences);
            }
            if (resolution == null)
            {
                return null;
            }

            _status = "Checking the installed game files...";
            GameCompatibilityReport report = GameCompatibilityProbe.Probe(
                resolution.GameRoot,
                sqliteLibrary);
            if (!report.IsCompatible || !report.Region.HasValue)
            {
                Debug.LogWarning(
                    "Saved or detected installation is not compatible: " +
                    report.Status + "\n" + report.Details);
                _setupPanel.Show(false, report.Message, resolution.GameRoot);
                return null;
            }

            if (resolution.ShouldRemember)
            {
                string saveError;
                if (!_installPreferences.TrySave(resolution.GameRoot, out saveError))
                {
                    Debug.LogWarning(saveError);
                }
            }
            Debug.Log(
                "Compatible Umamusume installation: " + report.Region.Value +
                " at " + resolution.GameRoot + ".");
            return resolution.GameRoot;
        }

        private void RequestGameInstallChange()
        {
            if (_setupPanel == null || _setupPanel.IsVisible)
            {
                return;
            }
            if (_interactionController != null)
            {
                _interactionController.enabled = false;
            }
            _setupPanel.Show(
                true,
                "Choose another JP or Global installation.",
                _catalog != null ? _catalog.GameRoot : null);
        }

        private void RequestCharacterChange(string characterKey)
        {
            PetCharacterProfile requested;
            if (!PetCharacterCatalog.TryGet(characterKey, out requested))
            {
                Debug.LogWarning(
                    "Ignoring unsupported desktop Uma selection: " + characterKey);
                return;
            }
            if (_selectedCharacter != null && string.Equals(
                requested.Key,
                _selectedCharacter.Key,
                StringComparison.Ordinal))
            {
                return;
            }

            string error;
            if (!_petPreferences.TrySave(requested.Key, out error))
            {
                Debug.LogWarning(error);
                _status = "Could not remember the selected Uma.";
                _hideStatusAt = Time.realtimeSinceStartup + 3.0f;
                return;
            }

            _selectedCharacter = requested;
            _status = "Switching to " + requested.DisplayName + "...";
            _reloadRequested = true;
        }

        private void RequestGameFilesReload()
        {
            if (_catalog == null || string.IsNullOrWhiteSpace(_catalog.GameRoot))
            {
                return;
            }

            _status = "Reloading " +
                (_selectedCharacter != null
                    ? _selectedCharacter.ShortName
                    : "the desktop pet") +
                " from the installed game files...";
            _oneShotSelectedRoot = _catalog.GameRoot;
            _reloadRequested = true;
        }

        private void HandleInstallAccepted(string gameRoot, GameRegion region)
        {
            if (_waitingForInitialSetup)
            {
                _setupSelectedRoot = gameRoot;
                _status = "Loading " +
                    (_selectedCharacter != null
                        ? _selectedCharacter.ShortName
                        : "the desktop pet") +
                    " from the " + region + " installation...";
                return;
            }

            _status = "Switching to the " + region + " installation...";
            _oneShotSelectedRoot = gameRoot;
            _reloadRequested = true;
        }

        private void Update()
        {
            if (!_reloadRequested)
            {
                return;
            }

            _reloadRequested = false;
            Scene activeScene = SceneManager.GetActiveScene();
            SceneManager.LoadScene(activeScene.buildIndex);
        }

        private void HandleSetupCancelled()
        {
            if (_interactionController != null)
            {
                _interactionController.enabled = true;
            }
        }

        private static string DescribeStartupFailure(Exception exception)
        {
            if (exception is DirectoryNotFoundException)
            {
                return "The selected game folder is no longer available. Open " +
                    "Game files and choose it again.";
            }
            if (exception is FileNotFoundException)
            {
                return "A required local file is missing. Finish or repair the " +
                    "Umamusume download, then restart the pet.";
            }
            if (exception is InvalidDataException)
            {
                return "The installed game data could not be read. Update or repair " +
                    "Umamusume, then try again.";
            }
            return "The desktop pet could not start. Details were written to Player.log.";
        }

        private static string ResolveNativeLibraryPath()
        {
            string path = Path.Combine(
                Application.dataPath,
                "Plugins",
                "x86_64",
                NativeLibraryName);
#if UNITY_EDITOR
            path = Path.Combine(Application.dataPath, "Plugins", "x86_64", NativeLibraryName);
#endif
            if (!File.Exists(path))
            {
                throw new FileNotFoundException(
                    "The verified SQLite3MC runtime is missing.",
                    path);
            }
            return path;
        }

        private void ConfigureScene(GameObject characterRoot)
        {
            Bounds bounds = CalculateBounds(characterRoot);

            _camera = Camera.main;
            if (_camera == null)
            {
                var cameraObject = new GameObject("Pet Camera");
                cameraObject.tag = "MainCamera";
                cameraObject.transform.SetParent(transform, false);
                _camera = cameraObject.AddComponent<Camera>();
            }

            _camera.orthographic = true;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = Color.clear;
            _camera.nearClipPlane = 0.01f;
            _camera.farClipPlane = 100.0f;
            _camera.allowHDR = false;
            _camera.allowMSAA = false;
            ConfigureFixedCameraViewports();

            float aspect = (float)DesktopWindowController.PetViewportWidth /
                DesktopWindowController.NativeWindowHeight;
            float heightSize = bounds.extents.y * 1.15f;
            float widthSize = bounds.extents.x / aspect * 1.15f;
            _camera.orthographicSize = Mathf.Max(0.5f, heightSize, widthSize);
            _camera.transform.position =
                bounds.center + Vector3.forward * Mathf.Max(4.0f, bounds.extents.z + 2.0f);
            _camera.transform.rotation = Quaternion.LookRotation(Vector3.back, Vector3.up);

            var lightObject = new GameObject("Pet Key Light");
            lightObject.transform.SetParent(transform, false);
            Light keyLight = lightObject.AddComponent<Light>();
            keyLight.type = LightType.Directional;
            keyLight.color = new Color(1.0f, 0.96f, 0.92f);
            keyLight.intensity = 1.1f;
            keyLight.shadows = LightShadows.None;
            lightObject.transform.rotation = Quaternion.Euler(35.0f, 145.0f, 0.0f);

            var fillObject = new GameObject("Pet Fill Light");
            fillObject.transform.SetParent(transform, false);
            Light fillLight = fillObject.AddComponent<Light>();
            fillLight.type = LightType.Directional;
            fillLight.color = new Color(0.66f, 0.75f, 1.0f);
            fillLight.intensity = 0.45f;
            fillLight.shadows = LightShadows.None;
            fillObject.transform.rotation = Quaternion.Euler(25.0f, -45.0f, 0.0f);
        }

        private void ConfigureFixedCameraViewports()
        {
            if (_camera == null)
            {
                return;
            }

            if (_clearCamera == null)
            {
                var clearObject = new GameObject("Transparent Surface Clear Camera");
                clearObject.transform.SetParent(transform, false);
                _clearCamera = clearObject.AddComponent<Camera>();
            }

            _clearCamera.orthographic = true;
            _clearCamera.clearFlags = CameraClearFlags.SolidColor;
            _clearCamera.backgroundColor = Color.clear;
            _clearCamera.cullingMask = 0;
            _clearCamera.rect = new Rect(0.0f, 0.0f, 1.0f, 1.0f);
            _clearCamera.depth = -100.0f;
            _clearCamera.allowHDR = false;
            _clearCamera.allowMSAA = false;

            _camera.pixelRect = new Rect(
                DesktopWindowController.SidePanelWidth,
                0.0f,
                DesktopWindowController.PetViewportWidth,
                DesktopWindowController.NativeWindowHeight);
            _camera.aspect = (float)DesktopWindowController.PetViewportWidth /
                DesktopWindowController.NativeWindowHeight;
            _camera.depth = 0.0f;
        }

        private GameObject CreateVisualFrame(GameObject characterRoot)
        {
            Bounds bounds = CalculateBounds(characterRoot);
            var visualFrame = new GameObject("Pet Visual Frame");
            visualFrame.transform.SetParent(transform, false);
            visualFrame.transform.position = bounds.center;
            characterRoot.transform.SetParent(visualFrame.transform, true);
            return visualFrame;
        }

        private static Bounds CalculateBounds(GameObject root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                throw new InvalidDataException(
                    "The assembled Oguri prefab contains no visible renderers.");
            }

            Bounds bounds = renderers[0].bounds;
            for (int index = 1; index < renderers.Length; index++)
            {
                bounds.Encapsulate(renderers[index].bounds);
            }
            return bounds;
        }

        private void OnGUI()
        {
            if (_setupPanel != null && _setupPanel.IsVisible)
            {
                return;
            }
            bool showStatus = !string.IsNullOrEmpty(_failure) ||
                ((_interactionController == null ||
                    !_interactionController.IsMenuOpen) &&
                    Time.realtimeSinceStartup < _hideStatusAt);
            if (!showStatus)
            {
                return;
            }

            float width = Mathf.Min(
                DesktopWindowController.PetViewportWidth - 32.0f,
                620.0f);
            float height = string.IsNullOrEmpty(_failure) ? 52.0f : Screen.height - 32.0f;
            var area = new Rect(
                DesktopWindowController.SidePanelWidth + 16.0f,
                16.0f,
                width,
                Mathf.Max(52.0f, height));
            GUI.Box(area, GUIContent.none);

            var style = new GUIStyle(GUI.skin.label)
            {
                wordWrap = true,
                fontSize = 14,
                padding = new RectOffset(12, 12, 10, 10)
            };
            string text = _status;
            if (!string.IsNullOrEmpty(_failure))
            {
                text += "\n\n" + _failure;
            }
            GUI.Label(area, text, style);

            if (!string.IsNullOrEmpty(_failure))
            {
                float buttonY = area.yMax - 42.0f;
                float gap = 8.0f;
                float buttonWidth = _setupPanel != null
                    ? (area.width - 24.0f - gap) * 0.5f
                    : area.width - 24.0f;
                if (_setupPanel != null && GUI.Button(
                    new Rect(
                        area.x + 12.0f,
                        buttonY,
                        buttonWidth,
                        30.0f),
                    "Choose game files"))
                {
                    string attemptedRoot = _catalog != null
                        ? _catalog.GameRoot
                        : null;
                    _failure = null;
                    _status = "Choose another JP or Global installation.";
                    _setupPanel.Show(false, _status, attemptedRoot);
                }

                float quitX = _setupPanel != null
                    ? area.x + 12.0f + buttonWidth + gap
                    : area.x + 12.0f;
                if (GUI.Button(
                    new Rect(quitX, buttonY, buttonWidth, 30.0f),
                    "Quit"))
                {
                    QuitApplication();
                }
            }
        }

        private static void QuitApplication()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void OnDestroy()
        {
            if (_setupPanel != null)
            {
                _setupPanel.InstallAccepted -= HandleInstallAccepted;
                _setupPanel.Cancelled -= HandleSetupCancelled;
            }
            if (_character != null)
            {
                _character.ReleaseResources();
                Destroy(_character.gameObject);
                _character = null;
            }
            if (_careUiAssets != null)
            {
                _careUiAssets.Dispose();
                _careUiAssets = null;
            }
            if (_bundles != null)
            {
                _bundles.Dispose();
                _bundles = null;
            }
            if (_catalog != null)
            {
                _catalog.Dispose();
                _catalog = null;
            }
        }
    }
}
