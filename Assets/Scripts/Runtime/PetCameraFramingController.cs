using System;
using System.Collections.Generic;
using UnityEngine;

namespace UmaDesktopPet.Standalone.Runtime
{
    /// <summary>
    /// Recenters the animated visual while a drag reaction or signature vignette
    /// is active. Drag framing may uniformly shrink an extreme held pose to keep
    /// it visible. Signature framing never changes the authored pet scale and is
    /// locked once at setup so an animated character cannot make grounded
    /// furniture drift. The camera and native desktop window stay untouched, so
    /// settings-sidecar positioning remains stable.
    /// </summary>
    public sealed class PetCameraFramingController : MonoBehaviour
    {
        private const float ViewportGutter = 0.055f;
        private const float ActiveResponsiveness = 22.0f;
        private const float RestoreResponsiveness = 12.0f;

        private Camera _camera;
        private Transform _visualFrame;
        private readonly List<Renderer> _renderers = new List<Renderer>();
        private GameObject _renderRoot;
        private OguriPetAnimationController _motions;
        private PetAmbientVignettePresenter _vignettePresenter;
        private Vector3 _compactLocalPosition;
        private Vector3 _compactLocalScale;
        private float _activeUniformScale;
        private Vector3 _signatureTargetLocalPosition;
        private Vector3 _signatureTargetLocalScale;
        private bool _signatureFramingLocked;
        private bool _initialized;

        internal float CompactUniformScale
        {
            get { return UniformScale(_compactLocalScale); }
        }

        internal float CurrentUniformScale
        {
            get
            {
                return _visualFrame == null
                    ? 0.0f
                    : UniformScale(_visualFrame.localScale);
            }
        }

        internal bool IsSignatureFramingLocked
        {
            get { return _signatureFramingLocked; }
        }

        public void Initialize(
            Camera camera,
            Transform visualFrame,
            GameObject renderRoot,
            OguriPetAnimationController motions,
            PetAmbientVignettePresenter vignettePresenter)
        {
            if (camera == null)
            {
                throw new ArgumentNullException("camera");
            }
            if (visualFrame == null)
            {
                throw new ArgumentNullException("visualFrame");
            }
            if (renderRoot == null)
            {
                throw new ArgumentNullException("renderRoot");
            }
            if (motions == null)
            {
                throw new ArgumentNullException("motions");
            }
            if (vignettePresenter == null)
            {
                throw new ArgumentNullException("vignettePresenter");
            }

            _camera = camera;
            _visualFrame = visualFrame;
            _renderRoot = renderRoot;
            RefreshRenderers();
            if (_renderers.Count == 0)
            {
                throw new InvalidOperationException(
                    "Active framing requires at least one visual renderer.");
            }
            foreach (SkinnedMeshRenderer renderer in
                renderRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                renderer.updateWhenOffscreen = true;
            }

            _motions = motions;
            _vignettePresenter = vignettePresenter;
            _compactLocalPosition = visualFrame.localPosition;
            _compactLocalScale = visualFrame.localScale;
            _activeUniformScale = UniformScale(_compactLocalScale);
            _initialized = true;
        }

        private void LateUpdate()
        {
            if (!_initialized || _camera == null || _visualFrame == null)
            {
                return;
            }

            bool usesDragFraming =
                _motions != null && _motions.UsesDragFraming;
            bool usesSignatureFraming =
                _vignettePresenter != null && _vignettePresenter.IsPlaying;
            if (!usesDragFraming && !usesSignatureFraming)
            {
                ClearSignatureFramingLock();
                RestoreCompactTransform();
                return;
            }

            if (usesSignatureFraming && !usesDragFraming)
            {
                if (!_signatureFramingLocked &&
                    !TryLockSignatureFraming())
                {
                    return;
                }

                ApplySignatureFramingLock();
                return;
            }

            // A drag may briefly overlap an interrupted signature feature. The
            // held character remains the only case that intentionally follows
            // changing animated bounds from frame to frame.
            ClearSignatureFramingLock();
            UpdateDynamicFraming();
        }

