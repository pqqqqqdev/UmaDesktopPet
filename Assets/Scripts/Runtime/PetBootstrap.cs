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
        private static string _recordingSelectedCharacterKey;
        private static bool _renderPipelinesCaptured;
        private static RenderPipelineAsset _defaultRenderPipeline;
        private static RenderPipelineAsset _qualityRenderPipeline;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private static string _smokeSceneTransitionKind;
        private static string _smokeSceneTransitionCharacterOverrideKey;
        private static string _smokeSceneTransitionExpectedCharacterKey;
        private static string _smokeSceneTransitionCleanupFailure;
        private static bool _smokeSceneTransitionCleanupObserved;
        private static int _smokeSceneTransitionExpectedBundleBaseline = -1;
        private static int _smokeSceneTransitionBundlesBefore = -1;
        private static int _smokeSceneTransitionBundlesAfter = -1;
#endif

        private GameDataCatalog _catalog;
        private BundleRepository _bundles;
        private InstalledCareUiAssets _careUiAssets;
        private InstalledShopUiAssets _shopUiAssets;
        private InstalledFoodUiAssets _foodUiAssets;
        private MiniCharacterInstance _character;
        private PetAttachmentRig _attachmentRig;
        private DesktopWindowController _windowController;
        private OguriPetAnimationController _animationController;
        private PetAmbientVignettePresenter _ambientVignettePresenter;
        private PetCameraFramingController _cameraFramingController;
        private StudyDeskPresenter _studyDeskPresenter;
        private PetStudyController _studyController;
        private PetInteractionController _interactionController;
        private PetNeedsState _needsState;
        private PetFocusState _focusState;
        private PetStudyRewardService _studyRewardService;
        private PetAutonomyController _autonomyController;
        private GameInstallPreferences _installPreferences;
        private DesktopPetPreferences _petPreferences;
        private PetCharacterDefinition _selectedCharacter;
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
        private int _cameraViewportScreenWidth = -1;
        private int _cameraViewportScreenHeight = -1;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private int _smokeVignetteBundleBaseline = -1;
#endif

        private static bool IsRecordingToolsBuild
        {
            get
            {
#if UMA_RECORDING_TOOLS
                return true;
#else
                return false;
#endif
            }
        }

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
                yield return ApplySmokeWindowScaleIfRequested();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                if (!string.IsNullOrWhiteSpace(_smokeSceneTransitionKind))
                {
                    yield return CompleteSmokeSceneTransition();
                    yield return SmokeFrameCapture.CaptureIfRequested(
                        _camera,
                        !string.IsNullOrEmpty(_failure));
                    yield break;
                }
#endif
                bool showWelcome = _showFirstRunHint &&
                    !HasSmokeCommandLineArgument() &&
                    !IsRecordingToolsBuild;
                if (showWelcome)
                {
                    _status = _selectedCharacter.Profile.ShortName +
                        " is here. Click, hold to pat, drag to move, or " +
                        "right-click for carrots and settings.";
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
#if UMA_RECORDING_TOOLS
                if (_interactionController != null)
                {
                    _interactionController.OpenRecordingTools();
                    Debug.Log(
                        "Recording tools opened with temporary state" +
                        (IsRecordingToolsRequested()
                            ? " from the recording launcher; "
                            : "; ") +
                        "normal care and focus saves are untouched.");
                }
#endif
                if (SmokeMenuBurstCapture.IsRequested)
                {
                    yield return SmokeMenuBurstCapture.CaptureIfRequested(
                        _interactionController,
                        !string.IsNullOrEmpty(_failure));
                    yield break;
                }
                yield return TriggerSmokeActionIfRequested();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                if (HasCommandLineArgument("--smoke-autonomy-quiet"))
                {
                    yield return RunQuietAutonomySmoke();
                }
#endif
                bool smokeCarrotEating =
                    HasCommandLineArgument("--smoke-carrot-eating");
                string smokeStudyItemId =
                    ReadCommandLineValue("--smoke-study-item");
                string smokeMoodName =
                    ReadCommandLineValue("--smoke-mood");
                bool smokeStudyIdle =
                    HasCommandLineArgument("--smoke-study-idle");
                bool smokeStudyReady =
                    HasCommandLineArgument("--smoke-study-ready");
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
                        ", selectable=" +
                        PetCharacterDefinitions.Selectable.Count + ".");
                }
                else if ((HasCommandLineArgument("--smoke-study") ||
                    smokeStudyIdle ||
                    smokeStudyReady ||
                    HasCommandLineArgument("--smoke-study-charm") ||
                    HasCommandLineArgument("--smoke-study-paused") ||
                    !string.IsNullOrWhiteSpace(smokeStudyItemId)) &&
                    _interactionController != null && _focusState != null)
                {
                    _focusState.ResetForSmokeTest();
                    if (HasCommandLineArgument("--smoke-study-charm"))
                    {
                        PrepareDeskItemForSmokeTest(
                            DeskShopCatalog.CarrotCharmId);
                    }
                    else if (!string.IsNullOrWhiteSpace(smokeStudyItemId))
                    {
                        PrepareDeskItemForSmokeTest(smokeStudyItemId);
                    }
                    if (!smokeStudyIdle)
                    {
                        _focusState.StartSession(
                            PetFocusState.ShortSessionSeconds);
                    }
                    if (smokeStudyReady)
                    {
                        _focusState.AdvanceTime(
                            PetFocusState.ShortSessionSeconds);
                    }
                    if (!string.IsNullOrWhiteSpace(smokeStudyItemId) &&
                        (_studyDeskPresenter == null ||
                         !_studyDeskPresenter.CanPresentDeskItem(smokeStudyItemId) ||
                         !string.Equals(
                             _studyDeskPresenter.EquippedDeskItemId,
                             smokeStudyItemId,
                             StringComparison.Ordinal)))
                    {
                        throw new InvalidDataException(
                            "Desk item smoke did not equip: " +
                            smokeStudyItemId + ".");
                    }
                    float studyTimeout = Time.realtimeSinceStartup + 5.0f;
                    while (!smokeStudyIdle && !smokeStudyReady &&
                        _animationController != null &&
                        _animationController.HasStudyMotion &&
                        !_animationController.IsStudying &&
                        Time.realtimeSinceStartup < studyTimeout)
                    {
                        yield return null;
                    }
                    if (HasCommandLineArgument("--smoke-study-paused"))
                    {
                        _focusState.PauseSession();
                    }
                    if (HasCommandLineArgument("--smoke-home"))
                    {
                        _interactionController.OpenMenuForSmokeTest();
                    }
                    else
                    {
                        _interactionController.OpenStudyForSmokeTest();
                    }
                    Debug.Log(
                        "Study smoke: status=" + _focusState.Status +
                        ", motion=" + _animationController.CurrentPhase +
                        ", desk=" +
                        (_studyDeskPresenter != null &&
                         _studyDeskPresenter.IsVisible) +
                        ", installedProps=" +
                        (_studyDeskPresenter != null &&
                         _studyDeskPresenter.UsesInstalledStudyProps) +
                        ", equipped=" + _focusState.EquippedDeskItemId + ".");
                }
                else if (HasCommandLineArgument("--smoke-inventory") &&
                    _interactionController != null && _focusState != null)
                {
                    _focusState.ResetForSmokeTest();
                    PrepareDeskItemForSmokeTest(DeskShopCatalog.CarrotCharmId);
                    PrepareDeskItemForSmokeTest(DeskShopCatalog.TazunaRedPenId);
                    _interactionController.OpenInventoryForSmokeTest();
                    Debug.Log(
                        "Inventory smoke: Moni=" + _focusState.Moni +
                        ", owned=" + _focusState.OwnedDeskItemCount +
                        ", equipped=" + _focusState.EquippedDeskItemId + ".");
                }
                else if (HasCommandLineArgument("--smoke-shop") &&
                    _interactionController != null && _focusState != null)
                {
                    _focusState.ResetForSmokeTest();
                    _focusState.StartSession(PetFocusState.ShortSessionSeconds);
                    _focusState.AdvanceTime(PetFocusState.ShortSessionSeconds);
                    _studyRewardService.TryCollectReward();
                    _focusState.PurchaseDeskItem(DeskShopCatalog.CarrotCharmId);
                    _focusState.StartSession(PetFocusState.LongSessionSeconds);
                    _focusState.AdvanceTime(PetFocusState.LongSessionSeconds);
                    _studyRewardService.TryCollectReward();
                    _interactionController.OpenShopForSmokeTest();
                    Debug.Log(
                        "Shop smoke: Moni=" + _focusState.Moni +
                        ", owned=" + _focusState.OwnedDeskItemCount +
                        ", equipped=" + _focusState.EquippedDeskItemId + ".");
                }
                else if (!string.IsNullOrWhiteSpace(smokeMoodName) &&
                    _interactionController != null && _needsState != null)
                {
                    PetMood smokeMood;
                    if (!Enum.TryParse(smokeMoodName, true, out smokeMood) ||
                        (int)smokeMood < (int)PetMood.Awful ||
                        (int)smokeMood > (int)PetMood.Great)
                    {
                        throw new InvalidDataException(
                            "--smoke-mood must be Awful, Bad, Normal, Good, " +
                            "or Great.");
                    }
                    _needsState.ResetForSmokeTest();
                    _needsState.SetMoodForSmokeTest(smokeMood);
                    _interactionController.OpenMenuForSmokeTest();
                    Debug.Log("Mood state smoke: " + smokeMood + ".");
                }
                else if (HasCommandLineArgument("--smoke-mood-pulse") &&
                    _interactionController != null && _needsState != null)
                {
                    _needsState.ResetForSmokeTest();
                    _interactionController.OpenMenuForSmokeTest();
                    yield return null;
                    yield return new WaitForEndOfFrame();
                    PetMood previousMood = _needsState.Mood;
                    if (!_needsState.TryPat())
                    {
                        throw new InvalidOperationException(
                            "Mood pulse smoke could not change Mood.");
                    }
                    Debug.Log(
                        "Mood pulse smoke: before=" + previousMood +
                        ", after=" + _needsState.Mood + ".");
                }
                else if (HasCommandLineArgument("--smoke-home") &&
                    _interactionController != null && _focusState != null)
                {
                    _focusState.ResetForSmokeTest();
                    _interactionController.OpenMenuForSmokeTest();
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
                _selectedCharacter = PetCharacterDefinitions.Oguri;
            }
            MiniCharacterAssemblyProfile assemblyProfile =
                _selectedCharacter.Assembly;
            PetCharacterMotionProfile motionProfile =
                _selectedCharacter.Motions;
            _status = "Loading " + _selectedCharacter.Profile.ShortName +
                " from " + gameRoot + "...";

            _catalog = GameDataCatalog.Open(gameRoot, sqliteLibrary);
            ConfigureRenderPipeline(_catalog.Region);
            _bundles = new BundleRepository(_catalog);
            _careUiAssets = InstalledCareUiAssets.TryLoad(_bundles);
            _shopUiAssets = InstalledShopUiAssets.TryLoad(_bundles);
            _foodUiAssets = InstalledFoodUiAssets.TryLoad(_bundles);

            var assembler = new MiniCharacterAssembler(_catalog, _bundles);
            _character = assembler.AssembleInstalled(assemblyProfile, transform);
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
                motionProfile,
                diagnosticDragAsset,
                diagnosticDragTime);

            PetAttachmentProfile attachmentProfile =
                _selectedCharacter.Attachments;
            _attachmentRig =
                _character.gameObject.AddComponent<PetAttachmentRig>();
            _attachmentRig.Initialize(
                _character.transform,
                visualFrame.transform,
                attachmentProfile);
            _character.SetAttachmentRig(_attachmentRig);
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            Transform leftHandSlot;
            Transform rightHandSlot;
            Transform mouthSlot;
            Debug.Log(
                "Pet attachment slots: leftHand=" +
                _attachmentRig.TryGetSlot(
                    PetAttachmentSlots.LeftHand,
                    out leftHandSlot) +
                ", rightHand=" +
                _attachmentRig.TryGetSlot(
                    PetAttachmentSlots.RightHand,
                    out rightHandSlot) +
                ", mouth=" +
                _attachmentRig.TryGetSlot(
                    PetAttachmentSlots.Mouth,
                    out mouthSlot));
