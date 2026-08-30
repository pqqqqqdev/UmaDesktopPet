using System;
using UnityEngine;

namespace UmaDesktopPet.Standalone.Runtime
{
    /// <summary>
    /// Gives the selected Uma occasional authored reactions without moving the window,
    /// touching the cursor, or interrupting an action the user already started.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PetAutonomyController : MonoBehaviour
    {
        private const float MinimumFirstReactionSeconds = 25.0f;
        private const float MaximumFirstReactionSeconds = 45.0f;
        private const float MinimumReactionSeconds = 90.0f;
        private const float MaximumReactionSeconds = 150.0f;
        private const float MinimumBlockedRetrySeconds = 8.0f;
        private const float MaximumBlockedRetrySeconds = 15.0f;
        private const float VignetteReactionChance = 0.55f;
        private const float HappyReactionChance = 0.30f;

        private OguriPetAnimationController _motions;
        private IPetSignatureFeatureHost _signatureFeatures;
        private PetNeedsState _needs;
        private PetFocusState _focus;
        private PetInteractionController _interaction;
        private bool _initialized;
        private float _nextReactionAt;
        private AmbientReaction _lastReaction;
        private int _sameReactionCount;
        private FocusSessionStatus _lastFocusStatus;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private bool _forceVignetteForSmokeTest;
        private int _smokeDueEvaluationSerial;

        internal int SmokeDueEvaluationSerial
        {
            get { return _smokeDueEvaluationSerial; }
        }
#endif

        public void Initialize(
            OguriPetAnimationController motions,
            IPetSignatureFeatureHost signatureFeatures,
            PetNeedsState needs,
            PetFocusState focus,
            PetInteractionController interaction)
        {
            if (_initialized)
            {
                throw new InvalidOperationException(
                    "The pet autonomy controller is already initialized.");
            }
            if (motions == null)
            {
                throw new ArgumentNullException("motions");
            }
            if (signatureFeatures == null)
            {
                throw new ArgumentNullException("signatureFeatures");
            }
            if (needs == null)
            {
                throw new ArgumentNullException("needs");
            }
            if (interaction == null)
            {
                throw new ArgumentNullException("interaction");
            }
            if (focus == null)
            {
                throw new ArgumentNullException("focus");
            }

            _motions = motions;
            _signatureFeatures = signatureFeatures;
            _needs = needs;
            _focus = focus;
            _interaction = interaction;
            _lastFocusStatus = _focus.Status;
            _focus.StateChanged += HandleFocusStateChanged;
            _initialized = true;
            ScheduleNext(
                MinimumFirstReactionSeconds,
                MaximumFirstReactionSeconds);
        }

        private void Update()
        {
            if (!_initialized || Time.unscaledTime < _nextReactionAt)
            {
                return;
            }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            _smokeDueEvaluationSerial++;
#endif

            // Quiet mode is a do-not-disturb switch. Low Energy also pauses
            // autonomous reactions until the user restores it. A blocked
            // reaction retries soon instead of disappearing for another two
            // minutes because the menu happened to be open at the due time.
            if (_needs.QuietMode || _needs.IsLowEnergy ||
                _focus.IsSessionActive ||
                _interaction.IsUserInteractionActive || _motions.IsBusy ||
                _signatureFeatures.IsPlaying)
            {
                ScheduleNext(
                    MinimumBlockedRetrySeconds,
                    MaximumBlockedRetrySeconds);
                return;
            }

            AmbientReaction reaction;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (_forceVignetteForSmokeTest)
            {
                _forceVignetteForSmokeTest = false;
                reaction = AmbientReaction.Vignette;
            }
            else
#endif
            {
                reaction = ChooseReaction();
            }
            bool started;
            switch (reaction)
            {
                case AmbientReaction.Vignette:
                    started = TryPlayAvailableVignette();
                    break;
                case AmbientReaction.Happy:
                    started = _motions.TriggerAmbientHappy();
                    break;
                default:
                    started = _motions.TriggerAmbientGreeting();
                    break;
            }
            if (!started && reaction == AmbientReaction.Vignette)
            {
                reaction = ChooseFallbackReaction();
                started = reaction == AmbientReaction.Happy
                    ? _motions.TriggerAmbientHappy()
                    : _motions.TriggerAmbientGreeting();
            }
            if (!started)
            {
                ScheduleNext(
                    MinimumBlockedRetrySeconds,
                    MaximumBlockedRetrySeconds);
                return;
            }

            RememberReaction(reaction);
            Debug.Log("Ambient pet reaction started: " + reaction + ".");
            ScheduleNext(MinimumReactionSeconds, MaximumReactionSeconds);
        }

        private AmbientReaction ChooseReaction()
        {
            bool happyAllowed = _needs.Mood >= PetMood.Normal;
            bool vignetteAllowed = HasAutonomousVignette();
            AmbientReaction selected;
            if (vignetteAllowed &&
                UnityEngine.Random.value < VignetteReactionChance)
            {
                selected = AmbientReaction.Vignette;
            }
            else
            {
                selected = happyAllowed &&
                    UnityEngine.Random.value < HappyReactionChance
                        ? AmbientReaction.Happy
                        : AmbientReaction.Greeting;
            }

            if (selected == _lastReaction && _sameReactionCount >= 2)
            {
                if (vignetteAllowed && selected != AmbientReaction.Vignette)
                {
                    selected = AmbientReaction.Vignette;
                }
                else if (happyAllowed && selected != AmbientReaction.Happy)
                {
                    selected = AmbientReaction.Happy;
                }
                else
                {
                    selected = AmbientReaction.Greeting;
                }
            }
            return selected;
        }

        private AmbientReaction ChooseFallbackReaction()
        {
            return _needs.Mood >= PetMood.Normal &&
                UnityEngine.Random.value < HappyReactionChance
                    ? AmbientReaction.Happy
                    : AmbientReaction.Greeting;
        }

        private bool HasAutonomousVignette()
        {
            if (_signatureFeatures == null ||
                !_signatureFeatures.HasAvailableFeatures)
            {
                return false;
            }

            for (int index = 0;
                index < _signatureFeatures.AvailableFeatures.Count;
                index++)
            {
                if (_signatureFeatures.AvailableFeatures[index]
                    .AutonomyWeight > 0.0f)
                {
                    return true;
                }
            }
            return false;
        }

        private bool TryPlayAvailableVignette()
        {
            float totalWeight = 0.0f;
            for (int index = 0;
                index < _signatureFeatures.AvailableFeatures.Count;
                index++)
            {
                totalWeight += Mathf.Max(
                    0.0f,
                    _signatureFeatures.AvailableFeatures[index].AutonomyWeight);
            }
            if (totalWeight <= 0.0f)
            {
                return false;
            }

            float selectedWeight = UnityEngine.Random.value * totalWeight;
            PetSignatureFeatureDefinition selected = null;
            for (int index = 0;
                index < _signatureFeatures.AvailableFeatures.Count;
                index++)
            {
                PetSignatureFeatureDefinition candidate =
                    _signatureFeatures.AvailableFeatures[index];
                float weight = Mathf.Max(0.0f, candidate.AutonomyWeight);
                if (weight <= 0.0f)
                {
                    continue;
                }
                selected = candidate;
                selectedWeight -= weight;
                if (selectedWeight <= 0.0f)
                {
                    break;
                }
            }
            return selected != null &&
                _signatureFeatures.TryPlay(selected.Id);
        }

        private void RememberReaction(AmbientReaction reaction)
        {
            if (reaction == _lastReaction)
            {
                _sameReactionCount++;
            }
            else
            {
                _lastReaction = reaction;
                _sameReactionCount = 1;
            }
        }

        private void ScheduleNext(float minimumSeconds, float maximumSeconds)
        {
            _nextReactionAt = Time.unscaledTime +
                UnityEngine.Random.Range(minimumSeconds, maximumSeconds);
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        internal void ForceVignetteDueForSmokeTest()
        {
            if (!_initialized)
            {
                throw new InvalidOperationException(
                    "Pet autonomy is not initialized.");
            }
            // The forced choice is deliberately consumed only after all normal
            // suppression gates. A Quiet-mode smoke therefore exercises the
            // same branch as real autonomous scheduling.
            _forceVignetteForSmokeTest = true;
            _nextReactionAt = Time.unscaledTime;
        }
#endif

        private void HandleFocusStateChanged(PetFocusSnapshot snapshot)
        {
            if (!_initialized)
            {
                return;
            }

            bool wasActive = _lastFocusStatus == FocusSessionStatus.Running ||
                _lastFocusStatus == FocusSessionStatus.Paused;
            bool isFinished = snapshot.Status == FocusSessionStatus.Idle ||
                snapshot.Status == FocusSessionStatus.RewardReady;
            _lastFocusStatus = snapshot.Status;

            // When a study session ends or is stopped, give the pet a full quiet
            // interval instead of immediately replaying a blocked greeting.
            if (wasActive && isFinished)
            {
                ScheduleNext(MinimumReactionSeconds, MaximumReactionSeconds);
            }
        }

        private void OnDestroy()
        {
            if (_focus != null)
            {
                _focus.StateChanged -= HandleFocusStateChanged;
            }
        }

        private enum AmbientReaction
        {
            Greeting,
            Happy,
            Vignette
        }
    }
}
