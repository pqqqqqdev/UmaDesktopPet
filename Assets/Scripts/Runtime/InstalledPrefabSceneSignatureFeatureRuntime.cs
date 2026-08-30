using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace UmaDesktopPet.Standalone.Runtime
{
    /// <summary>
    /// Character-neutral runtime for a signature scene assembled from exact
    /// installed GameObject prefabs. Required props fail the feature closed;
    /// optional props enrich installations that contain them without a region or
    /// character branch in the host.
    /// </summary>
    internal sealed class InstalledPrefabSceneSignatureFeatureRuntime :
        IPetSignatureFeaturePresentationRuntime
    {
        private const float MinimumCharacterHeight = 0.0001f;
        private const float PulseAmount = 0.018f;
        private const float PulseCyclesPerSecond = 0.72f;
        private const float SpinDegreesPerSecond = 28.0f;

        private readonly PetSignatureFeaturePresentation _presentation;
        private readonly List<AppOwnedBoxSceneProp> _appOwnedProps =
            new List<AppOwnedBoxSceneProp>();
        private readonly List<LoadedSceneProp> _props =
            new List<LoadedSceneProp>();
        private readonly List<LoadedSceneEffect> _effects =
            new List<LoadedSceneEffect>();
        private readonly HashSet<string> _unavailableOptionalVisualIds =
            new HashSet<string>(StringComparer.Ordinal);
        private readonly float _characterHeight;
        private BundleRepository _bundles;
        private Transform _worldSlot;
        private Vector3 _characterCenter;
        private bool _acquired;
        private bool _disposed;
        private bool _loadFailed;
        private bool _effectsLoaded;
        private float _stepElapsed;
        private string _currentStepId = string.Empty;

        public InstalledPrefabSceneSignatureFeatureRuntime(
            PetSignatureFeaturePresentation presentation,
            BundleRepository bundles,
            PetAttachmentRig rig)
        {
            _presentation = presentation ??
                throw new ArgumentNullException("presentation");
            if (bundles == null)
            {
                throw new ArgumentNullException("bundles");
            }
            if (rig == null)
            {
                throw new ArgumentNullException("rig");
            }
            if (!rig.IsInitialized)
            {
                throw new InvalidOperationException(
                    "Initialize the attachment rig before the installed 3D " +
                    "signature scene.");
            }

            if (!rig.TryGetSlot(PetAttachmentSlots.World, out _worldSlot) ||
                _worldSlot == null)
            {
                throw new InvalidOperationException(
                    "The attachment rig has no world slot for the installed 3D " +
                    "signature scene.");
            }

            Bounds bounds = rig.CharacterLocalBounds;
            _characterHeight = rig.CharacterHeight;
            if (!IsFinitePositive(_characterHeight) ||
                !IsFinite(bounds.center) || !IsFinite(bounds.size))
            {
                throw new InvalidOperationException(
                    "The character has no finite bounds for an installed 3D " +
                    "signature scene.");
            }
            _bundles = bundles;
            _characterCenter = bounds.center;
        }

        public bool IsAvailable
        {
            get
            {
                return !_disposed && !_loadFailed && _bundles != null &&
                    _worldSlot != null;
            }
        }

        public bool TryAcquire()
        {
            if (!IsAvailable || _acquired)
            {
                return false;
            }
            try
            {
                if (_appOwnedProps.Count == 0)
                {
                    LoadAppOwnedSceneProps();
                }
                if (_props.Count == 0)
                {
                    LoadSceneProps();
                }
                if (!_effectsLoaded)
                {
                    LoadSceneEffects();
                }
                _acquired = true;
                _stepElapsed = 0.0f;
                _currentStepId = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                DisposeLoadedEffects();
                DisposeLoadedProps();
                DisposeAppOwnedSceneProps();
                _effectsLoaded = false;
                _loadFailed = true;
                Debug.LogWarning(
                    _presentation.UnavailableMessage + " (" +
                    exception.GetType().Name + "): " + exception.Message);
                return false;
            }
        }

        public string ApplyStage(
            PetAmbientVignetteStage stage,
            string stepId,
            IList<string> activeVisualIds)
        {
            ThrowIfUnavailable();
            if (!_acquired)
            {
                throw new InvalidOperationException(
                    "The installed 3D signature scene was not acquired.");
            }
            if (activeVisualIds == null)
            {
                throw new ArgumentNullException("activeVisualIds");
            }
            if (string.IsNullOrWhiteSpace(stepId))
            {
                throw new ArgumentException(
                    "The installed 3D signature scene requires a step ID.",
                    "stepId");
            }

            string previousStepId = _currentStepId;
            string nextStepId = stepId.Trim();
            bool stepChanged = !string.Equals(
                _currentStepId,
                nextStepId,
                StringComparison.Ordinal);
            _currentStepId = nextStepId;
            if (stepChanged)
            {
                _stepElapsed = 0.0f;
            }
            activeVisualIds.Clear();
            activeVisualIds.Add(_presentation.VisualId);
            for (int index = 0; index < _appOwnedProps.Count; index++)
            {
                AppOwnedBoxSceneProp prop = _appOwnedProps[index];
                bool visible = prop.Definition.IsVisibleInStep(_currentStepId);
                if (visible)
                {
                    prop.Show();
                    activeVisualIds.Add(prop.Definition.VisualId);
                }
                else
                {
                    prop.Hide();
                }
            }
            for (int index = 0; index < _props.Count; index++)
            {
                LoadedSceneProp prop = _props[index];
                bool visible = prop.Definition.IsVisibleInStep(_currentStepId);
                if (visible)
                {
                    prop.Prefab.SetDisplayOffset(
                        Vector3.zero,
                        Quaternion.identity,
                        1.0f);
                    prop.Prefab.Show();
                    activeVisualIds.Add(prop.Definition.VisualId);
                }
                else
                {
                    prop.Prefab.Hide();
                }
                prop.Visible = visible;
            }
            for (int index = 0; index < _effects.Count; index++)
            {
                LoadedSceneEffect effect = _effects[index];
                bool definedForStep =
                    effect.Definition.IsVisibleInStep(_currentStepId);
                bool shouldBeVisible = definedForStep &&
                    stage != PetAmbientVignetteStage.Setup;
                if (shouldBeVisible)
                {
                    // Setup only preloads. A real start happens at StepStart;
                    // direct-loop recovery may also enter from an inactive state.
                    // Already-visible systems are not reset at Loop or End.
                    bool wasDefinedForPreviousStep =
                        effect.Definition.IsVisibleInStep(previousStepId);
                    if (!effect.Visible ||
                        (stepChanged && !wasDefinedForPreviousStep))
                    {
                        if (effect.Definition.Playback ==
                            PetInstalledSceneEffectPlayback.HoldWarmupSnapshot)
                        {
                            effect.Effect.ShowWarmupSnapshot(
                                effect.Definition.WarmupSeconds);
                        }
                        else
                        {
                            effect.Effect.ShowRestart(
                                effect.Definition.WarmupSeconds);
                        }
                    }
                    activeVisualIds.Add(effect.Definition.VisualId);
                }
                else
                {
                    effect.Effect.Hide();
                }
                effect.Visible = shouldBeVisible;
            }
            Tick(0.0f);
            return _presentation.ResolveStageLabel(stage, _currentStepId);
        }

        public void Tick(float unscaledDeltaTime)
        {
            if (!_acquired || _disposed)
            {
                return;
            }
            if (float.IsNaN(unscaledDeltaTime) ||
                float.IsInfinity(unscaledDeltaTime) ||
                unscaledDeltaTime < 0.0f)
            {
                throw new ArgumentOutOfRangeException("unscaledDeltaTime");
            }

            _stepElapsed += unscaledDeltaTime;
            for (int index = 0; index < _props.Count; index++)
            {
                LoadedSceneProp prop = _props[index];
                if (!prop.Visible)
                {
                    continue;
                }

                Quaternion rotationOffset = Quaternion.identity;
                float scale = 1.0f;
                switch (prop.Definition.Motion)
                {
                    case PetInstalledScenePropMotion.None:
                        break;
                    case PetInstalledScenePropMotion.Pulse:
                        scale += PulseAmount * Mathf.Sin(
                            _stepElapsed * Mathf.PI * 2.0f *
                            PulseCyclesPerSecond);
                        break;
                    case PetInstalledScenePropMotion.SpinY:
                        rotationOffset = Quaternion.Euler(
                            0.0f,
                            _stepElapsed * SpinDegreesPerSecond,
                            0.0f);
                        break;
                    default:
                        throw new InvalidOperationException(
                            "Unsupported installed scene prop motion " +
                            prop.Definition.Motion + ".");
                }
                prop.Prefab.SetDisplayOffset(
                    Vector3.zero,
                    rotationOffset,
                    scale);
            }
        }

        public void Release()
        {
            if (_disposed)
            {
                return;
            }
            for (int index = 0; index < _appOwnedProps.Count; index++)
            {
                _appOwnedProps[index].Hide();
            }
            for (int index = 0; index < _props.Count; index++)
            {
                LoadedSceneProp prop = _props[index];
                prop.Visible = false;
                prop.Prefab.Hide();
                prop.Prefab.SetDisplayOffset(
                    Vector3.zero,
                    Quaternion.identity,
                    1.0f);
            }
            for (int index = 0; index < _effects.Count; index++)
            {
                LoadedSceneEffect effect = _effects[index];
                effect.Visible = false;
                effect.Effect.Hide();
            }
            _acquired = false;
            _stepElapsed = 0.0f;
            _currentStepId = string.Empty;
            // A completed or interrupted rare idle must return the repository to
            // its pre-feature lease baseline. Reacquisition reloads exact assets.
            DisposeLoadedEffects();
            DisposeLoadedProps();
            DisposeAppOwnedSceneProps();
            _effectsLoaded = false;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            Release();
            _disposed = true;
            _bundles = null;
            _worldSlot = null;
            _unavailableOptionalVisualIds.Clear();
            _effectsLoaded = false;
        }

        private void LoadAppOwnedSceneProps()
        {
            if (_appOwnedProps.Count != 0)
            {
                throw new InvalidOperationException(
                    "The 3D signature scene attempted to build its app-owned " +
                    "geometry more than once.");
            }

            IReadOnlyList<PetAppOwnedScenePropDefinition> definitions =
                _presentation.AppOwnedSceneProps;
            for (int index = 0; index < definitions.Count; index++)
            {
                PetAppOwnedScenePropDefinition definition = definitions[index];
                if (!definition.Required &&
                    _unavailableOptionalVisualIds.Contains(definition.VisualId))
                {
                    continue;
                }
                AppOwnedBoxSceneProp candidate = null;
                try
                {
                    candidate = new AppOwnedBoxSceneProp(
                        definition,
                        _worldSlot,
                        _characterCenter,
                        _characterHeight);
                    _appOwnedProps.Add(candidate);
                    candidate = null;
                }
                catch (Exception exception)
                {
                    if (candidate != null)
                    {
                        candidate.Dispose();
                    }
                    if (definition.Required)
                    {
                        throw new InvalidDataException(
                            "Required app-owned 3D scene prop unavailable: " +
                            definition.VisualId + ".",
                            exception);
                    }
                    _unavailableOptionalVisualIds.Add(definition.VisualId);
                    Debug.LogWarning(
                        "Optional app-owned 3D scene prop unavailable: id=" +
                        definition.VisualId + " (" +
                        exception.GetType().Name + ").");
                }
            }
        }

        private void LoadSceneProps()
        {
            if (_props.Count != 0)
            {
                throw new InvalidOperationException(
                    "The installed 3D signature scene attempted to load its " +
                    "bounded prop cache more than once.");
            }

            IReadOnlyList<PetInstalledScenePropDefinition> definitions =
                _presentation.InstalledSceneProps;
            for (int index = 0; index < definitions.Count; index++)
            {
                PetInstalledScenePropDefinition definition = definitions[index];
                if (!definition.Required &&
                    _unavailableOptionalVisualIds.Contains(definition.VisualId))
                {
                    continue;
                }
                if (!LoadProp(
                    definition,
                    _bundles,
                    _worldSlot,
                    _characterCenter))
                {
                    _unavailableOptionalVisualIds.Add(definition.VisualId);
                }
            }

            int loadedRequired = 0;
            for (int index = 0; index < _appOwnedProps.Count; index++)
            {
                if (_appOwnedProps[index].Definition.Required)
                {
                    loadedRequired++;
                }
            }
            for (int index = 0; index < _props.Count; index++)
            {
                if (_props[index].Definition.Required)
                {
                    loadedRequired++;
                }
            }
            if (_props.Count + _appOwnedProps.Count == 0 || loadedRequired == 0)
            {
                throw new InvalidDataException(
                    "The 3D signature scene loaded no required scene anchors.");
            }
        }

        private bool LoadProp(
            PetInstalledScenePropDefinition definition,
            BundleRepository bundles,
            Transform worldSlot,
            Vector3 characterCenter)
        {
            InstalledVignettePrefab candidate = null;
            try
            {
                candidate = InstalledVignettePrefab.LoadExact(
                    bundles,
                    worldSlot,
                    definition.LogicalName,
                    definition.AssetName,
                    definition.VisualId);
                Quaternion rotation = Quaternion.Euler(definition.EulerDegrees);
                Vector3 targetCenter = characterCenter +
                    definition.OffsetInCharacterHeights * _characterHeight;
                if (definition.SupportPlacement != null)
                {
                    PetAppOwnedScenePropDefinition supportProp =
                        FindAppOwnedSceneProp(
                            definition.SupportPlacement.SupportVisualId);
                    PetAppOwnedSupportSurfaceDefinition supportSurface =
                        supportProp.FindSupportSurface(
                            definition.SupportPlacement.SurfaceId);
                    if (supportSurface == null)
                    {
                        throw new InvalidDataException(
                            "Missing validated app-owned support surface for " +
                            definition.VisualId + ".");
                    }
                    PetSceneSupportPlacementPlan placement =
                        PetSceneSupportPlacementPlanner.CreatePlan(
                            candidate.SourceBounds,
                            rotation,
                            definition.MaximumDimensionInCharacterHeights,
                            _characterHeight,
                            characterCenter,
                            supportProp,
                            supportSurface,
                            definition.SupportPlacement);
                    targetCenter = placement.LocalCenter;
                }
                candidate.FitAndPlace(
                    targetCenter,
                    rotation,
                    definition.MaximumDimensionInCharacterHeights *
                        _characterHeight);
                candidate.Hide();
                _props.Add(new LoadedSceneProp(definition, candidate));
                candidate = null;
                return true;
            }
            catch (Exception exception)
            {
                if (candidate != null)
                {
                    candidate.Dispose();
                }
                if (definition.Required)
                {
                    throw new InvalidDataException(
                        "Required installed 3D scene prop unavailable: " +
                        definition.VisualId + ".",
                        exception);
                }
                Debug.Log(
                    "Optional installed 3D scene prop unavailable: id=" +
                    definition.VisualId + " (" +
                    exception.GetType().Name + ").");
                return false;
            }
        }

        private void LoadSceneEffects()
        {
            if (_effectsLoaded || _effects.Count != 0)
            {
                throw new InvalidOperationException(
                    "The installed 3D signature scene attempted to load its " +
                    "bounded effect cache more than once.");
            }

            IReadOnlyList<PetInstalledSceneEffectDefinition> definitions =
                _presentation.InstalledSceneEffects;
            for (int index = 0; index < definitions.Count; index++)
            {
                PetInstalledSceneEffectDefinition definition = definitions[index];
                if (!definition.Required &&
                    _unavailableOptionalVisualIds.Contains(definition.VisualId))
                {
                    continue;
                }
                if (!LoadEffect(
                    definition,
                    _bundles,
                    _worldSlot,
                    _characterCenter))
                {
                    _unavailableOptionalVisualIds.Add(definition.VisualId);
                }
            }
            _effectsLoaded = true;
        }

        private bool LoadEffect(
            PetInstalledSceneEffectDefinition definition,
            BundleRepository bundles,
            Transform worldSlot,
            Vector3 characterCenter)
        {
            InstalledVignetteEffect candidate = null;
            try
            {
                candidate = InstalledVignetteEffect.LoadExact(
                    bundles,
                    worldSlot,
                    definition.LogicalName,
                    definition.AssetName,
                    definition.VisualId,
                    definition.Activation);
                candidate.SetLocalPlacement(
                    characterCenter +
                        definition.OffsetInCharacterHeights * _characterHeight,
                    Quaternion.Euler(definition.EulerDegrees),
                    definition.ScaleInCharacterHeights * _characterHeight);
                candidate.Hide();
                _effects.Add(new LoadedSceneEffect(definition, candidate));
                candidate = null;
                return true;
            }
            catch (Exception exception)
            {
                if (candidate != null)
                {
                    candidate.Dispose();
                }
                if (definition.Required)
                {
                    throw new InvalidDataException(
                        "Required installed 3D scene effect unavailable: " +
                        definition.VisualId + ".",
                        exception);
                }
                Debug.Log(
                    "Optional installed 3D scene effect unavailable: id=" +
                    definition.VisualId + " (" +
                    exception.GetType().Name + ").");
                return false;
            }
        }

        private PetAppOwnedScenePropDefinition FindAppOwnedSceneProp(
            string visualId)
        {
            IReadOnlyList<PetAppOwnedScenePropDefinition> definitions =
                _presentation.AppOwnedSceneProps;
            for (int index = 0; index < definitions.Count; index++)
            {
                if (string.Equals(
                    definitions[index].VisualId,
                    visualId,
                    StringComparison.Ordinal))
                {
                    return definitions[index];
                }
            }
            throw new InvalidDataException(
                "Missing validated app-owned scene prop " + visualId + ".");
        }

        private void DisposeAppOwnedSceneProps()
        {
            for (int index = _appOwnedProps.Count - 1; index >= 0; index--)
            {
                _appOwnedProps[index].Dispose();
            }
            _appOwnedProps.Clear();
        }

        private void DisposeLoadedProps()
        {
            for (int index = _props.Count - 1; index >= 0; index--)
            {
                _props[index].Prefab.Dispose();
            }
            _props.Clear();
        }

        private void DisposeLoadedEffects()
        {
            for (int index = _effects.Count - 1; index >= 0; index--)
            {
                _effects[index].Effect.Dispose();
            }
            _effects.Clear();
        }

        private void ThrowIfUnavailable()
        {
            if (_disposed ||
                (_props.Count == 0 && _appOwnedProps.Count == 0))
            {
                throw new ObjectDisposedException(
                    "InstalledPrefabSceneSignatureFeatureRuntime");
            }
        }

        private static bool IsFinitePositive(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value) &&
                value > MinimumCharacterHeight;
        }

        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
                !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
                !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        }

        private sealed class LoadedSceneProp
        {
            public LoadedSceneProp(
                PetInstalledScenePropDefinition definition,
                InstalledVignettePrefab prefab)
            {
                Definition = definition;
                Prefab = prefab;
            }

            public PetInstalledScenePropDefinition Definition
            {
                get;
                private set;
            }

            public InstalledVignettePrefab Prefab { get; private set; }
            public bool Visible { get; set; }
        }

        private sealed class LoadedSceneEffect
        {
            public LoadedSceneEffect(
                PetInstalledSceneEffectDefinition definition,
                InstalledVignetteEffect effect)
            {
                Definition = definition;
                Effect = effect;
            }

            public PetInstalledSceneEffectDefinition Definition
            {
                get;
                private set;
            }

            public InstalledVignetteEffect Effect { get; private set; }
            public bool Visible { get; set; }
        }
    }
}