        private void UpdateDynamicFraming()
        {
            Bounds bounds;
            if (!TryCalculateBounds(out bounds))
            {
                return;
            }

            Vector3 targetLocalPosition;
            Vector3 targetLocalScale;
            CalculateFramingTarget(
                bounds,
                Mathf.Min(
                    _activeUniformScale,
                    UniformScale(_visualFrame.localScale)),
                true,
                out targetLocalPosition,
                out targetLocalScale);
            _activeUniformScale = UniformScale(targetLocalScale);

            float blend = ExponentialBlend(ActiveResponsiveness);
            _visualFrame.localPosition = Vector3.Lerp(
                _visualFrame.localPosition,
                targetLocalPosition,
                blend);
            _visualFrame.localScale = Vector3.Lerp(
                _visualFrame.localScale,
                targetLocalScale,
                blend);
        }

        private bool TryLockSignatureFraming()
        {
            // Signature props become visible before LateUpdate. Evaluate the
            // complete authored setup from the canonical compact transform, then
            // keep that one transform for every motion phase. Assigning it in
            // this same LateUpdate prevents a visible furniture-settling frame.
            Vector3 previousLocalPosition = _visualFrame.localPosition;
            Vector3 previousLocalScale = _visualFrame.localScale;
            _visualFrame.localPosition = _compactLocalPosition;
            _visualFrame.localScale = _compactLocalScale;

            Bounds bounds;
            if (!TryCalculateBounds(out bounds))
            {
                _visualFrame.localPosition = previousLocalPosition;
                _visualFrame.localScale = previousLocalScale;
                return false;
            }

            CalculateFramingTarget(
                bounds,
                UniformScale(_compactLocalScale),
                false,
                out _signatureTargetLocalPosition,
                out _signatureTargetLocalScale);
            ApplySignatureHorizontalBias(ref _signatureTargetLocalPosition);
            _activeUniformScale = UniformScale(_signatureTargetLocalScale);
            _signatureFramingLocked = true;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log(
                "Signature framing locked: localPosition=" +
                _signatureTargetLocalPosition + ", localScale=" +
                _signatureTargetLocalScale + ", compactUniform=" +
                CompactUniformScale.ToString("0.######") +
                ", activeUniform=" +
                CurrentUniformScale.ToString("0.######") + ".");
#endif
            return true;
        }

        private void ApplySignatureFramingLock()
        {
            _visualFrame.localPosition = _signatureTargetLocalPosition;
            _visualFrame.localScale = _signatureTargetLocalScale;
        }

        private void ClearSignatureFramingLock()
        {
            _signatureFramingLocked = false;
        }

        private void ApplySignatureHorizontalBias(ref Vector3 localPosition)
        {
            float viewportBias = _vignettePresenter == null
                ? 0.0f
                : _vignettePresenter
                    .CurrentFramingHorizontalBiasInViewportWidths;
            if (Mathf.Approximately(viewportBias, 0.0f))
            {
                return;
            }

            float viewportWorldWidth =
                _camera.orthographicSize * 2.0f * _camera.aspect;
            Vector3 worldOffset =
                _camera.transform.right * viewportWorldWidth * viewportBias;
            Vector3 localOffset = _visualFrame.parent == null
                ? worldOffset
                : _visualFrame.parent.InverseTransformVector(worldOffset);
            localPosition += localOffset;
        }

