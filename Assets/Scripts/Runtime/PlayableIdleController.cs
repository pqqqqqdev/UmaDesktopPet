using System;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace UmaDesktopPet.Standalone.Runtime
{
    /// <summary>
    /// Plays a single looping clip directly through Playables, without a game AnimatorController.
    /// </summary>
    public sealed class PlayableIdleController : MonoBehaviour
    {
        private Animator _animator;
        private AnimationClip _idleClip;
        private PlayableGraph _graph;
        private AnimationClipPlayable _idlePlayable;

        public bool IsPlaying
        {
            get { return _graph.IsValid() && _graph.IsPlaying(); }
        }

        public void Play(Animator animator, AnimationClip idleClip)
        {
            if (animator == null)
            {
                throw new ArgumentNullException("animator");
            }
            if (idleClip == null)
            {
                throw new ArgumentNullException("idleClip");
            }
            if (idleClip.length <= 0.0f)
            {
                throw new ArgumentException("The idle animation has no duration.", "idleClip");
            }

            DestroyGraph();
            _animator = animator;
            _idleClip = idleClip;

            // The standalone player deliberately does not load the game's controller or scripts.
            _animator.runtimeAnimatorController = null;
            _animator.applyRootMotion = false;
            _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            _animator.updateMode = AnimatorUpdateMode.Normal;
            _animator.enabled = true;
            _animator.Rebind();

            try
            {
                _graph = PlayableGraph.Create(gameObject.name + " Idle");
                _graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);

                _idlePlayable = AnimationClipPlayable.Create(_graph, _idleClip);
                _idlePlayable.SetApplyFootIK(false);
                _idlePlayable.SetApplyPlayableIK(false);
                _idlePlayable.SetDuration(double.PositiveInfinity);

                AnimationPlayableOutput output = AnimationPlayableOutput.Create(
                    _graph,
                    "Idle",
                    _animator);
                output.SetSourcePlayable(_idlePlayable);

                _idlePlayable.SetTime(0.0d);
                _idlePlayable.SetSpeed(1.0d);
                _graph.Play();
            }
            catch
            {
                DestroyGraph();
                throw;
            }
        }

        public void Restart()
        {
            if (!_graph.IsValid() || !_idlePlayable.IsValid())
            {
                return;
            }

            _idlePlayable.SetTime(0.0d);
            _idlePlayable.SetDone(false);
            _graph.Play();
        }

        public void Stop()
        {
            DestroyGraph();
        }

        private void Update()
        {
            if (!_graph.IsValid() || !_idlePlayable.IsValid() || _idleClip == null)
            {
                return;
            }

            // The source clip is expected to be authored as a loop. This explicit wrap also
            // keeps playback looping if that import flag changes in a future game update.
            double duration = _idleClip.length;
            double time = _idlePlayable.GetTime();
            if (duration > 0.0d && (time >= duration || time < 0.0d))
            {
                double wrapped = time - Math.Floor(time / duration) * duration;
                _idlePlayable.SetTime(wrapped);
                _idlePlayable.SetDone(false);
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
            DestroyGraph();
        }

        private void DestroyGraph()
        {
            if (_graph.IsValid())
            {
                _graph.Destroy();
            }
            _graph = default(PlayableGraph);
            _idlePlayable = default(AnimationClipPlayable);
            _idleClip = null;
            _animator = null;
        }
    }
}
