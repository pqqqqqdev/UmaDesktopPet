using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;
using UmaDesktopPet.Standalone.Core;

namespace UmaDesktopPet.Standalone.Runtime
{
    /// <summary>
    /// Presents optional multi-step ambient vignettes using exact assets from the
    /// user's installed game. Missing visual assets omit only the affected
    /// vignette; generated stand-ins are never shown.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PetAmbientVignettePresenter : MonoBehaviour,
        IPetSignatureFeatureHost
    {
        private const float MinimumCharacterHeight = 0.0001f;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        public const string Cutin1032MainPrefabId = "cutin1032_02-main";
        public const string CelestialGlobe00PrefabId = "celestialglobe00";
        public const string CelestialGlobe01PrefabId = "celestialglobe01";
        public const string Table1031PrefabId = "table1031_13";
        public const string Table1048PrefabId = "table1048_23";
        public const string Book1051PrefabId = "book1051_26";
        public const string Cutin1080DeskPrefabId = "cutin1080-desk";
        public const string Cutin1080MonitorPrefabId = "cutin1080-monitor";
        public const string Cutin1080RackPrefabId = "cutin1080-rack";
        public const string Cutin1080ChairPrefabId = "cutin1080-chair";
        public const string Mini10005TablePrefabId = "mini10005-table";
        public const string Mini10005WhiteboardPrefabId =
            "mini10005-whiteboard";
        public const string TachyonProp00PrefabId = "tachyon-prop00";
        public const string TachyonProp01PrefabId = "tachyon-prop01";
        public const string TachyonProp02PrefabId = "tachyon-prop02";
        public const string TachyonProp03PrefabId = "tachyon-prop03";
        public const string TachyonProp04PrefabId = "tachyon-prop04";
        public const string TachyonProp05PrefabId = "tachyon-prop05";
        public const string StudyDeskPrefabId = "study-desk";
        public const string StudyChairPrefabId = "study-chair";
        public const string StudyBoardPrefabId = "study-board";
        public const string StudyBookPrefabId = "study-book";

        private const string FactorEffectAssetPrefix =
            "3d/effect/factor/pfb_eff_factor_";
        private const string FactorEffectPrefabPrefix = "pfb_eff_factor_";
        private const float FactorEffectPreviewScaleInCharacterHeights = 0.58f;
        private const float FactorEffectPreviewWarmupSeconds = 0.35f;
        private const float PrefabPreviewMaximumDimensionInCharacterHeights =
            0.72f;
        private const float PrefabPreviewDepthInCharacterHeights = 0.04f;

        private static readonly HashSet<string> FactorEffectPreviewIds =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "01_001",
                "01_002",
                "01_003",
                "01_004",
                "01_005",
                "01_006",
                "01_007",
                "01_008",
                "01_009",
                "01_010",
                "01_011",
                "01_012",
                "01_013",
                "01_014",
                "02_001",
                "02_002"
            };
#endif

        private readonly List<PetAmbientVignetteProfile> _availableVignettes =
            new List<PetAmbientVignetteProfile>();
        private readonly List<PetSignatureFeatureDefinition> _availableFeatures =
            new List<PetSignatureFeatureDefinition>();
        private readonly Dictionary<string,
            IPetSignatureFeaturePresentationRuntime> _featurePresentations =
            new Dictionary<string,
                IPetSignatureFeaturePresentationRuntime>(StringComparer.Ordinal);
        private readonly List<string> _activeVisualIds = new List<string>();

        private ReadOnlyCollection<PetAmbientVignetteProfile> _availableVignetteView;
        private ReadOnlyCollection<PetSignatureFeatureDefinition>
            _availableFeatureView;
        private ReadOnlyCollection<string> _activeVisualIdView;
        private OguriPetAnimationController _motions;
        private PetAttachmentRig _rig;
        private PetAmbientVignetteProfile _activeProfile;
        private PetSignatureFeatureDefinition _activeFeature;
        private IPetSignatureFeaturePresentationRuntime _activePresentation;
        private bool _initialized;
        private bool _subscribed;
        private bool _isPlaying;
        private bool _startInProgress;
        private bool _applicationQuitting;
        private Vector3 _characterCompactLocalPosition;
        private bool _characterPresentationOffsetApplied;
        private string _characterPresentationOffsetOwnerId = string.Empty;
        private string _currentId = string.Empty;
        private string _currentDisplayName = string.Empty;
        private string _currentStepId = string.Empty;
        private string _currentStageLabel = string.Empty;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private InstalledVignetteEffect _diagnosticFactorEffect;
        private InstalledVignettePrefab _diagnosticPrefab;
        private bool _diagnosticPrefabCharacterWasActive;
        private AppOwnedBoxSceneProp _diagnosticAppOwnedSceneProp;
        private bool _diagnosticAppOwnedScenePropCharacterWasActive;
#endif

        public bool HasAvailableVignettes
        {
            get { return _availableVignettes.Count > 0; }
        }

        public bool HasAvailableFeatures
        {
            get { return _availableFeatures.Count > 0; }
        }

        public IReadOnlyList<PetSignatureFeatureDefinition> AvailableFeatures
        {
            get
            {
                if (_availableFeatureView == null)
                {
                    _availableFeatureView = _availableFeatures.AsReadOnly();
                }
                return _availableFeatureView;
            }
        }

