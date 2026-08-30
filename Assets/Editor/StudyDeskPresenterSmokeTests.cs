using System;
using System.Reflection;
using UmaDesktopPet.Standalone.Runtime;
using UnityEngine;

namespace UmaDesktopPet.Standalone.Editor
{
    /// <summary>
    /// Dependency-free ownership checks for the study/ambient shared furniture.
    /// Installed bundle loading is covered by the standalone integration smoke;
    /// this test isolates the handoff rules that prevent cancellation flicker.
    /// </summary>
    public static class StudyDeskPresenterSmokeTests
    {
        public static void Run()
        {
            GameObject host = null;
            try
            {
                host = new GameObject("Study desk ownership smoke");
                StudyDeskPresenter presenter =
                    host.AddComponent<StudyDeskPresenter>();
                var visual = new GameObject("Study Desk Visual");
                visual.transform.SetParent(host.transform, false);
                visual.SetActive(false);

                SetField(presenter, "_visualRoot", visual.transform);
                SetField(presenter, "_initialized", true);
                SetField(presenter, "_characterHeight", 2.0f);
                SetInstalledState(presenter, true);

                Assert(presenter.TryAcquireAmbientVisibility(),
                    "installed ambient acquisition should succeed");
                Assert(presenter.IsAmbientVisibilityAcquired,
                    "ambient ownership should be recorded");
                Assert(presenter.IsVisible && visual.activeSelf,
                    "ambient ownership should show the setup");
                AssertApproximately(
                    0.80f,
                    visual.transform.localScale.x,
                    "ambient setup scale");

                presenter.Show(false);
                AssertApproximately(
                    1.0f,
                    visual.transform.localScale.x,
                    "Study should restore its calibrated scale");
                presenter.ReleaseAmbientVisibility();
                Assert(!presenter.IsAmbientVisibilityAcquired,
                    "ambient ownership should release");
                Assert(presenter.IsVisible && visual.activeSelf,
                    "ambient cleanup must not hide active Study");
                presenter.Hide();
                Assert(!presenter.IsVisible && !visual.activeSelf,
                    "ordinary Study Hide should hide without another owner");

                presenter.Show(false);
                Assert(presenter.TryAcquireAmbientVisibility(),
                    "ambient should share an active Study setup");
                presenter.Hide();
                Assert(presenter.IsVisible && visual.activeSelf,
                    "Study Hide must not hide an ambient-owned setup");
                AssertApproximately(
                    0.80f,
                    visual.transform.localScale.x,
                    "ambient pose should resume after Study releases");
                presenter.ReleaseAmbientVisibility();
                Assert(!presenter.IsVisible && !visual.activeSelf,
                    "last owner release should hide the setup");

                SetInstalledState(presenter, false);
                Assert(!presenter.TryAcquireAmbientVisibility(),
                    "procedural fallback must not be exposed to ambient scenes");
                Assert(!presenter.IsVisible && !visual.activeSelf,
                    "failed ambient acquisition must remain hidden");

                // Avoid invoking runtime deferred-destruction cleanup from this
                // dependency-free Edit Mode fixture.
                SetField(presenter, "_visualRoot", null);
                SetField(presenter, "_initialized", false);
                Debug.Log("Study desk ownership smoke tests passed.");
            }
            finally
            {
                if (host != null)
                {
                    UnityEngine.Object.DestroyImmediate(host);
                }
            }
        }

        private static void SetInstalledState(
            StudyDeskPresenter presenter,
            bool value)
        {
            SetField(
                presenter,
                "<UsesInstalledStudyProps>k__BackingField",
                value);
        }

        private static void SetField(
            StudyDeskPresenter presenter,
            string name,
            object value)
        {
            FieldInfo field = typeof(StudyDeskPresenter).GetField(
                name,
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
            {
                throw new MissingFieldException(
                    typeof(StudyDeskPresenter).FullName,
                    name);
            }
            field.SetValue(presenter, value);
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(
                    "Study desk ownership smoke failed: " + message + ".");
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
                    "Study desk ownership smoke failed: " + message +
                    " expected " + expected + " but was " + actual + ".");
            }
        }
    }
}
