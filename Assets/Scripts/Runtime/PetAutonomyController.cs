using System;
using UnityEngine;

namespace UmaDesktopPet.Standalone.Runtime
{
    /// <summary>
    /// Gives Oguri occasional authored reactions without moving the window,
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
        private const float HappyReactionChance = 0.30f;

        private OguriPetAnimationController _motions;
        private PetNeedsState _needs;
        private PetInteractionController _interaction;
        private bool _initialized;
        private float _nextReactionAt;
        private AmbientReaction _lastReaction;
        private int _sameReactionCount;

        public void Initialize(
            OguriPetAnimationController motions,
            PetNeedsState needs,
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
            if (needs == null)
            {
                throw new ArgumentNullException("needs");
            }
            if (interaction == null)
            {
                throw new ArgumentNullException("interaction");
            }

            _motions = motions;
            _needs = needs;
            _interaction = interaction;
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

            // Quiet mode is a do-not-disturb switch. Low Energy also pauses
            // autonomous reactions until the user restores it. A blocked
            // reaction retries soon instead of disappearing for another two
            // minutes because the menu happened to be open at the due time.
            if (_needs.QuietMode || _needs.IsLowEnergy ||
                _interaction.IsUserInteractionActive || _motions.IsBusy)
            {
                ScheduleNext(
                    MinimumBlockedRetrySeconds,
                    MaximumBlockedRetrySeconds);
                return;
            }

            AmbientReaction reaction = ChooseReaction();
            bool started = reaction == AmbientReaction.Happy
                ? _motions.TriggerAmbientHappy()
                : _motions.TriggerAmbientGreeting();
            if (!started)
            {
                ScheduleNext(
                    MinimumBlockedRetrySeconds,
                    MaximumBlockedRetrySeconds);
                return;
            }

            RememberReaction(reaction);
            ScheduleNext(MinimumReactionSeconds, MaximumReactionSeconds);
        }

        private AmbientReaction ChooseReaction()
        {
            bool happyAllowed = _needs.Mood >= PetMood.Normal;
            AmbientReaction selected = happyAllowed &&
                UnityEngine.Random.value < HappyReactionChance
                    ? AmbientReaction.Happy
                    : AmbientReaction.Greeting;

            if (happyAllowed && selected == _lastReaction &&
                _sameReactionCount >= 2)
            {
                selected = selected == AmbientReaction.Happy
                    ? AmbientReaction.Greeting
                    : AmbientReaction.Happy;
            }
            return selected;
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

        private enum AmbientReaction
        {
            Greeting,
            Happy
        }
    }
}