        public IReadOnlyList<PetAmbientVignetteProfile> AvailableVignettes
        {
            get
            {
                if (_availableVignetteView == null)
                {
                    _availableVignetteView = _availableVignettes.AsReadOnly();
                }
                return _availableVignetteView;
            }
        }

        public bool IsPlaying
        {
            get { return _isPlaying; }
        }

        public string CurrentId
        {
            get { return _currentId; }
        }

        public string CurrentDisplayName
        {
            get { return _currentDisplayName; }
        }

        public string CurrentStepId
        {
            get { return _currentStepId; }
        }

        public string CurrentStageLabel
        {
            get { return _currentStageLabel; }
        }

        public float CurrentFramingHorizontalBiasInViewportWidths
        {
            get
            {
                return _activeFeature != null
                    ? _activeFeature.Presentation
                        .FramingHorizontalBiasInViewportWidths
                    : 0.0f;
            }
        }

        public int ActiveVisualCount
        {
            get { return _activeVisualIds.Count; }
        }

        /// <summary>
        /// Stable installed-visual IDs intended for smoke diagnostics and
        /// recording tools. The returned view cannot mutate presenter state.
        /// </summary>
        public IReadOnlyList<string> ActiveVisualIds
        {
            get
            {
                if (_activeVisualIdView == null)
                {
                    _activeVisualIdView = _activeVisualIds.AsReadOnly();
                }
                return _activeVisualIdView;
            }
        }

        public void Initialize(
            PetCharacterDefinition character,
            OguriPetAnimationController motions,
            PetAttachmentRig rig,
            BundleRepository bundles,
            StudyDeskPresenter studyDesk)
        {
            if (_initialized)
            {
                throw new InvalidOperationException(
                    "The ambient vignette presenter is already initialized.");
            }
            if (character == null)
            {
                throw new ArgumentNullException("character");
            }
            if (motions == null)
            {
                throw new ArgumentNullException("motions");
            }
            if (rig == null)
            {
                throw new ArgumentNullException("rig");
            }
            if (studyDesk == null)
            {
                throw new ArgumentNullException("studyDesk");
            }
            if (bundles == null)
            {
                throw new ArgumentNullException("bundles");
            }
            if (!rig.IsInitialized)
            {
                throw new InvalidOperationException(
                    "Initialize the attachment rig before the vignette presenter.");
            }

            _motions = motions;
            _rig = rig;
            _characterCompactLocalPosition = transform.localPosition;
            _characterPresentationOffsetApplied = false;
            _availableVignettes.Clear();
            _availableVignetteView = null;
            _availableFeatures.Clear();
            _availableFeatureView = null;
            DisposeFeaturePresentations();

            IReadOnlyList<PetSignatureFeatureDefinition> configuredFeatures =
                character.SignatureFeatures;
            for (int index = 0; index < configuredFeatures.Count; index++)
            {
                PetSignatureFeatureDefinition feature = configuredFeatures[index];
                if (!motions.HasAmbientVignette(feature.Id))
                {
                    continue;
                }
                IPetSignatureFeaturePresentationRuntime presentation = null;
                try
                {
                    presentation =
                        PetSignatureFeaturePresentationFactory.Create(
                            feature,
                            studyDesk,
                            bundles,
                            rig);
                    if (presentation.IsAvailable)
                    {
                        _availableFeatures.Add(feature);
                        _availableVignettes.Add(feature.Vignette);
                        _featurePresentations.Add(feature.Id, presentation);
                        presentation = null;
                    }
                    else
                    {
                        Debug.LogWarning(
                            feature.Presentation.UnavailableMessage);
                    }
                }
                catch (Exception exception)
                {
                    Debug.LogWarning(
                        feature.Presentation.UnavailableMessage + " (" +
                        exception.GetType().Name + "): " +
                        exception.Message);
                }
                finally
                {
                    if (presentation != null)
                    {
                        presentation.Dispose();
                    }
                }
            }
            if (_availableFeatures.Count > 0)
            {
                _motions.AmbientVignetteStageChanged +=
                    HandleAmbientVignetteStageChanged;
                _subscribed = true;
            }

            _initialized = true;
        }

