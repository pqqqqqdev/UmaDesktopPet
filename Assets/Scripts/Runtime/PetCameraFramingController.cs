using System;
using UnityEngine;

namespace UmaDesktopPet.Standalone.Runtime
{
    /// <summary>
    /// Recenters and, only when necessary, uniformly shrinks the animated visual
    /// while a drag reaction is active. The camera and native desktop window stay
    /// untouched, so settings-sidecar positioning remains stable.
    /// </summary>
    public sealed class PetCameraFramingController : MonoBehaviour
    {
        private const float ViewportGutter = 0.055f;
        private const float ActiveResponsiveness = 22.0f;
        private const float RestoreResponsiveness = 12.0f;

        private Camera _camera;
        private Transform _visualFrame;
        private Renderer[] _renderers;
        private OguriPetAnimationController _motions;
        private Vector3 _compactLocalPosition;
        private Vector3 _compactLocalScale;
        private float _activeUniformScale;
        private bool _initialized;

        public void Initialize(
            Camera camera,
            Transform visualFrame,
            GameObject renderRoot,
            OguriPetAnimationController motions)
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

            _camera = camera;
            _visualFrame = visualFrame;
            _renderers = renderRoot.GetComponentsInChildren<Renderer>(true);
            if (_renderers.Length == 0)
            {
                throw new InvalidOperationException(
                    "Drag framing requires at least one character renderer.");
            }
            foreach (SkinnedMeshRenderer renderer in
                renderRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                renderer.updateWhenOffscreen = true;
            }

            _motions = motions;
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

            bool dragging = _motions != null && _motions.UsesDragFraming;
            if (!dragging)
            {
                RestoreCompactTransform();
                return;
            }

            Bounds bounds;
            if (!TryCalculateBounds(out bounds))
            {
                return;
            }

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
            float fit = Mathf.Min(
                1.0f,
                horizontalLimit / Mathf.Max(0.0001f, projectedHalfWidth),
                verticalLimit / Mathf.Max(0.0001f, projectedHalfHeight));
            _activeUniformScale = Mathf.Min(
                _activeUniformScale,
                currentUniformScale * fit);

            float scaleRatio = _activeUniformScale / currentUniformScale;
            Vector3 currentWorldPosition = _visualFrame.position;
            Vector3 scaledBoundsCenter = currentWorldPosition +
                (bounds.center - currentWorldPosition) * scaleRatio;
            Vector3 cameraDelta = scaledBoundsCenter - _camera.transform.position;
            Vector3 targetWorldPosition = currentWorldPosition -
                _camera.transform.right *
                    Vector3.Dot(cameraDelta, _camera.transform.right) -
                _camera.transform.up *
                    Vector3.Dot(cameraDelta, _camera.transform.up);
            Vector3 targetLocalPosition = _visualFrame.parent == null
                ? targetWorldPosition
                : _visualFrame.parent.InverseTransformPoint(targetWorldPosition);
            Vector3 targetLocalScale = WithUniformScale(
                _compactLocalScale,
                _activeUniformScale);

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
            foreach (Renderer renderer in _renderers)
            {
                if (renderer == null || !renderer.enabled ||
                    !renderer.gameObject.activeInHierarchy)
                {
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
                _visualFrame.localPosition = _compactLocalPosition;
                _visualFrame.localScale = _compactLocalScale;
            }
        }
    }
}
