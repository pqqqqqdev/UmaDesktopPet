using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace UmaDesktopPet.Standalone.Runtime
{
    /// <summary>
    /// Runtime presentation families supported by the shared signature-feature
    /// host. Character definitions choose a family; shared controllers never
    /// branch on a character ID.
    /// </summary>
    public enum PetSignatureFeaturePresentationKind
    {
        InstalledStudyFurniture,
        InstalledPrefabScene
    }

    /// <summary>
    /// Small app-owned motions that can be applied to an installed scene prop.
    /// The source prefab stays untouched; only the desktop pet's placement pivot
    /// moves.
    /// </summary>
    public enum PetInstalledScenePropMotion
    {
        None,
        Pulse,
        SpinY
    }

    /// <summary>
    /// Determines which authored particle systems may be copied from an exact
    /// installed effect prefab. Externally-triggered systems are opt-in because
    /// playOnAwake=false is also commonly used by sub-emitter-only children.
    /// </summary>
    public enum PetInstalledSceneEffectActivation
    {
        AuthoredPlayOnAwakeOnly,
        IncludeExternallyTriggered
    }

    /// <summary>
    /// Controls whether a copied installed effect follows its authored particle
    /// clock or presents one deterministic, persistent tableau. Signature scenes
    /// use held snapshots when several one-shot effects must read as one visual
    /// rather than disappearing independently.
    /// </summary>
    public enum PetInstalledSceneEffectPlayback
    {
        PlayOnce,
        HoldWarmupSnapshot
    }

    /// <summary>
    /// One exact installed GameObject used by a data-driven signature scene.
    /// Placement is normalized to character height so the same recipe survives
    /// window resizing and future character rigs without controller branches.
    /// </summary>
    public sealed class PetInstalledScenePropDefinition
    {
        private readonly IReadOnlyList<string> _visibleStepIds;

        public PetInstalledScenePropDefinition(
            string visualId,
            string logicalName,
            string assetName,
            bool required,
            IEnumerable<string> visibleStepIds,
            Vector3 offsetInCharacterHeights,
            Vector3 eulerDegrees,
            float maximumDimensionInCharacterHeights,
            PetInstalledScenePropMotion motion,
            PetSceneSupportPlacementDefinition supportPlacement = null)
        {
            if (string.IsNullOrWhiteSpace(visualId) ||
                string.IsNullOrWhiteSpace(logicalName) ||
                string.IsNullOrWhiteSpace(assetName))
            {
                throw new ArgumentException(
                    "An installed scene prop requires stable visual and exact " +
                    "installed-asset names.");
            }
            string normalizedLogicalName = logicalName.Trim();
            string normalizedAssetName = assetName.Trim();
            if (!normalizedLogicalName.StartsWith("3d/", StringComparison.Ordinal) ||
                normalizedLogicalName.IndexOf('\\') >= 0 ||
                normalizedLogicalName.IndexOf(':') >= 0 ||
                normalizedAssetName.IndexOf('/') >= 0 ||
                normalizedAssetName.IndexOf('\\') >= 0)
            {
                throw new ArgumentException(
                    "Installed scene props require one safe 3D logical name and " +
                    "one exact path-free prefab basename.");
            }
            if (!IsFinite(offsetInCharacterHeights) ||
                !IsFinite(eulerDegrees) ||
                !IsFinitePositive(maximumDimensionInCharacterHeights))
            {
                throw new ArgumentOutOfRangeException(
                    "maximumDimensionInCharacterHeights",
                    "Installed scene prop placement must be finite and positive.");
            }
            if (!Enum.IsDefined(typeof(PetInstalledScenePropMotion), motion))
            {
                throw new ArgumentOutOfRangeException("motion");
            }
            if (supportPlacement != null &&
                offsetInCharacterHeights != Vector3.zero)
            {
                throw new ArgumentException(
                    "A surface-grounded installed prop cannot also declare a " +
                    "free placement offset.",
                    "offsetInCharacterHeights");
            }
            if (visibleStepIds == null)
            {
                throw new ArgumentNullException("visibleStepIds");
            }

            var steps = new List<string>();
            var uniqueSteps = new HashSet<string>(StringComparer.Ordinal);
            foreach (string stepId in visibleStepIds)
            {
                if (string.IsNullOrWhiteSpace(stepId) ||
                    !uniqueSteps.Add(stepId.Trim()))
                {
                    throw new ArgumentException(
                        "Installed scene prop step IDs must be non-empty and unique.",
                        "visibleStepIds");
                }
                steps.Add(stepId.Trim());
            }
            if (steps.Count == 0)
            {
                throw new ArgumentException(
                    "An installed scene prop must be visible in at least one step.",
                    "visibleStepIds");
            }

            VisualId = visualId.Trim();
            LogicalName = normalizedLogicalName;
            AssetName = normalizedAssetName;
            Required = required;
            OffsetInCharacterHeights = offsetInCharacterHeights;
            EulerDegrees = eulerDegrees;
            MaximumDimensionInCharacterHeights =
                maximumDimensionInCharacterHeights;
            Motion = motion;
            SupportPlacement = supportPlacement;
            _visibleStepIds = new ReadOnlyCollection<string>(steps);
        }

        public string VisualId { get; private set; }
        public string LogicalName { get; private set; }
        public string AssetName { get; private set; }
        public bool Required { get; private set; }
        public Vector3 OffsetInCharacterHeights { get; private set; }
        public Vector3 EulerDegrees { get; private set; }
        public float MaximumDimensionInCharacterHeights { get; private set; }
        public PetInstalledScenePropMotion Motion { get; private set; }
        public PetSceneSupportPlacementDefinition SupportPlacement
        {
            get;
            private set;
        }
        public IReadOnlyList<string> VisibleStepIds
        {
            get { return _visibleStepIds; }
        }

        public bool IsVisibleInStep(string stepId)
        {
            if (string.IsNullOrWhiteSpace(stepId))
            {
                return false;
            }
            string normalized = stepId.Trim();
            for (int index = 0; index < _visibleStepIds.Count; index++)
            {
                if (string.Equals(
                    _visibleStepIds[index],
                    normalized,
                    StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
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
    }

    /// <summary>
    /// One exact installed particle-effect prefab used by a signature scene.
    /// The runtime never activates this foreign prefab. It copies a bounded,
    /// renderer-enabled subset into app-owned ParticleSystem objects instead.
    /// </summary>
    public sealed class PetInstalledSceneEffectDefinition
    {
        private readonly IReadOnlyList<string> _visibleStepIds;

        public PetInstalledSceneEffectDefinition(
            string visualId,
            string logicalName,
            string assetName,
            bool required,
            IEnumerable<string> visibleStepIds,
            Vector3 offsetInCharacterHeights,
            Vector3 eulerDegrees,
            float scaleInCharacterHeights,
            float warmupSeconds,
            PetInstalledSceneEffectActivation activation,
            PetInstalledSceneEffectPlayback playback)
        {
            if (string.IsNullOrWhiteSpace(visualId) ||
                string.IsNullOrWhiteSpace(logicalName) ||
                string.IsNullOrWhiteSpace(assetName))
            {
                throw new ArgumentException(
                    "An installed scene effect requires stable visual and exact " +
                    "installed-asset names.");
            }
            string normalizedLogicalName = logicalName.Trim();
            string normalizedAssetName = assetName.Trim();
            if (!normalizedLogicalName.StartsWith(
                    "3d/effect/",
                    StringComparison.Ordinal) ||
                normalizedLogicalName.IndexOf('\\') >= 0 ||
                normalizedLogicalName.IndexOf(':') >= 0 ||
                normalizedAssetName.IndexOf('/') >= 0 ||
                normalizedAssetName.IndexOf('\\') >= 0)
            {
                throw new ArgumentException(
                    "Installed scene effects require one safe 3D effect logical " +
                    "name and one exact path-free prefab basename.");
            }
            if (!IsFinite(offsetInCharacterHeights) ||
                !IsFinite(eulerDegrees) ||
                !IsFinitePositive(scaleInCharacterHeights) ||
                float.IsNaN(warmupSeconds) || float.IsInfinity(warmupSeconds) ||
                warmupSeconds < 0.0f || warmupSeconds > 5.0f)
            {
                throw new ArgumentOutOfRangeException(
                    "scaleInCharacterHeights",
                    "Installed scene effect placement and warmup must be finite " +
                    "and bounded.");
            }
            if (!Enum.IsDefined(
                typeof(PetInstalledSceneEffectActivation),
                activation))
            {
                throw new ArgumentOutOfRangeException("activation");
            }
            if (!Enum.IsDefined(
                typeof(PetInstalledSceneEffectPlayback),
                playback) ||
                playback == PetInstalledSceneEffectPlayback.HoldWarmupSnapshot &&
                warmupSeconds <= 0.0f)
            {
                throw new ArgumentOutOfRangeException("playback");
            }
            if (visibleStepIds == null)
            {
                throw new ArgumentNullException("visibleStepIds");
            }

            var steps = new List<string>();
            var uniqueSteps = new HashSet<string>(StringComparer.Ordinal);
            foreach (string stepId in visibleStepIds)
            {
                if (string.IsNullOrWhiteSpace(stepId) ||
                    !uniqueSteps.Add(stepId.Trim()))
                {
                    throw new ArgumentException(
                        "Installed scene effect step IDs must be non-empty and " +
                        "unique.",
                        "visibleStepIds");
                }
                steps.Add(stepId.Trim());
            }
            if (steps.Count == 0)
            {
                throw new ArgumentException(
                    "An installed scene effect must be visible in at least one " +
                    "step.",
                    "visibleStepIds");
            }

            VisualId = visualId.Trim();
            LogicalName = normalizedLogicalName;
            AssetName = normalizedAssetName;
            Required = required;
            OffsetInCharacterHeights = offsetInCharacterHeights;
            EulerDegrees = eulerDegrees;
            ScaleInCharacterHeights = scaleInCharacterHeights;
            WarmupSeconds = warmupSeconds;
            Activation = activation;
            Playback = playback;
            _visibleStepIds = new ReadOnlyCollection<string>(steps);
        }

        public string VisualId { get; private set; }
        public string LogicalName { get; private set; }
        public string AssetName { get; private set; }
        public bool Required { get; private set; }
        public Vector3 OffsetInCharacterHeights { get; private set; }
        public Vector3 EulerDegrees { get; private set; }
        public float ScaleInCharacterHeights { get; private set; }
        public float WarmupSeconds { get; private set; }
        public PetInstalledSceneEffectActivation Activation { get; private set; }
        public PetInstalledSceneEffectPlayback Playback { get; private set; }
        public IReadOnlyList<string> VisibleStepIds
        {
            get { return _visibleStepIds; }
        }

        public bool IsVisibleInStep(string stepId)
        {
            if (string.IsNullOrWhiteSpace(stepId))
            {
                return false;
            }
            string normalized = stepId.Trim();
            for (int index = 0; index < _visibleStepIds.Count; index++)
            {
                if (string.Equals(
                    _visibleStepIds[index],
                    normalized,
                    StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
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
    }

    public sealed class PetSignatureFeatureStepPresentation
    {
        public PetSignatureFeatureStepPresentation(
            string stepId,
            string startLabel,
            string loopLabel,
            string endLabel)
        {
            if (string.IsNullOrWhiteSpace(stepId) ||
                string.IsNullOrWhiteSpace(startLabel) ||
                string.IsNullOrWhiteSpace(loopLabel) ||
                string.IsNullOrWhiteSpace(endLabel))
            {
                throw new ArgumentException(
                    "Signature-feature step presentation requires an ID and " +
                    "start, loop, and end labels.");
            }

            StepId = stepId.Trim();
            StartLabel = startLabel.Trim();
            LoopLabel = loopLabel.Trim();
            EndLabel = endLabel.Trim();
        }

        public string StepId { get; private set; }
        public string StartLabel { get; private set; }
        public string LoopLabel { get; private set; }
        public string EndLabel { get; private set; }

        public string ResolveLabel(PetAmbientVignetteStage stage)
        {
            switch (stage)
            {
                case PetAmbientVignetteStage.StepStart:
                    return StartLabel;
                case PetAmbientVignetteStage.StepLoop:
                    return LoopLabel;
                case PetAmbientVignetteStage.StepEnd:
                    return EndLabel;
                default:
                    throw new InvalidOperationException(
                        "A signature-feature step label was requested for " +
                        "a non-step lifecycle stage.");
            }
        }
    }

    /// <summary>
    /// App-owned visual and UI recipe for one signature feature. It contains
    /// stable labels and logical IDs only; installed game assets stay installed.
    /// </summary>
    public sealed class PetSignatureFeaturePresentation
    {
        private readonly IReadOnlyDictionary<string,
            PetSignatureFeatureStepPresentation> _steps;
        private readonly IReadOnlyList<PetInstalledScenePropDefinition>
            _installedSceneProps;
        private readonly IReadOnlyList<PetInstalledSceneEffectDefinition>
            _installedSceneEffects;
        private readonly IReadOnlyList<PetAppOwnedScenePropDefinition>
            _appOwnedSceneProps;

        public PetSignatureFeaturePresentation(
            string recipeId,
            PetSignatureFeaturePresentationKind kind,
            string visualId,
            string setupLabel,
            string unavailableMessage,
            string failureMessage,
            IEnumerable<PetSignatureFeatureStepPresentation> steps,
            IEnumerable<PetInstalledScenePropDefinition> installedSceneProps = null,
            IEnumerable<PetInstalledSceneEffectDefinition>
                installedSceneEffects = null,
            IEnumerable<PetAppOwnedScenePropDefinition>
                appOwnedSceneProps = null,
            float characterHorizontalOffsetInCharacterHeights = 0.0f,
            float framingHorizontalBiasInViewportWidths = 0.0f)
        {
            if (string.IsNullOrWhiteSpace(recipeId) ||
                string.IsNullOrWhiteSpace(visualId) ||
                string.IsNullOrWhiteSpace(setupLabel) ||
                string.IsNullOrWhiteSpace(unavailableMessage) ||
                string.IsNullOrWhiteSpace(failureMessage))
            {
                throw new ArgumentException(
                    "A signature-feature presentation requires stable IDs, " +
                    "labels, and failure messages.");
            }
            if (!Enum.IsDefined(typeof(PetSignatureFeaturePresentationKind), kind))
            {
                throw new ArgumentOutOfRangeException("kind");
            }
            if (float.IsNaN(characterHorizontalOffsetInCharacterHeights) ||
                float.IsInfinity(characterHorizontalOffsetInCharacterHeights) ||
                Mathf.Abs(characterHorizontalOffsetInCharacterHeights) > 0.25f)
            {
                throw new ArgumentOutOfRangeException(
                    "characterHorizontalOffsetInCharacterHeights");
            }
            if (float.IsNaN(framingHorizontalBiasInViewportWidths) ||
                float.IsInfinity(framingHorizontalBiasInViewportWidths) ||
                Mathf.Abs(framingHorizontalBiasInViewportWidths) > 0.1f)
            {
                throw new ArgumentOutOfRangeException(
                    "framingHorizontalBiasInViewportWidths");
            }
            if (steps == null)
            {
                throw new ArgumentNullException("steps");
            }

            var map = new Dictionary<string,
                PetSignatureFeatureStepPresentation>(StringComparer.Ordinal);
            foreach (PetSignatureFeatureStepPresentation step in steps)
            {
                if (step == null || map.ContainsKey(step.StepId))
                {
                    throw new ArgumentException(
                        "Signature-feature presentation steps must be non-null " +
                        "and have unique IDs.",
                        "steps");
                }
                map.Add(step.StepId, step);
            }
            if (map.Count == 0)
            {
                throw new ArgumentException(
                    "A signature-feature presentation requires at least one step.",
                    "steps");
            }

            var sceneVisualIds = new HashSet<string>(StringComparer.Ordinal);
            var appSceneProps = new List<PetAppOwnedScenePropDefinition>();
            var appScenePropsById =
                new Dictionary<string, PetAppOwnedScenePropDefinition>(
                    StringComparer.Ordinal);
            bool hasRequiredSceneAnchor = false;
            if (appOwnedSceneProps != null)
            {
                foreach (PetAppOwnedScenePropDefinition prop in
                    appOwnedSceneProps)
                {
                    if (prop == null || !sceneVisualIds.Add(prop.VisualId))
                    {
                        throw new ArgumentException(
                            "App-owned scene props must be non-null and use " +
                            "unique visual IDs.",
                            "appOwnedSceneProps");
                    }
                    for (int stepIndex = 0;
                        stepIndex < prop.VisibleStepIds.Count;
                        stepIndex++)
                    {
                        if (!map.ContainsKey(prop.VisibleStepIds[stepIndex]))
                        {
                            throw new ArgumentException(
                                "App-owned scene prop " + prop.VisualId +
                                " references unknown step " +
                                prop.VisibleStepIds[stepIndex] + ".",
                                "appOwnedSceneProps");
                        }
                    }
                    hasRequiredSceneAnchor |= prop.Required;
                    appSceneProps.Add(prop);
                    appScenePropsById.Add(prop.VisualId, prop);
                }
            }

            var sceneProps = new List<PetInstalledScenePropDefinition>();
            if (installedSceneProps != null)
            {
                foreach (PetInstalledScenePropDefinition prop in installedSceneProps)
                {
                    if (prop == null || !sceneVisualIds.Add(prop.VisualId))
                    {
                        throw new ArgumentException(
                            "Installed scene props must be non-null and use unique " +
                            "visual IDs.",
                            "installedSceneProps");
                    }
                    for (int stepIndex = 0;
                        stepIndex < prop.VisibleStepIds.Count;
                        stepIndex++)
                    {
                        if (!map.ContainsKey(prop.VisibleStepIds[stepIndex]))
                        {
                            throw new ArgumentException(
                                "Installed scene prop " + prop.VisualId +
                                " references unknown step " +
                                prop.VisibleStepIds[stepIndex] + ".",
                                "installedSceneProps");
                        }
                    }
                    if (prop.SupportPlacement != null)
                    {
                        PetAppOwnedScenePropDefinition supportProp;
                        if (!appScenePropsById.TryGetValue(
                                prop.SupportPlacement.SupportVisualId,
                                out supportProp))
                        {
                            throw new ArgumentException(
                                "Installed scene prop " + prop.VisualId +
                                " references unknown app-owned support " +
                                prop.SupportPlacement.SupportVisualId + ".",
                                "installedSceneProps");
                        }
                        if (supportProp.FindSupportSurface(
                                prop.SupportPlacement.SurfaceId) == null)
                        {
                            throw new ArgumentException(
                                "Installed scene prop " + prop.VisualId +
                                " references unknown support surface " +
                                prop.SupportPlacement.SurfaceId + ".",
                                "installedSceneProps");
                        }
                        for (int stepIndex = 0;
                            stepIndex < prop.VisibleStepIds.Count;
                            stepIndex++)
                        {
                            if (!supportProp.IsVisibleInStep(
                                    prop.VisibleStepIds[stepIndex]))
                            {
                                throw new ArgumentException(
                                    "Installed scene prop " + prop.VisualId +
                                    " is visible without its app-owned support.",
                                    "installedSceneProps");
                            }
                        }
                    }
                    hasRequiredSceneAnchor |= prop.Required;
                    sceneProps.Add(prop);
                }
            }

            var sceneEffects = new List<PetInstalledSceneEffectDefinition>();
            if (installedSceneEffects != null)
            {
                foreach (PetInstalledSceneEffectDefinition effect in
                    installedSceneEffects)
                {
                    if (effect == null || !sceneVisualIds.Add(effect.VisualId))
                    {
                        throw new ArgumentException(
                            "Installed scene effects must be non-null and use " +
                            "visual IDs unique across the whole scene.",
                            "installedSceneEffects");
                    }
                    for (int stepIndex = 0;
                        stepIndex < effect.VisibleStepIds.Count;
                        stepIndex++)
                    {
                        if (!map.ContainsKey(effect.VisibleStepIds[stepIndex]))
                        {
                            throw new ArgumentException(
                                "Installed scene effect " + effect.VisualId +
                                " references unknown step " +
                                effect.VisibleStepIds[stepIndex] + ".",
                                "installedSceneEffects");
                        }
                    }
                    sceneEffects.Add(effect);
                }
            }
            if (kind == PetSignatureFeaturePresentationKind.InstalledPrefabScene)
            {
                if (sceneProps.Count + appSceneProps.Count == 0 ||
                    !hasRequiredSceneAnchor)
                {
                    throw new ArgumentException(
                        "A composite 3D scene requires at least one prop and one " +
                        "required scene anchor.",
                        "installedSceneProps");
                }
            }
            else if (sceneProps.Count != 0 || sceneEffects.Count != 0 ||
                appSceneProps.Count != 0)
            {
                throw new ArgumentException(
                    "Only a 3D signature scene may declare app-owned or installed " +
                    "scene props and effects.",
                    "installedSceneProps");
            }

            RecipeId = recipeId.Trim();
            Kind = kind;
            VisualId = visualId.Trim();
            SetupLabel = setupLabel.Trim();
            UnavailableMessage = unavailableMessage.Trim();
            FailureMessage = failureMessage.Trim();
            CharacterHorizontalOffsetInCharacterHeights =
                characterHorizontalOffsetInCharacterHeights;
            FramingHorizontalBiasInViewportWidths =
                framingHorizontalBiasInViewportWidths;
            _steps = new ReadOnlyDictionary<string,
                PetSignatureFeatureStepPresentation>(map);
            _installedSceneProps =
                new ReadOnlyCollection<PetInstalledScenePropDefinition>(
                    sceneProps);
            _installedSceneEffects =
                new ReadOnlyCollection<PetInstalledSceneEffectDefinition>(
                    sceneEffects);
            _appOwnedSceneProps =
                new ReadOnlyCollection<PetAppOwnedScenePropDefinition>(
                    appSceneProps);
        }

        public string RecipeId { get; private set; }
        public PetSignatureFeaturePresentationKind Kind { get; private set; }
        public string VisualId { get; private set; }
        public string SetupLabel { get; private set; }
        public string UnavailableMessage { get; private set; }
        public string FailureMessage { get; private set; }
        public float CharacterHorizontalOffsetInCharacterHeights
        {
            get;
            private set;
        }
        public float FramingHorizontalBiasInViewportWidths
        {
            get;
            private set;
        }
        public IReadOnlyList<PetInstalledScenePropDefinition> InstalledSceneProps
        {
            get { return _installedSceneProps; }
        }
        public IReadOnlyList<PetInstalledSceneEffectDefinition>
            InstalledSceneEffects
        {
            get { return _installedSceneEffects; }
        }
        public IReadOnlyList<PetAppOwnedScenePropDefinition> AppOwnedSceneProps
        {
            get { return _appOwnedSceneProps; }
        }

        public string ResolveStageLabel(
            PetAmbientVignetteStage stage,
            string stepId)
        {
            if (stage == PetAmbientVignetteStage.Setup)
            {
                return SetupLabel;
            }

            PetSignatureFeatureStepPresentation step;
            if (string.IsNullOrWhiteSpace(stepId) ||
                !_steps.TryGetValue(stepId.Trim(), out step))
            {
                throw new InvalidOperationException(
                    "The signature feature entered an unknown presentation step.");
            }
            return step.ResolveLabel(stage);
        }

        internal bool HasStep(string stepId)
        {
            return !string.IsNullOrWhiteSpace(stepId) &&
                _steps.ContainsKey(stepId.Trim());
        }
    }

    public sealed class PetSignatureFeatureDefinition
    {
        public PetSignatureFeatureDefinition(
            PetAmbientVignetteProfile vignette,
            PetSignatureFeaturePresentation presentation)
        {
            if (vignette == null)
            {
                throw new ArgumentNullException("vignette");
            }
            if (presentation == null)
            {
                throw new ArgumentNullException("presentation");
            }
            if (!string.Equals(
                vignette.VisualRecipeId,
                presentation.RecipeId,
                StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "The signature feature motion and presentation recipe IDs " +
                    "must match.");
            }
            for (int index = 0; index < vignette.Steps.Count; index++)
            {
                if (!presentation.HasStep(vignette.Steps[index].Id))
                {
                    throw new ArgumentException(
                        "The signature feature has no presentation for motion " +
                        "step " + vignette.Steps[index].Id + ".");
                }
            }

            Vignette = vignette;
            Presentation = presentation;
        }

        public string Id { get { return Vignette.Id; } }
        public string DisplayName { get { return Vignette.DisplayName; } }
        public float AutonomyWeight { get { return Vignette.AutonomyWeight; } }
        public PetAmbientVignetteProfile Vignette { get; private set; }
        public PetSignatureFeaturePresentation Presentation { get; private set; }
    }

    /// <summary>
    /// One complete, app-owned definition for a supported desktop Uma. Shared
    /// runtime code resolves this once instead of consulting parallel catalogs.
    /// </summary>
    public sealed class PetCharacterDefinition
    {
        private readonly IReadOnlyList<PetSignatureFeatureDefinition>
            _signatureFeatures;

        public PetCharacterDefinition(
            PetCharacterProfile profile,
            MiniCharacterAssemblyProfile assembly,
            PetCharacterMotionProfile motions,
            PetAttachmentProfile attachments,
            IEnumerable<PetSignatureFeatureDefinition> signatureFeatures)
        {
            if (profile == null)
            {
                throw new ArgumentNullException("profile");
            }
            if (assembly == null)
            {
                throw new ArgumentNullException("assembly");
            }
            if (motions == null)
            {
                throw new ArgumentNullException("motions");
            }
            if (attachments == null)
            {
                throw new ArgumentNullException("attachments");
            }
            if (!Matches(profile, assembly, motions, attachments))
            {
                throw new ArgumentException(
                    "Every part of a character definition must use the same " +
                    "character key and game ID.");
            }

            if (signatureFeatures == null)
            {
                throw new ArgumentNullException("signatureFeatures");
            }

            var orderedFeatures = new List<PetSignatureFeatureDefinition>();
            var featureIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (PetSignatureFeatureDefinition feature in signatureFeatures)
            {
                if (feature == null || !featureIds.Add(feature.Id))
                {
                    throw new ArgumentException(
                        "Character signature features must be non-null and use " +
                        "unique IDs.",
                        "signatureFeatures");
                }
                orderedFeatures.Add(feature);
            }
            if (orderedFeatures.Count != motions.AmbientVignettes.Count)
            {
                throw new ArgumentException(
                    "Every character vignette motion requires exactly one " +
                    "signature-feature presentation definition.");
            }
            for (int motionIndex = 0;
                motionIndex < motions.AmbientVignettes.Count;
                motionIndex++)
            {
                bool matched = false;
                for (int featureIndex = 0;
                    featureIndex < orderedFeatures.Count;
                    featureIndex++)
                {
                    if (ReferenceEquals(
                        motions.AmbientVignettes[motionIndex],
                        orderedFeatures[featureIndex].Vignette))
                    {
                        matched = true;
                        break;
                    }
                }
                if (!matched)
                {
                    throw new ArgumentException(
                        "A signature feature must own each exact configured " +
                        "vignette motion.");
                }
            }

            Profile = profile;
            Assembly = assembly;
            Motions = motions;
            Attachments = attachments;
            _signatureFeatures =
                new ReadOnlyCollection<PetSignatureFeatureDefinition>(
                    orderedFeatures);
        }

        public string Key { get { return Profile.Key; } }
        public PetCharacterProfile Profile { get; private set; }
        public MiniCharacterAssemblyProfile Assembly { get; private set; }
        public PetCharacterMotionProfile Motions { get; private set; }
        public PetAttachmentProfile Attachments { get; private set; }
        public IReadOnlyList<PetSignatureFeatureDefinition> SignatureFeatures
        {
            get { return _signatureFeatures; }
        }

        private static bool Matches(
            PetCharacterProfile profile,
            MiniCharacterAssemblyProfile assembly,
            PetCharacterMotionProfile motions,
            PetAttachmentProfile attachments)
        {
            return string.Equals(profile.Key, assembly.Key, StringComparison.Ordinal) &&
                string.Equals(profile.Key, motions.CharacterKey, StringComparison.Ordinal) &&
                string.Equals(
                    profile.Key,
                    attachments.CharacterKey,
                    StringComparison.Ordinal) &&
                profile.GameCharacterId == assembly.CharacterId &&
                profile.GameCharacterId == motions.CharacterId;
        }
    }

    public static class PetCharacterDefinitions
    {
        public const string TachyonExperimentId = "tachyon-experiment";
        public const string TachyonExperimentRecipeId = "tachyon-experiment";
        public const string TachyonExperimentSceneVisualId =
            "installed-3d-laboratory";
        public const string TachyonLabTableVisualId = "lab-workstation";
        public const string TachyonLabBenchResourcePath =
            "AppOwned/TachyonLabBench";
        public const float TachyonLabBenchHeightInCharacterHeights = 0.28f;
        public const float TachyonLabBenchYawDegrees = 12.0f;
        public const string TachyonCyanReagentVisualId = "cyan-reagent";
        public const string TachyonPurpleReagentVisualId = "purple-reagent";
        public const string TachyonYellowReagentVisualId = "yellow-reagent";
        public const string TachyonAmberReagentVisualId = "amber-reagent";
        public const string TachyonBlueReagentVisualId = "deep-blue-reagent";
        public const string TachyonFormula01VisualId = "tachyon-formula-01";
        public const string TachyonFormula02VisualId = "tachyon-formula-02";
        public const string TachyonFormula03VisualId = "tachyon-formula-03";
        public const string TachyonFormula04VisualId = "tachyon-formula-04";
        public const string TachyonFormula05VisualId = "tachyon-formula-05";
        public const string TachyonResearchStepId = "research";
        public const string TachyonBreakthroughStepId = "breakthrough";

        public static readonly PetCharacterDefinition Oguri =
            new PetCharacterDefinition(
                PetCharacterCatalog.Oguri,
                MiniCharacterAssemblyProfiles.OguriDefault,
                PetCharacterMotionProfiles.Oguri,
                PetAttachmentProfileCatalog.OguriCap,
                new PetSignatureFeatureDefinition[0]);

        public static readonly PetCharacterDefinition Tachyon =
            new PetCharacterDefinition(
                PetCharacterCatalog.Tachyon,
                MiniCharacterAssemblyProfiles.TachyonDefault,
                PetCharacterMotionProfiles.Tachyon,
                PetAttachmentProfileCatalog.AgnesTachyon,
                new[] { CreateTachyonExperiment() });

        private static readonly PetCharacterDefinition[] SelectableDefinitions =
        {
            Oguri,
            Tachyon
        };
        private static readonly IReadOnlyList<PetCharacterDefinition>
            SelectableView = Array.AsReadOnly(SelectableDefinitions);

        public static IReadOnlyList<PetCharacterDefinition> Selectable
        {
            get { return SelectableView; }
        }

        public static bool TryGet(
            string characterKey,
            out PetCharacterDefinition definition)
        {
            if (!string.IsNullOrWhiteSpace(characterKey))
            {
                string normalized = characterKey.Trim();
                for (int index = 0; index < SelectableDefinitions.Length; index++)
                {
                    PetCharacterDefinition candidate =
                        SelectableDefinitions[index];
                    if (string.Equals(
                        candidate.Key,
                        normalized,
                        StringComparison.Ordinal))
                    {
                        definition = candidate;
                        return true;
                    }
                }
            }

            definition = null;
            return false;
        }

        public static PetCharacterDefinition ResolveOrDefault(string characterKey)
        {
            PetCharacterDefinition definition;
            return TryGet(characterKey, out definition) ? definition : Oguri;
        }

        private static PetSignatureFeatureDefinition CreateTachyonExperiment()
        {
            PetAmbientVignetteProfile vignette =
                PetCharacterMotionProfiles.Tachyon.AmbientVignettes[0];
            return new PetSignatureFeatureDefinition(
                vignette,
                new PetSignatureFeaturePresentation(
                    TachyonExperimentRecipeId,
                    PetSignatureFeaturePresentationKind.InstalledPrefabScene,
                    TachyonExperimentSceneVisualId,
                    "Assembling the laboratory",
                    "The experiment needs its exact installed reagents and " +
                        "formula effects; omitting only that feature because a " +
                        "required asset is unavailable.",
                    "The installed experiment scene failed; omitting only " +
                        "that signature feature.",
                    new[]
                    {
                        new PetSignatureFeatureStepPresentation(
                            TachyonResearchStepId,
                            "Research starting",
                            "Research",
                            "Research finishing"),
                        new PetSignatureFeatureStepPresentation(
                            TachyonBreakthroughStepId,
                            "Experiment result starting",
                            "Experiment result",
                            "Experiment result complete")
                    },
                    CreateTachyonExperimentProps(),
                    CreateTachyonExperimentEffects(),
                    CreateTachyonExperimentAppOwnedProps(),
                    -0.07f,
                    0.02f));
        }

        private static PetInstalledScenePropDefinition[]
            CreateTachyonExperimentProps()
        {
            string[] bothSteps =
            {
                TachyonResearchStepId,
                TachyonBreakthroughStepId
            };
            return new[]
            {
                CreateTachyonReagent(
                    TachyonCyanReagentVisualId,
                    "00",
                    bothSteps,
                    -0.30f,
                    -0.25f),
                CreateTachyonReagent(
                    TachyonPurpleReagentVisualId,
                    "01",
                    bothSteps,
                    -0.30f,
                    0.25f),
                CreateTachyonReagent(
                    TachyonYellowReagentVisualId,
                    "02",
                    bothSteps,
                    0.0f,
                    0.0f),
                CreateTachyonReagent(
                    TachyonAmberReagentVisualId,
                    "03",
                    bothSteps,
                    0.30f,
                    -0.25f),
                CreateTachyonReagent(
                    TachyonBlueReagentVisualId,
                    "04",
                    bothSteps,
                    0.30f,
                    0.25f)
            };
        }

        private static PetInstalledScenePropDefinition CreateTachyonReagent(
            string visualId,
            string variant,
            IEnumerable<string> visibleStepIds,
            float trayU,
            float trayV)
        {
            string assetName = "pfb_chr_prop1143_" + variant;
            return new PetInstalledScenePropDefinition(
                visualId,
                "3d/chara/prop/prop1143_" + variant + "/" + assetName,
                assetName,
                true,
                visibleStepIds,
                Vector3.zero,
                new Vector3(0.0f, TachyonLabBenchYawDegrees, 0.0f),
                0.068f,
                PetInstalledScenePropMotion.None,
                new PetSceneSupportPlacementDefinition(
                    TachyonLabTableVisualId,
                    "worktop",
                    new Vector2(trayU, trayV),
                    0.0f));
        }

        private static PetAppOwnedScenePropDefinition[]
            CreateTachyonExperimentAppOwnedProps()
        {
            string[] bothSteps =
            {
                TachyonResearchStepId,
                TachyonBreakthroughStepId
            };
            var palette = new PetAppOwnedScenePalette(
                new Color32(121, 166, 168, 255),
                new Color32(84, 116, 124, 255),
                new Color32(35, 118, 122, 255),
                new Color32(218, 137, 58, 255),
                new Color32(13, 19, 22, 255));
            var resourcePrefab = new PetAppOwnedResourcePrefabDefinition(
                TachyonLabBenchResourcePath,
                TachyonLabBenchHeightInCharacterHeights,
                new[]
                {
                    new PetAppOwnedSceneMaterialMappingDefinition(
                        "wood",
                        PetAppOwnedSceneMaterialRole.Worktop)
                });
            return new[]
            {
                PetAppOwnedScenePropDefinition.CreateResourcePrefab(
                    TachyonLabTableVisualId,
                    true,
                    bothSteps,
                    new Vector3(0.29f, -0.36f, 0.04f),
                    TachyonLabBenchYawDegrees,
                    palette,
                    resourcePrefab,
                    new[]
                    {
                        new PetAppOwnedSupportSurfaceDefinition(
                            "worktop",
                            new Vector3(-0.06f, 0.14f, 0.0f),
                            new Vector2(0.16f, 0.16f))
                    })
            };
        }

        private static PetInstalledSceneEffectDefinition[]
            CreateTachyonExperimentEffects()
        {
            string[] researchOnly = { TachyonResearchStepId };
            string[] breakthroughOnly = { TachyonBreakthroughStepId };
            const string root =
                "3d/effect/charaskill/pfb_eff_chr1032_00/";
            const PetInstalledSceneEffectActivation activation =
                PetInstalledSceneEffectActivation.IncludeExternallyTriggered;
            const PetInstalledSceneEffectPlayback playback =
                PetInstalledSceneEffectPlayback.HoldWarmupSnapshot;
            const float snapshotSeconds = 0.18f;
            Vector3 readableFormulaFacing = new Vector3(0.0f, 180.0f, 0.0f);
            return new[]
            {
                new PetInstalledSceneEffectDefinition(
                    TachyonFormula01VisualId,
                    root + "pfb_eff_chr1032_00_06",
                    "pfb_eff_chr1032_00_06",
                    true,
                    researchOnly,
                    new Vector3(0.30f, 0.14f, 0.40f),
                    readableFormulaFacing,
                    0.34f,
                    snapshotSeconds,
                    activation,
                    playback),
                new PetInstalledSceneEffectDefinition(
                    TachyonFormula02VisualId,
                    root + "pfb_eff_chr1032_00_07",
                    "pfb_eff_chr1032_00_07",
                    true,
                    researchOnly,
                    new Vector3(0.33f, 0.10f, 0.41f),
                    readableFormulaFacing,
                    0.34f,
                    snapshotSeconds,
                    activation,
                    playback),
                new PetInstalledSceneEffectDefinition(
                    TachyonFormula03VisualId,
                    root + "pfb_eff_chr1032_00_08",
                    "pfb_eff_chr1032_00_08",
                    true,
                    researchOnly,
                    new Vector3(0.45f, 0.08f, 0.42f),
                    readableFormulaFacing,
                    0.34f,
                    snapshotSeconds,
                    activation,
                    playback),
                new PetInstalledSceneEffectDefinition(
                    TachyonFormula04VisualId,
                    root + "pfb_eff_chr1032_00_10",
                    "pfb_eff_chr1032_00_10",
                    true,
                    breakthroughOnly,
                    new Vector3(0.192f, 0.39f, 0.41f),
                    readableFormulaFacing,
                    0.36f,
                    snapshotSeconds,
                    activation,
                    playback),
                new PetInstalledSceneEffectDefinition(
                    TachyonFormula05VisualId,
                    root + "pfb_eff_chr1032_00_09",
                    "pfb_eff_chr1032_00_09",
                    true,
                    breakthroughOnly,
                    new Vector3(0.26f, 0.42f, 0.42f),
                    readableFormulaFacing,
                    0.48f,
                    snapshotSeconds,
                    activation,
                    playback)
            };
        }
    }
}