        public bool TryPlay(string vignetteId)
        {
            if (!_initialized || _isPlaying || _startInProgress ||
                string.IsNullOrWhiteSpace(vignetteId))
            {
                return false;
            }

            PetSignatureFeatureDefinition feature = null;
            IPetSignatureFeaturePresentationRuntime presentation = null;
            _startInProgress = true;
            try
            {
                if (!TryGetAvailableFeature(
                    vignetteId.Trim(),
                    out feature,
                    out presentation))
                {
                    return false;
                }
                if (!presentation.TryAcquire())
                {
                    bool unavailable = !presentation.IsAvailable;
                    RollbackFailedStart(feature, presentation, null);
                    if (unavailable)
                    {
                        Debug.LogWarning(feature.Presentation.UnavailableMessage);
                        RemoveAvailableFeature(feature.Id);
                    }
                    return false;
                }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
                HideDiagnosticFactorEffect();
                HideDiagnosticPrefab();
#endif
                _activeFeature = feature;
                _activeProfile = feature.Vignette;
                _activePresentation = presentation;
                _currentId = feature.Id;
                _currentDisplayName = feature.DisplayName;
                _currentStepId = feature.Vignette.Steps[0].Id;
                _currentStageLabel = string.Empty;
                ApplyCharacterPresentationOffset(feature);

                bool started = _motions != null &&
                    _motions.TryPlayAmbientVignette(feature.Id);
                if (!started)
                {
                    RollbackFailedStart(feature, presentation, null);
                    return false;
                }

                // Setup is specified to arrive synchronously. Keep a deterministic
                // fallback for manual tools without reviving a scene that failed
                // closed and was cancelled by its setup listener.
                if (!_isPlaying && _motions != null && string.Equals(
                    _motions.CurrentAmbientVignetteId,
                    feature.Id,
                    StringComparison.Ordinal))
                {
                    _isPlaying = true;
                    try
                    {
                        ApplyStagePresentation(
                            PetAmbientVignetteStage.Setup,
                            _currentStepId);
                    }
                    catch (Exception exception)
                    {
                        DisableActiveVignette(exception);
                    }
                }
                return _isPlaying;
            }
            catch (Exception exception)
            {
                RollbackFailedStart(feature, presentation, exception);
                return false;
            }
            finally
            {
                _startInProgress = false;
            }
        }

        public bool Cancel()
        {
            if (!_initialized ||
                (!_isPlaying && _activePresentation == null))
            {
                return false;
            }

            bool cancelled = false;
            try
            {
                cancelled = _motions != null &&
                    _motions.CancelAmbientVignette();
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "Could not cancel the ambient vignette motion (" +
                    exception.GetType().Name + "): " + exception.Message);
            }
            finally
            {
                if (!cancelled || _activePresentation != null)
                {
                    // Never strand installed furniture if a controller has
                    // already torn down or a terminal listener was unavailable.
                    ClearPresentationStateSafely("ambient cancellation");
                }
            }
            return cancelled;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>
        /// Development-only gallery seam for reviewing the exact installed
        /// factor-effect family in isolation. It does not alter the production
        /// vignette recipe or its stage routing.
        /// </summary>
        public bool TryShowDiagnosticFactorEffect(
            BundleRepository bundles,
            string factorEffectId)
        {
            if (!_initialized || _isPlaying || bundles == null ||
                string.IsNullOrWhiteSpace(factorEffectId) ||
                _rig == null || !_rig.IsInitialized)
            {
                return false;
            }

            string normalizedId = factorEffectId.Trim();
            if (!FactorEffectPreviewIds.Contains(normalizedId))
            {
                Debug.LogWarning(
                    "Factor effect preview rejected unsupported ID " +
                    normalizedId + ".");
                return false;
            }

            Transform worldSlot;
            if (!_rig.TryGetSlot(PetAttachmentSlots.World, out worldSlot) ||
                worldSlot == null)
            {
                return false;
            }

            InstalledVignetteEffect candidate = null;
            try
            {
                DisposeDiagnosticPrefab();
                if (_diagnosticFactorEffect != null)
                {
                    _diagnosticFactorEffect.Dispose();
                    _diagnosticFactorEffect = null;
                }

                string prefabName = FactorEffectPrefabPrefix + normalizedId;
                candidate = InstalledVignetteEffect.LoadExact(
                    bundles,
                    worldSlot,
                    FactorEffectAssetPrefix + normalizedId,
                    prefabName,
                    "factor-" + normalizedId);

                Bounds bounds = _rig.CharacterLocalBounds;
                float height = Mathf.Max(
                    MinimumCharacterHeight,
                    _rig.CharacterHeight);
                Vector3 previewPosition = new Vector3(
                    bounds.center.x + bounds.extents.x * 0.55f,
                    bounds.center.y + height * 0.03f,
                    bounds.max.z + height * 0.08f);
                candidate.SetLocalPlacement(
                    previewPosition,
                    Quaternion.identity,
                    height * FactorEffectPreviewScaleInCharacterHeights);
                candidate.ShowRestart(FactorEffectPreviewWarmupSeconds);
                _diagnosticFactorEffect = candidate;
                candidate = null;
                return true;
            }
            catch (Exception exception)
            {
                if (candidate != null)
                {
                    candidate.Dispose();
                }
                Debug.LogWarning(
                    "Factor effect preview " + normalizedId +
                    " could not load (" + exception.GetType().Name + ").");
                return false;
            }
        }

        public void HideDiagnosticFactorEffect()
        {
            if (_diagnosticFactorEffect != null)
            {
                _diagnosticFactorEffect.Hide();
            }
        }

