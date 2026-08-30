using System;
using System.Reflection;
using UmaDesktopPet.Standalone.Runtime;
using UnityEngine;

namespace UmaDesktopPet.Standalone.Editor
{
    /// <summary>
    /// Dependency-free policy checks for active pet framing. Signature scenes
    /// may recenter once, but only held drag poses may reduce the pet scale.
    /// </summary>
    public static class PetCameraFramingSmokeTests
    {
        public static void Run()
        {
            GameObject host = null;
            try
            {
                host = new GameObject("Pet camera framing smoke");

                var cameraObject = new GameObject("Camera");
                cameraObject.transform.SetParent(host.transform, false);
                cameraObject.transform.localPosition =
                    new Vector3(0.0f, 0.0f, -10.0f);
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.orthographic = true;
                camera.orthographicSize = 1.0f;
                camera.aspect = 1.0f;

                var frameObject = new GameObject("Visual frame");
                frameObject.transform.SetParent(host.transform, false);
                frameObject.transform.localPosition =
                    new Vector3(0.13f, -0.21f, 0.0f);
                frameObject.transform.localScale = Vector3.one;

                var renderRoot = new GameObject("Render root");
                renderRoot.transform.SetParent(frameObject.transform, false);
                GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.name = "Oversized character mesh";
                cube.transform.SetParent(renderRoot.transform, false);
                cube.transform.localScale = new Vector3(10.0f, 10.0f, 1.0f);
                MeshRenderer cubeRenderer = cube.GetComponent<MeshRenderer>();

                var particleObject = new GameObject("Ignored formula envelope");
                particleObject.transform.SetParent(renderRoot.transform, false);
                particleObject.transform.localPosition =
                    new Vector3(100.0f, 0.0f, 0.0f);
                ParticleSystem particles =
                    particleObject.AddComponent<ParticleSystem>();
                ParticleSystemRenderer particleRenderer =
                    particles.GetComponent<ParticleSystemRenderer>();
                particleRenderer.enabled = true;

                OguriPetAnimationController motions =
                    host.AddComponent<OguriPetAnimationController>();
                PetAmbientVignettePresenter presenter =
                    host.AddComponent<PetAmbientVignettePresenter>();
                PetCameraFramingController framing =
                    host.AddComponent<PetCameraFramingController>();
                framing.Initialize(
                    camera,
                    frameObject.transform,
                    renderRoot,
                    motions,
                    presenter);

                Bounds filteredBounds = InvokeBoundsMethod(
                    framing,
                    "TryCalculateBounds");
                AssertBoundsApproximately(
                    cubeRenderer.bounds,
                    filteredBounds,
                    "particle envelopes must not affect framing bounds");

                SetField(presenter, "_isPlaying", true);
                InvokeMethod(framing, "LateUpdate");
                AssertVectorApproximately(
                    Vector3.one,
                    frameObject.transform.localScale,
                    "signature framing must preserve compact scale");
                AssertVectorApproximately(
                    Vector3.one,
                    GetField<Vector3>(framing, "_signatureTargetLocalScale"),
                    "signature target scale must remain compact");
                AssertApproximately(
                    1.0f,
                    GetField<float>(framing, "_activeUniformScale"),
                    "signature active scale must remain compact");
                Assert(
                    GetField<bool>(framing, "_signatureFramingLocked"),
                    "signature framing should lock after setup");

                Vector3 lockedPosition = frameObject.transform.localPosition;
                cube.transform.localScale =
                    new Vector3(20.0f, 20.0f, 1.0f);
                InvokeMethod(framing, "LateUpdate");
                AssertVectorApproximately(
                    lockedPosition,
                    frameObject.transform.localPosition,
                    "animated signature bounds must not move the locked frame");
                AssertVectorApproximately(
                    Vector3.one,
                    frameObject.transform.localScale,
                    "animated signature bounds must not shrink the pet");

                SetField(motions, "_initialized", true);
                SetEnumField(motions, "_phase", "DragHold");
                InvokeMethod(framing, "LateUpdate");
                Assert(
                    !GetField<bool>(framing, "_signatureFramingLocked"),
                    "held drag should take precedence over a stale signature lock");
                Assert(
                    GetField<float>(framing, "_activeUniformScale") < 0.999f,
                    "an oversized held drag pose should retain adaptive shrink");

                SetField(motions, "_initialized", false);
                SetField(presenter, "_isPlaying", false);
                Debug.Log("Pet camera framing smoke tests passed.");
            }
            finally
            {
                if (host != null)
                {
                    UnityEngine.Object.DestroyImmediate(host);
                }
            }
        }

        private static Bounds InvokeBoundsMethod(
            PetCameraFramingController framing,
            string methodName)
        {
            object[] arguments = { null };
            object result = InvokeMethod(framing, methodName, arguments);
            Assert(
                result is bool && (bool)result,
                methodName + " should find the mesh bounds");
            return (Bounds)arguments[0];
        }

        private static object InvokeMethod(
            object instance,
            string methodName,
            params object[] arguments)
        {
            MethodInfo method = instance.GetType().GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (method == null)
            {
                throw new MissingMethodException(
                    instance.GetType().FullName,
                    methodName);
            }
            return method.Invoke(instance, arguments);
        }

        private static T GetField<T>(object instance, string fieldName)
        {
            FieldInfo field = instance.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
            {
                throw new MissingFieldException(
                    instance.GetType().FullName,
                    fieldName);
            }
            return (T)field.GetValue(instance);
        }

        private static void SetField(
            object instance,
            string fieldName,
            object value)
        {
            FieldInfo field = instance.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
            {
                throw new MissingFieldException(
                    instance.GetType().FullName,
                    fieldName);
            }
            field.SetValue(instance, value);
        }

        private static void SetEnumField(
            object instance,
            string fieldName,
            string value)
        {
            FieldInfo field = instance.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
            {
                throw new MissingFieldException(
                    instance.GetType().FullName,
                    fieldName);
            }
            field.SetValue(instance, Enum.Parse(field.FieldType, value));
        }

        private static void AssertBoundsApproximately(
            Bounds expected,
            Bounds actual,
            string message)
        {
            AssertVectorApproximately(expected.center, actual.center, message);
            AssertVectorApproximately(expected.size, actual.size, message);
        }

        private static void AssertVectorApproximately(
            Vector3 expected,
            Vector3 actual,
            string message)
        {
            if ((expected - actual).sqrMagnitude > 0.000001f)
            {
                throw new InvalidOperationException(
                    "Pet camera framing smoke failed: " + message +
                    "; expected " + expected + " but was " + actual + ".");
            }
        }

        private static void AssertApproximately(
            float expected,
            float actual,
            string message)
        {
            if (Mathf.Abs(expected - actual) > 0.0001f)
            {
                throw new InvalidOperationException(
                    "Pet camera framing smoke failed: " + message +
                    "; expected " + expected + " but was " + actual + ".");
            }
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(
                    "Pet camera framing smoke failed: " + message + ".");
            }
        }
    }
}
