using System;
using UmaDesktopPet.Standalone.Runtime;
using UnityEngine;

namespace UmaDesktopPet.Standalone.Editor
{
    /// <summary>
    /// Dependency-free checks for the fixed logical canvas and the public native
    /// resize constraint. These run without opening or resizing a real window.
    /// </summary>
    public static class DesktopWindowResizeSmokeTests
    {
        private const float Tolerance = 0.001f;

        public static void Run()
        {
            RunLayoutCase(new Vector2(720.0f, 480.0f), 1.0f);
            RunLayoutCase(new Vector2(900.0f, 600.0f), 1.25f);
            RunLayoutCase(new Vector2(1080.0f, 720.0f), 1.5f);
            RunLetterboxFallbackCase();
            RunClientSizeConstraintCases();
            RunResizeAvailabilityCases();
            RunPetOnlyWindowRegionCases();
            Debug.Log("Desktop window resize smoke tests passed.");
        }

        private static void RunLayoutCase(
            Vector2 physicalSize,
            float expectedScale)
        {
            string name = physicalSize.x + "x" + physicalSize.y;
            AssertNear(
                expectedScale,
                DesktopWindowLayout.CalculateScale(physicalSize),
                name + " scale");
            AssertVectorNear(
                Vector2.zero,
                DesktopWindowLayout.CalculateOffset(physicalSize),
                name + " offset");
            AssertRectNear(
                new Rect(0.0f, 0.0f, physicalSize.x, physicalSize.y),
                DesktopWindowLayout.PhysicalCanvasRect(physicalSize),
                name + " canvas");

            float panelWidth =
                DesktopWindowController.SidePanelWidth * expectedScale;
            float petWidth =
                DesktopWindowController.PetViewportWidth * expectedScale;
            AssertRectNear(
                new Rect(0.0f, 0.0f, panelWidth, physicalSize.y),
                DesktopWindowLayout.PhysicalSidePanelRect(physicalSize),
                name + " side panel");
            Rect cameraRect =
                DesktopWindowLayout.PhysicalPetViewportRect(physicalSize);
            AssertRectNear(
                new Rect(panelWidth, 0.0f, petWidth, physicalSize.y),
                cameraRect,
                name + " pet camera");
            AssertNear(
                DesktopWindowLayout.PetAspect,
                cameraRect.width / cameraRect.height,
                name + " pet camera aspect");
            AssertVectorNear(
                physicalSize,
                DesktopWindowLayout.PhysicalSizeForScale(expectedScale),
                name + " physical size for scale");

            AssertRoundTrip(Vector2.zero, physicalSize, name + " top-left");
            AssertRoundTrip(
                new Vector2(
                    DesktopWindowController.SidePanelWidth,
                    DesktopWindowController.NativeWindowHeight * 0.5f),
                physicalSize,
                name + " panel seam");
            AssertRoundTrip(
                new Vector2(
                    DesktopWindowLayout.LogicalWidth,
                    DesktopWindowLayout.LogicalHeight),
                physicalSize,
                name + " bottom-right");
        }

        private static void RunLetterboxFallbackCase()
        {
            var physicalSize = new Vector2(1000.0f, 600.0f);
            AssertNear(
                1.25f,
                DesktopWindowLayout.CalculateScale(physicalSize),
                "letterbox scale");
            AssertVectorNear(
                new Vector2(50.0f, 0.0f),
                DesktopWindowLayout.CalculateOffset(physicalSize),
                "letterbox offset");
            AssertRectNear(
                new Rect(50.0f, 0.0f, 900.0f, 600.0f),
                DesktopWindowLayout.PhysicalCanvasRect(physicalSize),
                "letterbox canvas");
            AssertRectNear(
                new Rect(50.0f, 0.0f, 450.0f, 600.0f),
                DesktopWindowLayout.PhysicalSidePanelRect(physicalSize),
                "letterbox side panel");
            AssertRectNear(
                new Rect(500.0f, 0.0f, 450.0f, 600.0f),
                DesktopWindowLayout.PhysicalPetViewportRect(physicalSize),
                "letterbox pet camera");
            AssertRoundTrip(
                new Vector2(547.0f, 163.0f),
                physicalSize,
                "letterbox point");
        }

        private static void RunClientSizeConstraintCases()
        {
            AssertConstrainedSize(720, 480, 720, 480, "default");
            AssertConstrainedSize(900, 600, 900, 600, "1.25 scale");
            AssertConstrainedSize(1080, 720, 1080, 720, "1.5 scale");
            AssertConstrainedSize(
                DesktopWindowController.MinimumWindowWidth / 2,
                DesktopWindowController.MinimumWindowHeight / 2,
                DesktopWindowController.MinimumWindowWidth,
                DesktopWindowController.MinimumWindowHeight,
                "minimum clamp");
            AssertConstrainedSize(
                DesktopWindowController.MaximumWindowWidth * 2,
                DesktopWindowController.MaximumWindowHeight * 2,
                DesktopWindowController.MaximumWindowWidth,
                DesktopWindowController.MaximumWindowHeight,
                "maximum clamp");
        }