        /// <summary>
        /// Development-only exact 3D prefab gallery. The CLI supplies only a
        /// stable allowlist ID; arbitrary logical names never reach the loader.
        /// </summary>
        public bool TryShowDiagnosticPrefab(
            BundleRepository bundles,
            string prefabId,
            float yawDegrees)
        {
            if (!_initialized || _isPlaying || bundles == null ||
                string.IsNullOrWhiteSpace(prefabId) ||
                float.IsNaN(yawDegrees) || float.IsInfinity(yawDegrees) ||
                _rig == null || !_rig.IsInitialized)
            {
                return false;
            }

            string normalizedId = prefabId.Trim();
            DiagnosticPrefabDefinition definition;
            if (!TryResolveDiagnosticPrefab(normalizedId, out definition))
            {
                Debug.LogWarning(
                    "Vignette prefab preview rejected unsupported ID " +
                    normalizedId + ".");
                return false;
            }

            Transform worldSlot;
            if (!_rig.TryGetSlot(PetAttachmentSlots.World, out worldSlot) ||
                worldSlot == null)
            {
                return false;
            }

            InstalledVignettePrefab candidate = null;
            try
            {
                HideDiagnosticFactorEffect();
                DisposeDiagnosticPrefab();
                DisposeDiagnosticAppOwnedSceneProp();
                candidate = InstalledVignettePrefab.LoadExact(
                    bundles,
                    worldSlot,
                    definition.LogicalName,
                    definition.PrefabName,
                    definition.Id);
                PlaceDiagnosticPrefab(
                    candidate,
                    Mathf.Repeat(yawDegrees, 360.0f));
                candidate.Show();
                _diagnosticPrefab = candidate;
                candidate = null;
                _diagnosticPrefabCharacterWasActive =
                    _rig.gameObject.activeSelf;
                _rig.gameObject.SetActive(false);
                return true;
            }
            catch (Exception exception)
            {
                if (candidate != null)
                {
                    candidate.Dispose();
                }
                Debug.LogWarning(
                    "Vignette prefab preview " + normalizedId +
                    " could not load (" + exception.GetType().Name + "): " +
                    exception.Message);
                return false;
            }
        }

        public void HideDiagnosticPrefab()
        {
            if (_diagnosticPrefab != null)
            {
                _diagnosticPrefab.Hide();
            }
        }

        private void PlaceDiagnosticPrefab(
            InstalledVignettePrefab preview,
            float yawDegrees)
        {
            if (preview == null || _rig == null || !_rig.IsInitialized)
            {
                throw new InvalidOperationException(
                    "The installed prefab gallery requires an initialized rig.");
            }

            Bounds bounds = _rig.CharacterLocalBounds;
            float height = _rig.CharacterHeight;
            if (!IsFinitePositive(height) || !IsFinite(bounds.center) ||
                !IsFinite(bounds.max))
            {
                throw new InvalidOperationException(
                    "The character has no usable bounds for the prefab gallery.");
            }

            float maximumDimension = Mathf.Max(
                MinimumCharacterHeight,
                height * PrefabPreviewMaximumDimensionInCharacterHeights);
            Vector3 previewCenter = new Vector3(
                bounds.center.x,
                bounds.center.y,
                bounds.max.z +
                    height * PrefabPreviewDepthInCharacterHeights);
            preview.FitAndPlace(
                previewCenter,
                Quaternion.Euler(0.0f, yawDegrees, 0.0f),
                maximumDimension);
        }

        private void DisposeDiagnosticPrefab()
        {
            if (_diagnosticPrefab == null)
            {
                return;
            }
            InstalledVignettePrefab preview = _diagnosticPrefab;
            _diagnosticPrefab = null;
            preview.Dispose();
            if (_diagnosticPrefabCharacterWasActive && _rig != null)
            {
                _rig.gameObject.SetActive(true);
            }
            _diagnosticPrefabCharacterWasActive = false;
        }

        /// <summary>
        /// Development-only turntable for app-owned signature-scene geometry.
        /// It resolves only profile-declared visual IDs and never accepts assets.
        /// </summary>
        public bool TryShowDiagnosticAppOwnedSceneProp(
            string visualId,
            float yawDegrees,
            float pitchDegrees)
        {
            if (!_initialized || _isPlaying ||
                string.IsNullOrWhiteSpace(visualId) ||
                _rig == null || !_rig.IsInitialized ||
                float.IsNaN(yawDegrees) || float.IsInfinity(yawDegrees) ||
                float.IsNaN(pitchDegrees) || float.IsInfinity(pitchDegrees) ||
                Mathf.Abs(pitchDegrees) > 45.0f)
            {
                return false;
            }

            string normalizedId = visualId.Trim();
            PetAppOwnedScenePropDefinition definition = null;
            for (int featureIndex = 0;
                featureIndex < _availableFeatures.Count && definition == null;
                featureIndex++)
            {
                IReadOnlyList<PetAppOwnedScenePropDefinition> candidates =
                    _availableFeatures[featureIndex].Presentation.AppOwnedSceneProps;
                for (int propIndex = 0; propIndex < candidates.Count; propIndex++)
                {
                    if (string.Equals(
                        candidates[propIndex].VisualId,
                        normalizedId,
                        StringComparison.Ordinal))
                    {
                        definition = candidates[propIndex];
                        break;
                    }
                }
            }
            if (definition == null)
            {
                Debug.LogWarning(
                    "App-owned scene-prop preview rejected unsupported ID " +
                    normalizedId + ".");
                return false;
            }

            Transform worldSlot;
            if (!_rig.TryGetSlot(PetAttachmentSlots.World, out worldSlot) ||
                worldSlot == null)
            {
                return false;
            }

            AppOwnedBoxSceneProp candidate = null;
            try
            {
                HideDiagnosticFactorEffect();
                DisposeDiagnosticPrefab();
                DisposeDiagnosticAppOwnedSceneProp();
                Bounds bounds = _rig.CharacterLocalBounds;
                float height = _rig.CharacterHeight;
                candidate = new AppOwnedBoxSceneProp(
                    definition,
                    worldSlot,
                    bounds.center,
                    height);
                candidate.PlaceForDiagnosticPreview(
                    new Vector3(
                        bounds.center.x,
                        bounds.center.y,
                        bounds.max.z +
                            height * PrefabPreviewDepthInCharacterHeights),
                    Mathf.Repeat(yawDegrees, 360.0f),
                    pitchDegrees);
                candidate.Show();
                _diagnosticAppOwnedSceneProp = candidate;
                candidate = null;
                _diagnosticAppOwnedScenePropCharacterWasActive =
                    _rig.gameObject.activeSelf;
                _rig.gameObject.SetActive(false);
                Debug.Log(
                    "App-owned scene-prop preview visible: id=" + normalizedId +
                    ", yaw=" +
                    Mathf.Repeat(yawDegrees, 360.0f).ToString("0.###") +
                    ", pitch=" + pitchDegrees.ToString("0.###") + ".");
                return true;
            }
            catch (Exception exception)
            {
                if (candidate != null)
                {
                    candidate.Dispose();
                }
                Debug.LogWarning(
                    "App-owned scene-prop preview " + normalizedId +
                    " could not load (" + exception.GetType().Name + "): " +
                    exception.Message);
                return false;
            }
        }

