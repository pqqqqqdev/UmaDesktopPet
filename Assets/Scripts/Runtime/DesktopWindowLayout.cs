using UnityEngine;

namespace UmaDesktopPet.Standalone.Runtime
{
    /// <summary>
    /// Maps the fixed 720x480 desktop-pet design canvas onto the current Unity
    /// client surface without changing its aspect ratio. Native window sizing may
    /// use the public scale and physical-rectangle helpers; IMGUI code should draw
    /// in logical coordinates between BeginGui and EndGui.
    /// </summary>
    public static class DesktopWindowLayout
    {
        public const float LogicalWidth = DesktopWindowController.NativeWindowWidth;
        public const float LogicalHeight = DesktopWindowController.NativeWindowHeight;
        public const float PetAspect =
            (float)DesktopWindowController.PetViewportWidth /
            DesktopWindowController.NativeWindowHeight;

        public static Vector2 CurrentPhysicalSize
        {
            get
            {
                return new Vector2(
                    Mathf.Max(1, Screen.width),
                    Mathf.Max(1, Screen.height));
            }
        }

        public static float CurrentScale
        {
            get { return CalculateScale(CurrentPhysicalSize); }
        }

        /// <summary>
        /// Top-left GUI-space inset of the centered logical canvas. Because the
        /// fallback is centered, the same numeric inset is also valid in Unity's
        /// bottom-left camera pixel coordinates.
        /// </summary>
        public static Vector2 CurrentOffset
        {
            get { return CalculateOffset(CurrentPhysicalSize); }
        }

        public static Rect CurrentPhysicalCanvasRect
        {
            get { return PhysicalCanvasRect(CurrentPhysicalSize); }
        }

        public static Rect CurrentPhysicalSidePanelRect
        {
            get { return PhysicalSidePanelRect(CurrentPhysicalSize); }
        }

        public static Rect CurrentPhysicalPetViewportRect
        {
            get { return PhysicalPetViewportRect(CurrentPhysicalSize); }
        }

        public static float CalculateScale(Vector2 physicalSize)
        {
            float width = Mathf.Max(1.0f, physicalSize.x);
            float height = Mathf.Max(1.0f, physicalSize.y);
            float widthScale = width / LogicalWidth;
            float heightScale = height / LogicalHeight;
            return Mathf.Max(0.0001f, Mathf.Min(widthScale, heightScale));
        }

        public static Vector2 CalculateOffset(Vector2 physicalSize)
        {
            float width = Mathf.Max(1.0f, physicalSize.x);
            float height = Mathf.Max(1.0f, physicalSize.y);
            float scale = CalculateScale(physicalSize);
            return new Vector2(
                (width - LogicalWidth * scale) * 0.5f,
                (height - LogicalHeight * scale) * 0.5f);
        }

        public static Rect PhysicalCanvasRect(Vector2 physicalSize)
        {
            float scale = CalculateScale(physicalSize);
            Vector2 offset = CalculateOffset(physicalSize);
            return new Rect(
                offset.x,
                offset.y,
                LogicalWidth * scale,
                LogicalHeight * scale);
        }

        public static Rect PhysicalSidePanelRect(Vector2 physicalSize)
        {
            float scale = CalculateScale(physicalSize);
            Vector2 offset = CalculateOffset(physicalSize);
            return new Rect(
                offset.x,
                offset.y,
                DesktopWindowController.SidePanelWidth * scale,
                LogicalHeight * scale);
        }

        public static Rect PhysicalPetViewportRect(Vector2 physicalSize)
        {
            float scale = CalculateScale(physicalSize);
            Vector2 offset = CalculateOffset(physicalSize);
            return new Rect(
                offset.x + DesktopWindowController.SidePanelWidth * scale,
                offset.y,
                DesktopWindowController.PetViewportWidth * scale,
                LogicalHeight * scale);
        }

        public static Vector2 PhysicalSizeForScale(float scale)
        {
            float safeScale = Mathf.Max(0.0001f, scale);
            return new Vector2(
                LogicalWidth * safeScale,
                LogicalHeight * safeScale);
        }

        public static float LogicalLengthToPhysical(float logicalLength)
        {
            return logicalLength * CurrentScale;
        }

        public static Vector2 PhysicalGuiToLogical(Vector2 physicalGuiPosition)
        {
            return PhysicalGuiToLogical(
                physicalGuiPosition,
                CurrentPhysicalSize);
        }

        public static Vector2 PhysicalGuiToLogical(
            Vector2 physicalGuiPosition,
            Vector2 physicalSize)
        {
            Vector2 offset = CalculateOffset(physicalSize);
            float scale = CalculateScale(physicalSize);
            return (physicalGuiPosition - offset) / scale;
        }

        public static Vector2 LogicalGuiToPhysical(Vector2 logicalGuiPosition)
        {
            return LogicalGuiToPhysical(
                logicalGuiPosition,
                CurrentPhysicalSize);
        }

        public static Vector2 LogicalGuiToPhysical(
            Vector2 logicalGuiPosition,
            Vector2 physicalSize)
        {
            return CalculateOffset(physicalSize) +
                logicalGuiPosition * CalculateScale(physicalSize);
        }

        /// <summary>
        /// Converts Unity Input.mousePosition (bottom-left origin) to the logical
        /// IMGUI canvas (top-left origin).
        /// </summary>
        public static Vector2 InputMouseToLogicalGui(Vector2 inputMousePosition)
        {
            Vector2 physicalSize = CurrentPhysicalSize;
            return PhysicalGuiToLogical(
                new Vector2(
                    inputMousePosition.x,
                    physicalSize.y - inputMousePosition.y),
                physicalSize);
        }

        /// <summary>
        /// Returns an IMGUI event pointer in the current GUI context. Unity has
        /// already inverse-transformed this position through GUI.matrix and the
        /// active group/scroll-view stack.
        /// </summary>
        public static Vector2 EventMouseToCurrentGui(Event current)
        {
            return current != null ? current.mousePosition : Vector2.zero;
        }

        public static Matrix4x4 BeginGui()
        {
            Matrix4x4 previous = GUI.matrix;
            Vector2 physicalSize = CurrentPhysicalSize;
            Vector2 offset = CalculateOffset(physicalSize);
            float scale = CalculateScale(physicalSize);
            Matrix4x4 logicalToPhysical = Matrix4x4.TRS(
                new Vector3(offset.x, offset.y, 0.0f),
                Quaternion.identity,
                new Vector3(scale, scale, 1.0f));
            GUI.matrix = logicalToPhysical * previous;
            return previous;
        }

        public static void EndGui(Matrix4x4 previous)
        {
            GUI.matrix = previous;
        }
    }
}