#endif

            _studyDeskPresenter =
                _character.gameObject.AddComponent<StudyDeskPresenter>();
            _studyDeskPresenter.Initialize(_attachmentRig, _bundles);

            _ambientVignettePresenter =
                _character.gameObject.AddComponent<PetAmbientVignettePresenter>();
            _ambientVignettePresenter.Initialize(
                _selectedCharacter,
                _animationController,
                _attachmentRig,
                _bundles,
                _studyDeskPresenter);

            _cameraFramingController =
                gameObject.AddComponent<PetCameraFramingController>();
            _cameraFramingController.Initialize(
                _camera,
                visualFrame.transform,
                visualFrame,
                _animationController,
                _ambientVignettePresenter);

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
            if (HasSmokeCommandLineArgument())
            {
                _needsState.ResetForSmokeTest();
            }
            _focusState = gameObject.AddComponent<PetFocusState>();
            if (HasSmokeCommandLineArgument())
            {
                _focusState.ResetForSmokeTest();
            }
            _studyRewardService = new PetStudyRewardService(
                _focusState,
                _needsState);
            _studyController = gameObject.AddComponent<PetStudyController>();
            _studyController.Initialize(
                _focusState,
                _animationController,
                _ambientVignettePresenter,
                _studyDeskPresenter);
            _windowController.SetRenderCamera(_camera);

            _interactionController =
                gameObject.AddComponent<PetInteractionController>();
            _interactionController.Initialize(
                _windowController,
                _animationController,
                _ambientVignettePresenter,
                _needsState,
                _focusState,
                _studyRewardService,
                _studyDeskPresenter,
                _careUiAssets,
                _shopUiAssets,
                _foodUiAssets,
                _camera,
                _character.transform,
                _selectedCharacter,
                _catalog.Region,
                _catalog.GameRoot,
                IsRecordingToolsBuild,
                RequestCharacterChange,
                RequestGameInstallChange,
                RequestGameFilesReload);

            if (!IsRecordingToolsBuild)
            {
                _autonomyController =
                    gameObject.AddComponent<PetAutonomyController>();
                _autonomyController.Initialize(
                    _animationController,
                    _ambientVignettePresenter,
                    _needsState,
                    _focusState,
                    _interactionController);
            }

            _status = _selectedCharacter.Profile.ShortName +
                " is running. Click, hold to pat, drag, or right-click.";
            Debug.Log(
                "Uma Desktop Pet loaded " +
                _selectedCharacter.Profile.DisplayName +
                " without exporting game assets.");
        }

        private static PetCharacterDefinition LoadSelectedCharacter(
            DesktopPetPreferences preferences,
            out bool showFirstRunHint)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (!string.IsNullOrWhiteSpace(
                _smokeSceneTransitionCharacterOverrideKey))
            {
                PetCharacterDefinition transitionCharacter;
                if (!PetCharacterDefinitions.TryGet(
                    _smokeSceneTransitionCharacterOverrideKey,
                    out transitionCharacter))
                {
                    throw new InvalidDataException(
                        "The smoke scene-transition character is invalid: " +
                        _smokeSceneTransitionCharacterOverrideKey + ".");
                }
                showFirstRunHint = false;
                return transitionCharacter;
            }
#endif
            string smokeCharacter = ReadCommandLineValue("--smoke-character");
            if (!string.IsNullOrWhiteSpace(smokeCharacter))
            {
                string requestedKey = smokeCharacter.Trim();
                if (string.Equals(requestedKey, "oguri", StringComparison.OrdinalIgnoreCase))
                {
                    requestedKey = PetCharacterDefinitions.Oguri.Key;
                }
                else if (string.Equals(
                    requestedKey,
                    "tachyon",
                    StringComparison.OrdinalIgnoreCase))
                {
                    requestedKey = PetCharacterDefinitions.Tachyon.Key;
                }

                PetCharacterDefinition requested;
                if (!PetCharacterDefinitions.TryGet(requestedKey, out requested))
                {
                    throw new InvalidDataException(
                        "--smoke-character requires oguri, tachyon, " +
                        "oguri-cap, or agnes-tachyon.");
                }

                showFirstRunHint = false;
                return requested;
            }

            if (!string.IsNullOrWhiteSpace(_recordingSelectedCharacterKey))
            {
                PetCharacterDefinition recordingCharacter;
                if (PetCharacterDefinitions.TryGet(
                    _recordingSelectedCharacterKey,
                    out recordingCharacter))
                {
                    showFirstRunHint = false;
                    return recordingCharacter;
                }
            }

            showFirstRunHint = true;
            DesktopPetPreferenceSnapshot snapshot;
            string error;
            if (preferences.TryLoad(out snapshot, out error))
            {
                showFirstRunHint = !snapshot.HasSeenInteractionHint;
                return PetCharacterDefinitions.ResolveOrDefault(
                    snapshot.SelectedCharacterKey);
            }
            if (preferences.Exists && !string.IsNullOrWhiteSpace(error))
            {
                Debug.LogWarning(error + " Falling back to Oguri Cap.");
            }
            return PetCharacterDefinitions.Oguri;
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
                    ? _selectedCharacter.Profile.ShortName
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

        private IEnumerator ApplySmokeWindowScaleIfRequested()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            const string ArgumentName = "--smoke-window-scale";
            if (!HasCommandLineArgument(ArgumentName))
            {
                yield break;
            }

            string value = ReadCommandLineValue(ArgumentName);
            float requestedScale;
            if (string.IsNullOrWhiteSpace(value) ||
                !float.TryParse(
                    value,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out requestedScale) ||
                float.IsNaN(requestedScale) ||
                float.IsInfinity(requestedScale) ||
                requestedScale <= 0.0f)
            {
                throw new InvalidDataException(
                    ArgumentName + " must be a positive finite number.");
            }

            int requestedWidth = (int)Math.Round(
                DesktopWindowController.NativeWindowWidth *
                    (double)requestedScale,
                MidpointRounding.AwayFromZero);
            int requestedHeight = (int)Math.Round(
                DesktopWindowController.NativeWindowHeight *
                    (double)requestedScale,
                MidpointRounding.AwayFromZero);
            int expectedWidth;
            int expectedHeight;
            DesktopWindowController.ConstrainClientSize(
                requestedWidth,
                requestedHeight,
                out expectedWidth,
                out expectedHeight);

            if (_windowController == null ||
                !_windowController.TrySetWindowScale(requestedScale))
            {
                throw new InvalidOperationException(
                    "Window scale smoke could not apply " +
                    requestedScale.ToString(
                        "0.###",
                        CultureInfo.InvariantCulture) + ".");
            }

            const float SizeTolerancePixels = 2.0f;
            float deadline = Time.realtimeSinceStartup + 5.0f;
            Vector2 windowSize = Vector2.zero;
            Vector2 clientSize = Vector2.zero;
            while (Time.realtimeSinceStartup < deadline)
            {
                windowSize = _windowController.WindowSize;
                clientSize = _windowController.ClientSize;
                bool screenMatches =
                    Mathf.Abs(Screen.width - expectedWidth) <=
                        SizeTolerancePixels &&
                    Mathf.Abs(Screen.height - expectedHeight) <=
                        SizeTolerancePixels;
                bool windowMatches =
                    Mathf.Abs(windowSize.x - expectedWidth) <=
                        SizeTolerancePixels &&
                    Mathf.Abs(windowSize.y - expectedHeight) <=
                        SizeTolerancePixels;
                bool clientMatches =
                    Mathf.Abs(clientSize.x - expectedWidth) <=
                        SizeTolerancePixels &&
                    Mathf.Abs(clientSize.y - expectedHeight) <=
                        SizeTolerancePixels;
                if (screenMatches && windowMatches && clientMatches)
                {
                    Debug.Log(
                        "Window scale smoke: requested=" +
                        requestedScale.ToString(
                            "0.###",
                            CultureInfo.InvariantCulture) +
                        ", expected=" + expectedWidth + "x" +
                        expectedHeight +
                        ", screen=" + Screen.width + "x" + Screen.height +
                        ", window=" +
                        windowSize.x.ToString(
                            "0",
                            CultureInfo.InvariantCulture) + "x" +
                        windowSize.y.ToString(
                            "0",
                            CultureInfo.InvariantCulture) +
                        ", client=" +
                        clientSize.x.ToString(
                            "0",
                            CultureInfo.InvariantCulture) + "x" +
                        clientSize.y.ToString(
                            "0",
                            CultureInfo.InvariantCulture) + ".");
                    yield break;
                }

                yield return null;
            }

            throw new InvalidOperationException(
                "Window scale smoke timed out: requested=" +
                requestedScale.ToString(
                    "0.###",
                    CultureInfo.InvariantCulture) +
                ", expected=" + expectedWidth + "x" + expectedHeight +
                ", screen=" + Screen.width + "x" + Screen.height +
                ", window=" +
                windowSize.x.ToString("0", CultureInfo.InvariantCulture) +
                "x" +
                windowSize.y.ToString("0", CultureInfo.InvariantCulture) +
                ", client=" +
                clientSize.x.ToString("0", CultureInfo.InvariantCulture) +
                "x" +
                clientSize.y.ToString("0", CultureInfo.InvariantCulture) +
                ".");