        private void DisposeDiagnosticAppOwnedSceneProp()
        {
            if (_diagnosticAppOwnedSceneProp != null)
            {
                AppOwnedBoxSceneProp preview = _diagnosticAppOwnedSceneProp;
                _diagnosticAppOwnedSceneProp = null;
                preview.Dispose();
            }
            if (_diagnosticAppOwnedScenePropCharacterWasActive && _rig != null)
            {
                _rig.gameObject.SetActive(true);
            }
            _diagnosticAppOwnedScenePropCharacterWasActive = false;
        }

        private static bool TryResolveDiagnosticPrefab(
            string prefabId,
            out DiagnosticPrefabDefinition definition)
        {
            switch (prefabId)
            {
                case Cutin1032MainPrefabId:
                    definition = new DiagnosticPrefabDefinition(
                        Cutin1032MainPrefabId,
                        "3d/env/cutin/cutin1032_02/pfb_env_cutin1032_02_00",
                        "pfb_env_cutin1032_02_00");
                    return true;
                case CelestialGlobe00PrefabId:
                    definition = new DiagnosticPrefabDefinition(
                        CelestialGlobe00PrefabId,
                        "3d/env/cutin/cutin1032_02/" +
                            "pfb_env_cutin1032_02_00_celestialglobe00",
                        "pfb_env_cutin1032_02_00_celestialglobe00");
                    return true;
                case CelestialGlobe01PrefabId:
                    definition = new DiagnosticPrefabDefinition(
                        CelestialGlobe01PrefabId,
                        "3d/env/cutin/cutin1032_02/" +
                            "pfb_env_cutin1032_02_00_celestialglobe01",
                        "pfb_env_cutin1032_02_00_celestialglobe01");
                    return true;
                case Cutin1080DeskPrefabId:
                    definition = new DiagnosticPrefabDefinition(
                        Cutin1080DeskPrefabId,
                        "3d/env/cutin/cutin1080_00/" +
                            "pfb_env_cutin1080_00_00_desk",
                        "pfb_env_cutin1080_00_00_desk");
                    return true;
                case Cutin1080MonitorPrefabId:
                    definition = new DiagnosticPrefabDefinition(
                        Cutin1080MonitorPrefabId,
                        "3d/env/cutin/cutin1080_00/" +
                            "pfb_env_cutin1080_00_00_monitor",
                        "pfb_env_cutin1080_00_00_monitor");
                    return true;
                case Cutin1080RackPrefabId:
                    definition = new DiagnosticPrefabDefinition(
                        Cutin1080RackPrefabId,
                        "3d/env/cutin/cutin1080_00/" +
                            "pfb_env_cutin1080_00_00_rack",
                        "pfb_env_cutin1080_00_00_rack");
                    return true;
                case Cutin1080ChairPrefabId:
                    definition = new DiagnosticPrefabDefinition(
                        Cutin1080ChairPrefabId,
                        "3d/env/cutin/cutin1080_00/" +
                            "pfb_env_cutin1080_00_00_chair",
                        "pfb_env_cutin1080_00_00_chair");
                    return true;
                case Mini10005TablePrefabId:
                    definition = new DiagnosticPrefabDefinition(
                        Mini10005TablePrefabId,
                        "3d/env/mini/mini10005/main/" +
                            "pfb_env_mini10005_main000_000_table_a",
                        "pfb_env_mini10005_main000_000_table_a");
                    return true;
                case Mini10005WhiteboardPrefabId:
                    definition = new DiagnosticPrefabDefinition(
                        Mini10005WhiteboardPrefabId,
                        "3d/env/mini/mini10005/main/" +
                            "pfb_env_mini10005_main000_000_white_board",
                        "pfb_env_mini10005_main000_000_white_board");
                    return true;
                case TachyonProp00PrefabId:
                    definition = CreateTachyonPropDefinition(
                        TachyonProp00PrefabId,
                        "00");
                    return true;
                case TachyonProp01PrefabId:
                    definition = CreateTachyonPropDefinition(
                        TachyonProp01PrefabId,
                        "01");
                    return true;
                case TachyonProp02PrefabId:
                    definition = CreateTachyonPropDefinition(
                        TachyonProp02PrefabId,
                        "02");
                    return true;
                case TachyonProp03PrefabId:
                    definition = CreateTachyonPropDefinition(
                        TachyonProp03PrefabId,
                        "03");
                    return true;
                case TachyonProp04PrefabId:
                    definition = CreateTachyonPropDefinition(
                        TachyonProp04PrefabId,
                        "04");
                    return true;
                case TachyonProp05PrefabId:
                    definition = CreateTachyonPropDefinition(
                        TachyonProp05PrefabId,
                        "05");
                    return true;
                case StudyDeskPrefabId:
                    definition = new DiagnosticPrefabDefinition(
                        StudyDeskPrefabId,
                        "3d/env/set/set10001/prop/" +
                            "pfb_env_set10001_prop001_000",
                        "pfb_env_set10001_prop001_000");
                    return true;
                case StudyChairPrefabId:
                    definition = new DiagnosticPrefabDefinition(
                        StudyChairPrefabId,
                        "3d/env/set/set10001/prop/" +
                            "pfb_env_set10001_prop000_000",
                        "pfb_env_set10001_prop000_000");
                    return true;
                case StudyBoardPrefabId:
                    definition = new DiagnosticPrefabDefinition(
                        StudyBoardPrefabId,
                        "3d/env/set/set10001/prop/" +
                            "pfb_env_set10001_prop002_000",
                        "pfb_env_set10001_prop002_000");
                    return true;
                case StudyBookPrefabId:
                    definition = new DiagnosticPrefabDefinition(
                        StudyBookPrefabId,
                        "3d/chara/prop/prop1025_00/pfb_chr_prop1025_00",
                        "pfb_chr_prop1025_00");
                    return true;
                case Table1031PrefabId:
                    definition = new DiagnosticPrefabDefinition(
                        Table1031PrefabId,
                        "3d/env/cutin/cutin1031_13/" +
                            "pfb_env_cutin1031_13_00_table",
                        "pfb_env_cutin1031_13_00_table");
                    return true;
                case Table1048PrefabId:
                    definition = new DiagnosticPrefabDefinition(
                        Table1048PrefabId,
                        "3d/env/cutin/cutin1048_23/" +
                            "pfb_env_cutin1048_23_00_table",
                        "pfb_env_cutin1048_23_00_table");
                    return true;
                case Book1051PrefabId:
                    definition = new DiagnosticPrefabDefinition(
                        Book1051PrefabId,
                        "3d/env/cutin/cutin1051_26/" +
                            "pfb_env_cutin1051_26_00_book",
                        "pfb_env_cutin1051_26_00_book");
                    return true;
                default:
                    definition = null;
                    return false;
            }
        }

