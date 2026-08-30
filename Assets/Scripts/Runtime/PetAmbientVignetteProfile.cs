using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace UmaDesktopPet.Standalone.Runtime
{
    /// <summary>
    /// Stable lifecycle stages shared by the body-motion controller and any
    /// app-owned visual presenter. A terminal stage is raised exactly once for
    /// each successfully started vignette.
    /// </summary>
    public enum PetAmbientVignetteStage
    {
        Setup,
        StepStart,
        StepLoop,
        StepEnd,
        Completed,
        Interrupted
    }

    public sealed class PetAmbientVignetteEvent
    {
        public PetAmbientVignetteEvent(
            PetAmbientVignetteStage stage,
            string vignetteId,
            string stepId)
        {
            if (!Enum.IsDefined(typeof(PetAmbientVignetteStage), stage))
            {
                throw new ArgumentOutOfRangeException("stage");
            }
            if (string.IsNullOrWhiteSpace(vignetteId))
            {
                throw new ArgumentException(
                    "A vignette lifecycle event requires a vignette ID.",
                    "vignetteId");
            }
            if (string.IsNullOrWhiteSpace(stepId))
            {
                throw new ArgumentException(
                    "A vignette lifecycle event requires a step ID.",
                    "stepId");
            }

            Stage = stage;
            VignetteId = vignetteId.Trim();
            StepId = stepId.Trim();
        }

        public PetAmbientVignetteStage Stage { get; private set; }

        public string VignetteId { get; private set; }

        public string StepId { get; private set; }
    }

    public sealed class PetAmbientVignetteStepProfile
    {
        public PetAmbientVignetteStepProfile(
            string id,
            PetMotionSequenceProfile motion,
            bool transitionDirectlyToNext)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException(
                    "An ambient vignette step requires an ID.",
                    "id");
            }
            if (motion == null)
            {
                throw new ArgumentNullException("motion");
            }

            Id = id.Trim();
            Motion = motion;
            TransitionDirectlyToNext = transitionDirectlyToNext;
        }

        public string Id { get; private set; }

        public PetMotionSequenceProfile Motion { get; private set; }

        /// <summary>
        /// Skips this step's authored end clip and starts the following step as
        /// soon as the requested loop repetitions finish.
        /// </summary>
        public bool TransitionDirectlyToNext { get; private set; }
    }

    /// <summary>
    /// App-owned recipe for one ordered ambient scene. Body clips and any selected
    /// visual assets remain in the user's installed game bundles; VisualRecipeId
    /// chooses the exact presentation recipe without exporting them.
    /// </summary>
    public sealed class PetAmbientVignetteProfile
    {
        private readonly IReadOnlyList<PetAmbientVignetteStepProfile> _steps;

        public PetAmbientVignetteProfile(
            string id,
            string displayName,
            string visualRecipeId,
            float autonomyWeight,
            IEnumerable<PetAmbientVignetteStepProfile> steps)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException(
                    "An ambient vignette requires an ID.",
                    "id");
            }
            if (string.IsNullOrWhiteSpace(displayName))
            {
                throw new ArgumentException(
                    "An ambient vignette requires a display name.",
                    "displayName");
            }
            if (string.IsNullOrWhiteSpace(visualRecipeId))
            {
                throw new ArgumentException(
                    "An ambient vignette requires a visual recipe ID.",
                    "visualRecipeId");
            }
            if (float.IsNaN(autonomyWeight) ||
                float.IsInfinity(autonomyWeight) || autonomyWeight < 0.0f)
            {
                throw new ArgumentOutOfRangeException(
                    "autonomyWeight",
                    "An ambient vignette autonomy weight must be finite and non-negative.");
            }
            if (steps == null)
            {
                throw new ArgumentNullException("steps");
            }

            var ordered = new List<PetAmbientVignetteStepProfile>();
            var stepIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (PetAmbientVignetteStepProfile step in steps)
            {
                if (step == null)
                {
                    throw new ArgumentException(
                        "Ambient vignette steps cannot contain null.",
                        "steps");
                }
                if (!stepIds.Add(step.Id))
                {
                    throw new ArgumentException(
                        "An ambient vignette repeats step ID " + step.Id + ".",
                        "steps");
                }
                ordered.Add(step);
            }
            if (ordered.Count == 0)
            {
                throw new ArgumentException(
                    "An ambient vignette requires at least one ordered step.",
                    "steps");
            }
            if (ordered[ordered.Count - 1].TransitionDirectlyToNext)
            {
                throw new ArgumentException(
                    "The final ambient vignette step cannot transition to a missing next step.",
                    "steps");
            }

            Id = id.Trim();
            DisplayName = displayName.Trim();
            VisualRecipeId = visualRecipeId.Trim();
            AutonomyWeight = autonomyWeight;
            _steps = new ReadOnlyCollection<PetAmbientVignetteStepProfile>(ordered);
        }

        public string Id { get; private set; }

        public string DisplayName { get; private set; }

        public string VisualRecipeId { get; private set; }

        public float AutonomyWeight { get; private set; }

        public IReadOnlyList<PetAmbientVignetteStepProfile> Steps
        {
            get { return _steps; }
        }
    }
}
