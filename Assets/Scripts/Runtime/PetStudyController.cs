using System;
using UnityEngine;

namespace UmaDesktopPet.Standalone.Runtime
{
    /// <summary>
    /// Keeps the focus-session state, optional installed book motion, and the
    /// app-owned desk visual together. The timer remains authoritative: a busy
    /// animation only delays the pose and never delays or cancels focus time.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PetStudyController : MonoBehaviour
    {
        private PetFocusState _focus;
        private OguriPetAnimationController _motions;
        private StudyDeskPresenter _desk;
        private bool _initialized;

        public void Initialize(
            PetFocusState focus,
            OguriPetAnimationController motions,
            StudyDeskPresenter desk)
        {
            if (_initialized)
            {
                throw new InvalidOperationException(
                    "The pet study controller is already initialized.");
            }
            if (focus == null)
            {
                throw new ArgumentNullException("focus");
            }
            if (motions == null)
            {
                throw new ArgumentNullException("motions");
            }
            if (desk == null)
            {
                throw new ArgumentNullException("desk");
            }

            _focus = focus;
            _motions = motions;
            _desk = desk;
            _focus.StateChanged += HandleFocusStateChanged;
            _initialized = true;
            Apply(_focus.CurrentSnapshot);
        }

        private void Update()
        {
            if (!_initialized || _focus.Status != FocusSessionStatus.Running)
            {
                return;
            }

            // Starting a session while another authored reaction is ending is
            // allowed. Retry only the visual until the clean idle is available.
            EnsureStudyMotion(false);
        }

        private void HandleFocusStateChanged(PetFocusSnapshot snapshot)
        {
            if (!_initialized)
            {
                return;
            }

            Apply(snapshot);
        }

        private void Apply(PetFocusSnapshot snapshot)
        {
            // Restored sessions deliberately return as Paused. Keep the exact
            // timer available to resume, but do not make the pet appear to be
            // studying immediately when the app opens.
            if (snapshot.Status != FocusSessionStatus.Running)
            {
                if (_desk.IsVisible)
                {
                    _desk.Hide();
                }
                if (_motions.IsStudying)
                {
                    _motions.EndStudy();
                }
                return;
            }

            if (!_desk.IsVisible)
            {
                _desk.Show(snapshot.EquippedDeskItemId);
            }
            else
            {
                _desk.SetEquippedDeskItem(snapshot.EquippedDeskItemId);
            }

            _desk.SetPaused(false);
            EnsureStudyMotion(false);
        }

        private void EnsureStudyMotion(bool paused)
        {
            if (!_motions.HasStudyMotion)
            {
                return;
            }

            if (!_motions.IsStudying && !_motions.BeginStudy())
            {
                return;
            }
            if (_motions.IsStudying && _motions.IsStudyPaused != paused)
            {
                _motions.SetStudyPaused(paused);
            }
        }

        private void OnDestroy()
        {
            if (_focus != null)
            {
                _focus.StateChanged -= HandleFocusStateChanged;
            }
        }
    }
}