        private static DiagnosticPrefabDefinition CreateTachyonPropDefinition(
            string id,
            string suffix)
        {
            string prefabName = "pfb_chr_prop1032_" + suffix;
            return new DiagnosticPrefabDefinition(
                id,
                "3d/chara/prop/prop1032_" + suffix + "/" + prefabName,
                prefabName);
        }

        private sealed class DiagnosticPrefabDefinition
        {
            public DiagnosticPrefabDefinition(
                string id,
                string logicalName,
                string prefabName)
            {
                Id = id;
                LogicalName = logicalName;
                PrefabName = prefabName;
            }

            public string Id { get; private set; }
            public string LogicalName { get; private set; }
            public string PrefabName { get; private set; }
        }
#endif

        private void HandleAmbientVignetteStageChanged(
            PetAmbientVignetteEvent vignetteEvent)
        {
            if (!_initialized || vignetteEvent == null)
            {
                return;
            }

            bool terminal =
                vignetteEvent.Stage == PetAmbientVignetteStage.Completed ||
                vignetteEvent.Stage == PetAmbientVignetteStage.Interrupted;
            if (terminal)
            {
                if (_activePresentation != null && string.Equals(
                    _currentId,
                    vignetteEvent.VignetteId,
                    StringComparison.Ordinal))
                {
                    ClearPresentationStateSafely(
                        "ambient terminal notification");
                }
                return;
            }

            PetSignatureFeatureDefinition feature;
            IPetSignatureFeaturePresentationRuntime presentation;
            bool matchesAcquiredPresentation =
                _activeFeature != null &&
                _activeProfile != null &&
                _activePresentation != null &&
                string.Equals(
                    _currentId,
                    vignetteEvent.VignetteId,
                    StringComparison.Ordinal) &&
                TryGetAvailableFeature(
                    vignetteEvent.VignetteId,
                    out feature,
                    out presentation) &&
                ReferenceEquals(_activeFeature, feature) &&
                ReferenceEquals(_activePresentation, presentation);
            if (!matchesAcquiredPresentation)
            {
                CancelUnexpectedMotionEvent(vignetteEvent);
                return;
            }

            _isPlaying = true;
            _currentStepId = vignetteEvent.StepId;
            try
            {
                ApplyStagePresentation(
                    vignetteEvent.Stage,
                    vignetteEvent.StepId);
            }
            catch (Exception exception)
            {
                DisableActiveVignette(exception);
            }
        }

