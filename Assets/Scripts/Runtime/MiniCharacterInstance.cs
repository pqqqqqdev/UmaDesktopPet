using System;
using UnityEngine;

namespace UmaDesktopPet.Standalone.Runtime
{
    /// <summary>
    /// Owns the runtime objects and bundle lease that back one assembled mini character.
    /// </summary>
    public sealed class MiniCharacterInstance : MonoBehaviour
    {
        private BundleLease _bundleLease;

        public Animator Animator { get; private set; }
        public PlayableIdleController IdleController { get; private set; }
        public MiniFaceExpressionController FaceController { get; private set; }

        internal void Initialize(
            BundleLease bundleLease,
            Animator animator,
            PlayableIdleController idleController,
            MiniFaceExpressionController faceController)
        {
            if (bundleLease == null)
            {
                throw new ArgumentNullException("bundleLease");
            }
            if (animator == null)
            {
                throw new ArgumentNullException("animator");
            }
            if (idleController == null)
            {
                throw new ArgumentNullException("idleController");
            }
            if (faceController == null)
            {
                throw new ArgumentNullException("faceController");
            }
            if (_bundleLease != null)
            {
                throw new InvalidOperationException("The character instance is already initialized.");
            }

            _bundleLease = bundleLease;
            Animator = animator;
            IdleController = idleController;
            FaceController = faceController;
        }

        private void OnDestroy()
        {
            ReleaseResources();
        }

        internal void ReleaseResources()
        {
            if (_bundleLease != null)
            {
                _bundleLease.Dispose();
                _bundleLease = null;
            }
        }
    }
}
