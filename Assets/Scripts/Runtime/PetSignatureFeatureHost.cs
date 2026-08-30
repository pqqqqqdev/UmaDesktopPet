using System;
using System.Collections.Generic;
using UnityEngine;

namespace UmaDesktopPet.Standalone.Runtime
{
    /// <summary>
    /// Character-neutral lifecycle used by interaction, autonomy, and study.
    /// Concrete feature presentation stays behind the host.
    /// </summary>
    public interface IPetSignatureFeatureHost
    {
        bool HasAvailableFeatures { get; }
        IReadOnlyList<PetSignatureFeatureDefinition> AvailableFeatures { get; }
        bool IsPlaying { get; }
        string CurrentId { get; }
        string CurrentDisplayName { get; }
        string CurrentStepId { get; }
        string CurrentStageLabel { get; }
        bool TryPlay(string featureId);
        bool Cancel();
    }

    internal interface IPetSignatureFeaturePresentationRuntime : IDisposable
    {
        bool IsAvailable { get; }
        bool TryAcquire();
        string ApplyStage(
            PetAmbientVignetteStage stage,
            string stepId,
            IList<string> activeVisualIds);
        void Tick(float unscaledDeltaTime);
        void Release();
    }

    internal static class PetSignatureFeaturePresentationFactory
    {
        public static IPetSignatureFeaturePresentationRuntime Create(
            PetSignatureFeatureDefinition definition,
            StudyDeskPresenter studyDesk,
            BundleRepository bundles,
            PetAttachmentRig rig)
        {
            if (definition == null)
            {
                throw new ArgumentNullException("definition");
            }

            switch (definition.Presentation.Kind)
            {
                case PetSignatureFeaturePresentationKind.InstalledStudyFurniture:
                    return new InstalledStudyFurnitureSignatureFeatureRuntime(
                        definition.Presentation,
                        studyDesk);
                case PetSignatureFeaturePresentationKind.InstalledPrefabScene:
                    return new InstalledPrefabSceneSignatureFeatureRuntime(
                        definition.Presentation,
                        bundles,
                        rig);
                default:
                    throw new NotSupportedException(
                        "Unsupported signature-feature presentation kind " +
                        definition.Presentation.Kind + ".");
            }
        }
    }

    /// <summary>
    /// Reusable adapter for any signature feature presented with the installed
    /// Study furniture. Character-specific labels and logical visual IDs remain
    /// in the character definition.
    /// </summary>
    internal sealed class InstalledStudyFurnitureSignatureFeatureRuntime :
        IPetSignatureFeaturePresentationRuntime
    {
        private readonly PetSignatureFeaturePresentation _presentation;
        private StudyDeskPresenter _studyDesk;
        private bool _acquired;

        public InstalledStudyFurnitureSignatureFeatureRuntime(
            PetSignatureFeaturePresentation presentation,
            StudyDeskPresenter studyDesk)
        {
            _presentation = presentation ??
                throw new ArgumentNullException("presentation");
            _studyDesk = studyDesk ?? throw new ArgumentNullException("studyDesk");
        }

        public bool IsAvailable
        {
            get
            {
                return _studyDesk != null &&
                    _studyDesk.UsesInstalledStudyProps;
            }
        }

        public bool TryAcquire()
        {
            if (!IsAvailable || _acquired)
            {
                return false;
            }

            // A focus session owns the shared furniture until it ends. A
            // signature feature must never replace its reward or equipped state.
            if (_studyDesk.IsVisible &&
                !_studyDesk.IsAmbientVisibilityAcquired)
            {
                return false;
            }

            _studyDesk.SetEquippedDeskItem(string.Empty);
            _acquired = _studyDesk.TryAcquireAmbientVisibility();
            return _acquired;
        }

        public string ApplyStage(
            PetAmbientVignetteStage stage,
            string stepId,
            IList<string> activeVisualIds)
        {
            if (!_acquired || _studyDesk == null ||
                !_studyDesk.IsAmbientVisibilityAcquired)
            {
                throw new InvalidOperationException(
                    "The signature feature lost its installed furniture lease.");
            }
            if (activeVisualIds == null)
            {
                throw new ArgumentNullException("activeVisualIds");
            }

            activeVisualIds.Clear();
            activeVisualIds.Add(_presentation.VisualId);
            return _presentation.ResolveStageLabel(stage, stepId);
        }

        public void Tick(float unscaledDeltaTime)
        {
        }

        public void Release()
        {
            if (_acquired && _studyDesk != null)
            {
                _studyDesk.ReleaseAmbientVisibility();
            }
            _acquired = false;
        }

        public void Dispose()
        {
            Release();
            _studyDesk = null;
        }
    }
}