        private void ApplyStagePresentation(
            PetAmbientVignetteStage stage,
            string stepId)
        {
            if (_activeFeature == null || _activeProfile == null ||
                _activePresentation == null)
            {
                throw new InvalidOperationException(
                    "The signature feature has no active presentation runtime.");
            }

            _currentStepId = string.IsNullOrEmpty(stepId)
                ? _currentStepId
                : stepId;
            _currentStageLabel = _activePresentation.ApplyStage(
                stage,
                _currentStepId,
                _activeVisualIds);
        }

        private void Update()
        {
            if (!_initialized || !_isPlaying || _activePresentation == null)
            {
                return;
            }

            try
            {
                _activePresentation.Tick(Time.unscaledDeltaTime);
            }
            catch (Exception exception)
            {
                DisableActiveVignette(exception);
            }
        }

        private void DisableActiveVignette(Exception exception)
        {
            string failedId = _currentId;
            string failureMessage = _activeFeature != null
                ? _activeFeature.Presentation.FailureMessage
                : "The installed signature-feature scene failed; omitting " +
                    "only that feature.";
            Debug.LogWarning(
                failureMessage + " (" + exception.GetType().Name + ").");

            ClearPresentationStateSafely("failed signature-feature cleanup");
            RemoveAvailableFeature(failedId);

            if (_motions != null && !string.IsNullOrEmpty(failedId) &&
                string.Equals(
                    _motions.CurrentAmbientVignetteId,
                    failedId,
                    StringComparison.Ordinal))
            {
                try
                {
                    _motions.CancelAmbientVignette();
                }
                catch (Exception cancelError)
                {
                    Debug.LogWarning(
                        "Could not cancel the failed signature-feature motion (" +
                        cancelError.GetType().Name + ").");
                }
            }
        }

        private bool TryGetAvailableVignette(
            string vignetteId,
            out PetAmbientVignetteProfile vignette)
        {
            for (int index = 0; index < _availableVignettes.Count; index++)
            {
                PetAmbientVignetteProfile candidate = _availableVignettes[index];
                if (candidate != null && string.Equals(
                    candidate.Id,
                    vignetteId,
                    StringComparison.Ordinal))
                {
                    vignette = candidate;
                    return true;
                }
            }
            vignette = null;
            return false;
        }

        private bool TryGetAvailableFeature(
            string featureId,
            out PetSignatureFeatureDefinition feature,
            out IPetSignatureFeaturePresentationRuntime presentation)
        {
            for (int index = 0; index < _availableFeatures.Count; index++)
            {
                PetSignatureFeatureDefinition candidate =
                    _availableFeatures[index];
                if (candidate != null && string.Equals(
                    candidate.Id,
                    featureId,
                    StringComparison.Ordinal) &&
                    _featurePresentations.TryGetValue(
                        candidate.Id,
                        out presentation))
                {
                    feature = candidate;
                    return true;
                }
            }

            feature = null;
            presentation = null;
            return false;
        }

        private void ClearPresentationState()
        {
            IPetSignatureFeaturePresentationRuntime presentation =
                _activePresentation;
            RestoreCharacterPresentationOffset();
            _activeVisualIds.Clear();
            _activeFeature = null;
            _activeProfile = null;
            _activePresentation = null;
            _isPlaying = false;
            _currentId = string.Empty;
            _currentDisplayName = string.Empty;
            _currentStepId = string.Empty;
            _currentStageLabel = string.Empty;
            if (presentation != null)
            {
                // Clear presenter ownership before calling external cleanup so
                // even a throwing runtime cannot leave the host half-active.
                presentation.Release();
            }
        }