        private static void RunResizeAvailabilityCases()
        {
            AssertResizeAvailability(
                false, false, true, true, false, false, "closed menu");
            AssertResizeAvailability(
                true, true, true, true, false, false, "setup override");
            AssertResizeAvailability(
                true, false, false, true, false, false, "window not ready");
            AssertResizeAvailability(
                true, false, true, false, false, false, "native bridge missing");
            AssertResizeAvailability(
                true, false, true, true, true, false, "menu reveal pending");
            AssertResizeAvailability(
                true, false, true, true, false, true, "open menu");
        }

        private static void AssertResizeAvailability(
            bool sidePanelVisible,
            bool fullSurfaceOverrideVisible,
            bool windowReady,
            bool nativeResizeAvailable,
            bool fullRegionRevealPending,
            bool expected,
            string name)
        {
            bool actual = DesktopWindowController.ShouldOfferInteractiveResize(
                sidePanelVisible,
                fullSurfaceOverrideVisible,
                windowReady,
                nativeResizeAvailable,
                fullRegionRevealPending);
            if (!actual.Equals(expected))
            {
                throw new InvalidOperationException(
                    name + " resize availability expected " + expected +
                    " but was " + actual + ".");
            }
        }

        private static void RunPetOnlyWindowRegionCases()
        {
            AssertPetOnlyWindowRegion(540, 360, 270, 0, 270, 360, "75%");
            AssertPetOnlyWindowRegion(720, 480, 360, 0, 360, 480, "100%");
            AssertPetOnlyWindowRegion(900, 600, 450, 0, 450, 600, "125%");
            AssertPetOnlyWindowRegion(1080, 720, 540, 0, 540, 720, "150%");
            AssertPetOnlyWindowRegion(1440, 960, 720, 0, 720, 960, "200%");

            if (!DesktopWindowController.ShouldClipWindowToPet(false, false) ||
                DesktopWindowController.ShouldClipWindowToPet(true, false) ||
                DesktopWindowController.ShouldClipWindowToPet(false, true))
            {
                throw new InvalidOperationException(
                    "Pet-only region state did not respect menu/setup visibility.");
            }
        }

        private static void AssertPetOnlyWindowRegion(
            int width,
            int height,
            int expectedX,
            int expectedY,
            int expectedWidth,
            int expectedHeight,
            string name)
        {
            RectInt actual = DesktopWindowController.CalculatePetOnlyWindowRegion(
                width,
                height);
            var expected = new RectInt(
                expectedX,
                expectedY,
                expectedWidth,
                expectedHeight);
            if (!actual.Equals(expected))
            {
                throw new InvalidOperationException(
                    name + " pet-only region expected " + expected +
                    " but was " + actual + ".");
            }
        }

        private static void AssertRoundTrip(
            Vector2 logicalPoint,
            Vector2 physicalSize,
            string name)
        {
            Vector2 physicalPoint = DesktopWindowLayout.LogicalGuiToPhysical(
                logicalPoint,
                physicalSize);
            Vector2 restoredPoint = DesktopWindowLayout.PhysicalGuiToLogical(
                physicalPoint,
                physicalSize);
            AssertVectorNear(logicalPoint, restoredPoint, name + " round trip");
        }

        private static void AssertConstrainedSize(
            int requestedWidth,
            int requestedHeight,
            int expectedWidth,
            int expectedHeight,
            string name)
        {
            int actualWidth;
            int actualHeight;
            DesktopWindowController.ConstrainClientSize(
                requestedWidth,
                requestedHeight,
                out actualWidth,
                out actualHeight);
            if (actualWidth != expectedWidth || actualHeight != expectedHeight)
            {
                throw new InvalidOperationException(
                    name + " expected " + expectedWidth + "x" + expectedHeight +
                    " but was " + actualWidth + "x" + actualHeight + ".");
            }
            if (actualWidth * DesktopWindowController.WindowAspectHeight !=
                actualHeight * DesktopWindowController.WindowAspectWidth)
            {
                throw new InvalidOperationException(
                    name + " did not preserve the exact 3:2 aspect ratio.");
            }
        }

        private static void AssertNear(
            float expected,
            float actual,
            string name)
        {
            if (Mathf.Abs(expected - actual) > Tolerance)
            {
                throw new InvalidOperationException(
                    name + " expected " + expected + " but was " + actual + ".");
            }
        }

        private static void AssertVectorNear(
            Vector2 expected,
            Vector2 actual,
            string name)
        {
            if (Vector2.SqrMagnitude(expected - actual) > Tolerance * Tolerance)
            {
                throw new InvalidOperationException(
                    name + " expected " + expected.ToString("F3") +
                    " but was " + actual.ToString("F3") + ".");
            }
        }

        private static void AssertRectNear(
            Rect expected,
            Rect actual,
            string name)
        {
            AssertNear(expected.x, actual.x, name + " x");
            AssertNear(expected.y, actual.y, name + " y");
            AssertNear(expected.width, actual.width, name + " width");
            AssertNear(expected.height, actual.height, name + " height");
        }
    }
}