#else
            yield break;
#endif
        }

        private IEnumerator TriggerSmokeActionIfRequested()
        {
            string action = ReadCommandLineValue("--smoke-action");
            string requestedVignette = ReadCommandLineValue("--smoke-vignette");
            string vignetteInterrupt = ReadCommandLineValue(
                "--smoke-vignette-interrupt");
            string requestedFactorEffect =
                ReadCommandLineValue("--smoke-factor-effect");
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            string requestedVignettePrefab =
                ReadCommandLineValue("--smoke-vignette-prefab");
            string requestedAppOwnedSceneProp =
                ReadCommandLineValue("--smoke-app-owned-scene-prop");
            float requestedSceneYaw = 0.0f;
            float requestedScenePitch = 0.0f;
            if (HasCommandLineArgument("--smoke-scene-yaw"))
            {
                string yawValue = ReadCommandLineValue("--smoke-scene-yaw");
                if (string.IsNullOrWhiteSpace(yawValue) ||
                    !float.TryParse(
                        yawValue,
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out requestedSceneYaw) ||
                    float.IsNaN(requestedSceneYaw) ||
                    float.IsInfinity(requestedSceneYaw) ||
                    (string.IsNullOrWhiteSpace(requestedAppOwnedSceneProp) &&
                        string.IsNullOrWhiteSpace(requestedVignettePrefab)))
                {
                    _failure =
                        "--smoke-scene-yaw requires a finite number and " +
                        "either --smoke-app-owned-scene-prop or " +
                        "--smoke-vignette-prefab.";
                    _status = "Could not run the app-owned 3D turntable.";
                    Debug.LogError(_failure);
                    yield break;
                }
                requestedSceneYaw = Mathf.Repeat(requestedSceneYaw, 360.0f);
            }
            if (HasCommandLineArgument("--smoke-scene-pitch"))
            {
                string pitchValue = ReadCommandLineValue("--smoke-scene-pitch");
                if (string.IsNullOrWhiteSpace(pitchValue) ||
                    !float.TryParse(
                        pitchValue,
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out requestedScenePitch) ||
                    float.IsNaN(requestedScenePitch) ||
                    float.IsInfinity(requestedScenePitch) ||
                    Mathf.Abs(requestedScenePitch) > 45.0f ||
                    string.IsNullOrWhiteSpace(requestedAppOwnedSceneProp))
                {
                    _failure =
                        "--smoke-scene-pitch requires a number from -45 to 45 " +
                        "and --smoke-app-owned-scene-prop.";
                    _status = "Could not run the app-owned 3D turntable.";
                    Debug.LogError(_failure);
                    yield break;
                }
            }
            bool vignetteRepeatRequested =
                HasCommandLineArgument("--smoke-vignette-repeat");
            int vignetteRepeatCount = 0;
            if (vignetteRepeatRequested)
            {
                string repeatValue =
                    ReadCommandLineValue("--smoke-vignette-repeat");
                if (string.IsNullOrWhiteSpace(repeatValue) ||
                    !int.TryParse(
                        repeatValue,
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out vignetteRepeatCount) ||
                    vignetteRepeatCount < 1 || vignetteRepeatCount > 20)
                {
                    _failure =
                        "--smoke-vignette-repeat requires an integer from 1 " +
                        "through 20.";
                    _status = "Could not run the repeated vignette smoke.";
                    Debug.LogError(_failure);
                    yield break;
                }
                if (string.IsNullOrWhiteSpace(requestedVignette))
                {
                    _failure =
                        "--smoke-vignette-repeat requires --smoke-vignette.";
                    _status = "Could not run the repeated vignette smoke.";
                    Debug.LogError(_failure);
                    yield break;
                }
                if (!string.IsNullOrWhiteSpace(
                        ReadCommandLineValue("--smoke-vignette-phase")) ||
                    !string.IsNullOrWhiteSpace(
                        ReadCommandLineValue("--smoke-vignette-interrupt")))
                {
                    _failure =
                        "--smoke-vignette-repeat cannot be combined with " +
                        "--smoke-vignette-phase or --smoke-vignette-interrupt.";
                    _status = "Could not run the repeated vignette smoke.";
                    Debug.LogError(_failure);
                    yield break;
                }
                if (!string.IsNullOrWhiteSpace(requestedFactorEffect) ||
                    !string.IsNullOrWhiteSpace(requestedVignettePrefab) ||
                    !string.IsNullOrWhiteSpace(requestedAppOwnedSceneProp))
                {
                    _failure =
                        "--smoke-vignette-repeat cannot be combined with a " +
                        "diagnostic factor or prefab preview.";
                    _status = "Could not run the repeated vignette smoke.";
                    Debug.LogError(_failure);
                    yield break;
                }
            }
#else
            string requestedVignettePrefab = null;
            string requestedAppOwnedSceneProp = null;
            float requestedSceneYaw = 0.0f;
            float requestedScenePitch = 0.0f;
#endif
            if (string.IsNullOrWhiteSpace(action) &&
                HasCommandLineArgument("--smoke-tap"))
            {
                action = "tap";
            }
            if ((string.IsNullOrWhiteSpace(action) &&
                    string.IsNullOrWhiteSpace(requestedVignette) &&
                    string.IsNullOrWhiteSpace(requestedFactorEffect) &&
                    string.IsNullOrWhiteSpace(requestedVignettePrefab) &&
                    string.IsNullOrWhiteSpace(requestedAppOwnedSceneProp)) ||
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

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (vignetteRepeatRequested)
            {
                yield return RunRepeatedSmokeVignette(
                    requestedVignette.Trim(),
                    vignetteRepeatCount);
                yield break;
            }
#endif

            bool started;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (!string.IsNullOrWhiteSpace(requestedAppOwnedSceneProp))
            {
                string propId = requestedAppOwnedSceneProp.Trim();
                action = "app-owned scene prop " + propId + " at yaw " +
                    requestedSceneYaw.ToString(
                        "0.###",
                        CultureInfo.InvariantCulture) + " and pitch " +
                    requestedScenePitch.ToString(
                        "0.###",
                        CultureInfo.InvariantCulture);
                started = _ambientVignettePresenter != null &&
                    _ambientVignettePresenter.TryShowDiagnosticAppOwnedSceneProp(
                        propId,
                        requestedSceneYaw,
                        requestedScenePitch);
            }
            else if (!string.IsNullOrWhiteSpace(requestedVignettePrefab))
            {
                string prefabId = requestedVignettePrefab.Trim();
                action = "vignette prefab " + prefabId;
                started = _ambientVignettePresenter != null &&
                    _ambientVignettePresenter.TryShowDiagnosticPrefab(
                        _bundles,
                        prefabId,
                        requestedSceneYaw);
            }
            else if (!string.IsNullOrWhiteSpace(requestedFactorEffect))
            {
                string factorEffectId = requestedFactorEffect.Trim();
                action = "factor effect " + factorEffectId;
                started = _ambientVignettePresenter != null &&
                    _ambientVignettePresenter.TryShowDiagnosticFactorEffect(
                        _bundles,
                        factorEffectId);
            }
            else
#endif
            if (!string.IsNullOrWhiteSpace(requestedVignette))
            {
                action = "vignette " + requestedVignette.Trim();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                if (IsSmokeSceneTransitionInterrupt(vignetteInterrupt) &&
                    _bundles != null)
                {
                    _smokeVignetteBundleBaseline =
                        _bundles.LoadedBundleCount;
                }
#endif
                started = _ambientVignettePresenter != null &&
                    _ambientVignettePresenter.TryPlay(requestedVignette.Trim());
            }
            else
            {
                action = action.Trim().ToLowerInvariant();
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
                            _needsState.TryFeed(FoodCatalog.CarrotJellyId);
                        }
                        break;
                    case "ambient":
                        started = _animationController.TriggerAmbientGreeting();
                        break;
                    case "ambient-happy":
                        started = _animationController.TriggerAmbientHappy();
                        break;
                    case "signature":
                        started = _ambientVignettePresenter != null &&
                            _ambientVignettePresenter.AvailableVignettes.Count > 0 &&
                            _ambientVignettePresenter.TryPlay(
                                _ambientVignettePresenter
                                    .AvailableVignettes[0].Id);
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
            }

            if (!started)
            {
                _failure =
                    "The " + _selectedCharacter.Profile.ShortName + " " + action +
                    " smoke action could not start.";
                _status = "Could not play " +
                    _selectedCharacter.Profile.ShortName +
                    "'s " + action + " action.";
                Debug.LogError(_failure);
                yield break;
            }

            Debug.Log(
                _selectedCharacter.Profile.ShortName + " " + action +
                " smoke action started.");
            if (!string.IsNullOrWhiteSpace(requestedVignette))
            {
                string vignettePhase = ReadCommandLineValue(
                    "--smoke-vignette-phase");
                if (string.IsNullOrWhiteSpace(vignettePhase) &&
                    !string.IsNullOrWhiteSpace(vignetteInterrupt))
                {
                    vignettePhase = "research-loop";
                }
                if (!string.IsNullOrWhiteSpace(vignettePhase))
                {
                    yield return WaitForSmokeVignettePhase(vignettePhase);
                }
                if (string.IsNullOrEmpty(_failure) &&
                    !string.IsNullOrWhiteSpace(vignetteInterrupt))
                {
                    yield return ApplySmokeVignetteInterrupt(vignetteInterrupt);
                }
            }
            string actionFrame = ReadCommandLineValue("--smoke-action-frame");
            if (string.IsNullOrWhiteSpace(requestedVignette) &&
                string.IsNullOrWhiteSpace(requestedFactorEffect) &&
                string.IsNullOrWhiteSpace(requestedVignettePrefab) &&
                !string.IsNullOrWhiteSpace(actionFrame))
            {
                yield return WaitForSmokeActionFrame(actionFrame);
            }
            if (_needsState != null)
            {
                Debug.Log(
                    "Pet state after smoke action: mood=" +
                    _needsState.MoodLabel +
                    ", energy=" + _needsState.Energy.ToString("0.0") + ".");
            }
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private IEnumerator RunRepeatedSmokeVignette(
            string vignetteId,
            int repeatCount)
        {
            if (_ambientVignettePresenter == null || _bundles == null ||
                _animationController == null)
            {
                _failure =
                    "The repeated vignette smoke requires the presenter, " +
                    "motion controller, and bundle repository.";
                _status = "Could not run the repeated vignette smoke.";
                Debug.LogError(_failure);
                yield break;
            }

            int baselineBundleCount = _bundles.LoadedBundleCount;
            for (int cycleIndex = 0;
                cycleIndex < repeatCount;
                cycleIndex++)
            {
                int cycle = cycleIndex + 1;
                if (_ambientVignettePresenter.IsPlaying ||
                    _ambientVignettePresenter.ActiveVisualCount != 0 ||
                    !string.Equals(
                        _animationController.CurrentPhase,
                        "IdleLoop",
                        StringComparison.Ordinal))
                {
                    _failure =
                        "Repeated vignette smoke cycle " + cycle +
                        " did not begin from clean idle; motion=" +
                        _animationController.CurrentPhase + ", visuals=" +
                        _ambientVignettePresenter.ActiveVisualCount + ".";
                    _status = "Could not run the repeated vignette smoke.";
                    CancelRepeatedSmokeVignette();
                    Debug.LogError(_failure);
                    yield break;
                }

                int bundlesBefore = _bundles.LoadedBundleCount;
                if (bundlesBefore != baselineBundleCount)
                {
                    _failure =
                        "Repeated vignette smoke did not begin at the original " +
                        "bundle baseline: baseline=" + baselineBundleCount +
                        ", cycle=" + cycle + ", before=" + bundlesBefore + ".";
                    _status =
                        "Repeated vignette smoke detected a retained bundle lease.";
                    CancelRepeatedSmokeVignette();
                    Debug.LogError(_failure);
                    yield break;
                }
                Debug.Log(
                    "Smoke vignette repeat cycle starting: vignette=" +
                    vignetteId + ", cycle=" + cycle + "/" + repeatCount +
                    ", bundlesBefore=" + bundlesBefore + ", visuals=0.");

                if (!_ambientVignettePresenter.TryPlay(vignetteId))
                {
                    _failure =
                        "Repeated vignette smoke could not start " +
                        vignetteId + " on cycle " + cycle + ".";
                    _status = "Could not run the repeated vignette smoke.";
                    CancelRepeatedSmokeVignette();
                    Debug.LogError(_failure);
                    yield break;
                }

                yield return WaitForRepeatedSmokeVignetteCompletion(
                    vignetteId,
                    cycle,
                    repeatCount);
                if (!string.IsNullOrEmpty(_failure))
                {
                    CancelRepeatedSmokeVignette();
                    yield break;
                }

                int bundlesAfter = _bundles.LoadedBundleCount;
                if (bundlesAfter != baselineBundleCount)
                {
                    _failure =
                        "Repeated vignette smoke did not release every signature " +
                        "scene bundle lease: baseline=" + baselineBundleCount +
                        ", cycle=" + cycle + ", before=" + bundlesBefore +
                        ", after=" + bundlesAfter + ".";
                    _status =
                        "Repeated vignette smoke detected a retained bundle lease.";
                    CancelRepeatedSmokeVignette();
                    Debug.LogError(_failure);
                    yield break;
                }

                if (_ambientVignettePresenter.ActiveVisualCount != 0 ||
                    _ambientVignettePresenter.IsPlaying)
                {
                    _failure =
                        "Repeated vignette smoke cycle " + cycle +
                        " completed with stale vignette visuals.";
                    _status = "Repeated vignette smoke left stale visuals.";
                    CancelRepeatedSmokeVignette();
                    Debug.LogError(_failure);
                    yield break;
                }

                Debug.Log(
                    "Smoke vignette repeat cycle complete: vignette=" +
                    vignetteId + ", cycle=" + cycle + "/" + repeatCount +
                    ", bundlesBefore=" + bundlesBefore +
                    ", bundlesAfter=" + bundlesAfter +
                    ", visuals=0, motion=" +
                    _animationController.CurrentPhase + ".");
            }

            if (_ambientVignettePresenter.IsPlaying ||
                _ambientVignettePresenter.ActiveVisualCount != 0 ||
                !string.Equals(
                    _animationController.CurrentPhase,
                    "IdleLoop",
                    StringComparison.Ordinal))
            {
                _failure =
                    "Repeated vignette smoke did not finish in clean idle.";
                _status = "Repeated vignette smoke left stale state.";
                CancelRepeatedSmokeVignette();
                Debug.LogError(_failure);
                yield break;
            }

            Debug.Log(
                "SMOKE_VIGNETTE_REPEAT_PASS: vignette=" + vignetteId +
                ", cycles=" + repeatCount + ", baselineBundles=" +
                baselineBundleCount + ", visuals=0, motion=IdleLoop.");
        }

        private IEnumerator WaitForRepeatedSmokeVignetteCompletion(
            string vignetteId,
            int cycle,
            int repeatCount)
        {
            float deadline = Time.realtimeSinceStartup + 45.0f;
            while (Time.realtimeSinceStartup < deadline)
            {
                if (_ambientVignettePresenter == null ||
                    _animationController == null)
                {
                    _failure =
                        "The repeated vignette smoke runtime was destroyed " +
                        "during cycle " + cycle + ".";
                    _status = "Could not finish the repeated vignette smoke.";
                    Debug.LogError(_failure);
                    yield break;
                }

                bool completed =
                    !_ambientVignettePresenter.IsPlaying &&
                    _ambientVignettePresenter.ActiveVisualCount == 0 &&
                    string.Equals(
                        _animationController.CurrentPhase,
                        "IdleLoop",
                        StringComparison.Ordinal);
                if (completed)
                {
                    yield return new WaitForEndOfFrame();
                    if (!_ambientVignettePresenter.IsPlaying &&
                        _ambientVignettePresenter.ActiveVisualCount == 0 &&
                        string.Equals(
                            _animationController.CurrentPhase,
                            "IdleLoop",
                            StringComparison.Ordinal))
                    {
                        yield break;
                    }
                }
                yield return null;
            }

            _failure =
                "Timed out waiting for repeated vignette " + vignetteId +
                " cycle " + cycle + "/" + repeatCount +
                " to complete in IdleLoop; step=" +
                (_ambientVignettePresenter != null
                    ? _ambientVignettePresenter.CurrentStepId
                    : "missing") +
                ", stage=" +
                (_ambientVignettePresenter != null
                    ? _ambientVignettePresenter.CurrentStageLabel
                    : "missing") +
                ", visuals=" +
                (_ambientVignettePresenter != null
                    ? _ambientVignettePresenter.ActiveVisualCount.ToString(
                        CultureInfo.InvariantCulture)
                    : "missing") +
                ", motion=" +
                (_animationController != null
                    ? _animationController.CurrentPhase
                    : "missing") + ".";
            _status = "Repeated vignette smoke timed out.";
            CancelRepeatedSmokeVignette();
            Debug.LogError(_failure);
        }

        private void CancelRepeatedSmokeVignette()
        {
            if (_ambientVignettePresenter != null &&
                _ambientVignettePresenter.IsPlaying)
            {
                _ambientVignettePresenter.Cancel();
            }
        }

        private IEnumerator RunQuietAutonomySmoke()
        {
            if (_autonomyController == null || _needsState == null ||
                _focusState == null || _interactionController == null ||
                _ambientVignettePresenter == null || _animationController == null ||
                _bundles == null)
            {
                FailQuietAutonomySmoke(
                    "Quiet-autonomy smoke requires every pet runtime.");
                yield break;
            }
            if (_selectedCharacter == null || !string.Equals(
                _selectedCharacter.Key,
                PetCharacterDefinitions.Tachyon.Key,
                StringComparison.Ordinal))
            {
                FailQuietAutonomySmoke(
                    "Quiet-autonomy smoke requires Agnes Tachyon.");
                yield break;
            }
            if (_ambientVignettePresenter.IsPlaying ||
                _ambientVignettePresenter.ActiveVisualCount != 0 ||
                _focusState.IsSessionActive ||
                _interactionController.IsUserInteractionActive)
            {
                FailQuietAutonomySmoke(
                    "Quiet-autonomy smoke did not begin from clean idle.");
                yield break;
            }

            float idleDeadline = Time.realtimeSinceStartup + 10.0f;
            while (!string.Equals(
                    _animationController.CurrentPhase,
                    "IdleLoop",
                    StringComparison.Ordinal) &&
                Time.realtimeSinceStartup < idleDeadline)
            {
                yield return null;
            }
            if (!string.Equals(
                _animationController.CurrentPhase,
                "IdleLoop",
                StringComparison.Ordinal))
            {
                FailQuietAutonomySmoke(
                    "Quiet-autonomy smoke could not reach IdleLoop.");
                yield break;
            }

            int baselineBundles = _bundles.LoadedBundleCount;
            _needsState.SetQuietMode(true);
            int quietEvaluationSerial =
                _autonomyController.SmokeDueEvaluationSerial;
            _autonomyController.ForceVignetteDueForSmokeTest();
            yield return WaitForAutonomySmokeEvaluation(
                quietEvaluationSerial,
                "Quiet suppression");
            if (!string.IsNullOrEmpty(_failure))
            {
                CleanupQuietAutonomySmoke();
                yield break;
            }
            yield return new WaitForEndOfFrame();

            if (!_needsState.QuietMode ||
                _ambientVignettePresenter.IsPlaying ||
                !string.IsNullOrEmpty(_ambientVignettePresenter.CurrentId) ||
                _ambientVignettePresenter.ActiveVisualCount != 0 ||
                !string.Equals(
                    _animationController.CurrentPhase,
                    "IdleLoop",
                    StringComparison.Ordinal) ||
                _bundles.LoadedBundleCount != baselineBundles)
            {
                FailQuietAutonomySmoke(
                    "Quiet mode did not suppress a due autonomous Experiment " +
                    "without changing motion, visuals, or bundle leases.");
                yield break;
            }

            _needsState.SetQuietMode(false);
            int positiveEvaluationSerial =
                _autonomyController.SmokeDueEvaluationSerial;
            _autonomyController.ForceVignetteDueForSmokeTest();
            yield return WaitForAutonomySmokeEvaluation(
                positiveEvaluationSerial,
                "Quiet positive control");
            if (!string.IsNullOrEmpty(_failure))
            {
                CleanupQuietAutonomySmoke();
                yield break;
            }

            float playDeadline = Time.realtimeSinceStartup + 10.0f;
            while ((!_ambientVignettePresenter.IsPlaying ||
                    _ambientVignettePresenter.ActiveVisualCount == 0) &&
                Time.realtimeSinceStartup < playDeadline)
            {
                yield return null;
            }
            if (!_ambientVignettePresenter.IsPlaying ||
                !string.Equals(
                    _ambientVignettePresenter.CurrentId,
                    PetCharacterDefinitions.TachyonExperimentId,
                    StringComparison.Ordinal) ||
                _ambientVignettePresenter.ActiveVisualCount == 0 ||
                _bundles.LoadedBundleCount <= baselineBundles)
            {
                FailQuietAutonomySmoke(
                    "Disabling Quiet mode did not let the same forced " +
                    "autonomy path start Tachyon's exact Experiment.");
                yield break;
            }

            yield return new WaitForEndOfFrame();
            if (_cameraFramingController == null ||
                !_cameraFramingController.IsSignatureFramingLocked ||
                Mathf.Abs(
                    _cameraFramingController.CurrentUniformScale -
                    _cameraFramingController.CompactUniformScale) > 0.000001f)
            {
                FailQuietAutonomySmoke(
                    "The autonomous Experiment changed Tachyon's compact " +
                    "character scale or did not lock signature framing.");
                yield break;
            }
            Debug.Log(
                "SMOKE_SIGNATURE_FRAMING: route=autonomy, locked=true, " +
                "compact=" +
                _cameraFramingController.CompactUniformScale.ToString(
                    "0.######") +
                ", active=" +
                _cameraFramingController.CurrentUniformScale.ToString(
                    "0.######") + ".");

            int activeBundles = _bundles.LoadedBundleCount;
            if (!_ambientVignettePresenter.Cancel())
            {
                FailQuietAutonomySmoke(
                    "Could not cancel the Quiet-mode positive-control Experiment.");
                yield break;
            }
            yield return WaitForMotionPhase("IdleLoop", 10.0f);
            if (!string.IsNullOrEmpty(_failure))
            {
                CleanupQuietAutonomySmoke();
                yield break;
            }
            yield return new WaitForEndOfFrame();
            if (_ambientVignettePresenter.IsPlaying ||
                !string.IsNullOrEmpty(_ambientVignettePresenter.CurrentId) ||
                _ambientVignettePresenter.ActiveVisualCount != 0 ||
                _bundles.LoadedBundleCount != baselineBundles)
            {
                FailQuietAutonomySmoke(
                    "Quiet-autonomy positive-control cleanup did not return " +
                    "to the exact bundle and visual baseline.");
                yield break;
            }

            Debug.Log(
                "SMOKE_AUTONOMY_QUIET_PASS: vignette=" +
                PetCharacterDefinitions.TachyonExperimentId +
                ", baselineBundles=" + baselineBundles +
                ", activeBundles=" + activeBundles +
                ", visuals=0, motion=IdleLoop, quiet=false.");
        }

        private IEnumerator WaitForAutonomySmokeEvaluation(
            int previousSerial,
            string label)
        {
            float deadline = Time.realtimeSinceStartup + 5.0f;
            while (_autonomyController != null &&
                _autonomyController.SmokeDueEvaluationSerial == previousSerial &&
                Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }
            if (_autonomyController == null ||
                _autonomyController.SmokeDueEvaluationSerial == previousSerial)
            {
                _failure = label + " did not reach the autonomy due-time gate.";
                _status = "Could not verify Quiet-mode autonomy suppression.";
                Debug.LogError(_failure);
            }
        }

        private void FailQuietAutonomySmoke(string message)
        {
            _failure = message;
            _status = "Could not verify Quiet-mode autonomy suppression.";
            CleanupQuietAutonomySmoke();
            Debug.LogError(_failure);
        }

        private void CleanupQuietAutonomySmoke()
        {
            if (_needsState != null)
            {
                _needsState.SetQuietMode(false);
            }
            if (_ambientVignettePresenter != null &&
                _ambientVignettePresenter.IsPlaying)
            {
                _ambientVignettePresenter.Cancel();
            }
        }

        private void BeginSmokeSceneTransition(string kind)
        {
            if (_ambientVignettePresenter == null || _bundles == null ||
                _catalog == null || _selectedCharacter == null ||
                _smokeVignetteBundleBaseline < 0 ||
                !_ambientVignettePresenter.IsPlaying ||
                !string.Equals(
                    _ambientVignettePresenter.CurrentId,
                    PetCharacterDefinitions.TachyonExperimentId,
                    StringComparison.Ordinal) ||
                _ambientVignettePresenter.ActiveVisualCount == 0 ||
                _bundles.LoadedBundleCount <= _smokeVignetteBundleBaseline)
            {
                _failure =
                    "Scene-transition smoke did not begin with an active " +
                    "Experiment above its original bundle baseline.";
                _status = "Could not verify vignette scene-transition cleanup.";
                Debug.LogError(_failure);
                return;
            }

            PetCharacterDefinition target = _selectedCharacter;
            if (string.Equals(
                kind,
                "character-switch",
                StringComparison.Ordinal))
            {
                target = string.Equals(
                    _selectedCharacter.Key,
                    PetCharacterDefinitions.Tachyon.Key,
                    StringComparison.Ordinal)
                        ? PetCharacterDefinitions.Oguri
                        : PetCharacterDefinitions.Tachyon;
                GameCompatibilityReport report = GameCompatibilityProbe.Probe(
                    _catalog.GameRoot,
                    ResolveNativeLibraryPath(),
                    target.Profile);
                if (!report.IsCompatible)
                {
                    _failure =
                        "Scene-transition smoke target is incompatible: " +
                        report.Message + " " + report.Details;
                    _status =
                        "Could not verify vignette character-switch cleanup.";
                    Debug.LogError(_failure);
                    return;
                }
            }

            _smokeSceneTransitionKind = kind;
            _smokeSceneTransitionCharacterOverrideKey = target.Key;
            _smokeSceneTransitionExpectedCharacterKey = target.Key;
            _smokeSceneTransitionExpectedBundleBaseline =
                _smokeVignetteBundleBaseline;
            _smokeSceneTransitionCleanupFailure = null;
            _smokeSceneTransitionCleanupObserved = false;
            _smokeSceneTransitionBundlesBefore =
                _bundles.LoadedBundleCount;
            _smokeSceneTransitionBundlesAfter = -1;

            Debug.Log(
                "Smoke vignette scene transition starting: kind=" + kind +
                ", from=" + _selectedCharacter.Key +
                ", to=" + target.Key +
                ", baselineBundles=" + _smokeVignetteBundleBaseline +
                ", activeBundles=" + _bundles.LoadedBundleCount +
                ", visuals=" +
                _ambientVignettePresenter.ActiveVisualCount + ".");

            if (string.Equals(kind, "reload", StringComparison.Ordinal))
            {
                RequestGameFilesReload();
            }
            else
            {
                // This mirrors the accepted character-switch scene reload but
                // deliberately keeps the test choice process-local. Normal
                // desktop-pet preferences are never written by this path.
                _selectedCharacter = target;
                _oneShotSelectedRoot = _catalog.GameRoot;
                _status = "Smoke-switching to " +
                    target.Profile.DisplayName + "...";
                _reloadRequested = true;
            }
        }

        private IEnumerator CompleteSmokeSceneTransition()
        {
            string kind = _smokeSceneTransitionKind;
            string expectedCharacter =
                _smokeSceneTransitionExpectedCharacterKey;
            yield return null;
            yield return new WaitForEndOfFrame();

            if (!_smokeSceneTransitionCleanupObserved)
            {
                _failure =
                    "The old scene did not report signature-feature cleanup.";
            }
            else if (!string.IsNullOrEmpty(
                _smokeSceneTransitionCleanupFailure))
            {
                _failure = _smokeSceneTransitionCleanupFailure;
            }
            else if (_selectedCharacter == null || !string.Equals(
                _selectedCharacter.Key,
                expectedCharacter,
                StringComparison.Ordinal))
            {
                _failure =
                    "Scene transition loaded " +
                    (_selectedCharacter != null
                        ? _selectedCharacter.Key
                        : "no character") +
                    " instead of " + expectedCharacter + ".";
            }

            if (string.IsNullOrEmpty(_failure))
            {
                yield return WaitForMotionPhase("IdleLoop", 10.0f);
            }
            if (string.IsNullOrEmpty(_failure) &&
                (_ambientVignettePresenter == null ||
                    _ambientVignettePresenter.IsPlaying ||
                    !string.IsNullOrEmpty(
                        _ambientVignettePresenter.CurrentId) ||
                    _ambientVignettePresenter.ActiveVisualCount != 0))
            {
                _failure =
                    "The new scene inherited active Experiment state or visuals.";
            }
            int staleSceneObjects = CountSmokeSignatureSceneObjects();
            if (string.IsNullOrEmpty(_failure) && staleSceneObjects != 0)
            {
                _failure =
                    "The new scene retained " + staleSceneObjects +
                    " old Experiment scene objects.";
            }

            if (string.IsNullOrEmpty(_failure))
            {
                Debug.Log(
                    "SMOKE_VIGNETTE_SCENE_TRANSITION_PASS: kind=" + kind +
                    ", character=" + _selectedCharacter.Key +
                    ", releasedBundles=" +
                    _smokeSceneTransitionBundlesBefore + "->" +
                    _smokeSceneTransitionBundlesAfter +
                    ", baselineBundles=" +
                    _smokeSceneTransitionExpectedBundleBaseline +
                    ", staleObjects=0, visuals=0, motion=IdleLoop.");
            }
            else
            {
                _status = "Could not verify vignette scene-transition cleanup.";
                Debug.LogError(_failure);
            }
            ClearSmokeSceneTransitionState();
        }

        private static int CountSmokeSignatureSceneObjects()
        {
            int count = 0;
            GameObject[] objects =
                Resources.FindObjectsOfTypeAll<GameObject>();
            for (int index = 0; index < objects.Length; index++)
            {
                GameObject candidate = objects[index];
                if (candidate == null || string.IsNullOrEmpty(candidate.name))
                {
                    continue;
                }
                if (candidate.name.StartsWith(
                        "App-owned signature prop [",
                        StringComparison.Ordinal) ||
                    candidate.name.StartsWith(
                        "Installed Particle Effect tachyon-formula-",
                        StringComparison.Ordinal) ||
                    (candidate.name.StartsWith(
                            "pfb_chr_prop1143_",
                            StringComparison.Ordinal) &&
                        candidate.name.EndsWith(
                            " (Render Only)",
                            StringComparison.Ordinal)))
                {
                    count++;
                }
            }
            return count;
        }

        private static void ClearSmokeSceneTransitionState()
        {
            _smokeSceneTransitionKind = null;
            _smokeSceneTransitionCharacterOverrideKey = null;
            _smokeSceneTransitionExpectedCharacterKey = null;
            _smokeSceneTransitionCleanupFailure = null;
            _smokeSceneTransitionCleanupObserved = false;
            _smokeSceneTransitionExpectedBundleBaseline = -1;
            _smokeSceneTransitionBundlesBefore = -1;
            _smokeSceneTransitionBundlesAfter = -1;
        }

        private static bool IsSmokeSceneTransitionInterrupt(string interrupt)
        {
            if (string.IsNullOrWhiteSpace(interrupt))
            {
                return false;
            }
            string normalized = interrupt.Trim().ToLowerInvariant();
            return normalized == "reload" || normalized == "character-switch";
        }
#endif

        private IEnumerator WaitForSmokeVignettePhase(string requestedPhase)
        {
            string phase = requestedPhase.Trim().ToLowerInvariant();
            string expectedStep;
            string expectedStage;
            bool expectFinished = false;
            switch (phase)
            {
                case "setup":
                    expectedStep =
                        PetCharacterDefinitions.TachyonResearchStepId;
                    expectedStage = "Assembling the laboratory";
                    break;
                case "research-start":
                    expectedStep =
                        PetCharacterDefinitions.TachyonResearchStepId;
                    expectedStage = "Research starting";
                    break;
                case "research-loop":
                    expectedStep =
                        PetCharacterDefinitions.TachyonResearchStepId;
                    expectedStage = "Research";
                    break;
                case "research-end":
                    expectedStep =
                        PetCharacterDefinitions.TachyonResearchStepId;
                    expectedStage = "Research finishing";
                    break;
                case "breakthrough-start":
                    expectedStep =
                        PetCharacterDefinitions.TachyonBreakthroughStepId;
                    expectedStage = "Experiment result starting";
                    break;
                case "breakthrough-loop":
                    expectedStep =
                        PetCharacterDefinitions.TachyonBreakthroughStepId;
                    expectedStage = "Experiment result";
                    break;
                case "breakthrough-end":
                    expectedStep =
                        PetCharacterDefinitions.TachyonBreakthroughStepId;
                    expectedStage = "Experiment result complete";
                    break;
                case "return-idle":
                case "completed":
                    expectedStep = string.Empty;
                    expectedStage = string.Empty;
                    expectFinished = true;
                    break;
                default:
                    _failure =
                        "--smoke-vignette-phase requires setup, research-start, " +
                        "research-loop, research-end, breakthrough-start, " +
                        "breakthrough-loop, breakthrough-end, or return-idle.";
                    Debug.LogError(_failure);
                    yield break;
            }

            float deadline = Time.realtimeSinceStartup + 35.0f;
            while (Time.realtimeSinceStartup < deadline)
            {
                if (_ambientVignettePresenter == null)
                {
                    _failure = "The ambient vignette presenter was destroyed.";
                    Debug.LogError(_failure);
                    yield break;
                }

                bool ready = expectFinished
                    ? !_ambientVignettePresenter.IsPlaying &&
                        _ambientVignettePresenter.ActiveVisualCount == 0 &&
                        string.Equals(
                            _animationController.CurrentPhase,
                            "IdleLoop",
                            StringComparison.Ordinal)
                    : _ambientVignettePresenter.IsPlaying &&
                        string.Equals(
                            _ambientVignettePresenter.CurrentStepId,
                            expectedStep,
                            StringComparison.Ordinal) &&
                        string.Equals(
                            _ambientVignettePresenter.CurrentStageLabel,
                            expectedStage,
                            StringComparison.Ordinal);
                if (ready)
                {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    if (!_animationController.FreezeCurrentMotionForSmokeTest())
                    {
                        _failure =
                            "Could not freeze the requested ambient vignette phase.";
                        Debug.LogError(_failure);
                        yield break;
                    }
#endif
                    yield return new WaitForEndOfFrame();
                    Debug.Log(
                        "Smoke vignette phase ready: requested=" + phase +
                        ", step=" + _ambientVignettePresenter.CurrentStepId +
                        ", stage=" + _ambientVignettePresenter.CurrentStageLabel +
                        ", visuals=" +
                        FormatActiveVignetteVisuals(_ambientVignettePresenter) +
                        ", motion=" + _animationController.CurrentPhase + ".");
                    yield break;
                }
                yield return null;
            }

            _failure =
                "Timed out waiting for ambient vignette phase " + phase +
                "; step=" +
                (_ambientVignettePresenter != null
                    ? _ambientVignettePresenter.CurrentStepId
                    : "missing") +
                ", stage=" +
                (_ambientVignettePresenter != null
                    ? _ambientVignettePresenter.CurrentStageLabel
                    : "missing") +
                ", motion=" + _animationController.CurrentPhase + ".";
            Debug.LogError(_failure);
        }

        private IEnumerator ApplySmokeVignetteInterrupt(string requestedInterrupt)
        {
            string interrupt = requestedInterrupt.Trim().ToLowerInvariant();
            switch (interrupt)
            {
                case "drag-hold":
                case "drag-return-idle":
                    if (!_animationController.BeginDragReaction())
                    {
                        _failure =
                            "Could not interrupt the ambient vignette with drag.";
                        Debug.LogError(_failure);
                        yield break;
                    }
                    yield return WaitForMotionPhase("DragHold", 5.0f);
                    if (interrupt == "drag-return-idle" &&
                        string.IsNullOrEmpty(_failure))
                    {
                        yield return new WaitForSecondsRealtime(0.35f);
                        _animationController.EndDragReaction();
                        yield return WaitForMotionPhase("IdleLoop", 10.0f);
                    }
                    break;
                case "menu":
                    if (_interactionController == null)
                    {
                        _failure = "The interaction controller is unavailable.";
                        Debug.LogError(_failure);
                        yield break;
                    }
                    _interactionController.OpenMenuForSmokeTest();
                    yield return null;
                    break;
                case "study":
                    if (_focusState == null ||
                        !_focusState.StartSession(PetFocusState.ShortSessionSeconds))
                    {
                        _failure =
                            "Could not start Study while testing vignette cleanup.";
                        Debug.LogError(_failure);
                        yield break;
                    }
                    yield return WaitForMotionPhase("StudyLoop", 10.0f);
                    break;
                case "tap":
                    if (_ambientVignettePresenter != null)
                    {
                        _ambientVignettePresenter.Cancel();
                    }
                    if (!_animationController.TriggerTapReaction())
                    {
                        _failure =
                            "Tap could not start after ambient vignette cleanup.";
                        Debug.LogError(_failure);
                        yield break;
                    }
                    yield return WaitForMotionPhase("ActionStart", 5.0f);
                    break;
                case "stop":
                    if (_ambientVignettePresenter == null ||
                        !_ambientVignettePresenter.Cancel())
                    {
                        _failure = "Could not stop the ambient vignette.";
                        Debug.LogError(_failure);
                        yield break;
                    }
                    yield return WaitForMotionPhase("IdleLoop", 5.0f);
                    break;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                case "reload":
                case "character-switch":
                    BeginSmokeSceneTransition(interrupt);
                    yield break;
#endif
                default:
                    _failure =
                        "--smoke-vignette-interrupt requires drag-hold, " +
                        "drag-return-idle, menu, study, tap, stop, reload, " +
                        "or character-switch.";
                    Debug.LogError(_failure);
                    yield break;
            }

            if (string.IsNullOrEmpty(_failure) &&
                _ambientVignettePresenter != null &&
                (_ambientVignettePresenter.IsPlaying ||
                    _ambientVignettePresenter.ActiveVisualCount != 0))
            {
                _failure =
                    "Ambient vignette visuals survived the " + interrupt +
                    " interruption.";
                Debug.LogError(_failure);
                yield break;
            }

            if (string.IsNullOrEmpty(_failure))
            {
                yield return new WaitForEndOfFrame();
                Debug.Log(
                    "Smoke vignette interruption ready: interrupt=" + interrupt +
                    ", motion=" + _animationController.CurrentPhase +
                    ", focus=" +
                    (_focusState != null
                        ? _focusState.Status.ToString()
                        : "missing") +
                    ", visuals=none.");
            }
        }

        private static string FormatActiveVignetteVisuals(
            PetAmbientVignettePresenter presenter)
        {
            if (presenter == null || presenter.ActiveVisualIds.Count == 0)
            {
                return "none";
            }

            string result = string.Empty;
            for (int index = 0; index < presenter.ActiveVisualIds.Count; index++)
            {
                if (index > 0)
                {
                    result += ",";
                }
                result += presenter.ActiveVisualIds[index];
            }
            return result;
        }

        private IEnumerator WaitForSmokeActionFrame(string requestedFrame)
        {
            string frame = requestedFrame.Trim().ToLowerInvariant();
            if (frame == "drag-hold" || frame == "drag-return-idle")
            {
                yield return WaitForMotionPhase("ActionLoop", 20.0f);
                if (!string.IsNullOrEmpty(_failure))
                {
                    yield break;
                }
                if (!_animationController.BeginDragReaction())
                {
                    _failure = "Could not interrupt the ambient vignette with drag.";
                    Debug.LogError(_failure);
                    yield break;
                }
                yield return WaitForMotionPhase("DragHold", 5.0f);
                if (frame == "drag-return-idle" &&
                    string.IsNullOrEmpty(_failure))
                {
                    yield return new WaitForSecondsRealtime(0.35f);
                    _animationController.EndDragReaction();
                    yield return WaitForMotionPhase("IdleLoop", 10.0f);
                }
            }
            else
            {
                string phase;
                switch (frame)
                {
                    case "start":
                        phase = "ActionStart";
                        break;
                    case "loop":
                        phase = "ActionLoop";
                        break;
                    case "end":
                        phase = "ActionEnd";
                        break;
                    case "return-idle":
                        phase = "IdleLoop";
                        break;
                    default:
                        _failure =
                            "--smoke-action-frame requires start, loop, end, " +
                            "return-idle, drag-hold, or drag-return-idle.";
                        Debug.LogError(_failure);
                        yield break;
                }
                yield return WaitForMotionPhase(phase, 20.0f);
            }

            if (string.IsNullOrEmpty(_failure))
            {
                yield return new WaitForEndOfFrame();
                Debug.Log(
                    "Smoke action frame ready: requested=" + frame +
                    ", phase=" + _animationController.CurrentPhase + ".");
            }
        }

        private IEnumerator WaitForMotionPhase(
            string expectedPhase,
            float timeoutSeconds)
        {
            float deadline = Time.realtimeSinceStartup + timeoutSeconds;
            while (Time.realtimeSinceStartup < deadline)
            {
                string current = _animationController.CurrentPhase;
                if (string.Equals(
                    current,
                    expectedPhase,
                    StringComparison.Ordinal) ||
                    current.EndsWith(
                        "/" + expectedPhase,
                        StringComparison.Ordinal))
                {
                    yield break;
                }
                yield return null;
            }

            _failure =
                "Timed out waiting for motion phase " + expectedPhase +
                "; current=" + _animationController.CurrentPhase + ".";
            Debug.LogError(_failure);
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

        private static bool IsRecordingToolsRequested()
        {
#if UMA_RECORDING_TOOLS
            return Array.Exists(
                Environment.GetCommandLineArgs(),
                argument => string.Equals(
                    argument,
                    "--recording-tools",
                    StringComparison.OrdinalIgnoreCase));
#else
            return false;
#endif
        }

        private void PrepareDeskItemForSmokeTest(string itemId)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            DeskShopItem item;
            if (!DeskShopCatalog.TryGet(itemId, out item))
            {
                throw new InvalidDataException(
                    "Unknown desk item smoke id: " + itemId + ".");
            }
            if (_studyDeskPresenter == null ||
                !_studyDeskPresenter.CanPresentDeskItem(item.Id))
            {
                throw new InvalidDataException(
                    "Desk item smoke asset is unavailable: " + item.Id + ".");
            }

            int remaining = item.Cost;
            while (remaining >= 2)
            {
                _focusState.StartSession(PetFocusState.LongSessionSeconds);
                _focusState.AdvanceTime(PetFocusState.LongSessionSeconds);
                _studyRewardService.TryCollectReward();
                remaining -= 2;
            }
            if (remaining == 1)
            {
                _focusState.StartSession(PetFocusState.ShortSessionSeconds);
                _focusState.AdvanceTime(PetFocusState.ShortSessionSeconds);
                _studyRewardService.TryCollectReward();
            }
            if (!_focusState.PurchaseDeskItem(item.Id))
            {
                throw new InvalidDataException(
                    "Could not prepare desk item smoke: " + item.Id + ".");
            }
#else
            throw new InvalidOperationException(
                "Desk item smoke setup is unavailable in a release build.");
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
                sqliteLibrary,
                _selectedCharacter.Profile);
            _setupPanel.InstallAccepted += HandleInstallAccepted;
            _setupPanel.Cancelled += HandleSetupCancelled;
        }

        private string ResolveInitialGameRoot(string sqliteLibrary)
        {
            GameInstallResolution resolution;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            string smokeGameRoot = ReadCommandLineValue("--smoke-game-root");
            if (!string.IsNullOrWhiteSpace(smokeGameRoot))
            {
                // Keep cross-region QA process-local. The user's remembered
                // installation remains untouched after the smoke player exits.
                resolution = new GameInstallResolution(
                    Path.GetFullPath(smokeGameRoot.Trim()),
                    GameInstallSource.Remembered);
            }
            else
#endif
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
                sqliteLibrary,
                _selectedCharacter.Profile);
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
            PetCharacterDefinition requested;
            if (!PetCharacterDefinitions.TryGet(characterKey, out requested))
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

            if (_catalog == null || string.IsNullOrWhiteSpace(_catalog.GameRoot))
            {
                RejectCharacterChange(
                    requested.Profile,
                    "The current game files are not ready.",
                    "No loaded game-data root was available for compatibility preflight.");
                return;
            }

            GameCompatibilityReport report;
            try
            {
                report = GameCompatibilityProbe.Probe(
                    _catalog.GameRoot,
                    ResolveNativeLibraryPath(),
                    requested.Profile);
            }
            catch (Exception exception)
            {
                RejectCharacterChange(
                    requested.Profile,
                    "The current game files could not be checked.",
                    exception.ToString());
                return;
            }
            if (!report.IsCompatible)
            {
                RejectCharacterChange(
                    requested.Profile,
                    report.Message,
                    report.Details);
                return;
            }

            if (IsRecordingToolsBuild)
            {
                // Recording builds never write the user's normal character
                // preference. Keep this temporary selection for every scene
                // reload in the current recording process instead.
                _recordingSelectedCharacterKey = requested.Key;
            }
            else
            {
                string error;
                if (!_petPreferences.TrySave(requested.Key, out error))
                {
                    Debug.LogWarning(error);
                    _status = "Could not remember the selected Uma.";
                    _hideStatusAt = Time.realtimeSinceStartup + 3.0f;
                    return;
                }
            }

            _selectedCharacter = requested;
            _status = "Switching to " +
                requested.Profile.DisplayName + "...";
            _reloadRequested = true;
        }

        private void RejectCharacterChange(
            PetCharacterProfile requested,
            string reason,
            string details)
        {
            string shortName = requested != null
                ? requested.ShortName
                : "that Uma";
            _status = "Couldn't switch to " + shortName + ". " + reason;
            _hideStatusAt = Time.realtimeSinceStartup + 4.0f;
            Debug.LogWarning(
                "Character compatibility preflight rejected " +
                (requested != null ? requested.Key : "an unknown character") +
                ": " + reason + "\n" + details);
        }

        private void RequestGameFilesReload()
        {
            if (_catalog == null || string.IsNullOrWhiteSpace(_catalog.GameRoot))
            {
                return;
            }

            _status = "Reloading " +
                (_selectedCharacter != null
                    ? _selectedCharacter.Profile.ShortName
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
                        ? _selectedCharacter.Profile.ShortName
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
            UpdateCameraViewportsIfNeeded();
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

            _camera.depth = 0.0f;
            UpdateCameraViewportsIfNeeded(true);
        }

        private void UpdateCameraViewportsIfNeeded(bool force = false)
        {
            if (_camera == null)
            {
                return;
            }

            int screenWidth = Mathf.Max(1, Screen.width);
            int screenHeight = Mathf.Max(1, Screen.height);
            if (!force &&
                screenWidth == _cameraViewportScreenWidth &&
                screenHeight == _cameraViewportScreenHeight)
            {
                return;
            }

            _cameraViewportScreenWidth = screenWidth;
            _cameraViewportScreenHeight = screenHeight;
            if (_clearCamera != null)
            {
                _clearCamera.rect = new Rect(0.0f, 0.0f, 1.0f, 1.0f);
            }
            _camera.pixelRect = DesktopWindowLayout.PhysicalPetViewportRect(
                new Vector2(screenWidth, screenHeight));
            _camera.aspect = DesktopWindowLayout.PetAspect;
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
                    "The assembled mini-character contains no visible renderers.");
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

            Matrix4x4 previousMatrix = DesktopWindowLayout.BeginGui();
            try
            {
                DrawStatusGui();
            }
            finally
            {
                DesktopWindowLayout.EndGui(previousMatrix);
            }
        }

        private void DrawStatusGui()
        {

            float width = Mathf.Min(
                DesktopWindowController.PetViewportWidth - 32.0f,
                620.0f);
            float height = string.IsNullOrEmpty(_failure)
                ? 52.0f
                : DesktopWindowLayout.LogicalHeight - 32.0f;
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
            if (_studyRewardService != null)
            {
                _studyRewardService.Dispose();
                _studyRewardService = null;
            }
            if (_ambientVignettePresenter != null)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                bool verifySceneTransitionCleanup =
                    !string.IsNullOrWhiteSpace(
                        _smokeSceneTransitionKind) &&
                    !_smokeSceneTransitionCleanupObserved;
                bool transitionWasPlaying = verifySceneTransitionCleanup &&
                    _ambientVignettePresenter.IsPlaying;
                int transitionVisualsBefore = verifySceneTransitionCleanup
                    ? _ambientVignettePresenter.ActiveVisualCount
                    : 0;
#endif
                _ambientVignettePresenter.ReleaseResources();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                if (verifySceneTransitionCleanup)
                {
                    _smokeSceneTransitionCleanupObserved = true;
                    _smokeSceneTransitionBundlesAfter = _bundles != null
                        ? _bundles.LoadedBundleCount
                        : -1;
                    bool presenterClean =
                        !_ambientVignettePresenter.IsPlaying &&
                        string.IsNullOrEmpty(
                            _ambientVignettePresenter.CurrentId) &&
                        _ambientVignettePresenter.ActiveVisualCount == 0;
                    if (_smokeSceneTransitionBundlesBefore <=
                        _smokeSceneTransitionExpectedBundleBaseline)
                    {
                        _smokeSceneTransitionCleanupFailure =
                            "The scene transition was not requested above the " +
                            "pre-Experiment bundle baseline.";
                    }
                    else if (!presenterClean)
                    {
                        _smokeSceneTransitionCleanupFailure =
                            "Signature-feature presenter state survived old-scene " +
                            "cleanup.";
                    }
                    else if (_smokeSceneTransitionBundlesAfter !=
                        _smokeSceneTransitionExpectedBundleBaseline)
                    {
                        _smokeSceneTransitionCleanupFailure =
                            "Old-scene Experiment leases did not return to " +
                            "baseline: expected=" +
                            _smokeSceneTransitionExpectedBundleBaseline +
                            ", actual=" +
                            _smokeSceneTransitionBundlesAfter + ".";
                    }
                    Debug.Log(
                        "Smoke vignette old-scene cleanup: kind=" +
                        _smokeSceneTransitionKind +
                        ", playingBefore=" + transitionWasPlaying +
                        ", visualsBefore=" + transitionVisualsBefore +
                        ", bundles=" +
                        _smokeSceneTransitionBundlesBefore + "->" +
                        _smokeSceneTransitionBundlesAfter +
                        ", baseline=" +
                        _smokeSceneTransitionExpectedBundleBaseline +
                        ", clean=" +
                        string.IsNullOrEmpty(
                            _smokeSceneTransitionCleanupFailure) + ".");
                }
#endif
                _ambientVignettePresenter = null;
            }
            if (_animationController != null)
            {
                _animationController.ReleaseResources();
                _animationController = null;
            }
            if (_studyDeskPresenter != null)
            {
                _studyDeskPresenter.ReleaseResources();
                _studyDeskPresenter = null;
            }
            if (_attachmentRig != null)
            {
                _attachmentRig.ReleaseResources();
                _attachmentRig = null;
            }
            if (_character != null)
            {
                _character.ReleaseResources();
                Destroy(_character.gameObject);
                _character = null;
            }
            if (_shopUiAssets != null)
            {
                _shopUiAssets.Dispose();
                _shopUiAssets = null;
            }
            if (_foodUiAssets != null)
            {
                _foodUiAssets.Dispose();
                _foodUiAssets = null;
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