        private void ClearPresentationStateSafely(string context)
        {
            try
            {
                ClearPresentationState();
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "Could not release the signature-feature presentation during " +
                    context + " (" + exception.GetType().Name + "): " +
                    exception.Message);
            }
        }

        private void RollbackFailedStart(
            PetSignatureFeatureDefinition feature,
            IPetSignatureFeaturePresentationRuntime presentation,
            Exception exception)
        {
            string featureId = feature != null ? feature.Id : string.Empty;
            if (exception != null)
            {
                Debug.LogWarning(
                    "Could not start the signature feature " +
                    (string.IsNullOrEmpty(featureId) ? "scene" : featureId) +
                    "; the acquired presentation was rolled back (" +
                    exception.GetType().Name + "): " + exception.Message);
            }

            if (_motions != null && !string.IsNullOrEmpty(featureId) &&
                string.Equals(
                    _motions.CurrentAmbientVignetteId,
                    featureId,
                    StringComparison.Ordinal))
            {
                try
                {
                    _motions.CancelAmbientVignette();
                }
                catch (Exception cancelException)
                {
                    Debug.LogWarning(
                        "Could not cancel a partially started ambient motion (" +
                        cancelException.GetType().Name + "): " +
                        cancelException.Message);
                }
            }

            if (ReferenceEquals(_activePresentation, presentation))
            {
                // Reset fields first; Release below is allowed to fail without
                // reviving ownership or leaving IsPlaying true.
                _activeVisualIds.Clear();
                _activeFeature = null;
                _activeProfile = null;
                _activePresentation = null;
                _isPlaying = false;
                _currentId = string.Empty;
                _currentDisplayName = string.Empty;
                _currentStepId = string.Empty;
                _currentStageLabel = string.Empty;
            }
            RestoreCharacterPresentationOffset();

            if (presentation != null)
            {
                try
                {
                    presentation.Release();
                }
                catch (Exception releaseException)
                {
                    Debug.LogWarning(
                        "Could not release a partially acquired signature " +
                        "presentation (" + releaseException.GetType().Name +
                        "): " + releaseException.Message);
                }
            }
        }

        private void CancelUnexpectedMotionEvent(
            PetAmbientVignetteEvent vignetteEvent)
        {
            Debug.LogWarning(
                "Ignored ambient vignette stage " + vignetteEvent.Stage +
                " for " + vignetteEvent.VignetteId +
                " because the presenter does not own its presentation.");
            if (_motions == null || !string.Equals(
                _motions.CurrentAmbientVignetteId,
                vignetteEvent.VignetteId,
                StringComparison.Ordinal))
            {
                return;
            }

            try
            {
                _motions.CancelAmbientVignette();
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "Could not cancel the unowned ambient motion (" +
                    exception.GetType().Name + "): " + exception.Message);
            }
        }

        private void ApplyCharacterPresentationOffset(
            PetSignatureFeatureDefinition feature)
        {
            RestoreCharacterPresentationOffset();
            if (feature == null || _rig == null || !_rig.IsInitialized)
            {
                return;
            }

            float horizontalOffset = feature.Presentation
                .CharacterHorizontalOffsetInCharacterHeights;
            if (Mathf.Abs(horizontalOffset) <= 0.000001f)
            {
                return;
            }

            transform.localPosition = _characterCompactLocalPosition +
                Vector3.right * horizontalOffset * _rig.CharacterHeight;
            _characterPresentationOffsetApplied = true;
            _characterPresentationOffsetOwnerId = feature.Id;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log(
                "Signature character presentation offset applied: id=" +
                feature.Id + ", horizontalInCharacterHeights=" +
                horizontalOffset.ToString("0.###") + ".");
#endif
        }

        private void RestoreCharacterPresentationOffset()
        {
            if (!_characterPresentationOffsetApplied)
            {
                return;
            }
            string ownerId = _characterPresentationOffsetOwnerId;
            transform.localPosition = _characterCompactLocalPosition;
            _characterPresentationOffsetApplied = false;
            _characterPresentationOffsetOwnerId = string.Empty;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log(
                "Signature character presentation offset restored: id=" +
                (string.IsNullOrEmpty(ownerId) ? "unknown" : ownerId) + ".");
#endif
        }

        private void RemoveAvailableFeature(string featureId)
        {
            if (string.IsNullOrEmpty(featureId))
            {
                return;
            }

            for (int index = _availableFeatures.Count - 1; index >= 0; index--)
            {
                if (string.Equals(
                    _availableFeatures[index].Id,
                    featureId,
                    StringComparison.Ordinal))
                {
                    _availableFeatures.RemoveAt(index);
                }
            }
            for (int index = _availableVignettes.Count - 1; index >= 0; index--)
            {
                if (string.Equals(
                    _availableVignettes[index].Id,
                    featureId,
                    StringComparison.Ordinal))
                {
                    _availableVignettes.RemoveAt(index);
                }
            }
            IPetSignatureFeaturePresentationRuntime presentation;
            if (_featurePresentations.TryGetValue(featureId, out presentation))
            {
                presentation.Dispose();
                _featurePresentations.Remove(featureId);
            }
            _availableFeatureView = null;
            _availableVignetteView = null;
        }

        private void DisposeFeaturePresentations()
        {
            foreach (IPetSignatureFeaturePresentationRuntime presentation in
                _featurePresentations.Values)
            {
                presentation.Dispose();
            }
            _featurePresentations.Clear();
        }

        private static bool IsFinitePositive(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value) &&
                value > 0.0f;
        }

        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
                !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
                !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        }

        private void OnDestroy()
        {
            ReleaseResources();
        }

        private void OnApplicationQuit()
        {
            _applicationQuitting = true;
        }

        public void ReleaseResources()
        {
            RestoreCharacterPresentationOffset();
            if (!_applicationQuitting &&
                (_isPlaying || _activePresentation != null) &&
                _motions != null)
            {
                try
                {
                    _motions.CancelAmbientVignette();
                }
                catch (Exception exception)
                {
                    Debug.LogWarning(
                        "Could not cancel the ambient vignette during cleanup: " +
                        exception.Message);
                }
            }

            if (_subscribed && _motions != null)
            {
                _motions.AmbientVignetteStageChanged -=
                    HandleAmbientVignetteStageChanged;
            }
            _subscribed = false;
            ClearPresentationStateSafely("presenter cleanup");

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (_diagnosticFactorEffect != null)
            {
                _diagnosticFactorEffect.Dispose();
                _diagnosticFactorEffect = null;
            }
            DisposeDiagnosticPrefab();
            DisposeDiagnosticAppOwnedSceneProp();
#endif

            _availableVignettes.Clear();
            _availableVignetteView = null;
            _availableFeatures.Clear();
            _availableFeatureView = null;
            DisposeFeaturePresentations();
            _motions = null;
            _rig = null;
            _startInProgress = false;
            _initialized = false;
        }
    }
}
