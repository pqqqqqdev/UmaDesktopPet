using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace UmaDesktopPet.Standalone.Runtime
{
    /// <summary>
    /// Owns the desktop-pet motion state machine: character-specific idle/tap
    /// clips plus shared authored responses and optional personality scenes.
    /// Motion clips stay inside their installed game bundles and are never exported.
    /// </summary>
    public sealed class OguriPetAnimationController : MonoBehaviour
    {
        public const string IdleStartAsset =
            "3d/motion/mini/event/body/chara/chr1006_00/" +
            "anm_min_eve_chr1006_00_idle01_s";
        public const string IdleLoopAsset =
            "3d/motion/mini/event/body/chara/chr1006_00/" +
            "anm_min_eve_chr1006_00_idle01_loop";
        public const string IdleEndAsset =
            "3d/motion/mini/event/body/chara/chr1006_00/" +
            "anm_min_eve_chr1006_00_idle01_e";

        public const string TapStartAsset =
            "3d/motion/mini/event/body/chara/chr1006_00/" +
            "anm_min_eve_chr1006_00_res01_s";
        public const string TapLoopAsset =
            "3d/motion/mini/event/body/chara/chr1006_00/" +
            "anm_min_eve_chr1006_00_res01_loop";
        public const string TapEndAsset =
            "3d/motion/mini/event/body/chara/chr1006_00/" +
            "anm_min_eve_chr1006_00_res01_e";

        public const string PatHappyStartAsset =
            "3d/motion/mini/event/body/type00/" +
            "anm_min_eve_type00_happy03_s";
        public const string PatHappyLoopAsset =
            "3d/motion/mini/event/body/type00/" +
            "anm_min_eve_type00_happy03_loop";
        public const string PatHappyEndAsset =
            "3d/motion/mini/event/body/type00/" +
            "anm_min_eve_type00_happy03_e";

        public const string FeedResponseStartAsset =
            "3d/motion/mini/event/body/type00/" +
            "anm_min_eve_type00_happy05_s";
        public const string FeedResponseLoopAsset =
            "3d/motion/mini/event/body/type00/" +
            "anm_min_eve_type00_happy05_loop";
        public const string FeedResponseEndAsset =
            "3d/motion/mini/event/body/type00/" +
            "anm_min_eve_type00_happy05_e";

        public const string AmbientGreetingStartAsset =
            "3d/motion/mini/event/body/type00/" +
            "anm_min_eve_type00_hello04_s";
        public const string AmbientGreetingLoopAsset =
            "3d/motion/mini/event/body/type00/" +
            "anm_min_eve_type00_hello04_loop";
        public const string AmbientGreetingEndAsset =
            "3d/motion/mini/event/body/type00/" +
            "anm_min_eve_type00_hello04_e";

        public const string DragLiftAsset =
            "3d/motion/mini/event/body/type00/" +
            "anm_min_eve_type00_jump01";

        public const string DragHoldAsset =
            "3d/motion/mini/minigame/mng_0001/body/type00/" +
            "anm_min_mng_0001_type00_catch01_bad01";

        public const string StudyStartAsset =
            "3d/motion/mini/event/body/type00/" +
            "anm_min_eve_type00_book_s";
        public const string StudyLoopAsset =
            "3d/motion/mini/event/body/type00/" +
            "anm_min_eve_type00_book_loop";
        public const string StudyEndAsset =
            "3d/motion/mini/event/body/type00/" +
            "anm_min_eve_type00_book_e";

        private const double DragHoldNormalizedTime = 0.42d;
        private const double DragPlaybackSpeed = 1.65d;
        private const double DragHoldSwayRange = 0.012d;
        private const double DragHoldSwayCyclesPerSecond = 0.7d;
        private const float DragPickupBlendSeconds = 0.16f;
        private const float DragReleaseBlendSeconds = 0.10f;
        private const float StudyExitBlendSeconds = 0.16f;

        private static readonly string[] SharedRequiredMotionAssets =
        {
            PatHappyStartAsset,
            PatHappyLoopAsset,
            PatHappyEndAsset,
            FeedResponseStartAsset,
            FeedResponseLoopAsset,
            FeedResponseEndAsset,
            AmbientGreetingStartAsset,
            AmbientGreetingLoopAsset,
            AmbientGreetingEndAsset,
            DragLiftAsset,
            DragHoldAsset
        };

        private static readonly string[] OptionalStudyMotionAssets =
        {
            StudyStartAsset,
            StudyLoopAsset,
            StudyEndAsset
        };

        private Animator _animator;
        private MiniFaceExpressionController _face;
        private BundleLease _motionLease;
        private BundleLease _studyMotionLease;
        private readonly Dictionary<string, BundleLease> _ambientVignetteMotionLeases =
            new Dictionary<string, BundleLease>(StringComparer.Ordinal);
        private readonly Dictionary<string, LoadedAmbientVignette> _ambientVignettes =
            new Dictionary<string, LoadedAmbientVignette>(StringComparer.Ordinal);
        private readonly List<PetAmbientVignetteProfile> _availableAmbientVignettes =
            new List<PetAmbientVignetteProfile>();
        private ReadOnlyCollection<PetAmbientVignetteProfile>
            _availableAmbientVignetteView;
        private PetCharacterMotionProfile _characterMotionProfile;
        private PlayableGraph _graph;
        private AnimationMixerPlayable _mixer;
        private AnimationLayerMixerPlayable _layerMixer;
        private AnimationClipPlayable _currentPlayable;
        private AnimationClipPlayable _blendFromPlayable;
        private AnimationClipPlayable _currentFacePlayable;
        private AnimationClipPlayable _currentEarPlayable;
        private AnimationClip _currentClip;
        private AnimationClip _currentFaceClip;
        private AnimationClip _currentEarClip;
        private int _currentMixerInput;
        private int _blendFromMixerInput = -1;
        private float _blendStartedAt;
        private float _blendDuration;
        private bool _blendActive;
        private AnimationClip _idleStart;
        private AnimationClip _idleLoop;
        private AnimationClip _idleEnd;
        private MotionSequence _tapReaction;
        private MotionSequence _patHappy;
        private MotionSequence _feedResponse;
        private MotionSequence _ambientGreeting;
        private MotionSequence _study;
        private AnimationClip _dragLift;
        private AnimationClip _dragHold;
        private double _dragHoldNormalizedTime;
        private double _dragHoldStartedAt;
        private MotionSequence _activeAction;
        private int _activeActionLoopsRemaining;
        private LoadedAmbientVignette _activeAmbientVignette;
        private int _activeAmbientStepIndex = -1;
        private bool _activeAmbientTerminalRaised;
        private MotionPhase _phase;
        private bool _dragHeld;
        private bool _studyRequested;
        private bool _studyPaused;
        private bool _ambientFaceControllerSuppressed;
        private bool _ambientFaceControllerWasEnabled;
        private bool _initialized;

        /// <summary>
        /// Raised when the authored feeding response reaches the part where the
        /// carrot is at the pet's mouth, when the bite commits, and when the whole
        /// response finishes. This keeps care state synchronized with the motion
        /// instead of guessing with a wall-clock timer.
        /// </summary>
        public event Action FeedBiteStarted;
        public event Action FeedBiteCommitted;
        public event Action FeedResponseCompleted;
        public event Action<PetAmbientVignetteEvent> AmbientVignetteStageChanged;

        public bool IsBusy
        {
            get { return _initialized && _phase != MotionPhase.IdleLoop; }
        }

        /// <summary>
        /// True when all three optional installed book clips were available.
        /// A false value is a supported degraded mode and never blocks startup.
        /// </summary>
        public bool HasStudyMotion
        {
            get { return _study != null; }
        }

        public IReadOnlyList<PetAmbientVignetteProfile> AvailableAmbientVignettes
        {
            get
            {
                if (_availableAmbientVignetteView == null)
                {
                    _availableAmbientVignetteView =
                        _availableAmbientVignettes.AsReadOnly();
                }
                return _availableAmbientVignetteView;
            }
        }

        public string CurrentAmbientVignetteId
        {
            get
            {
                return _activeAmbientVignette != null
                    ? _activeAmbientVignette.Profile.Id
                    : string.Empty;
            }
        }

        public string CurrentAmbientVignetteDisplayName
        {
            get
            {
                return _activeAmbientVignette != null
                    ? _activeAmbientVignette.Profile.DisplayName
                    : string.Empty;
            }
        }

        public string CurrentAmbientVignetteStepId
        {
            get
            {
                LoadedAmbientVignette vignette = _activeAmbientVignette;
                return vignette != null &&
                    _activeAmbientStepIndex >= 0 &&
                    _activeAmbientStepIndex < vignette.Steps.Count
                        ? vignette.Steps[_activeAmbientStepIndex].Profile.Id
                        : string.Empty;
            }
        }

        public bool HasSignatureAmbient
        {
            get { return _availableAmbientVignettes.Count > 0; }
        }

        public bool IsSignatureAmbientActive
        {
            get
            {
                return _initialized && _activeAmbientVignette != null &&
                    _availableAmbientVignettes.Count > 0 &&
                    string.Equals(
                        _activeAmbientVignette.Profile.Id,
                        _availableAmbientVignettes[0].Id,
                        StringComparison.Ordinal);
            }
        }

        public string SignatureAmbientDisplayName
        {
            get
            {
                return _availableAmbientVignettes.Count > 0
                        ? _availableAmbientVignettes[0].DisplayName
                        : string.Empty;
            }
        }

        /// <summary>
        /// True while a caller wants the held study visual to remain active.
        /// The value stays true while dragging so the study visual can resume
        /// after the existing drag release completes.
        /// </summary>
        public bool IsStudying
        {
            get { return _initialized && _studyRequested; }
        }

        public bool IsStudyPaused
        {
            get { return IsStudying && _studyPaused; }
        }

        public string CurrentPhase
        {
            get
            {
                if (!_initialized)
                {
                    return "Uninitialized";
                }

                string action = GetCurrentActionName();
                return string.Equals(action, "None", StringComparison.Ordinal)
                    ? _phase.ToString()
                    : action + "/" + _phase;
            }
        }

        public string CurrentAction
        {
            get { return GetCurrentActionName(); }
        }

        public bool UsesDragFraming
        {
            get
            {
                return _initialized &&
                    (_phase == MotionPhase.DragLift ||
                     _phase == MotionPhase.DragHold ||
                     _phase == MotionPhase.DragRelease);
            }
        }

        public void Initialize(
            Animator animator,
            BundleRepository repository,
            MiniFaceExpressionController face,
            PetCharacterMotionProfile characterMotionProfile,
            string diagnosticDragHoldAsset = null,
            double? diagnosticDragHoldNormalizedTime = null)
        {
            if (_initialized)
            {
                throw new InvalidOperationException(
                    "The pet animation controller is already initialized.");
            }
            if (animator == null)
            {
                throw new ArgumentNullException("animator");
            }
            if (repository == null)
            {
                throw new ArgumentNullException("repository");
            }
            if (face == null)
            {
                throw new ArgumentNullException("face");
            }
            if (characterMotionProfile == null)
            {
                throw new ArgumentNullException("characterMotionProfile");
            }

            PlayableIdleController oldIdle = GetComponent<PlayableIdleController>();
            if (oldIdle != null)
            {
                oldIdle.Stop();
                oldIdle.enabled = false;
            }

            _animator = animator;
            _face = face;
            _characterMotionProfile = characterMotionProfile;
            try
            {
                string selectedDragHoldAsset =
                    string.IsNullOrWhiteSpace(diagnosticDragHoldAsset)
                        ? DragHoldAsset
                        : diagnosticDragHoldAsset.Trim();
                _dragHoldNormalizedTime = Math.Max(
                    0.0d,
                    Math.Min(
                        1.0d,
                        diagnosticDragHoldNormalizedTime ??
                            DragHoldNormalizedTime));
                string[] requiredAssets = SharedRequiredMotionAssets
                    .Concat(new[]
                    {
                        characterMotionProfile.Idle.StartAsset,
                        characterMotionProfile.Idle.LoopAsset,
                        characterMotionProfile.Idle.EndAsset,
                        characterMotionProfile.Tap.StartAsset,
                        characterMotionProfile.Tap.LoopAsset,
                        characterMotionProfile.Tap.EndAsset
                    })
                    .Concat(new[] { selectedDragHoldAsset })
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                _motionLease = repository.AcquireMany(requiredAssets);
                _idleStart = LoadRequiredClip(
                    _motionLease,
                    characterMotionProfile.Idle.StartAsset);
                _idleLoop = LoadRequiredClip(
                    _motionLease,
                    characterMotionProfile.Idle.LoopAsset);
                _idleEnd = LoadRequiredClip(
                    _motionLease,
                    characterMotionProfile.Idle.EndAsset);
                _tapReaction = LoadSequence(
                    _motionLease,
                    characterMotionProfile.Tap.Name,
                    characterMotionProfile.Tap.StartAsset,
                    characterMotionProfile.Tap.LoopAsset,
                    characterMotionProfile.Tap.EndAsset,
                    characterMotionProfile.Tap.Expression);
                _patHappy = LoadSequence(
                    _motionLease,
                    "PatHappy",
                    PatHappyStartAsset,
                    PatHappyLoopAsset,
                    PatHappyEndAsset,
                    MiniFaceExpression.Happy);
                _feedResponse = LoadSequence(
                    _motionLease,
                    "FeedResponse",
                    FeedResponseStartAsset,
                    FeedResponseLoopAsset,
                    FeedResponseEndAsset,
                    MiniFaceExpression.Happy);
                _ambientGreeting = LoadSequence(
                    _motionLease,
                    "AmbientGreeting",
                    AmbientGreetingStartAsset,
                    AmbientGreetingLoopAsset,
                    AmbientGreetingEndAsset,
                    MiniFaceExpression.Greeting);
                _dragLift = LoadRequiredClip(_motionLease, DragLiftAsset);
                _dragHold = LoadRequiredClip(
                    _motionLease,
                    selectedDragHoldAsset);
                TryLoadOptionalStudyMotion(repository);
                TryLoadOptionalAmbientVignettes(repository);

                ConfigureAnimator();
                CreateGraph();
                _initialized = true;
                _face.Show(MiniFaceExpression.Neutral);
                PlayPhase(MotionPhase.IdleStart, _idleStart);
            }
            catch
            {
                ReleaseResources();
                throw;
            }
        }

        /// <summary>
        /// Starts one complete pet response and rejects overlap until idle resumes.
        /// </summary>
        public bool TriggerTapReaction()
        {
            return TryStartAction(_tapReaction);
        }

        /// <summary>
        /// Plays a generic happy response selected for successful patting.
        /// </summary>
        public bool TriggerPatHappy()
        {
            return TryStartAction(_patHappy);
        }

        /// <summary>
        /// Plays a second generic happy response selected for successful feeding.
        /// </summary>
        public bool TriggerFeedResponse()
        {
            return TryStartAction(_feedResponse);
        }

        /// <summary>
        /// Plays the generic hello response as an occasional ambient greeting.
        /// </summary>
        public bool TriggerAmbientGreeting()
        {
            return TryStartAction(_ambientGreeting);
        }

        /// <summary>
        /// Reuses the authored happy response as a second ambient reaction.
        /// Keeping this semantic entry point separate lets the autonomy policy
        /// evolve without pretending that the user patted the pet.
        /// </summary>
        public bool TriggerAmbientHappy()
        {
            return TryStartAction(_patHappy);
        }

        public bool HasAmbientVignette(string vignetteId)
        {
            return !string.IsNullOrWhiteSpace(vignetteId) &&
                _ambientVignettes.ContainsKey(vignetteId.Trim());
        }

        /// <summary>
        /// Starts one complete multi-step personality scene from stable idle.
        /// Setup is raised synchronously after the request succeeds; body and
        /// visual phases then share the authored step lifecycle.
        /// </summary>
        internal bool TryPlayAmbientVignette(string vignetteId)
        {
            if (!_initialized || _phase != MotionPhase.IdleLoop ||
                _activeAction != null || _studyRequested ||
                string.IsNullOrWhiteSpace(vignetteId))
            {
                return false;
            }

            LoadedAmbientVignette vignette;
            if (!_ambientVignettes.TryGetValue(
                vignetteId.Trim(),
                out vignette) || vignette == null || vignette.Steps.Count == 0)
            {
                return false;
            }

            _activeAmbientVignette = vignette;
            _activeAmbientStepIndex = 0;
            _activeAmbientTerminalRaised = false;
            try
            {
                SetActiveAmbientStep(0);
                PlayPhase(MotionPhase.IdleEnd, _idleEnd);
                BeginAmbientFaceControl(vignette);
                RaiseAmbientVignetteStage(PetAmbientVignetteStage.Setup);
                return ReferenceEquals(_activeAmbientVignette, vignette) &&
                    !_activeAmbientTerminalRaised;
            }
            catch (Exception exception)
            {
                AbortActiveAmbientVignette(exception);
                return false;
            }
        }

        /// <summary>
        /// Stops only the active ambient vignette. Ordinary tap, feed, pat,
        /// study, and drag actions retain their existing lifecycle.
        /// </summary>
        internal bool CancelAmbientVignette()
        {
            if (!_initialized || _activeAmbientVignette == null)
            {
                return false;
            }

            try
            {
                InterruptActiveAmbientVignette();
                _activeAction = null;
                _activeActionLoopsRemaining = 0;

                // Unity can invalidate bundle-backed clips before sibling
                // OnDisable callbacks finish during application teardown. The
                // interruption event above is still authoritative, but there is no
                // live motion runtime left to transition back to idle.
                if (_face == null || !_graph.IsValid() || _idleLoop == null ||
                    _idleLoop.length <= 0.0f)
                {
                    Debug.Log(
                        "Ambient vignette interrupted during motion teardown; " +
                        "skipping the idle transition.");
                    return true;
                }

                _face.Show(MiniFaceExpression.Neutral);
                PlayPhase(MotionPhase.IdleLoop, _idleLoop);
                Debug.Log("Ambient vignette interrupted; returned to idle.");
                return true;
            }
            catch (Exception exception)
            {
                AbortActiveAmbientVignette(exception);
                return true;
            }
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        internal bool FreezeCurrentMotionForSmokeTest()
        {
            if (!_initialized || !_graph.IsValid() ||
                !_currentPlayable.IsValid())
            {
                return false;
            }

            _currentPlayable.SetSpeed(0.0d);
            if (_currentFacePlayable.IsValid())
            {
                _currentFacePlayable.SetSpeed(0.0d);
            }
            if (_currentEarPlayable.IsValid())
            {
                _currentEarPlayable.SetSpeed(0.0d);
            }
            _graph.Evaluate(0.0f);
            return true;
        }
#endif

        /// <summary>
        /// Starts the optional book sequence from a clean idle. Study is a held
        /// action: its loop continues until EndStudy is called. Ordinary authored
        /// responses are never interrupted to begin studying.
        /// </summary>
        public bool BeginStudy()
        {
            if (!_initialized || _study == null || _studyRequested ||
                _phase != MotionPhase.IdleLoop || _activeAction != null ||
                _dragHeld)
            {
                return false;
            }

            _studyRequested = true;
            _studyPaused = false;
            _face.Show(MiniFaceExpression.Neutral);
            PlayPhase(MotionPhase.IdleEnd, _idleEnd);
            return true;
        }

        /// <summary>
        /// Freezes or resumes the held study loop without changing the caller-owned
        /// focus timer. The short authored entrance is allowed to settle before a
        /// requested pause takes effect. A pause requested during drag is remembered
        /// and applied when the study visual resumes after landing.
        /// </summary>
        public bool SetStudyPaused(bool paused)
        {
            if (!_initialized || !_studyRequested)
            {
                return false;
            }

            _studyPaused = paused;
            ApplyStudyPlaybackSpeed();
            return true;
        }

        /// <summary>
        /// Ends a requested study visual. If a drag is in progress, the landing
        /// remains untouched and simply returns to idle instead of resuming study.
        /// </summary>
        public bool EndStudy()
        {
            if (!_initialized || !_studyRequested)
            {
                return false;
            }

            _studyRequested = false;
            _studyPaused = false;
            _face.Show(MiniFaceExpression.Neutral);

            if (_phase == MotionPhase.IdleEnd && _activeAction == null)
            {
                BlendToPhase(
                    MotionPhase.IdleStart,
                    _idleStart,
                    0.0d,
                    1.0d,
                    StudyExitBlendSeconds);
            }
            else if (_phase == MotionPhase.StudyStart ||
                _phase == MotionPhase.StudyLoop)
            {
                // book_e visibly puts away an imaginary hand-held book. That
                // authored action does not match the desk/notebook study setup,
                // so leave the held pose with a short neutral blend instead.
                BlendToPhase(
                    MotionPhase.IdleStart,
                    _idleStart,
                    0.0d,
                    1.0d,
                    StudyExitBlendSeconds);
            }
            return true;
        }

        /// <summary>
        /// Interrupts an ordinary idle or reaction with an authored held pose.
        /// Feeding owns pointer input while it is active, so it is never cut off
        /// by this interaction.
        /// </summary>
        public bool BeginDragReaction()
        {
            if (!_initialized || _dragHold == null ||
                _activeAction == _feedResponse)
            {
                return false;
            }

            InterruptActiveAmbientVignette();
            _activeAction = null;
            _activeActionLoopsRemaining = 0;
            _dragHeld = true;
            _face.Show(MiniFaceExpression.Neutral);
            BlendToPhase(
                MotionPhase.DragHold,
                _dragHold,
                _dragHoldNormalizedTime,
                0.0d,
                DragPickupBlendSeconds);
            _dragHoldStartedAt = Time.unscaledTimeAsDouble;
            return true;
        }

        /// <summary>
        /// Ends the held loop with a short landing motion before returning to idle.
        /// </summary>
        public bool EndDragReaction()
        {
            if (!_initialized || !_dragHeld)
            {
                return false;
            }

            _dragHeld = false;
            _face.Show(MiniFaceExpression.Neutral);
            BlendToPhase(
                MotionPhase.DragRelease,
                _dragLift,
                DragHoldNormalizedTime,
                DragPlaybackSpeed,
                DragReleaseBlendSeconds);
            return true;
        }

        /// <summary>
        /// Shows immediate face feedback while the user is carrying a care item.
        /// Previewing is intentionally limited to the stable idle loop so it cannot
        /// overwrite an authored reaction already in progress.
        /// </summary>
        public bool TryPreviewFace(MiniFaceExpression expression)
        {
            if (!_initialized || _phase != MotionPhase.IdleLoop ||
                _activeAction != null || _studyRequested)
            {
                return false;
            }

            _face.Show(expression);
            return true;
        }

        public void ClearPreviewFace()
        {
            if (!_initialized || _phase != MotionPhase.IdleLoop ||
                _activeAction != null || _studyRequested)
            {
                return;
            }

            _face.Show(MiniFaceExpression.Neutral);
        }

        private bool TryStartAction(MotionSequence action)
        {
            if (!_initialized || _phase != MotionPhase.IdleLoop || action == null ||
                _studyRequested)
            {
                return false;
            }

            PlayPhase(MotionPhase.IdleEnd, _idleEnd);
            _activeAction = action;
            _activeActionLoopsRemaining = action.LoopRepetitions;
            return true;
        }

        private void SetActiveAmbientStep(int stepIndex)
        {
            LoadedAmbientVignette vignette = _activeAmbientVignette;
            if (vignette == null || stepIndex < 0 || stepIndex >= vignette.Steps.Count)
            {
                throw new InvalidOperationException(
                    "An ambient vignette tried to select an unavailable step.");
            }

            _activeAmbientStepIndex = stepIndex;
            _activeAction = vignette.Steps[stepIndex].Motion;
            _activeActionLoopsRemaining = _activeAction.LoopRepetitions;
        }

        private bool HasNextAmbientStep()
        {
            return _activeAmbientVignette != null &&
                _activeAmbientStepIndex >= 0 &&
                _activeAmbientStepIndex + 1 < _activeAmbientVignette.Steps.Count;
        }

        private bool ShouldTransitionDirectlyToNextAmbientStep()
        {
            return HasNextAmbientStep() &&
                _activeAmbientVignette.Steps[_activeAmbientStepIndex]
                    .Profile.TransitionDirectlyToNext;
        }

        private void AdvanceActiveAmbientStep()
        {
            if (!HasNextAmbientStep())
            {
                throw new InvalidOperationException(
                    "An ambient vignette tried to advance past its final step.");
            }

            SetActiveAmbientStep(_activeAmbientStepIndex + 1);
            PlayActiveActionPhase(MotionPhase.ActionStart, ActionClip.Start);
        }

        private void CompleteActiveAmbientVignette()
        {
            FinishActiveAmbientVignette(PetAmbientVignetteStage.Completed);
        }

        private void InterruptActiveAmbientVignette()
        {
            FinishActiveAmbientVignette(PetAmbientVignetteStage.Interrupted);
        }

        private void FinishActiveAmbientVignette(PetAmbientVignetteStage stage)
        {
            if (_activeAmbientVignette == null || _activeAmbientTerminalRaised)
            {
                return;
            }

            PetAmbientVignetteEvent notification = CreateAmbientVignetteEvent(stage);
            _activeAmbientTerminalRaised = true;
            try
            {
                StopAmbientCompanionTracksAndRestoreFace();
            }
            finally
            {
                // A broken companion playable must not leave the controller in
                // a terminal-but-still-active state. Publish the terminal event
                // after clearing ownership even when cleanup itself fails.
                _activeAmbientVignette = null;
                _activeAmbientStepIndex = -1;
                DispatchAmbientVignetteStageChanged(notification);
            }
        }

        private void RaiseAmbientVignetteStage(PetAmbientVignetteStage stage)
        {
            if (_activeAmbientVignette == null || _activeAmbientTerminalRaised)
            {
                return;
            }
            DispatchAmbientVignetteStageChanged(CreateAmbientVignetteEvent(stage));
        }

        private PetAmbientVignetteEvent CreateAmbientVignetteEvent(
            PetAmbientVignetteStage stage)
        {
            LoadedAmbientVignette vignette = _activeAmbientVignette;
            if (vignette == null ||
                _activeAmbientStepIndex < 0 ||
                _activeAmbientStepIndex >= vignette.Steps.Count)
            {
                throw new InvalidOperationException(
                    "An ambient vignette lifecycle event has no current step.");
            }
            return new PetAmbientVignetteEvent(
                stage,
                vignette.Profile.Id,
                vignette.Steps[_activeAmbientStepIndex].Profile.Id);
        }

        private void DispatchAmbientVignetteStageChanged(
            PetAmbientVignetteEvent notification)
        {
            Action<PetAmbientVignetteEvent> handlers = AmbientVignetteStageChanged;
            if (handlers == null)
            {
                return;
            }

            Delegate[] subscribers = handlers.GetInvocationList();
            for (int index = 0; index < subscribers.Length; index++)
            {
                try
                {
                    ((Action<PetAmbientVignetteEvent>)subscribers[index])(notification);
                }
                catch (Exception exception)
                {
                    Debug.LogError(
                        "An ambient vignette stage listener failed during " +
                        notification.Stage + ".\n" + exception);
                }
            }
        }

        private void Update()
        {
            if (!_initialized || !_currentPlayable.IsValid() || _currentClip == null)
            {
                return;
            }

            UpdateBlend();
            double duration = _currentClip.length;
            double time = _currentPlayable.GetTime();
            if (_phase == MotionPhase.DragHold && _dragHeld && duration > 0.0d)
            {
                double elapsed =
                    Time.unscaledTimeAsDouble - _dragHoldStartedAt;
                double sway = Math.Sin(
                    elapsed * Math.PI * 2.0d *
                    DragHoldSwayCyclesPerSecond) * DragHoldSwayRange;
                double sampledNormalizedTime = Math.Max(
                    0.0d,
                    Math.Min(1.0d, _dragHoldNormalizedTime + sway));
                _currentPlayable.SetTime(duration * sampledNormalizedTime);
                return;
            }
            if (duration <= 0.0d || time < duration)
            {
                return;
            }

            switch (_phase)
            {
                case MotionPhase.IdleStart:
                    PlayPhase(MotionPhase.IdleLoop, _idleLoop);
                    break;
                case MotionPhase.IdleLoop:
                    WrapCurrentClip(duration, time);
                    break;
                case MotionPhase.IdleEnd:
                    if (_studyRequested && _study != null)
                    {
                        PlayStudyPhase(MotionPhase.StudyStart, _study.Start);
                    }
                    else
                    {
                        PlayActiveActionPhase(MotionPhase.ActionStart, ActionClip.Start);
                    }
                    break;
                case MotionPhase.ActionStart:
                    PlayActiveActionPhase(MotionPhase.ActionLoop, ActionClip.Loop);
                    break;
                case MotionPhase.ActionLoop:
                    _activeActionLoopsRemaining--;
                    if (_activeActionLoopsRemaining > 0)
                    {
                        WrapCurrentClip(duration, time);
                    }
                    else if (ShouldTransitionDirectlyToNextAmbientStep())
                    {
                        AdvanceActiveAmbientStep();
                    }
                    else
                    {
                        PlayActiveActionPhase(
                            MotionPhase.ActionEnd,
                            ActionClip.End);
                    }
                    break;
                case MotionPhase.ActionEnd:
                    bool completedFeed = _activeAction == _feedResponse;
                    if (HasNextAmbientStep())
                    {
                        AdvanceActiveAmbientStep();
                    }
                    else
                    {
                        CompleteActiveAmbientVignette();
                        _activeAction = null;
                        _activeActionLoopsRemaining = 0;
                        _face.Show(MiniFaceExpression.Neutral);
                        PlayPhase(MotionPhase.IdleStart, _idleStart);
                        if (completedFeed)
                        {
                            Raise(FeedResponseCompleted);
                        }
                    }
                    break;
                case MotionPhase.StudyStart:
                    if (_studyRequested)
                    {
                        PlayStudyPhase(MotionPhase.StudyLoop, _study.Loop);
                    }
                    else
                    {
                        BlendToPhase(
                            MotionPhase.IdleStart,
                            _idleStart,
                            0.0d,
                            1.0d,
                            StudyExitBlendSeconds);
                    }
                    break;
                case MotionPhase.StudyLoop:
                    if (_studyRequested)
                    {
                        WrapCurrentClip(duration, time);
                    }
                    else
                    {
                        BlendToPhase(
                            MotionPhase.IdleStart,
                            _idleStart,
                            0.0d,
                            1.0d,
                            StudyExitBlendSeconds);
                    }
                    break;
                case MotionPhase.StudyEnd:
                    _face.Show(MiniFaceExpression.Neutral);
                    PlayPhase(MotionPhase.IdleStart, _idleStart);
                    break;
                case MotionPhase.DragLift:
                case MotionPhase.DragHold:
                    break;
                case MotionPhase.DragRelease:
                    _face.Show(MiniFaceExpression.Neutral);
                    if (_studyRequested && _study != null)
                    {
                        PlayStudyPhase(MotionPhase.StudyStart, _study.Start);
                    }
                    else
                    {
                        PlayPhase(MotionPhase.IdleStart, _idleStart);
                    }
                    break;
                default:
                    throw new InvalidOperationException(
                        "Unknown pet motion phase: " + _phase);
            }
        }

        private void PlayActiveActionPhase(MotionPhase phase, ActionClip clip)
        {
            if (_activeAction == null)
            {
                throw new InvalidOperationException(
                    "A pet action phase started without an active action.");
            }

            bool isAmbientVignette = _activeAmbientVignette != null;
            bool usesSynchronizedCompanions = isAmbientVignette &&
                _activeAmbientVignette.HasSynchronizedCompanionTracks;
            try
            {
                AnimationClip animation = SelectClip(_activeAction, clip);
                bool isFeedResponse = _activeAction == _feedResponse;
                if (!usesSynchronizedCompanions &&
                    phase == MotionPhase.ActionStart)
                {
                    _face.Show(
                        isFeedResponse
                            ? MiniFaceExpression.Eating
                            : _activeAction.Expression);
                }
                else if (!usesSynchronizedCompanions &&
                    phase == MotionPhase.ActionLoop && isFeedResponse)
                {
                    _face.Show(_activeAction.Expression);
                }

                PlayPhase(phase, animation);
                if (usesSynchronizedCompanions)
                {
                    PlayAmbientCompanionPhase(clip);
                }

                if (isAmbientVignette)
                {
                    PetAmbientVignetteStage stage;
                    switch (phase)
                    {
                        case MotionPhase.ActionStart:
                            stage = PetAmbientVignetteStage.StepStart;
                            break;
                        case MotionPhase.ActionLoop:
                            stage = PetAmbientVignetteStage.StepLoop;
                            break;
                        case MotionPhase.ActionEnd:
                            stage = PetAmbientVignetteStage.StepEnd;
                            break;
                        default:
                            throw new InvalidOperationException(
                                "An ambient vignette entered an unsupported action phase: " +
                                phase + ".");
                    }
                    RaiseAmbientVignetteStage(stage);
                }

                if (isFeedResponse)
                {
                    if (phase == MotionPhase.ActionStart)
                    {
                        Raise(FeedBiteStarted);
                    }
                    else if (phase == MotionPhase.ActionLoop)
                    {
                        Raise(FeedBiteCommitted);
                    }
                }
            }
            catch (Exception exception)
            {
                if (!isAmbientVignette)
                {
                    throw;
                }
                AbortActiveAmbientVignette(exception);
            }
        }

        private static AnimationClip SelectClip(
            MotionSequence sequence,
            ActionClip clip)
        {
            switch (clip)
            {
                case ActionClip.Start:
                    return sequence.Start;
                case ActionClip.Loop:
                    return sequence.Loop;
                case ActionClip.End:
                    return sequence.End;
                default:
                    throw new InvalidOperationException(
                        "Unknown pet action clip: " + clip);
            }
        }

        private static AnimationClip SelectClip(
            MotionTrack sequence,
            ActionClip clip)
        {
            switch (clip)
            {
                case ActionClip.Start:
                    return sequence.Start;
                case ActionClip.Loop:
                    return sequence.Loop;
                case ActionClip.End:
                    return sequence.End;
                default:
                    throw new InvalidOperationException(
                        "Unknown synchronized action clip: " + clip);
            }
        }

        private void PlayAmbientCompanionPhase(ActionClip clip)
        {
            LoadedAmbientVignette vignette = _activeAmbientVignette;
            if (vignette == null || _activeAmbientStepIndex < 0 ||
                _activeAmbientStepIndex >= vignette.Steps.Count)
            {
                throw new InvalidOperationException(
                    "A synchronized ambient phase has no current step.");
            }

            LoadedAmbientVignetteStep step =
                vignette.Steps[_activeAmbientStepIndex];
            if (step.FaceTrack == null || step.EarTrack == null)
            {
                throw new InvalidDataException(
                    "The ambient vignette is missing its synchronized face or ear track.");
            }

            AnimationClip faceClip = SelectClip(step.FaceTrack, clip);
            AnimationClip earClip = SelectClip(step.EarTrack, clip);
            DestroyAmbientCompanionPlayables();

            AnimationClipPlayable facePlayable = default(AnimationClipPlayable);
            AnimationClipPlayable earPlayable = default(AnimationClipPlayable);
            bool faceConnected = false;
            bool earConnected = false;
            try
            {
                facePlayable = CreateSynchronizedClipPlayable(
                    faceClip,
                    _currentClip);
                earPlayable = CreateSynchronizedClipPlayable(
                    earClip,
                    _currentClip);
                _graph.Connect(facePlayable, 0, _layerMixer, 1);
                faceConnected = true;
                _graph.Connect(earPlayable, 0, _layerMixer, 2);
                earConnected = true;
                _layerMixer.SetInputWeight(1, 1.0f);
                _layerMixer.SetInputWeight(2, 1.0f);
                _currentFacePlayable = facePlayable;
                _currentEarPlayable = earPlayable;
                _currentFaceClip = faceClip;
                _currentEarClip = earClip;
                _graph.Play();
            }
            catch (Exception exception)
            {
                if (_layerMixer.IsValid())
                {
                    if (faceConnected)
                    {
                        _layerMixer.DisconnectInput(1);
                    }
                    if (earConnected)
                    {
                        _layerMixer.DisconnectInput(2);
                    }
                    _layerMixer.SetInputWeight(1, 0.0f);
                    _layerMixer.SetInputWeight(2, 0.0f);
                }
                if (facePlayable.IsValid())
                {
                    facePlayable.Destroy();
                }
                if (earPlayable.IsValid())
                {
                    earPlayable.Destroy();
                }
                throw new InvalidDataException(
                    "The synchronized ambient face and ear tracks could not start.",
                    exception);
            }
        }

        private AnimationClipPlayable CreateSynchronizedClipPlayable(
            AnimationClip companionClip,
            AnimationClip bodyClip)
        {
            if (companionClip == null || companionClip.length <= 0.0f ||
                bodyClip == null || bodyClip.length <= 0.0f)
            {
                throw new InvalidDataException(
                    "A synchronized ambient clip or its body phase is empty.");
            }

            return CreateClipPlayable(
                companionClip,
                0.0d,
                companionClip.length / bodyClip.length);
        }

        private void BeginAmbientFaceControl(LoadedAmbientVignette vignette)
        {
            if (vignette == null || !vignette.HasSynchronizedCompanionTracks ||
                _face == null || _ambientFaceControllerSuppressed)
            {
                return;
            }

            _face.Show(MiniFaceExpression.Neutral);
            _ambientFaceControllerWasEnabled = _face.enabled;
            _face.enabled = false;
            _ambientFaceControllerSuppressed = true;
        }

        private void StopAmbientCompanionTracksAndRestoreFace()
        {
            DestroyAmbientCompanionPlayables();
            if (_face != null)
            {
                if (_ambientFaceControllerSuppressed)
                {
                    _face.enabled = _ambientFaceControllerWasEnabled;
                }
                _face.Show(MiniFaceExpression.Neutral);
            }
            _ambientFaceControllerSuppressed = false;
            _ambientFaceControllerWasEnabled = false;
        }

        private void AbortActiveAmbientVignette(Exception exception)
        {
            Debug.LogError(
                "Ambient vignette motion failed closed and was interrupted.\n" +
                exception);
            try
            {
                InterruptActiveAmbientVignette();
            }
            catch (Exception interruptionException)
            {
                Debug.LogError(
                    "The ambient vignette interruption cleanup also failed.\n" +
                    interruptionException);
            }
            finally
            {
                // Startup can fail after only some of these fields and
                // playables have been installed. Cleanup is best-effort, but
                // ownership state is always cleared before control returns.
                try
                {
                    StopAmbientCompanionTracksAndRestoreFace();
                }
                catch (Exception cleanupException)
                {
                    Debug.LogError(
                        "The ambient companion-track rollback failed.\n" +
                        cleanupException);
                }
                _activeAction = null;
                _activeActionLoopsRemaining = 0;
                _activeAmbientVignette = null;
                _activeAmbientStepIndex = -1;
                _activeAmbientTerminalRaised = true;
            }

            if (_face == null || !_graph.IsValid() || _idleLoop == null ||
                _idleLoop.length <= 0.0f)
            {
                return;
            }

            try
            {
                PlayPhase(MotionPhase.IdleLoop, _idleLoop);
            }
            catch (Exception idleException)
            {
                try
                {
                    StopAmbientCompanionTracksAndRestoreFace();
                }
                catch (Exception cleanupException)
                {
                    Debug.LogError(
                        "The ambient idle-recovery cleanup also failed.\n" +
                        cleanupException);
                }
                Debug.LogError(
                    "The pet could not return to idle after an ambient motion " +
                    "failure.\n" + idleException);
            }
        }

        private void PlayStudyPhase(MotionPhase phase, AnimationClip clip)
        {
            _face.Show(MiniFaceExpression.Neutral);
            PlayPhase(phase, clip);
            ApplyStudyPlaybackSpeed();
        }

        private void ApplyStudyPlaybackSpeed()
        {
            if (!_currentPlayable.IsValid() || _phase != MotionPhase.StudyLoop)
            {
                return;
            }

            _currentPlayable.SetSpeed(_studyPaused ? 0.0d : 1.0d);
        }

        private string GetCurrentActionName()
        {
            if (_activeAction != null)
            {
                return _activeAction.Name;
            }
            if (_studyRequested || IsStudyPhase(_phase))
            {
                return "Study";
            }
            return "None";
        }

        private static bool IsStudyPhase(MotionPhase phase)
        {
            return phase == MotionPhase.StudyStart ||
                phase == MotionPhase.StudyLoop ||
                phase == MotionPhase.StudyEnd;
        }

        private static void Raise(Action handler)
        {
            if (handler != null)
            {
                handler();
            }
        }

        private void ConfigureAnimator()
        {
            _animator.runtimeAnimatorController = null;
            _animator.applyRootMotion = false;
            _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            _animator.updateMode = AnimatorUpdateMode.Normal;
            _animator.enabled = true;
            _animator.Rebind();
        }

        private void CreateGraph()
        {
            _graph = PlayableGraph.Create(gameObject.name + " Pet Motions");
            _graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            _mixer = AnimationMixerPlayable.Create(_graph, 2);
            _layerMixer = AnimationLayerMixerPlayable.Create(_graph, 3);
            _graph.Connect(_mixer, 0, _layerMixer, 0);
            _layerMixer.SetInputWeight(0, 1.0f);
            _layerMixer.SetInputWeight(1, 0.0f);
            _layerMixer.SetInputWeight(2, 0.0f);
            _currentMixerInput = 0;
            _blendFromMixerInput = -1;
            AnimationPlayableOutput output = AnimationPlayableOutput.Create(
                _graph,
                "Pet Motion",
                _animator);
            output.SetSourcePlayable(_layerMixer);
            _graph.Play();
        }

        private void PlayPhase(MotionPhase phase, AnimationClip clip)
        {
            if (clip == null || clip.length <= 0.0f)
            {
                throw new InvalidDataException(
                    "The installed motion clip is empty for phase " + phase + ".");
            }

            DestroyAmbientCompanionPlayables();
            DestroyAnimationPlayables();

            _phase = phase;
            _currentClip = clip;
            _currentMixerInput = 0;
            _currentPlayable = CreateClipPlayable(clip, 0.0d, 1.0d);
            _graph.Connect(
                _currentPlayable,
                0,
                _mixer,
                _currentMixerInput);
            _mixer.SetInputWeight(_currentMixerInput, 1.0f);
            _mixer.SetInputWeight(1, 0.0f);
            _graph.Play();
            Debug.Log("Pet motion: " + phase + " (" + clip.name + ").");
        }

        private void BlendToPhase(
            MotionPhase phase,
            AnimationClip clip,
            double normalizedTime,
            double speed,
            float blendDuration)
        {
            if (clip == null || clip.length <= 0.0f)
            {
                throw new InvalidDataException(
                    "The installed motion clip is empty for phase " + phase + ".");
            }
            if (!_currentPlayable.IsValid() || blendDuration <= 0.0f)
            {
                PlayPhase(phase, clip);
                _currentPlayable.SetTime(
                    clip.length * Math.Max(0.0d, Math.Min(1.0d, normalizedTime)));
                _currentPlayable.SetSpeed(speed);
                return;
            }

            CompleteBlendImmediately();
            int nextInput = 1 - _currentMixerInput;
            _blendFromPlayable = _currentPlayable;
            _blendFromMixerInput = _currentMixerInput;
            _currentMixerInput = nextInput;
            _phase = phase;
            _currentClip = clip;
            _currentPlayable = CreateClipPlayable(
                clip,
                normalizedTime,
                speed);
            _graph.Connect(
                _currentPlayable,
                0,
                _mixer,
                _currentMixerInput);
            _mixer.SetInputWeight(_blendFromMixerInput, 1.0f);
            _mixer.SetInputWeight(_currentMixerInput, 0.0f);
            _blendStartedAt = Time.unscaledTime;
            _blendDuration = blendDuration;
            _blendActive = true;
            _graph.Play();
            Debug.Log("Pet motion: " + phase + " (" + clip.name + ", blended).");
        }

        private AnimationClipPlayable CreateClipPlayable(
            AnimationClip clip,
            double normalizedTime,
            double speed)
        {
            AnimationClipPlayable playable =
                AnimationClipPlayable.Create(_graph, clip);
            playable.SetApplyFootIK(false);
            playable.SetApplyPlayableIK(false);
            playable.SetDuration(double.PositiveInfinity);
            playable.SetTime(
                clip.length * Math.Max(0.0d, Math.Min(1.0d, normalizedTime)));
            playable.SetSpeed(speed);
            return playable;
        }

        private void UpdateBlend()
        {
            if (!_blendActive)
            {
                return;
            }

            float progress = Mathf.Clamp01(
                (Time.unscaledTime - _blendStartedAt) /
                Mathf.Max(0.0001f, _blendDuration));
            float smoothProgress =
                progress * progress * (3.0f - 2.0f * progress);
            _mixer.SetInputWeight(_blendFromMixerInput, 1.0f - smoothProgress);
            _mixer.SetInputWeight(_currentMixerInput, smoothProgress);
            if (progress >= 1.0f)
            {
                CompleteBlendImmediately();
            }
        }

        private void CompleteBlendImmediately()
        {
            if (!_blendActive)
            {
                return;
            }

            if (_blendFromMixerInput >= 0)
            {
                _mixer.DisconnectInput(_blendFromMixerInput);
            }
            if (_blendFromPlayable.IsValid())
            {
                _blendFromPlayable.Destroy();
            }
            _blendFromPlayable = default(AnimationClipPlayable);
            _blendFromMixerInput = -1;
            _blendActive = false;
            _mixer.SetInputWeight(_currentMixerInput, 1.0f);
        }

        private void DestroyAnimationPlayables()
        {
            if (_blendFromMixerInput >= 0)
            {
                _mixer.DisconnectInput(_blendFromMixerInput);
            }
            if (_blendFromPlayable.IsValid())
            {
                _blendFromPlayable.Destroy();
            }
            if (_currentPlayable.IsValid())
            {
                _mixer.DisconnectInput(_currentMixerInput);
                _currentPlayable.Destroy();
            }
            _blendFromPlayable = default(AnimationClipPlayable);
            _currentPlayable = default(AnimationClipPlayable);
            _blendFromMixerInput = -1;
            _blendActive = false;
        }

        private void DestroyAmbientCompanionPlayables()
        {
            if (_layerMixer.IsValid())
            {
                if (_currentFacePlayable.IsValid())
                {
                    _layerMixer.DisconnectInput(1);
                }
                if (_currentEarPlayable.IsValid())
                {
                    _layerMixer.DisconnectInput(2);
                }
                _layerMixer.SetInputWeight(1, 0.0f);
                _layerMixer.SetInputWeight(2, 0.0f);
            }
            if (_currentFacePlayable.IsValid())
            {
                _currentFacePlayable.Destroy();
            }
            if (_currentEarPlayable.IsValid())
            {
                _currentEarPlayable.Destroy();
            }
            _currentFacePlayable = default(AnimationClipPlayable);
            _currentEarPlayable = default(AnimationClipPlayable);
            _currentFaceClip = null;
            _currentEarClip = null;
        }

        private void WrapCurrentClip(double duration, double time)
        {
            double wrapped = time - Math.Floor(time / duration) * duration;
            _currentPlayable.SetTime(wrapped);
            _currentPlayable.SetDone(false);
            double normalizedTime = wrapped / duration;
            WrapCompanionClip(
                _currentFacePlayable,
                _currentFaceClip,
                normalizedTime);
            WrapCompanionClip(
                _currentEarPlayable,
                _currentEarClip,
                normalizedTime);
        }

        private static void WrapCompanionClip(
            AnimationClipPlayable playable,
            AnimationClip clip,
            double normalizedTime)
        {
            if (!playable.IsValid() || clip == null || clip.length <= 0.0f)
            {
                return;
            }
            playable.SetTime(clip.length * normalizedTime);
            playable.SetDone(false);
        }

        private static AnimationClip LoadRequiredClip(
            BundleLease lease,
            string logicalName)
        {
            AssetBundle bundle = lease.GetRequiredBundle(logicalName);
            string expectedName = Path.GetFileName(logicalName);
            string matchingAssetName = null;
            foreach (string assetName in bundle.GetAllAssetNames())
            {
                if (!string.Equals(
                    Path.GetFileNameWithoutExtension(assetName),
                    expectedName,
                    StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (matchingAssetName != null)
                {
                    throw new InvalidDataException(
                        "The installed bundle contains multiple matches for " +
                        "the expected motion: " + logicalName);
                }
                matchingAssetName = assetName;
            }

            if (matchingAssetName != null)
            {
                AnimationClip exact =
                    bundle.LoadAsset<AnimationClip>(matchingAssetName);
                if (exact != null)
                {
                    return exact;
                }
            }

            throw new InvalidDataException(
                "The installed bundle does not contain the expected motion: " + logicalName);
        }

        private static MotionSequence LoadSequence(
            BundleLease lease,
            string name,
            string startAsset,
            string loopAsset,
            string endAsset,
            MiniFaceExpression expression,
            int loopRepetitions = 1)
        {
            return new MotionSequence(
                name,
                LoadRequiredClip(lease, startAsset),
                LoadRequiredClip(lease, loopAsset),
                LoadRequiredClip(lease, endAsset),
                expression,
                loopRepetitions);
        }

        private static AnimationClip LoadRequiredClip(
            BundleLease lease,
            string logicalName,
            IDictionary<string, AnimationClip> cache)
        {
            AnimationClip clip;
            if (cache.TryGetValue(logicalName, out clip))
            {
                return clip;
            }
            clip = LoadRequiredClip(lease, logicalName);
            cache.Add(logicalName, clip);
            return clip;
        }

        private static MotionSequence LoadSequence(
            BundleLease lease,
            PetMotionSequenceProfile profile,
            IDictionary<string, AnimationClip> cache)
        {
            return new MotionSequence(
                profile.Name,
                LoadRequiredClip(lease, profile.StartAsset, cache),
                LoadRequiredClip(lease, profile.LoopAsset, cache),
                LoadRequiredClip(lease, profile.EndAsset, cache),
                profile.Expression,
                profile.LoopRepetitions);
        }

        private static MotionTrack LoadSynchronizedTrack(
            BundleLease lease,
            PetSynchronizedMotionTrackProfile profile,
            IDictionary<string, AnimationClip> clipCache,
            ISet<string> validatedMetadata)
        {
            if (profile == null)
            {
                return null;
            }

            // The plain face/ear roots contain AnimationClips. Their paired
            // *_driven roots are proprietary metadata, so validate the exact
            // embedded basenames without trying to deserialize them as clips.
            ValidateRequiredMetadataAsset(
                lease,
                profile.DrivenStartAsset,
                validatedMetadata);
            ValidateRequiredMetadataAsset(
                lease,
                profile.DrivenLoopAsset,
                validatedMetadata);
            ValidateRequiredMetadataAsset(
                lease,
                profile.DrivenEndAsset,
                validatedMetadata);
            return new MotionTrack(
                LoadRequiredClip(lease, profile.StartAsset, clipCache),
                LoadRequiredClip(lease, profile.LoopAsset, clipCache),
                LoadRequiredClip(lease, profile.EndAsset, clipCache));
        }

        private static void ValidateRequiredMetadataAsset(
            BundleLease lease,
            string logicalName,
            ISet<string> validatedMetadata)
        {
            if (!validatedMetadata.Add(logicalName))
            {
                return;
            }

            AssetBundle bundle = lease.GetRequiredBundle(logicalName);
            string expectedName = Path.GetFileName(logicalName);
            int matchingAssetCount = 0;
            foreach (string assetName in bundle.GetAllAssetNames())
            {
                if (string.Equals(
                    Path.GetFileNameWithoutExtension(assetName),
                    expectedName,
                    StringComparison.OrdinalIgnoreCase))
                {
                    matchingAssetCount++;
                }
            }

            if (matchingAssetCount != 1)
            {
                throw new InvalidDataException(
                    matchingAssetCount == 0
                        ? "The installed bundle does not contain the expected " +
                            "driven motion metadata: " + logicalName
                        : "The installed bundle contains multiple matches for " +
                            "the expected driven motion metadata: " + logicalName);
            }
        }

        private void TryLoadOptionalStudyMotion(BundleRepository repository)
        {
            BundleLease optionalLease = null;
            try
            {
                optionalLease = repository.AcquireMany(OptionalStudyMotionAssets);
                MotionSequence sequence = LoadSequence(
                    optionalLease,
                    "Study",
                    StudyStartAsset,
                    StudyLoopAsset,
                    StudyEndAsset,
                    MiniFaceExpression.Neutral);
                _studyMotionLease = optionalLease;
                _study = sequence;
                Debug.Log("Optional study motion is available.");
            }
            catch (Exception exception)
            {
                if (optionalLease != null)
                {
                    try
                    {
                        optionalLease.Dispose();
                    }
                    catch (Exception cleanupException)
                    {
                        Debug.LogWarning(
                            "Could not release the optional study bundles: " +
                            cleanupException.Message);
                    }
                }

                _studyMotionLease = null;
                _study = null;
                Debug.LogWarning(
                    "Optional study motion is unavailable; continuing " +
                    "without it. " + exception.Message);
            }
        }

        private void TryLoadOptionalAmbientVignettes(BundleRepository repository)
        {
            if (_characterMotionProfile == null)
            {
                return;
            }

            foreach (PetAmbientVignetteProfile profile in
                _characterMotionProfile.AmbientVignettes)
            {
                BundleLease optionalLease = null;
                try
                {
                    string[] requiredAssets = profile.Steps
                        .SelectMany(step => step.Motion.EnumerateRequiredAssets())
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray();
                    optionalLease = repository.AcquireMany(requiredAssets);

                    var loadedSteps = new List<LoadedAmbientVignetteStep>();
                    var loadedClips = new Dictionary<string, AnimationClip>(
                        StringComparer.OrdinalIgnoreCase);
                    var validatedMetadata = new HashSet<string>(
                        StringComparer.OrdinalIgnoreCase);
                    foreach (PetAmbientVignetteStepProfile step in profile.Steps)
                    {
                        PetMotionSequenceProfile motion = step.Motion;
                        loadedSteps.Add(new LoadedAmbientVignetteStep(
                            step,
                            LoadSequence(optionalLease, motion, loadedClips),
                            LoadSynchronizedTrack(
                                optionalLease,
                                motion.FaceTrack,
                                loadedClips,
                                validatedMetadata),
                            LoadSynchronizedTrack(
                                optionalLease,
                                motion.EarTrack,
                                loadedClips,
                                validatedMetadata)));
                    }

                    var loaded = new LoadedAmbientVignette(profile, loadedSteps);
                    _ambientVignetteMotionLeases.Add(profile.Id, optionalLease);
                    _ambientVignettes.Add(profile.Id, loaded);
                    _availableAmbientVignettes.Add(profile);
                    optionalLease = null;
                    Debug.Log(
                        "Optional ambient vignette is available for " +
                        _characterMotionProfile.CharacterKey + ": " +
                        profile.DisplayName + " (" + profile.Steps.Count +
                        " steps).");
                }
                catch (Exception exception)
                {
                    _ambientVignettes.Remove(profile.Id);
                    _ambientVignetteMotionLeases.Remove(profile.Id);
                    _availableAmbientVignettes.Remove(profile);
                    if (optionalLease != null)
                    {
                        try
                        {
                            optionalLease.Dispose();
                        }
                        catch (Exception cleanupException)
                        {
                            Debug.LogWarning(
                                "Could not release rejected ambient vignette bundles " +
                                "for " + profile.Id + ": " +
                                cleanupException.Message);
                        }
                    }

                    Debug.LogWarning(
                        "Optional ambient vignette is unavailable for " +
                        _characterMotionProfile.CharacterKey + ": " +
                        profile.DisplayName + ". Continuing without it. " +
                        exception.Message);
                }
            }
        }

        private void OnEnable()
        {
            if (_graph.IsValid())
            {
                _graph.Play();
            }
        }

        private void OnDisable()
        {
            if (_graph.IsValid())
            {
                _graph.Stop();
            }
        }

        private void OnDestroy()
        {
            ReleaseResources();
        }

        public void ReleaseResources()
        {
            InterruptActiveAmbientVignette();
            StopAmbientCompanionTracksAndRestoreFace();
            if (_graph.IsValid())
            {
                _graph.Destroy();
            }
            _graph = default(PlayableGraph);
            _mixer = default(AnimationMixerPlayable);
            _layerMixer = default(AnimationLayerMixerPlayable);
            _currentPlayable = default(AnimationClipPlayable);
            _blendFromPlayable = default(AnimationClipPlayable);
            _currentFacePlayable = default(AnimationClipPlayable);
            _currentEarPlayable = default(AnimationClipPlayable);
            _currentClip = null;
            _currentFaceClip = null;
            _currentEarClip = null;
            _currentMixerInput = 0;
            _blendFromMixerInput = -1;
            _blendStartedAt = 0.0f;
            _blendDuration = 0.0f;
            _blendActive = false;
            _idleStart = null;
            _idleLoop = null;
            _idleEnd = null;
            _tapReaction = null;
            _patHappy = null;
            _feedResponse = null;
            _ambientGreeting = null;
            _study = null;
            _dragLift = null;
            _dragHold = null;
            _dragHoldNormalizedTime = DragHoldNormalizedTime;
            _dragHoldStartedAt = 0.0d;
            _activeAction = null;
            _activeActionLoopsRemaining = 0;
            _activeAmbientVignette = null;
            _activeAmbientStepIndex = -1;
            _activeAmbientTerminalRaised = false;
            _dragHeld = false;
            _studyRequested = false;
            _studyPaused = false;
            _ambientFaceControllerSuppressed = false;
            _ambientFaceControllerWasEnabled = false;
            _face = null;

            // Drop every loaded clip reference before releasing the bundles that
            // own those clips. Profiles contain logical names only and may remain
            // in the read-only availability view while the list is being cleared.
            _ambientVignettes.Clear();
            _availableAmbientVignettes.Clear();
            foreach (KeyValuePair<string, BundleLease> item in
                _ambientVignetteMotionLeases)
            {
                if (item.Value == null)
                {
                    continue;
                }
                try
                {
                    item.Value.Dispose();
                }
                catch (Exception exception)
                {
                    Debug.LogWarning(
                        "Could not release optional ambient vignette bundles for " +
                        item.Key + ": " + exception.Message);
                }
            }
            _ambientVignetteMotionLeases.Clear();

            if (_studyMotionLease != null)
            {
                try
                {
                    _studyMotionLease.Dispose();
                }
                catch (Exception exception)
                {
                    Debug.LogWarning(
                        "Could not release the optional study bundles: " +
                        exception.Message);
                }
                _studyMotionLease = null;
            }

            if (_motionLease != null)
            {
                _motionLease.Dispose();
                _motionLease = null;
            }
            _characterMotionProfile = null;
            AmbientVignetteStageChanged = null;
            _initialized = false;
        }

        private enum MotionPhase
        {
            IdleStart,
            IdleLoop,
            IdleEnd,
            ActionStart,
            ActionLoop,
            ActionEnd,
            StudyStart,
            StudyLoop,
            StudyEnd,
            DragLift,
            DragHold,
            DragRelease
        }

        private enum ActionClip
        {
            Start,
            Loop,
            End
        }

        private sealed class LoadedAmbientVignette
        {
            private readonly IReadOnlyList<LoadedAmbientVignetteStep> _steps;

            public LoadedAmbientVignette(
                PetAmbientVignetteProfile profile,
                IList<LoadedAmbientVignetteStep> steps)
            {
                if (profile == null)
                {
                    throw new ArgumentNullException("profile");
                }
                if (steps == null || steps.Count != profile.Steps.Count)
                {
                    throw new ArgumentException(
                        "A loaded ambient vignette requires every profiled step.",
                        "steps");
                }

                Profile = profile;
                _steps = new ReadOnlyCollection<LoadedAmbientVignetteStep>(
                    new List<LoadedAmbientVignetteStep>(steps));
                int synchronizedStepCount = steps.Count(
                    step => step.HasSynchronizedCompanionTracks);
                if (synchronizedStepCount != 0 &&
                    synchronizedStepCount != steps.Count)
                {
                    throw new InvalidDataException(
                        "An ambient vignette cannot mix synchronized and " +
                        "body-only motion steps.");
                }
                HasSynchronizedCompanionTracks = synchronizedStepCount > 0;
            }

            public PetAmbientVignetteProfile Profile { get; private set; }

            public IReadOnlyList<LoadedAmbientVignetteStep> Steps
            {
                get { return _steps; }
            }

            public bool HasSynchronizedCompanionTracks { get; private set; }
        }

        private sealed class LoadedAmbientVignetteStep
        {
            public LoadedAmbientVignetteStep(
                PetAmbientVignetteStepProfile profile,
                MotionSequence motion,
                MotionTrack faceTrack,
                MotionTrack earTrack)
            {
                if (profile == null)
                {
                    throw new ArgumentNullException("profile");
                }
                if (motion == null)
                {
                    throw new ArgumentNullException("motion");
                }
                if ((faceTrack == null) != (earTrack == null) ||
                    profile.Motion.HasSynchronizedCompanionTracks !=
                        (faceTrack != null && earTrack != null))
                {
                    throw new InvalidDataException(
                        "A loaded ambient step does not match its synchronized " +
                        "face and ear profile.");
                }
                Profile = profile;
                Motion = motion;
                FaceTrack = faceTrack;
                EarTrack = earTrack;
            }

            public PetAmbientVignetteStepProfile Profile { get; private set; }

            public MotionSequence Motion { get; private set; }

            public MotionTrack FaceTrack { get; private set; }

            public MotionTrack EarTrack { get; private set; }

            public bool HasSynchronizedCompanionTracks
            {
                get { return FaceTrack != null && EarTrack != null; }
            }
        }

        private sealed class MotionTrack
        {
            public MotionTrack(
                AnimationClip start,
                AnimationClip loop,
                AnimationClip end)
            {
                if (start == null || loop == null || end == null)
                {
                    throw new ArgumentNullException(
                        "A synchronized motion track requires every phase.");
                }
                Start = start;
                Loop = loop;
                End = end;
            }

            public AnimationClip Start { get; private set; }
            public AnimationClip Loop { get; private set; }
            public AnimationClip End { get; private set; }
        }

        private sealed class MotionSequence
        {
            public MotionSequence(
                string name,
                AnimationClip start,
                AnimationClip loop,
                AnimationClip end,
                MiniFaceExpression expression,
                int loopRepetitions)
            {
                Name = name;
                Start = start;
                Loop = loop;
                End = end;
                Expression = expression;
                LoopRepetitions = loopRepetitions;
            }

            public string Name { get; private set; }

            public AnimationClip Start { get; private set; }

            public AnimationClip Loop { get; private set; }

            public AnimationClip End { get; private set; }

            public MiniFaceExpression Expression { get; private set; }

            public int LoopRepetitions { get; private set; }
        }
    }
}
