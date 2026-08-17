using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace UmaDesktopPet.Standalone.Runtime
{
    /// <summary>
    /// Owns the first desktop-pet motion state machine: Oguri idle plus named responses.
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

        private const double DragHoldNormalizedTime = 0.42d;
        private const double DragPlaybackSpeed = 1.65d;
        private const double DragHoldSwayRange = 0.012d;
        private const double DragHoldSwayCyclesPerSecond = 0.7d;
        private const float DragPickupBlendSeconds = 0.16f;
        private const float DragReleaseBlendSeconds = 0.10f;

        private static readonly string[] RequiredMotionAssets =
        {
            IdleStartAsset,
            IdleLoopAsset,
            IdleEndAsset,
            TapStartAsset,
            TapLoopAsset,
            TapEndAsset,
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

        private Animator _animator;
        private MiniFaceExpressionController _face;
        private BundleLease _motionLease;
        private PlayableGraph _graph;
        private AnimationMixerPlayable _mixer;
        private AnimationClipPlayable _currentPlayable;
        private AnimationClipPlayable _blendFromPlayable;
        private AnimationClip _currentClip;
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
        private AnimationClip _dragLift;
        private AnimationClip _dragHold;
        private double _dragHoldNormalizedTime;
        private double _dragHoldStartedAt;
        private MotionSequence _activeAction;
        private MotionPhase _phase;
        private bool _dragHeld;
        private bool _initialized;

        /// <summary>
        /// Raised when the authored feeding response reaches the part where the
        /// carrot is at Oguri's mouth, when the bite commits, and when the whole
        /// response finishes. This keeps care state synchronized with the motion
        /// instead of guessing with a wall-clock timer.
        /// </summary>
        public event Action FeedBiteStarted;
        public event Action FeedBiteCommitted;
        public event Action FeedResponseCompleted;

        public bool IsBusy
        {
            get { return _initialized && _phase != MotionPhase.IdleLoop; }
        }

        public string CurrentPhase
        {
            get
            {
                if (!_initialized)
                {
                    return "Uninitialized";
                }

                return _activeAction == null
                    ? _phase.ToString()
                    : _activeAction.Name + "/" + _phase;
            }
        }

        public string CurrentAction
        {
            get { return _activeAction == null ? "None" : _activeAction.Name; }
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
            string diagnosticDragHoldAsset = null,
            double? diagnosticDragHoldNormalizedTime = null)
        {
            if (_initialized)
            {
                throw new InvalidOperationException(
                    "The Oguri animation controller is already initialized.");
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

            PlayableIdleController oldIdle = GetComponent<PlayableIdleController>();
            if (oldIdle != null)
            {
                oldIdle.Stop();
                oldIdle.enabled = false;
            }

            _animator = animator;
            _face = face;
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
                string[] requiredAssets = RequiredMotionAssets
                    .Concat(new[] { selectedDragHoldAsset })
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                _motionLease = repository.AcquireMany(requiredAssets);
                _idleStart = LoadRequiredClip(_motionLease, IdleStartAsset);
                _idleLoop = LoadRequiredClip(_motionLease, IdleLoopAsset);
                _idleEnd = LoadRequiredClip(_motionLease, IdleEndAsset);
                _tapReaction = LoadSequence(
                    _motionLease,
                    "TapReaction",
                    TapStartAsset,
                    TapLoopAsset,
                    TapEndAsset,
                    MiniFaceExpression.Tap);
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
        /// Starts one complete Oguri response and rejects overlap until idle resumes.
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
        /// evolve without pretending that the user patted Oguri.
        /// </summary>
        public bool TriggerAmbientHappy()
        {
            return TryStartAction(_patHappy);
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

            _activeAction = null;
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
                _activeAction != null)
            {
                return false;
            }

            _face.Show(expression);
            return true;
        }

        public void ClearPreviewFace()
        {
            if (!_initialized || _phase != MotionPhase.IdleLoop ||
                _activeAction != null)
            {
                return;
            }

            _face.Show(MiniFaceExpression.Neutral);
        }

        private bool TryStartAction(MotionSequence action)
        {
            if (!_initialized || _phase != MotionPhase.IdleLoop || action == null)
            {
                return false;
            }

            PlayPhase(MotionPhase.IdleEnd, _idleEnd);
            _activeAction = action;
            return true;
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
                    PlayActiveActionPhase(MotionPhase.ActionStart, ActionClip.Start);
                    break;
                case MotionPhase.ActionStart:
                    PlayActiveActionPhase(MotionPhase.ActionLoop, ActionClip.Loop);
                    break;
                case MotionPhase.ActionLoop:
                    PlayActiveActionPhase(MotionPhase.ActionEnd, ActionClip.End);
                    break;
                case MotionPhase.ActionEnd:
                    bool completedFeed = _activeAction == _feedResponse;
                    _activeAction = null;
                    _face.Show(MiniFaceExpression.Neutral);
                    PlayPhase(MotionPhase.IdleStart, _idleStart);
                    if (completedFeed)
                    {
                        Raise(FeedResponseCompleted);
                    }
                    break;
                case MotionPhase.DragLift:
                case MotionPhase.DragHold:
                    break;
                case MotionPhase.DragRelease:
                    _face.Show(MiniFaceExpression.Neutral);
                    PlayPhase(MotionPhase.IdleStart, _idleStart);
                    break;
                default:
                    throw new InvalidOperationException(
                        "Unknown Oguri motion phase: " + _phase);
            }
        }

        private void PlayActiveActionPhase(MotionPhase phase, ActionClip clip)
        {
            if (_activeAction == null)
            {
                throw new InvalidOperationException(
                    "An Oguri action phase started without an active action.");
            }

            AnimationClip animation;
            switch (clip)
            {
                case ActionClip.Start:
                    animation = _activeAction.Start;
                    break;
                case ActionClip.Loop:
                    animation = _activeAction.Loop;
                    break;
                case ActionClip.End:
                    animation = _activeAction.End;
                    break;
                default:
                    throw new InvalidOperationException(
                        "Unknown Oguri action clip: " + clip);
            }

            bool isFeedResponse = _activeAction == _feedResponse;
            if (phase == MotionPhase.ActionStart)
            {
                _face.Show(
                    isFeedResponse
                        ? MiniFaceExpression.Eating
                        : _activeAction.Expression);
            }
            else if (phase == MotionPhase.ActionLoop && isFeedResponse)
            {
                _face.Show(_activeAction.Expression);
            }

            PlayPhase(phase, animation);

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
            _currentMixerInput = 0;
            _blendFromMixerInput = -1;
            AnimationPlayableOutput output = AnimationPlayableOutput.Create(
                _graph,
                "Pet Motion",
                _animator);
            output.SetSourcePlayable(_mixer);
            _graph.Play();
        }

        private void PlayPhase(MotionPhase phase, AnimationClip clip)
        {
            if (clip == null || clip.length <= 0.0f)
            {
                throw new InvalidDataException(
                    "The installed motion clip is empty for phase " + phase + ".");
            }

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
            Debug.Log("Oguri motion: " + phase + " (" + clip.name + ").");
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
            Debug.Log("Oguri motion: " + phase + " (" + clip.name + ", blended).");
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

        private void WrapCurrentClip(double duration, double time)
        {
            double wrapped = time - Math.Floor(time / duration) * duration;
            _currentPlayable.SetTime(wrapped);
            _currentPlayable.SetDone(false);
        }

        private static AnimationClip LoadRequiredClip(
            BundleLease lease,
            string logicalName)
        {
            AssetBundle bundle = lease.GetRequiredBundle(logicalName);
            string expectedName = Path.GetFileName(logicalName);
            foreach (string assetName in bundle.GetAllAssetNames())
            {
                if (!string.Equals(
                    Path.GetFileNameWithoutExtension(assetName),
                    expectedName,
                    StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                AnimationClip exact = bundle.LoadAsset<AnimationClip>(assetName);
                if (exact != null)
                {
                    return exact;
                }
            }

            AnimationClip[] candidates = bundle.LoadAllAssets<AnimationClip>();
            AnimationClip named = candidates.FirstOrDefault(
                clip => string.Equals(
                    clip.name,
                    expectedName,
                    StringComparison.OrdinalIgnoreCase));
            if (named != null)
            {
                return named;
            }
            if (candidates.Length == 1)
            {
                return candidates[0];
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
            MiniFaceExpression expression)
        {
            return new MotionSequence(
                name,
                LoadRequiredClip(lease, startAsset),
                LoadRequiredClip(lease, loopAsset),
                LoadRequiredClip(lease, endAsset),
                expression);
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

        private void ReleaseResources()
        {
            if (_graph.IsValid())
            {
                _graph.Destroy();
            }
            _graph = default(PlayableGraph);
            _mixer = default(AnimationMixerPlayable);
            _currentPlayable = default(AnimationClipPlayable);
            _blendFromPlayable = default(AnimationClipPlayable);
            _currentClip = null;
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
            _dragLift = null;
            _dragHold = null;
            _dragHoldNormalizedTime = DragHoldNormalizedTime;
            _dragHoldStartedAt = 0.0d;
            _activeAction = null;
            _dragHeld = false;
            _face = null;

            if (_motionLease != null)
            {
                _motionLease.Dispose();
                _motionLease = null;
            }
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

        private sealed class MotionSequence
        {
            public MotionSequence(
                string name,
                AnimationClip start,
                AnimationClip loop,
                AnimationClip end,
                MiniFaceExpression expression)
            {
                Name = name;
                Start = start;
                Loop = loop;
                End = end;
                Expression = expression;
            }

            public string Name { get; private set; }

            public AnimationClip Start { get; private set; }

            public AnimationClip Loop { get; private set; }

            public AnimationClip End { get; private set; }

            public MiniFaceExpression Expression { get; private set; }
        }
    }
}