        private void CalculateFramingTarget(
            Bounds bounds,
            float maximumUniformScale,
            bool allowShrink,
            out Vector3 targetLocalPosition,
            out Vector3 targetLocalScale)
        {
            float currentUniformScale = Mathf.Max(
                0.0001f,
                UniformScale(_visualFrame.localScale));
            float verticalLimit =
                _camera.orthographicSize * (1.0f - ViewportGutter * 2.0f);
            float horizontalLimit = verticalLimit * Mathf.Max(0.1f, _camera.aspect);
            float projectedHalfWidth = ProjectedExtent(
                bounds.extents,
                _camera.transform.right);
            float projectedHalfHeight = ProjectedExtent(
                bounds.extents,
                _camera.transform.up);
            float fit = allowShrink
                ? Mathf.Min(
                    1.0f,
                    horizontalLimit / Mathf.Max(0.0001f, projectedHalfWidth),
                    verticalLimit / Mathf.Max(0.0001f, projectedHalfHeight))
                : 1.0f;
            float targetUniformScale = Mathf.Min(
                Mathf.Max(0.0001f, maximumUniformScale),
                currentUniformScale * fit);

            float scaleRatio = targetUniformScale / currentUniformScale;
            Vector3 currentWorldPosition = _visualFrame.position;
            Vector3 scaledBoundsCenter = currentWorldPosition +
                (bounds.center - currentWorldPosition) * scaleRatio;
            Vector3 cameraDelta = scaledBoundsCenter - _camera.transform.position;
            Vector3 targetWorldPosition = currentWorldPosition -
                _camera.transform.right *
                    Vector3.Dot(cameraDelta, _camera.transform.right) -
                _camera.transform.up *
                    Vector3.Dot(cameraDelta, _camera.transform.up);
            targetLocalPosition = _visualFrame.parent == null
                ? targetWorldPosition
                : _visualFrame.parent.InverseTransformPoint(targetWorldPosition);
            targetLocalScale = WithUniformScale(
                _compactLocalScale,
                targetUniformScale);
        }

        private void RestoreCompactTransform()
        {
            float blend = ExponentialBlend(RestoreResponsiveness);
            _visualFrame.localPosition = Vector3.Lerp(
                _visualFrame.localPosition,
                _compactLocalPosition,
                blend);
            _visualFrame.localScale = Vector3.Lerp(
                _visualFrame.localScale,
                _compactLocalScale,
                blend);
            if (Vector3.SqrMagnitude(
                _visualFrame.localPosition - _compactLocalPosition) < 0.000001f &&
                Vector3.SqrMagnitude(
                _visualFrame.localScale - _compactLocalScale) < 0.000001f)
            {
                _visualFrame.localPosition = _compactLocalPosition;
                _visualFrame.localScale = _compactLocalScale;
                _activeUniformScale = UniformScale(_compactLocalScale);
            }
        }

        private bool TryCalculateBounds(out Bounds bounds)
        {
            bounds = default(Bounds);
            bool found = false;
            RefreshRenderers();
            foreach (Renderer renderer in _renderers)
            {
                if (renderer == null || !renderer.enabled ||
                    !renderer.gameObject.activeInHierarchy)
                {
                    continue;
                }
                if (!(renderer is MeshRenderer) &&
                    !(renderer is SkinnedMeshRenderer))
                {
                    // Particle/trail renderer bounds are often conservative
                    // simulation envelopes rather than the visible formula
                    // marks. They follow the fitted visual frame, but must not
                    // shrink the authored physical composition.
                    continue;
                }
                if (!found)
                {
                    bounds = renderer.bounds;
                    found = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }
            return found;
        }

        private void RefreshRenderers()
        {
            _renderers.Clear();
            if (_renderRoot != null)
            {
                _renderRoot.GetComponentsInChildren<Renderer>(true, _renderers);
            }
        }

        private float ExponentialBlend(float responsiveness)
        {
            return 1.0f - Mathf.Exp(
                -responsiveness * Mathf.Max(0.0f, Time.unscaledDeltaTime));
        }

        private static float ProjectedExtent(Vector3 extents, Vector3 axis)
        {
            return Mathf.Abs(axis.x) * extents.x +
                Mathf.Abs(axis.y) * extents.y +
                Mathf.Abs(axis.z) * extents.z;
        }

        private static float UniformScale(Vector3 scale)
        {
            return Mathf.Min(
                Mathf.Abs(scale.x),
                Mathf.Abs(scale.y),
                Mathf.Abs(scale.z));
        }

        private static Vector3 WithUniformScale(
            Vector3 originalScale,
            float uniformScale)
        {
            float originalUniform = Mathf.Max(
                0.0001f,
                UniformScale(originalScale));
            return originalScale * (uniformScale / originalUniform);
        }

        private void OnDisable()
        {
            if (_initialized && _visualFrame != null)
            {
                ClearSignatureFramingLock();
                _visualFrame.localPosition = _compactLocalPosition;
                _visualFrame.localScale = _compactLocalScale;
                _activeUniformScale = UniformScale(_compactLocalScale);
            }
        }
    }
}
