using System;
using UnityEngine;

namespace UmaDesktopPet.Standalone.Runtime
{
    /// <summary>
    /// Reconciles the independently persisted focus timer and care save. The
    /// care-side completion ID makes retries safe when either save fails.
    /// </summary>
    public sealed class PetStudyRewardService : IDisposable
    {
        public const float ShortSessionEnergyCost = 12.0f;
        public const float LongSessionEnergyCost = 24.0f;

        private PetFocusState _focus;
        private PetNeedsState _needs;
        private bool _disposed;

        public PetStudyRewardService(
            PetFocusState focus,
            PetNeedsState needs)
        {
            _focus = focus ?? throw new ArgumentNullException("focus");
            _needs = needs ?? throw new ArgumentNullException("needs");
            _focus.SessionCompleted += HandleSessionCompleted;

            // A completed session may have been restored after the previous
            // process stopped between the two durable writes.
            if (_focus.Status == FocusSessionStatus.RewardReady &&
                !EnsurePendingCareReward())
            {
                Debug.LogWarning(
                    "The pending study care reward could not be saved yet. " +
                    "It will be retried before collection.");
            }
        }

        public int PendingFoodQuantity
        {
            get
            {
                return _focus != null &&
                    _focus.Status == FocusSessionStatus.RewardReady
                        ? FoodQuantityForDuration(
                            _focus.SessionDurationSeconds)
                        : 0;
            }
        }

        public bool EnsurePendingCareReward()
        {
            if (_disposed || _focus == null || _needs == null)
            {
                return false;
            }
            if (_focus.Status != FocusSessionStatus.RewardReady)
            {
                return true;
            }

            int durationSeconds = _focus.SessionDurationSeconds;
            return _needs.TryApplyStudyCompletion(
                _focus.LifetimeCompletedFocusSeconds,
                FoodQuantityForDuration(durationSeconds),
                EnergyCostForDuration(durationSeconds));
        }

        public bool TryCollectReward()
        {
            return !_disposed &&
                EnsurePendingCareReward() &&
                _focus.CollectReward();
        }

        public static int FoodQuantityForDuration(int durationSeconds)
        {
            if (durationSeconds == PetFocusState.ShortSessionSeconds)
            {
                return 1;
            }
            if (durationSeconds == PetFocusState.LongSessionSeconds)
            {
                return 2;
            }
            throw new ArgumentOutOfRangeException(
                "durationSeconds",
                "Only 25- and 50-minute study sessions are supported.");
        }

        public static float EnergyCostForDuration(int durationSeconds)
        {
            if (durationSeconds == PetFocusState.ShortSessionSeconds)
            {
                return ShortSessionEnergyCost;
            }
            if (durationSeconds == PetFocusState.LongSessionSeconds)
            {
                return LongSessionEnergyCost;
            }
            throw new ArgumentOutOfRangeException(
                "durationSeconds",
                "Only 25- and 50-minute study sessions are supported.");
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            if (_focus != null)
            {
                _focus.SessionCompleted -= HandleSessionCompleted;
            }
            _focus = null;
            _needs = null;
        }

        private void HandleSessionCompleted()
        {
            if (!EnsurePendingCareReward())
            {
                Debug.LogWarning(
                    "Study completed, but its Energy and food reward could " +
                    "not be saved. Collection will retry it.");
            }
        }
    }
}
