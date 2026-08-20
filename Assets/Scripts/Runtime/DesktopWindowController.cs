using System;
using System.Collections;
using UnityEngine;
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
using Kirurobo;
using System.Runtime.InteropServices;
#endif

namespace UmaDesktopPet.Standalone.Runtime
{
    /// <summary>
    /// Configures the standalone Windows player as a small transparent desktop-pet
    /// window. In the Editor and on non-Windows players this component is a no-op.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DesktopWindowController : MonoBehaviour
    {
        public const int PetViewportWidth = 360;
        public const int SidePanelWidth = 360;
        public const int NativeWindowWidth = PetViewportWidth + SidePanelWidth;
        public const int NativeWindowHeight = 480;
        public const int WindowAspectWidth = 3;
        public const int WindowAspectHeight = 2;
        public const int MinimumWindowWidth = 540;
        public const int MinimumWindowHeight = 360;
        public const int MaximumWindowWidth = 1440;
        public const int MaximumWindowHeight = 960;

        private const float SidePanelCloseTimeoutSeconds = 2.0f;
        private const float SidePanelOpenTimeoutSeconds = 2.0f;
        private const float SidePanelSizeTolerancePixels = 2.0f;
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        private const uint SystemParametersInfoGetWorkArea = 0x0030;
        private const int ResizeGutterDips = 8;
        private const uint DefaultDpi = 96;
        private const int WindowLongWindowProcedure = -4;
        private const uint WindowMessageGetMinMaxInfo = 0x0024;
        private const uint WindowMessageNcHitTest = 0x0084;
        private const uint WindowMessageNcLeftButtonDown = 0x00A1;
        private const uint WindowMessageSizing = 0x0214;
        private const uint WindowMessageEnterSizeMove = 0x0231;
        private const uint WindowMessageExitSizeMove = 0x0232;
        private const uint MonitorDefaultToNearest = 0x00000002;
        private const int VirtualKeyLeftButton = 0x01;

        private const int HitTestLeft = 10;
        private const int HitTestRight = 11;
        private const int HitTestTop = 12;
        private const int HitTestTopLeft = 13;
        private const int HitTestTopRight = 14;
        private const int HitTestBottom = 15;
        private const int HitTestBottomLeft = 16;
        private const int HitTestBottomRight = 17;

        private const int SizingLeft = 1;
        private const int SizingRight = 2;
        private const int SizingTop = 3;
        private const int SizingTopLeft = 4;
        private const int SizingTopRight = 5;
        private const int SizingBottom = 6;
        private const int SizingBottomLeft = 7;
        private const int SizingBottomRight = 8;
#endif

        [Header("Window")]
        [SerializeField, Min(240)] private int windowWidth = NativeWindowWidth;
        [SerializeField, Min(320)] private int windowHeight = NativeWindowHeight;
        [SerializeField] private bool alwaysOnTop = true;
        [SerializeField] private bool placeAtPrimaryBottomRight = true;
        [SerializeField, Min(0)] private int screenEdgePadding = 24;

        [Header("Interaction")]
        [SerializeField] private bool clickThroughTransparentPixels = true;
        [SerializeField, Range(0.01f, 1.0f)] private float opacityThreshold = 0.08f;
        [SerializeField] private Camera renderCamera;

        private bool _isReady;
        private bool _isDragging;
        private bool _isQuitting;
        private bool _fileDropEnabled;
        private Vector3 _compactCameraPosition;
        private bool _hasCompactCameraPosition;

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        private UniWindowController _nativeWindow;
        private bool _ownsNativeWindow;
        private bool _hitTestWasEnabled;
        private bool _clickThroughWasEnabled;
        private bool _sidePanelVisible;
        private bool _fullSurfaceOverrideVisible = true;
        private bool _windowRegionClippedToPet;
        private int _windowRegionWidth;
        private int _windowRegionHeight;
        private bool _loggedWindowRegionFailure;
        private bool _fullRegionRevealPending;
        private Coroutine _fullRegionRevealCoroutine;
        private Vector2 _compactWindowPosition;
        private Vector2 _compactWindowSize;
        private Vector2 _dragWindowOffset;
        private float _nextTopmostCheck;
        private IntPtr _playerWindowHandle;
        private NativeRect _compactNativeRect;
        private bool _sidePanelUsesAtomicGeometry;
        private bool _loggedAtomicGeometryFallback;
        private Coroutine _sidePanelOpenCoroutine;
        private bool _sidePanelOpenPending;
        private bool _sidePanelCloseRequestedDuringOpen;
        private Coroutine _sidePanelCloseCoroutine;
        private bool _sidePanelClosePending;
        private bool _sidePanelCloseGeometryApplied;
        private bool _sidePanelCameraManaged;
        private IntPtr _previousWindowProcedure;
        private IntPtr _resizeWindowProcedurePointer;
        private bool _resizeBridgeInstalled;
        private bool _nativeSizing;
        private bool _cursorInResizeGutter;
        private bool _resizeHitTestOverrideActive;
        private bool _resizeHitTestWasEnabled;
        private bool _resizeClickThroughWasEnabled;
        private bool _resizeWindowProcedureFaulted;
        private bool _resizeWindowProcedureFaultLogged;
        private NativeRect _nativeSizingStartRect;
        private bool _hasNativeSizingStartRect;
        private bool _managedSizing;
        private bool _leftMouseWasDown;
        private bool _managedResizeInputSuppressedUntilRelease;
        private bool _nativeSizingObservedSincePoll;
        private bool _managedResizeApplied;
        private bool _managedResizeCompletionLogged;
        private int _managedSizingEdge;
        private NativePoint _managedSizingStartCursor;
        private NativeRect _managedSizingStartRect;
        private bool _managedResizeBeginPending;
        private int _pendingManagedSizingEdge;
        private IntPtr _pendingManagedSizingWindow;
        private NativePoint _pendingManagedSizingCursor;
        private NativeRect _pendingManagedSizingRect;
        private bool _pendingManagedSizingSnapshotValid;

        // A native WndProc delegate must stay rooted for as long as Windows can call
        // its function pointer. Static ownership also protects a late teardown call
        // if another native hook was installed above ours.
        private static WindowProcedure s_resizeWindowProcedure;
        private static DesktopWindowController s_resizeWindowOwner;

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;

            public int Width
            {
                get { return Right - Left; }
            }

            public int Height
            {
                get { return Bottom - Top; }
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeMinMaxInfo
        {
            public NativePoint Reserved;
            public NativePoint MaxSize;
            public NativePoint MaxPosition;
            public NativePoint MinTrackSize;
            public NativePoint MaxTrackSize;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct NativeMonitorInfo
        {
            public int Size;
            public NativeRect Monitor;
            public NativeRect Work;
            public uint Flags;
        }

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate IntPtr WindowProcedure(
            IntPtr windowHandle,
            uint message,
            IntPtr wParam,
            IntPtr lParam);

        [Flags]
        private enum SetWindowPosFlags : uint
        {
            NoZOrder = 0x0004,
            NoActivate = 0x0010,
            NoOwnerZOrder = 0x0200
        }

        [DllImport("LibUniWinC", CallingConvention = CallingConvention.Winapi)]
        private static extern IntPtr GetWindowHandle();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindow(IntPtr windowHandle);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(
            IntPtr windowHandle,
            out uint processId);

        [DllImport("user32.dll")]
        private static extern IntPtr GetActiveWindow();

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
        private static extern IntPtr GetWindowLongPtr(
            IntPtr windowHandle,
            int index);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
        private static extern IntPtr SetWindowLongPtr(
            IntPtr windowHandle,
            int index,
            IntPtr newValue);

        [DllImport("user32.dll", EntryPoint = "CallWindowProcW")]
        private static extern IntPtr CallWindowProc(
            IntPtr previousWindowProcedure,
            IntPtr windowHandle,
            uint message,
            IntPtr wParam,
            IntPtr lParam);

        [DllImport("user32.dll", EntryPoint = "DefWindowProcW")]
        private static extern IntPtr DefWindowProc(
            IntPtr windowHandle,
            uint message,
            IntPtr wParam,
            IntPtr lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetCursorPos(out NativePoint point);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int virtualKey);

        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr windowHandle);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(
            IntPtr windowHandle,
            uint flags);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromRect(
            ref NativeRect rectangle,
            uint flags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetMonitorInfo(
            IntPtr monitorHandle,
            ref NativeMonitorInfo monitorInfo);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(
            IntPtr windowHandle,
            out NativeRect rectangle);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowPos(
            IntPtr windowHandle,
            IntPtr insertAfter,
            int x,
            int y,
            int width,
            int height,
            SetWindowPosFlags flags);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern IntPtr CreateRectRgn(
            int left,
            int top,
            int right,
            int bottom);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowRgn(
            IntPtr windowHandle,
            IntPtr region,
            [MarshalAs(UnmanagedType.Bool)] bool redraw);

        [DllImport("gdi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeleteObject(IntPtr value);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SystemParametersInfo(
            uint action,
            uint parameter,
            out NativeRect result,
            uint updateFlags);
#endif

        /// <summary>
        /// True only in a built Windows standalone player. The Editor intentionally
        /// reports false so play mode cannot alter the Unity Game view window.
        /// </summary>
        public bool IsSupported
        {
            get
            {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
                return true;
#else
                return false;
#endif
            }
        }

        public bool IsReady
        {
            get { return _isReady; }
        }

        public bool IsDragging
        {
            get { return _isDragging; }
        }

        /// <summary>
        /// True while Windows is running its native edge/corner sizing loop.
        /// </summary>
        public bool IsResizing
        {
            get
            {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
                return _nativeSizing || _managedSizing;
#else
                return false;
#endif
            }
        }

        /// <summary>
        /// True when a user should be able to resize the native window by dragging
        /// one of its invisible edges or corners. Keeping this tied to the visible
        /// side panel prevents the transparent reserved half from feeling like a
        /// large empty pet window while contextual UI is closed.
        /// </summary>
        public bool IsInteractiveResizeEnabled
        {
            get
            {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
                return ShouldOfferInteractiveResize(
                    _sidePanelVisible,
                    _fullSurfaceOverrideVisible,
                    _isReady,
                    _resizeBridgeInstalled,
                    _fullRegionRevealPending);
#else
                return false;
#endif
            }
        }

        public static bool ShouldOfferInteractiveResize(
            bool sidePanelVisible,
            bool fullSurfaceOverrideVisible,
            bool windowReady,
            bool nativeResizeAvailable,
            bool fullRegionRevealPending)
        {
            return sidePanelVisible && !fullSurfaceOverrideVisible &&
                windowReady && nativeResizeAvailable &&
                !fullRegionRevealPending;
        }

        public static bool ShouldClipWindowToPet(
            bool sidePanelVisible,
            bool fullSurfaceOverrideVisible)
        {
            return !sidePanelVisible && !fullSurfaceOverrideVisible;
        }

        public static RectInt CalculatePetOnlyWindowRegion(
            int windowRegionWidth,
            int windowRegionHeight)
        {
            int safeWidth = Math.Max(1, windowRegionWidth);
            int safeHeight = Math.Max(1, windowRegionHeight);
            int petWidth = (int)Math.Round(
                safeWidth * (PetViewportWidth / (double)NativeWindowWidth),
                MidpointRounding.AwayFromZero);
            petWidth = Math.Max(1, Math.Min(safeWidth, petWidth));
            return new RectInt(
                safeWidth - petWidth,
                0,
                petWidth,
                safeHeight);
        }

        public bool IsPetOnlyWindowRegionActive
        {
            get
            {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
                return _windowRegionClippedToPet;
#else
                return false;
#endif
            }
        }

        /// <summary>
        /// Pure, platform-independent sizing rule used by editor smoke tests and
        /// runtime callers. The returned integers always form an exact 3:2 pair and
        /// stay within the public minimum and maximum bounds.
        /// </summary>
        public static void ConstrainClientSize(
            int requestedWidth,
            int requestedHeight,
            out int constrainedWidth,
            out int constrainedHeight)
        {
            ConstrainClientSizeCore(
                requestedWidth,
                requestedHeight,
                0,
                MaximumWindowWidth,
                MaximumWindowHeight,
                out constrainedWidth,
                out constrainedHeight);
        }

        /// <summary>
        /// True when contextual UI should be drawn in the permanently allocated
        /// transparent side panel.
        /// </summary>
        public bool IsSidePanelRenderReady
        {
            get
            {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
                return _sidePanelVisible;
#else
                return true;
#endif
            }
        }

        /// <summary>
        /// Global cursor position in UniWindow's lower-left-origin desktop
        /// coordinates. Returns zero outside a built Windows player.
        /// </summary>
        public Vector2 CursorPosition
        {
            get
            {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
                return _nativeWindow != null
                    ? _nativeWindow.cursorPosition
                    : Vector2.zero;
#else
                return Vector2.zero;
#endif
            }
        }

        /// <summary>
        /// Global lower-left window position in UniWindow's desktop coordinates.
        /// Returns zero outside a built Windows player.
        /// </summary>
        public Vector2 WindowPosition
        {
            get
            {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
                return _nativeWindow != null
                    ? _nativeWindow.windowPosition
                    : Vector2.zero;
#else
                return Vector2.zero;
#endif
            }
        }

        /// <summary>
        /// Native outer-window size. With the borderless resize bridge installed,
        /// this is also the client size. Returns zero outside a Windows player.
        /// </summary>
        public Vector2 WindowSize
        {
            get
            {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
                return _nativeWindow != null
                    ? _nativeWindow.windowSize
                    : Vector2.zero;
#else
                return Vector2.zero;
#endif
            }
        }

        /// <summary>
        /// Current native client size, or zero outside a built Windows player.
        /// </summary>
        public Vector2 ClientSize
        {
            get
            {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
                return _nativeWindow != null
                    ? _nativeWindow.clientSize
                    : Vector2.zero;
#else
                return Vector2.zero;
#endif
            }
        }

        /// <summary>
        /// Raised after a drag starts or ends. Interaction code can use this to
        /// suppress pet click reactions while the native window is moving.
        /// </summary>
        public event Action<bool> DragStateChanged;

        /// <summary>
        /// Raised when Windows drops one or more files or folders onto the pet
        /// window. Setup code enables this only while choosing a game install.
        /// </summary>
        public event Action<string[]> FilesDropped;

        private IEnumerator Start()
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            Application.runInBackground = true;
            Screen.fullScreenMode = FullScreenMode.Windowed;
            ConstrainClientSizeCore(
                windowWidth,
                windowHeight,
                0,
                MaximumWindowWidth,
                MaximumWindowHeight,
                out windowWidth,
                out windowHeight);
            Screen.SetResolution(windowWidth, windowHeight, FullScreenMode.Windowed);

            _nativeWindow = FindAnyObjectByType<UniWindowController>();
            if (_nativeWindow == null)
            {
                var nativeHost = new GameObject("Native Desktop Window");
                nativeHost.transform.SetParent(transform, false);
                _nativeWindow = nativeHost.AddComponent<UniWindowController>();
                _ownsNativeWindow = true;
            }

            // UniWindowController attaches to the player HWND during its Update.
            // These values are retained and applied as soon as attachment succeeds.
            _nativeWindow.forceWindowed = true;
            _nativeWindow.shouldFitMonitor = false;
            _nativeWindow.transparentType = UniWindowController.TransparentType.Alpha;
            _nativeWindow.autoSwitchCameraBackground = true;
            _nativeWindow.hitTestType = clickThroughTransparentPixels
                ? UniWindowController.HitTestType.Opacity
                : UniWindowController.HitTestType.None;
            _nativeWindow.isHitTestEnabled = clickThroughTransparentPixels;
            _nativeWindow.opacityThreshold = opacityThreshold;
            _nativeWindow.OnDropFiles += HandleDroppedFiles;
            _nativeWindow.allowDropFiles = _fileDropEnabled;
            _nativeWindow.isClickThrough = false;
            _nativeWindow.isTransparent = true;

            TryBindRenderCamera();

            // A zero native size means UniWindowController has not acquired the HWND
            // yet. Allow up to five seconds instead of racing its first Update.
            float timeout = Time.realtimeSinceStartup + 5.0f;
            while (Time.realtimeSinceStartup < timeout &&
                (_nativeWindow == null || _nativeWindow.windowSize.sqrMagnitude < 1.0f))
            {
                TryBindRenderCamera();
                yield return null;
            }

            if (_nativeWindow == null || _nativeWindow.windowSize.sqrMagnitude < 1.0f)
            {
                Debug.LogWarning(
                    "The native desktop window was not acquired; continuing as a " +
                    "normal Unity window.");
                yield break;
            }

            // Reapply after attachment. Alpha transparency also enables the native
            // borderless style in UniWindowController 0.9.8.
            _nativeWindow.isTransparent = true;
            _nativeWindow.windowSize = new Vector2(windowWidth, windowHeight);
            yield return null;

            if (!InstallNativeResizeBridge())
            {
                Debug.LogWarning(
                    "Native edge resizing could not be enabled; the desktop pet " +
                    "will keep its current fixed window size.");
            }

            _nativeWindow.isTopmost = alwaysOnTop;
            if (placeAtPrimaryBottomRight)
            {
                PlaceAtPrimaryBottomRight();
            }

            _isReady = true;
            // No native region could have been applied before readiness. A retained
            // full-surface request is therefore already satisfied at this point.
            _fullRegionRevealPending = false;
            RefreshWindowRegionIfNeeded(true);
            _nextTopmostCheck = Time.unscaledTime + 1.0f;
            Debug.Log(
                "Desktop window ready: size=" + _nativeWindow.windowSize +
                ", position=" + _nativeWindow.windowPosition +
                ", transparent=" + _nativeWindow.isTransparent +
                ", topmost=" + _nativeWindow.isTopmost +
                ", opacityHitTest=" + _nativeWindow.isHitTestEnabled + ".");
#else
            yield break;
#endif
        }

        private void Update()
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            if (_nativeWindow == null)
            {
                return;
            }

            TryBindRenderCamera();
            if (!_isReady)
            {
                return;
            }

            UpdateManagedResizeFallback();
            UpdateResizeHitTestOverride();
            RefreshWindowRegionIfNeeded(false);
            if (_resizeWindowProcedureFaulted &&
                !_resizeWindowProcedureFaultLogged)
            {
                _resizeWindowProcedureFaultLogged = true;
                Debug.LogWarning(
                    "A native resize message could not be handled. The original " +
                    "Unity/UniWindow window procedure was allowed to continue.");
            }

            if (_isDragging)
            {
                ContinueDrag();
            }

            // The native plugin normally preserves this itself. A low-frequency
            // recheck also recovers if Explorer or another application changes z-order.
            if (alwaysOnTop && Time.unscaledTime >= _nextTopmostCheck)
            {
                _nativeWindow.isTopmost = true;
                _nextTopmostCheck = Time.unscaledTime + 1.0f;
            }
#endif
        }

        /// <summary>
        /// Assigns the camera whose clear color should become transparent. It is safe
        /// to call this after the character scene and camera have been created.
        /// </summary>
        public void SetRenderCamera(Camera camera)
        {
            renderCamera = camera;
            if (renderCamera != null)
            {
                _compactCameraPosition = renderCamera.transform.position;
                _hasCompactCameraPosition = true;
            }
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            if (_nativeWindow != null && renderCamera != null)
            {
                _nativeWindow.SetCamera(renderCamera);
            }
#endif
        }

        /// <summary>
        /// Enables native file and folder drops. The preference is retained if this
        /// is called before UniWindow has attached to the player HWND.
        /// </summary>
        public void SetFileDropEnabled(bool enabled)
        {
            _fileDropEnabled = enabled;
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            if (_nativeWindow != null)
            {
                _nativeWindow.allowDropFiles = enabled;
            }
#endif
        }

        /// <summary>
        /// Keeps the complete backing surface visible for setup and recovery UI.
        /// Requests made before the native window is ready are retained.
        /// </summary>
        public void SetFullSurfaceVisible(bool visible)
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            if (_fullSurfaceOverrideVisible == visible)
            {
                return;
            }

            _fullSurfaceOverrideVisible = visible;
            if (visible)
            {
                EndDrag();
                if (_managedSizing || _managedResizeBeginPending)
                {
                    EndManagedResizeFallback(true);
                }
                ReleaseResizeHitTestOverride();
                RequestFullWindowRegionAfterRender();
            }
            else
            {
                CancelFullWindowRegionReveal();
                RefreshWindowRegionIfNeeded(true);
            }
#endif
        }

        /// <summary>
        /// Applies an exact scale relative to the default 720x480 client area.
        /// The request is clamped to the 3:2 limits and the nearest monitor's work
        /// area. This is intentionally not persisted yet.
        /// </summary>
        public bool TrySetWindowScale(float scale)
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            if (!_isReady || !_resizeBridgeInstalled ||
                float.IsNaN(scale) || float.IsInfinity(scale) || scale <= 0.0f)
            {
                return false;
            }

            IntPtr windowHandle = AcquirePlayerWindowHandle();
            NativeRect currentRectangle;
            if (windowHandle == IntPtr.Zero ||
                !GetWindowRect(windowHandle, out currentRectangle))
            {
                return false;
            }

            NativeRect workArea;
            int maximumWidth;
            int maximumHeight;
            GetResizeLimitsForWindow(
                windowHandle,
                out workArea,
                out maximumWidth,
                out maximumHeight);

            int requestedWidth = (int)Math.Round(
                NativeWindowWidth * (double)scale,
                MidpointRounding.AwayFromZero);
            int requestedHeight = (int)Math.Round(
                NativeWindowHeight * (double)scale,
                MidpointRounding.AwayFromZero);
            int constrainedWidth;
            int constrainedHeight;
            ConstrainClientSizeCore(
                requestedWidth,
                requestedHeight,
                0,
                maximumWidth,
                maximumHeight,
                out constrainedWidth,
                out constrainedHeight);

            NativeRect targetRectangle = new NativeRect
            {
                Right = currentRectangle.Right,
                Bottom = currentRectangle.Bottom,
                Left = currentRectangle.Right - constrainedWidth,
                Top = currentRectangle.Bottom - constrainedHeight
            };
            ClampRectangleToWorkArea(ref targetRectangle, workArea);

            const SetWindowPosFlags Flags =
                SetWindowPosFlags.NoZOrder |
                SetWindowPosFlags.NoActivate |
                SetWindowPosFlags.NoOwnerZOrder;
            bool resized = SetWindowPos(
                windowHandle,
                IntPtr.Zero,
                targetRectangle.Left,
                targetRectangle.Top,
                targetRectangle.Width,
                targetRectangle.Height,
                Flags);
            if (resized)
            {
                RefreshWindowRegionIfNeeded(true);
            }
            return resized;
#else
            return false;
#endif
        }

        /// <summary>
        /// Toggles contextual UI in the fixed transparent side panel. Native geometry
        /// never changes, which keeps the pet and compositor surface stable. Manual
        /// edge/corner resizing is available only while this panel is visible.
        /// </summary>
        public void SetSidePanelVisible(bool visible, int panelWidth)
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            if (!_isReady || _nativeWindow == null || panelWidth <= 0)
            {
                return;
            }
            if (_sidePanelVisible == visible)
            {
                return;
            }

            EndDrag();
            if (!visible)
            {
                if (_managedSizing || _managedResizeBeginPending)
                {
                    EndManagedResizeFallback(true);
                }
                else
                {
                    ClearPendingManagedResizeBegin();
                }
                ReleaseResizeHitTestOverride();
                CancelFullWindowRegionReveal();
            }
            // The Unity backing surface remains permanently wide. Windows clips the
            // hidden side-panel half from the actual window, so toggling contextual UI
            // does not resize the backbuffer or expose a stale frame.
            _sidePanelVisible = visible;
            if (visible)
            {
                RequestFullWindowRegionAfterRender();
            }
            else
            {
                RefreshWindowRegionIfNeeded(true);
            }
#endif
        }

        /// <summary>
        /// Begins mouse-driven native-window dragging. Call from the pet's pointer-down
        /// or mouse-down handler; Update moves the window until EndDrag is called.
        /// </summary>
        public bool BeginDrag()
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            if (!_isReady || _nativeWindow == null)
            {
                return false;
            }
            _cursorInResizeGutter = IsCursorInResizeGutter();
            if (_nativeSizing || _managedSizing || _cursorInResizeGutter ||
                _resizeHitTestOverrideActive)
            {
                return false;
            }
            if (_isDragging)
            {
                return true;
            }

            _hitTestWasEnabled = _nativeWindow.isHitTestEnabled;
            _clickThroughWasEnabled = _nativeWindow.isClickThrough;
            _nativeWindow.isHitTestEnabled = false;
            _nativeWindow.isClickThrough = false;
            _dragWindowOffset =
                _nativeWindow.windowPosition - _nativeWindow.cursorPosition;
            _isDragging = true;
            RaiseDragStateChanged(true);
            return true;
#else
            return false;
#endif
        }

        /// <summary>
        /// Moves the window once using the global cursor. Calling this explicitly is
        /// optional because Update calls it while a drag is active.
        /// </summary>
        public bool ContinueDrag()
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            if (!_isDragging || _nativeWindow == null)
            {
                return false;
            }

            _nativeWindow.windowPosition =
                _nativeWindow.cursorPosition + _dragWindowOffset;
            return true;
#else
            return false;
#endif
        }

        /// <summary>
        /// Ends native-window dragging. Call from pointer-up, mouse-up, cancellation,
        /// or when the interaction object is disabled.
        /// </summary>
        public void EndDrag()
        {
            if (!_isDragging)
            {
                return;
            }

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            if (_nativeWindow != null)
            {
                _nativeWindow.isHitTestEnabled = _hitTestWasEnabled;
                if (!_hitTestWasEnabled)
                {
                    _nativeWindow.isClickThrough = _clickThroughWasEnabled;
                }
            }
#endif
            _isDragging = false;
            RaiseDragStateChanged(false);
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus)
            {
                EndDrag();
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
                EndManagedResizeFallback(true);
#endif
            }
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            else
            {
                // UniWindow can reacquire the HWND after focus changes. Force the
                // region to be applied to whichever handle is current now.
                _windowRegionWidth = 0;
                _windowRegionHeight = 0;
                RefreshWindowRegionIfNeeded(true);
            }
#endif
        }

        private void OnDisable()
        {
            EndDrag();
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            CancelFullWindowRegionReveal();
            EndManagedResizeFallback(true);
            ReleaseResizeHitTestOverride();
            if (!_isQuitting)
            {
                RestoreFullWindowRegion();
            }
#endif
        }

        private void OnApplicationQuit()
        {
            _isQuitting = true;
        }

        private void OnDestroy()
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            CancelFullWindowRegionReveal();
            EndManagedResizeFallback(true);
            ReleaseResizeHitTestOverride();
            if (!_isQuitting)
            {
                RestoreFullWindowRegion();
            }
            RemoveNativeResizeBridge();
            if (_nativeWindow != null)
            {
                _nativeWindow.OnDropFiles -= HandleDroppedFiles;
            }
            if (!_isQuitting && _ownsNativeWindow && _nativeWindow != null)
            {
                // Restore an ordinary interactive window when this component is removed
                // during runtime. On application quit, avoid a distracting style flash.
                _nativeWindow.isClickThrough = false;
                _nativeWindow.isTopmost = false;
                _nativeWindow.isTransparent = false;
                Destroy(_nativeWindow.gameObject);
            }
#endif
        }

        private void HandleDroppedFiles(string[] paths)
        {
            Action<string[]> handler = FilesDropped;
            if (handler != null && paths != null && paths.Length > 0)
            {
                handler(paths);
            }
        }

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        private void RequestFullWindowRegionAfterRender()
        {
            if (_fullRegionRevealPending)
            {
                return;
            }

            _fullRegionRevealPending = true;
            if (!_isReady)
            {
                return;
            }
            _fullRegionRevealCoroutine = StartCoroutine(
                RevealFullWindowRegionAfterRender());
        }

        private IEnumerator RevealFullWindowRegionAfterRender()
        {
            // Wait through one complete render after the request. Setup can be
            // opened from another component's OnGUI, after its own draw pass has
            // already run for the current frame.
            yield return null;
            yield return new WaitForEndOfFrame();
            _fullRegionRevealCoroutine = null;
            if (!_fullRegionRevealPending)
            {
                yield break;
            }

            _fullRegionRevealPending = false;
            RefreshWindowRegionIfNeeded(true);
        }

        private void CancelFullWindowRegionReveal()
        {
            if (_fullRegionRevealCoroutine != null)
            {
                StopCoroutine(_fullRegionRevealCoroutine);
                _fullRegionRevealCoroutine = null;
            }
            _fullRegionRevealPending = false;
        }

        private void RefreshWindowRegionIfNeeded(bool force)
        {
            if (!_isReady)
            {
                return;
            }
            if (_loggedWindowRegionFailure && !force)
            {
                return;
            }

            bool shouldClip = ShouldClipWindowToPet(
                _sidePanelVisible,
                _fullSurfaceOverrideVisible);
            if (!shouldClip && _fullRegionRevealPending)
            {
                return;
            }

            IntPtr windowHandle = AcquirePlayerWindowHandle();
            if (windowHandle == IntPtr.Zero)
            {
                LogWindowRegionFailure("the Unity player window was unavailable");
                return;
            }

            if (!shouldClip)
            {
                if (_windowRegionClippedToPet)
                {
                    RestoreFullWindowRegion();
                }
                return;
            }

            NativeRect rectangle;
            if (!GetWindowRect(windowHandle, out rectangle) ||
                rectangle.Width <= 0 || rectangle.Height <= 0)
            {
                LogWindowRegionFailure(
                    "GetWindowRect failed with error " +
                    Marshal.GetLastWin32Error());
                return;
            }
            if (!force && _windowRegionClippedToPet &&
                _windowRegionWidth == rectangle.Width &&
                _windowRegionHeight == rectangle.Height)
            {
                return;
            }

            RectInt petBounds = CalculatePetOnlyWindowRegion(
                rectangle.Width,
                rectangle.Height);
            IntPtr petRegion = CreateRectRgn(
                petBounds.xMin,
                petBounds.yMin,
                petBounds.xMax,
                petBounds.yMax);
            if (petRegion == IntPtr.Zero)
            {
                LogWindowRegionFailure(
                    "CreateRectRgn failed with error " +
                    Marshal.GetLastWin32Error());
                return;
            }

            if (SetWindowRgn(windowHandle, petRegion, true) == 0)
            {
                int error = Marshal.GetLastWin32Error();
                DeleteObject(petRegion);
                LogWindowRegionFailure(
                    "SetWindowRgn(pet) failed with error " + error);
                return;
            }

            // Windows owns petRegion after a successful SetWindowRgn call.
            bool changed = !_windowRegionClippedToPet ||
                _windowRegionWidth != rectangle.Width ||
                _windowRegionHeight != rectangle.Height;
            _windowRegionClippedToPet = true;
            _windowRegionWidth = rectangle.Width;
            _windowRegionHeight = rectangle.Height;
            if (changed)
            {
                Debug.Log(
                    "Desktop window region: pet-only " + petBounds.width + "x" +
                    petBounds.height + " inside " + rectangle.Width + "x" +
                    rectangle.Height + ".");
            }
        }

        private void RestoreFullWindowRegion()
        {
            if (!_windowRegionClippedToPet)
            {
                return;
            }

            IntPtr windowHandle = AcquirePlayerWindowHandle();
            if (windowHandle == IntPtr.Zero ||
                SetWindowRgn(windowHandle, IntPtr.Zero, true) == 0)
            {
                LogWindowRegionFailure(
                    "SetWindowRgn(full) failed with error " +
                    Marshal.GetLastWin32Error());
                return;
            }

            _windowRegionClippedToPet = false;
            _windowRegionWidth = 0;
            _windowRegionHeight = 0;
            Debug.Log("Desktop window region: full menu surface.");
        }

        private void LogWindowRegionFailure(string reason)
        {
            if (_loggedWindowRegionFailure)
            {
                return;
            }

            _loggedWindowRegionFailure = true;
            Debug.LogWarning(
                "The pet-only native window region could not be applied because " +
                reason + ". Transparent pixels will remain click-through.");
        }

        private bool InstallNativeResizeBridge()
        {
            if (_resizeBridgeInstalled)
            {
                return true;
            }

            IntPtr windowHandle = AcquirePlayerWindowHandle();
            if (windowHandle == IntPtr.Zero ||
                (s_resizeWindowOwner != null &&
                    !ReferenceEquals(s_resizeWindowOwner, this)))
            {
                return false;
            }

            IntPtr currentProcedure = GetWindowLongPtr(
                windowHandle,
                WindowLongWindowProcedure);
            if (currentProcedure == IntPtr.Zero)
            {
                return false;
            }

            s_resizeWindowOwner = this;
            s_resizeWindowProcedure = ResizeWindowProcedureRoot;
            _resizeWindowProcedurePointer =
                Marshal.GetFunctionPointerForDelegate(s_resizeWindowProcedure);
            _previousWindowProcedure = SetWindowLongPtr(
                windowHandle,
                WindowLongWindowProcedure,
                _resizeWindowProcedurePointer);
            if (_previousWindowProcedure == IntPtr.Zero)
            {
                ClearResizeWindowProcedureRoot();
                return false;
            }

            _playerWindowHandle = windowHandle;
            _resizeBridgeInstalled = true;
            return true;
        }

        private void RemoveNativeResizeBridge()
        {
            if (!_resizeBridgeInstalled || _playerWindowHandle == IntPtr.Zero)
            {
                return;
            }

            bool procedureIsSafeToRelease = false;
            if (IsWindow(_playerWindowHandle))
            {
                IntPtr currentProcedure = GetWindowLongPtr(
                    _playerWindowHandle,
                    WindowLongWindowProcedure);
                if (currentProcedure == _resizeWindowProcedurePointer)
                {
                    procedureIsSafeToRelease = SetWindowLongPtr(
                        _playerWindowHandle,
                        WindowLongWindowProcedure,
                        _previousWindowProcedure) != IntPtr.Zero;
                }
                else if (currentProcedure == _previousWindowProcedure)
                {
                    procedureIsSafeToRelease = true;
                }
            }

            _resizeBridgeInstalled = false;
            _nativeSizing = false;
            _hasNativeSizingStartRect = false;
            if (procedureIsSafeToRelease)
            {
                ClearResizeWindowProcedureRoot();
            }
        }

        private void ClearResizeWindowProcedureRoot()
        {
            if (ReferenceEquals(s_resizeWindowOwner, this))
            {
                s_resizeWindowOwner = null;
                s_resizeWindowProcedure = null;
            }
            _previousWindowProcedure = IntPtr.Zero;
            _resizeWindowProcedurePointer = IntPtr.Zero;
        }

        private static IntPtr ResizeWindowProcedureRoot(
            IntPtr windowHandle,
            uint message,
            IntPtr wParam,
            IntPtr lParam)
        {
            DesktopWindowController owner = s_resizeWindowOwner;
            if (ReferenceEquals(owner, null))
            {
                return DefWindowProc(
                    windowHandle,
                    message,
                    wParam,
                    lParam);
            }

            try
            {
                return owner.HandleResizeWindowMessage(
                    windowHandle,
                    message,
                    wParam,
                    lParam);
            }
            catch
            {
                owner._resizeWindowProcedureFaulted = true;
                return owner.CallPreviousWindowProcedure(
                    windowHandle,
                    message,
                    wParam,
                    lParam);
            }
        }

        private IntPtr HandleResizeWindowMessage(
            IntPtr windowHandle,
            uint message,
            IntPtr wParam,
            IntPtr lParam)
        {
            switch (message)
            {
                case WindowMessageNcHitTest:
                    int hitTest = IsInteractiveResizeEnabled
                        ? GetResizeHitTest(windowHandle)
                        : 0;
                    if (hitTest != 0)
                    {
                        return new IntPtr(hitTest);
                    }
                    break;

                case WindowMessageNcLeftButtonDown:
                {
                    int pendingEdge = SizingEdgeFromHitTest(
                        unchecked((int)wParam.ToInt64()));
                    if (IsInteractiveResizeEnabled &&
                        pendingEdge != 0 && !_nativeSizing)
                    {
                        // Do not call DefWindowProc for our invisible resize gutter:
                        // it starts a modal non-client sizing loop that can block
                        // Unity before the polling fallback observes the mouse-down.
                        if (!_managedSizing && !_managedResizeBeginPending)
                        {
                            _pendingManagedSizingEdge = pendingEdge;
                            _pendingManagedSizingWindow = windowHandle;
                            _pendingManagedSizingSnapshotValid =
                                GetCursorPos(out _pendingManagedSizingCursor) &&
                                GetWindowRect(
                                    windowHandle,
                                    out _pendingManagedSizingRect);
                            _managedResizeBeginPending = true;
                            _cursorInResizeGutter = true;
                        }
                        return IntPtr.Zero;
                    }
                    break;
                }

                case WindowMessageGetMinMaxInfo:
                    ApplyMinMaxInfo(windowHandle, lParam);
                    return IntPtr.Zero;

                case WindowMessageSizing:
                    NativeRect proposedRectangle =
                        Marshal.PtrToStructure<NativeRect>(lParam);
                    ConstrainSizingRectangle(
                        windowHandle,
                        unchecked((int)wParam.ToInt64()),
                        ref proposedRectangle);
                    Marshal.StructureToPtr(proposedRectangle, lParam, false);
                    return new IntPtr(1);

                case WindowMessageEnterSizeMove:
                    _nativeSizing = true;
                    _nativeSizingObservedSincePoll = true;
                    _cursorInResizeGutter = true;
                    _hasNativeSizingStartRect = GetWindowRect(
                        windowHandle,
                        out _nativeSizingStartRect);
                    break;

                case WindowMessageExitSizeMove:
                    _nativeSizing = false;
                    _hasNativeSizingStartRect = false;
                    break;
            }

            return CallPreviousWindowProcedure(
                windowHandle,
                message,
                wParam,
                lParam);
        }

        private IntPtr CallPreviousWindowProcedure(
            IntPtr windowHandle,
            uint message,
            IntPtr wParam,
            IntPtr lParam)
        {
            return _previousWindowProcedure != IntPtr.Zero
                ? CallWindowProc(
                    _previousWindowProcedure,
                    windowHandle,
                    message,
                    wParam,
                    lParam)
                : DefWindowProc(windowHandle, message, wParam, lParam);
        }

        private static void ApplyMinMaxInfo(
            IntPtr windowHandle,
            IntPtr minMaxInfoPointer)
        {
            if (minMaxInfoPointer == IntPtr.Zero)
            {
                return;
            }

            NativeRect workArea;
            int maximumWidth;
            int maximumHeight;
            GetResizeLimitsForWindow(
                windowHandle,
                out workArea,
                out maximumWidth,
                out maximumHeight);
            NativeMinMaxInfo info =
                Marshal.PtrToStructure<NativeMinMaxInfo>(minMaxInfoPointer);
            info.MinTrackSize.X = MinimumWindowWidth;
            info.MinTrackSize.Y = MinimumWindowHeight;
            info.MaxTrackSize.X = maximumWidth;
            info.MaxTrackSize.Y = maximumHeight;
            info.MaxSize.X = maximumWidth;
            info.MaxSize.Y = maximumHeight;

            IntPtr monitorHandle = MonitorFromWindow(
                windowHandle,
                MonitorDefaultToNearest);
            NativeMonitorInfo monitorInfo = CreateMonitorInfo();
            if (monitorHandle != IntPtr.Zero &&
                GetMonitorInfo(monitorHandle, ref monitorInfo))
            {
                info.MaxPosition.X = workArea.Left - monitorInfo.Monitor.Left;
                info.MaxPosition.Y = workArea.Top - monitorInfo.Monitor.Top;
            }
            Marshal.StructureToPtr(info, minMaxInfoPointer, false);
        }

        private void ConstrainSizingRectangle(
            IntPtr windowHandle,
            int sizingEdge,
            ref NativeRect rectangle)
        {
            NativeRect workArea;
            int maximumWidth;
            int maximumHeight;
            GetResizeLimitsForRectangle(
                windowHandle,
                ref rectangle,
                out workArea,
                out maximumWidth,
                out maximumHeight);

            int preferredDimension = 0;
            if (sizingEdge == SizingLeft || sizingEdge == SizingRight)
            {
                preferredDimension = 1;
            }
            else if (sizingEdge == SizingTop || sizingEdge == SizingBottom)
            {
                preferredDimension = 2;
            }
            else if (_hasNativeSizingStartRect)
            {
                double widthScaleChange = Math.Abs(
                    rectangle.Width - _nativeSizingStartRect.Width) /
                    (double)WindowAspectWidth;
                double heightScaleChange = Math.Abs(
                    rectangle.Height - _nativeSizingStartRect.Height) /
                    (double)WindowAspectHeight;
                preferredDimension = widthScaleChange >= heightScaleChange ? 1 : 2;
            }

            int width;
            int height;
            ConstrainClientSizeCore(
                rectangle.Width,
                rectangle.Height,
                preferredDimension,
                maximumWidth,
                maximumHeight,
                out width,
                out height);

            switch (sizingEdge)
            {
                case SizingLeft:
                    rectangle.Left = rectangle.Right - width;
                    rectangle.Bottom = rectangle.Top + height;
                    break;
                case SizingRight:
                    rectangle.Right = rectangle.Left + width;
                    rectangle.Bottom = rectangle.Top + height;
                    break;
                case SizingTop:
                    rectangle.Top = rectangle.Bottom - height;
                    rectangle.Right = rectangle.Left + width;
                    break;
                case SizingBottom:
                    rectangle.Bottom = rectangle.Top + height;
                    rectangle.Right = rectangle.Left + width;
                    break;
                case SizingTopLeft:
                    rectangle.Left = rectangle.Right - width;
                    rectangle.Top = rectangle.Bottom - height;
                    break;
                case SizingTopRight:
                    rectangle.Right = rectangle.Left + width;
                    rectangle.Top = rectangle.Bottom - height;
                    break;
                case SizingBottomLeft:
                    rectangle.Left = rectangle.Right - width;
                    rectangle.Bottom = rectangle.Top + height;
                    break;
                default:
                    rectangle.Right = rectangle.Left + width;
                    rectangle.Bottom = rectangle.Top + height;
                    break;
            }
            ClampRectangleToWorkArea(ref rectangle, workArea);
        }

        private static int GetResizeHitTest(IntPtr windowHandle)
        {
            NativePoint cursor;
            NativeRect rectangle;
            if (!GetCursorPos(out cursor) ||
                !GetWindowRect(windowHandle, out rectangle))
            {
                return 0;
            }

            int gutter = GetResizeGutterPixels(windowHandle);
            bool left = cursor.X >= rectangle.Left &&
                cursor.X < rectangle.Left + gutter;
            bool right = cursor.X < rectangle.Right &&
                cursor.X >= rectangle.Right - gutter;
            bool top = cursor.Y >= rectangle.Top &&
                cursor.Y < rectangle.Top + gutter;
            bool bottom = cursor.Y < rectangle.Bottom &&
                cursor.Y >= rectangle.Bottom - gutter;
            if (top && left) return HitTestTopLeft;
            if (top && right) return HitTestTopRight;
            if (bottom && left) return HitTestBottomLeft;
            if (bottom && right) return HitTestBottomRight;
            if (left) return HitTestLeft;
            if (right) return HitTestRight;
            if (top) return HitTestTop;
            if (bottom) return HitTestBottom;
            return 0;
        }

        private void UpdateManagedResizeFallback()
        {
            bool leftMouseDown =
                (GetAsyncKeyState(VirtualKeyLeftButton) & 0x8000) != 0;

            if (!IsInteractiveResizeEnabled &&
                !_nativeSizing && !_managedSizing)
            {
                ClearPendingManagedResizeBegin();
                _managedResizeInputSuppressedUntilRelease = false;
                _leftMouseWasDown = leftMouseDown;
                return;
            }

            // A real Windows modal sizing loop always owns the gesture. The flag is
            // latched in WndProc because ENTER/EXIT can both occur before Unity gets
            // another Update when the modal loop blocks the player thread.
            if (_nativeSizingObservedSincePoll || _nativeSizing)
            {
                _nativeSizingObservedSincePoll = false;
                ClearPendingManagedResizeBegin();
                if (_managedSizing)
                {
                    EndManagedResizeFallback(leftMouseDown);
                }
                if (leftMouseDown)
                {
                    _managedResizeInputSuppressedUntilRelease = true;
                }
                _leftMouseWasDown = leftMouseDown;
                return;
            }

            if (_managedResizeInputSuppressedUntilRelease)
            {
                ClearPendingManagedResizeBegin();
                if (!leftMouseDown)
                {
                    _managedResizeInputSuppressedUntilRelease = false;
                }
                _leftMouseWasDown = leftMouseDown;
                return;
            }

            if (_managedResizeBeginPending)
            {
                if (leftMouseDown)
                {
                    TryBeginPendingManagedResizeFallback();
                }
                else
                {
                    ClearPendingManagedResizeBegin();
                }
            }

            if (_managedSizing)
            {
                if (leftMouseDown)
                {
                    ContinueManagedResizeFallback();
                }
                else
                {
                    EndManagedResizeFallback(false);
                }
            }
            else if (leftMouseDown && !_leftMouseWasDown)
            {
                TryBeginManagedResizeFallback();
            }

            _leftMouseWasDown = leftMouseDown;
        }

        private bool TryBeginManagedResizeFallback()
        {
            if (!IsInteractiveResizeEnabled ||
                _nativeSizing || _managedSizing)
            {
                return false;
            }

            IntPtr windowHandle = AcquirePlayerWindowHandle();
            if (windowHandle == IntPtr.Zero)
            {
                return false;
            }

            int sizingEdge = SizingEdgeFromHitTest(
                GetResizeHitTest(windowHandle));
            NativePoint cursor;
            NativeRect rectangle;
            if (sizingEdge == 0 ||
                !GetCursorPos(out cursor) ||
                !GetWindowRect(windowHandle, out rectangle))
            {
                return false;
            }

            return BeginManagedResizeFallback(
                windowHandle,
                sizingEdge,
                cursor,
                rectangle);
        }

        private bool TryBeginPendingManagedResizeFallback()
        {
            if (!_managedResizeBeginPending)
            {
                return false;
            }

            IntPtr windowHandle = _pendingManagedSizingWindow;
            int sizingEdge = _pendingManagedSizingEdge;
            NativePoint cursor = _pendingManagedSizingCursor;
            NativeRect rectangle = _pendingManagedSizingRect;
            bool snapshotValid = _pendingManagedSizingSnapshotValid;
            ClearPendingManagedResizeBegin();
            if (!snapshotValid &&
                (!GetCursorPos(out cursor) ||
                    !GetWindowRect(windowHandle, out rectangle)))
            {
                return false;
            }

            return BeginManagedResizeFallback(
                windowHandle,
                sizingEdge,
                cursor,
                rectangle);
        }

        private bool BeginManagedResizeFallback(
            IntPtr windowHandle,
            int sizingEdge,
            NativePoint cursor,
            NativeRect rectangle)
        {
            if (!IsInteractiveResizeEnabled ||
                _nativeSizing || _managedSizing || sizingEdge == 0 ||
                !IsWindow(windowHandle))
            {
                return false;
            }

            if (_isDragging)
            {
                EndDrag();
            }
            _playerWindowHandle = windowHandle;
            _managedSizingEdge = sizingEdge;
            _managedSizingStartCursor = cursor;
            _managedSizingStartRect = rectangle;
            _nativeSizingStartRect = rectangle;
            _hasNativeSizingStartRect = true;
            _managedResizeApplied = false;
            _managedSizing = true;
            return true;
        }

        private void ClearPendingManagedResizeBegin()
        {
            _managedResizeBeginPending = false;
            _pendingManagedSizingEdge = 0;
            _pendingManagedSizingWindow = IntPtr.Zero;
            _pendingManagedSizingSnapshotValid = false;
        }

        private void ContinueManagedResizeFallback()
        {
            if (!_managedSizing || _nativeSizing)
            {
                return;
            }

            NativePoint cursor;
            if (!IsWindow(_playerWindowHandle) || !GetCursorPos(out cursor))
            {
                EndManagedResizeFallback(true);
                return;
            }

            int deltaX = cursor.X - _managedSizingStartCursor.X;
            int deltaY = cursor.Y - _managedSizingStartCursor.Y;
            NativeRect proposedRectangle = _managedSizingStartRect;
            switch (_managedSizingEdge)
            {
                case SizingLeft:
                    proposedRectangle.Left += deltaX;
                    break;
                case SizingRight:
                    proposedRectangle.Right += deltaX;
                    break;
                case SizingTop:
                    proposedRectangle.Top += deltaY;
                    break;
                case SizingTopLeft:
                    proposedRectangle.Top += deltaY;
                    proposedRectangle.Left += deltaX;
                    break;
                case SizingTopRight:
                    proposedRectangle.Top += deltaY;
                    proposedRectangle.Right += deltaX;
                    break;
                case SizingBottom:
                    proposedRectangle.Bottom += deltaY;
                    break;
                case SizingBottomLeft:
                    proposedRectangle.Bottom += deltaY;
                    proposedRectangle.Left += deltaX;
                    break;
                case SizingBottomRight:
                    proposedRectangle.Bottom += deltaY;
                    proposedRectangle.Right += deltaX;
                    break;
                default:
                    EndManagedResizeFallback(true);
                    return;
            }

            ConstrainSizingRectangle(
                _playerWindowHandle,
                _managedSizingEdge,
                ref proposedRectangle);
            const SetWindowPosFlags Flags =
                SetWindowPosFlags.NoZOrder |
                SetWindowPosFlags.NoActivate |
                SetWindowPosFlags.NoOwnerZOrder;
            if (!SetWindowPos(
                _playerWindowHandle,
                IntPtr.Zero,
                proposedRectangle.Left,
                proposedRectangle.Top,
                proposedRectangle.Width,
                proposedRectangle.Height,
                Flags))
            {
                EndManagedResizeFallback(true);
                return;
            }

            if (proposedRectangle.Width != _managedSizingStartRect.Width ||
                proposedRectangle.Height != _managedSizingStartRect.Height)
            {
                _managedResizeApplied = true;
            }
        }

        private void EndManagedResizeFallback(bool suppressUntilRelease)
        {
            ClearPendingManagedResizeBegin();
            bool wasSizing = _managedSizing;
            NativeRect startingRectangle = _managedSizingStartRect;
            bool appliedResize = _managedResizeApplied;
            _managedSizing = false;
            _managedSizingEdge = 0;
            _managedResizeApplied = false;
            if (!_nativeSizing)
            {
                _hasNativeSizingStartRect = false;
            }
            if (suppressUntilRelease)
            {
                _managedResizeInputSuppressedUntilRelease = true;
            }

            if (!wasSizing || !appliedResize ||
                _managedResizeCompletionLogged ||
                !IsWindow(_playerWindowHandle))
            {
                return;
            }

            NativeRect completedRectangle;
            if (GetWindowRect(_playerWindowHandle, out completedRectangle) &&
                (completedRectangle.Width != startingRectangle.Width ||
                    completedRectangle.Height != startingRectangle.Height))
            {
                _managedResizeCompletionLogged = true;
                Debug.Log(
                    "Managed window resize complete: " +
                    startingRectangle.Width + "x" + startingRectangle.Height +
                    " -> " + completedRectangle.Width + "x" +
                    completedRectangle.Height + ".");
            }
        }

        private static int SizingEdgeFromHitTest(int hitTest)
        {
            switch (hitTest)
            {
                case HitTestLeft: return SizingLeft;
                case HitTestRight: return SizingRight;
                case HitTestTop: return SizingTop;
                case HitTestTopLeft: return SizingTopLeft;
                case HitTestTopRight: return SizingTopRight;
                case HitTestBottom: return SizingBottom;
                case HitTestBottomLeft: return SizingBottomLeft;
                case HitTestBottomRight: return SizingBottomRight;
                default: return 0;
            }
        }

        private bool IsCursorInResizeGutter()
        {
            if (!IsInteractiveResizeEnabled)
            {
                return false;
            }
            IntPtr windowHandle = AcquirePlayerWindowHandle();
            return windowHandle != IntPtr.Zero &&
                GetResizeHitTest(windowHandle) != 0;
        }

        private static int GetResizeGutterPixels(IntPtr windowHandle)
        {
            uint dpi = DefaultDpi;
            try
            {
                uint windowDpi = GetDpiForWindow(windowHandle);
                if (windowDpi > 0)
                {
                    dpi = windowDpi;
                }
            }
            catch (EntryPointNotFoundException)
            {
                dpi = DefaultDpi;
            }
            return Math.Max(
                4,
                (int)Math.Round(
                    ResizeGutterDips * dpi / (double)DefaultDpi,
                    MidpointRounding.AwayFromZero));
        }

        private void UpdateResizeHitTestOverride()
        {
            bool shouldOverride =
                _nativeSizing || _managedSizing || IsCursorInResizeGutter();
            _cursorInResizeGutter = shouldOverride;
            if (shouldOverride && _isDragging)
            {
                EndDrag();
            }

            if (shouldOverride && !_resizeHitTestOverrideActive)
            {
                _resizeHitTestWasEnabled = _nativeWindow.isHitTestEnabled;
                _resizeClickThroughWasEnabled = _nativeWindow.isClickThrough;
                _nativeWindow.isHitTestEnabled = false;
                _nativeWindow.isClickThrough = false;
                _resizeHitTestOverrideActive = true;
            }
            else if (!shouldOverride)
            {
                ReleaseResizeHitTestOverride();
            }
        }

        private void ReleaseResizeHitTestOverride()
        {
            if (!_resizeHitTestOverrideActive)
            {
                return;
            }
            if (_nativeWindow != null)
            {
                _nativeWindow.isHitTestEnabled = _resizeHitTestWasEnabled;
                _nativeWindow.isClickThrough = _resizeClickThroughWasEnabled;
            }
            _resizeHitTestOverrideActive = false;
            _cursorInResizeGutter = false;
        }

        private static void GetResizeLimitsForWindow(
            IntPtr windowHandle,
            out NativeRect workArea,
            out int maximumWidth,
            out int maximumHeight)
        {
            IntPtr monitorHandle = MonitorFromWindow(
                windowHandle,
                MonitorDefaultToNearest);
            if (!TryGetMonitorWorkArea(monitorHandle, out workArea))
            {
                SystemParametersInfo(
                    SystemParametersInfoGetWorkArea,
                    0,
                    out workArea,
                    0);
            }
            CalculateMaximumSize(workArea, out maximumWidth, out maximumHeight);
        }

        private static void GetResizeLimitsForRectangle(
            IntPtr windowHandle,
            ref NativeRect rectangle,
            out NativeRect workArea,
            out int maximumWidth,
            out int maximumHeight)
        {
            IntPtr monitorHandle = MonitorFromRect(
                ref rectangle,
                MonitorDefaultToNearest);
            if (!TryGetMonitorWorkArea(monitorHandle, out workArea))
            {
                GetResizeLimitsForWindow(
                    windowHandle,
                    out workArea,
                    out maximumWidth,
                    out maximumHeight);
                return;
            }
            CalculateMaximumSize(workArea, out maximumWidth, out maximumHeight);
        }

        private static bool TryGetMonitorWorkArea(
            IntPtr monitorHandle,
            out NativeRect workArea)
        {
            NativeMonitorInfo monitorInfo = CreateMonitorInfo();
            if (monitorHandle != IntPtr.Zero &&
                GetMonitorInfo(monitorHandle, ref monitorInfo) &&
                monitorInfo.Work.Width > 0 && monitorInfo.Work.Height > 0)
            {
                workArea = monitorInfo.Work;
                return true;
            }
            workArea = default(NativeRect);
            return false;
        }

        private static NativeMonitorInfo CreateMonitorInfo()
        {
            return new NativeMonitorInfo
            {
                Size = Marshal.SizeOf(typeof(NativeMonitorInfo))
            };
        }

        private static void CalculateMaximumSize(
            NativeRect workArea,
            out int maximumWidth,
            out int maximumHeight)
        {
            int maximumScale = Math.Min(
                MaximumWindowWidth / WindowAspectWidth,
                MaximumWindowHeight / WindowAspectHeight);
            if (workArea.Width > 0 && workArea.Height > 0)
            {
                maximumScale = Math.Min(
                    maximumScale,
                    Math.Min(
                        workArea.Width / WindowAspectWidth,
                        workArea.Height / WindowAspectHeight));
            }
            maximumScale = Math.Max(
                MinimumWindowWidth / WindowAspectWidth,
                maximumScale);
            maximumWidth = maximumScale * WindowAspectWidth;
            maximumHeight = maximumScale * WindowAspectHeight;
        }

        private static void ClampRectangleToWorkArea(
            ref NativeRect rectangle,
            NativeRect workArea)
        {
            if (workArea.Width <= 0 || workArea.Height <= 0)
            {
                return;
            }
            int horizontalShift = rectangle.Left < workArea.Left
                ? workArea.Left - rectangle.Left
                : rectangle.Right > workArea.Right
                    ? workArea.Right - rectangle.Right
                    : 0;
            int verticalShift = rectangle.Top < workArea.Top
                ? workArea.Top - rectangle.Top
                : rectangle.Bottom > workArea.Bottom
                    ? workArea.Bottom - rectangle.Bottom
                    : 0;
            rectangle.Left += horizontalShift;
            rectangle.Right += horizontalShift;
            rectangle.Top += verticalShift;
            rectangle.Bottom += verticalShift;
        }

        private void BeginSidePanelOpenTransition(int panelWidth)
        {
            _sidePanelCameraManaged = true;
            _sidePanelOpenPending = true;
            _sidePanelCloseRequestedDuringOpen = false;
            _sidePanelOpenCoroutine = StartCoroutine(
                OpenSidePanelAfterRenderSync(panelWidth));
        }

        private IEnumerator OpenSidePanelAfterRenderSync(int panelWidth)
        {
            if (!TrySetSidePanelGeometryAtomically(true, panelWidth))
            {
                _nativeWindow.windowSize = new Vector2(
                    _compactWindowSize.x + panelWidth,
                    _compactWindowSize.y);
                _nativeWindow.windowPosition = new Vector2(
                    _compactWindowPosition.x - panelWidth,
                    _compactWindowPosition.y);
            }

            float timeoutAt =
                Time.realtimeSinceStartup + SidePanelOpenTimeoutSeconds;
            while (_sidePanelOpenPending &&
                !IsExpandedRenderSurfaceReady(panelWidth) &&
                Time.realtimeSinceStartup < timeoutAt)
            {
                yield return null;
            }
            if (!_sidePanelOpenPending)
            {
                yield break;
            }

            if (!IsExpandedRenderSurfaceReady(panelWidth))
            {
                Vector2 nativeSize = _nativeWindow != null
                    ? _nativeWindow.windowSize
                    : Vector2.zero;
                Vector2 clientSize = _nativeWindow != null
                    ? _nativeWindow.clientSize
                    : Vector2.zero;
                Debug.LogWarning(
                    "Timed out waiting for the expanded desktop-pet render surface. " +
                    "Completing the panel transition after " +
                    SidePanelOpenTimeoutSeconds + "s; native=" + nativeSize +
                    ", client=" + clientSize +
                    ", screen=" + Screen.width + "x" + Screen.height + ".");
            }

            bool closeRequested = _sidePanelCloseRequestedDuringOpen;
            _sidePanelOpenPending = false;
            _sidePanelCloseRequestedDuringOpen = false;
            _sidePanelOpenCoroutine = null;
            _sidePanelVisible = true;

            if (closeRequested)
            {
                BeginSidePanelCloseTransition(panelWidth);
                yield break;
            }
        }

        private void BeginSidePanelCloseTransition(int panelWidth)
        {
            _sidePanelCameraManaged = true;
            _sidePanelClosePending = true;
            _sidePanelCloseGeometryApplied = false;
            _sidePanelCloseCoroutine = StartCoroutine(
                CloseSidePanelAfterRenderSync(panelWidth));
        }

        private IEnumerator CloseSidePanelAfterRenderSync(int panelWidth)
        {
            // In particular, the carrot-eating smoke opens and closes the menu in one
            // frame. Let Unity observe the expanded HWND before issuing the compact
            // resize, otherwise its backbuffer can retain the expanded width.
            yield return null;
            if (!_sidePanelClosePending)
            {
                yield break;
            }

            if (!TrySetSidePanelGeometryAtomically(false, panelWidth))
            {
                _nativeWindow.windowSize = _compactWindowSize;
                _nativeWindow.windowPosition = _compactWindowPosition;
            }
            _sidePanelCloseGeometryApplied = true;

            float timeoutAt =
                Time.realtimeSinceStartup + SidePanelCloseTimeoutSeconds;
            while (_sidePanelClosePending &&
                !IsCompactRenderSurfaceReady() &&
                Time.realtimeSinceStartup < timeoutAt)
            {
                yield return null;
            }
            if (!_sidePanelClosePending)
            {
                yield break;
            }

            if (!IsCompactRenderSurfaceReady())
            {
                Vector2 nativeSize = _nativeWindow != null
                    ? _nativeWindow.windowSize
                    : Vector2.zero;
                Vector2 clientSize = _nativeWindow != null
                    ? _nativeWindow.clientSize
                    : Vector2.zero;
                Debug.LogWarning(
                    "Timed out waiting for the compact desktop-pet render surface. " +
                    "Restoring interaction after " +
                    SidePanelCloseTimeoutSeconds + "s; native=" + nativeSize +
                    ", client=" + clientSize +
                    ", screen=" + Screen.width + "x" + Screen.height + ".");
            }

            _sidePanelVisible = false;
            _sidePanelClosePending = false;
            _sidePanelCloseGeometryApplied = false;
            _sidePanelCloseCoroutine = null;
        }

        private bool IsCompactRenderSurfaceReady()
        {
            if (_nativeWindow == null ||
                _compactWindowSize.x <= 0.0f ||
                _compactWindowSize.y <= 0.0f)
            {
                return false;
            }

            float compactWidth = _compactWindowSize.x;
            return Mathf.Abs(_nativeWindow.windowSize.x - compactWidth) <=
                    SidePanelSizeTolerancePixels &&
                Mathf.Abs(_nativeWindow.clientSize.x - compactWidth) <=
                    SidePanelSizeTolerancePixels &&
                Mathf.Abs(Screen.width - compactWidth) <=
                    SidePanelSizeTolerancePixels;
        }

        private bool IsExpandedRenderSurfaceReady(int panelWidth)
        {
            if (_nativeWindow == null ||
                _compactWindowSize.x <= 0.0f ||
                _compactWindowSize.y <= 0.0f)
            {
                return false;
            }

            float expandedWidth = _compactWindowSize.x + panelWidth;
            return Mathf.Abs(_nativeWindow.windowSize.x - expandedWidth) <=
                    SidePanelSizeTolerancePixels &&
                Mathf.Abs(_nativeWindow.clientSize.x - expandedWidth) <=
                    SidePanelSizeTolerancePixels &&
                Mathf.Abs(Screen.width - expandedWidth) <=
                    SidePanelSizeTolerancePixels;
        }

        private void CancelPendingSidePanelCloseForReopen(int panelWidth)
        {
            bool geometryWasApplied = _sidePanelCloseGeometryApplied;
            if (_sidePanelCloseCoroutine != null)
            {
                StopCoroutine(_sidePanelCloseCoroutine);
            }
            _sidePanelCloseCoroutine = null;
            _sidePanelClosePending = false;
            _sidePanelCloseGeometryApplied = false;

            if (geometryWasApplied)
            {
                _sidePanelVisible = false;
                BeginSidePanelOpenTransition(panelWidth);
                return;
            }

            _sidePanelVisible = true;
        }

        private bool TrySetSidePanelGeometryAtomically(bool visible, int panelWidth)
        {
            const SetWindowPosFlags Flags =
                SetWindowPosFlags.NoZOrder |
                SetWindowPosFlags.NoActivate |
                SetWindowPosFlags.NoOwnerZOrder;

            if (visible)
            {
                IntPtr windowHandle = AcquirePlayerWindowHandle();
                if (windowHandle == IntPtr.Zero)
                {
                    LogAtomicGeometryFallback(
                        "the Unity player window handle was unavailable");
                    return false;
                }

                NativeRect compactRectangle;
                if (!GetWindowRect(windowHandle, out compactRectangle) ||
                    compactRectangle.Width <= 0 || compactRectangle.Height <= 0)
                {
                    LogAtomicGeometryFallback(
                        "GetWindowRect failed with error " +
                        Marshal.GetLastWin32Error());
                    return false;
                }

                if (!SetWindowPos(
                    windowHandle,
                    IntPtr.Zero,
                    compactRectangle.Left - panelWidth,
                    compactRectangle.Top,
                    compactRectangle.Width + panelWidth,
                    compactRectangle.Height,
                    Flags))
                {
                    LogAtomicGeometryFallback(
                        "SetWindowPos(open) failed with error " +
                        Marshal.GetLastWin32Error());
                    return false;
                }

                _playerWindowHandle = windowHandle;
                _compactNativeRect = compactRectangle;
                _sidePanelUsesAtomicGeometry = true;
                return true;
            }

            if (!_sidePanelUsesAtomicGeometry)
            {
                return false;
            }

            _sidePanelUsesAtomicGeometry = false;
            uint processId;
            IntPtr ignoredMainWindow;
            if (!TryGetCurrentProcessWindow(out processId, out ignoredMainWindow) ||
                !IsWindowForProcess(_playerWindowHandle, processId))
            {
                LogAtomicGeometryFallback(
                    "the cached Unity player window handle was no longer valid");
                return false;
            }

            if (!SetWindowPos(
                _playerWindowHandle,
                IntPtr.Zero,
                _compactNativeRect.Left,
                _compactNativeRect.Top,
                _compactNativeRect.Width,
                _compactNativeRect.Height,
                Flags))
            {
                LogAtomicGeometryFallback(
                    "SetWindowPos(close) failed with error " +
                    Marshal.GetLastWin32Error());
                return false;
            }

            return true;
        }

        private IntPtr AcquirePlayerWindowHandle()
        {
            uint processId;
            IntPtr mainWindow;
            if (!TryGetCurrentProcessWindow(out processId, out mainWindow))
            {
                return IntPtr.Zero;
            }
            if (IsWindowForProcess(_playerWindowHandle, processId))
            {
                return _playerWindowHandle;
            }

            // UniWindow already owns the authoritative HWND and exports it from
            // LibUniWinC even though its managed wrapper keeps that API internal.
            try
            {
                IntPtr pluginWindow = GetWindowHandle();
                if (IsWindowForProcess(pluginWindow, processId))
                {
                    _playerWindowHandle = pluginWindow;
                    return _playerWindowHandle;
                }
            }
            catch (EntryPointNotFoundException)
            {
                // Preserve compatibility with older UniWindow binaries.
            }
            catch (DllNotFoundException)
            {
                // The normal process-window fallbacks below remain available.
            }
            if (IsWindowForProcess(mainWindow, processId))
            {
                _playerWindowHandle = mainWindow;
                return _playerWindowHandle;
            }

            IntPtr activeWindow = GetActiveWindow();
            if (IsWindowForProcess(activeWindow, processId))
            {
                _playerWindowHandle = activeWindow;
                return _playerWindowHandle;
            }

            _playerWindowHandle = IntPtr.Zero;
            return IntPtr.Zero;
        }

        private bool TryGetCurrentProcessWindow(
            out uint processId,
            out IntPtr mainWindow)
        {
            processId = 0;
            mainWindow = IntPtr.Zero;
            try
            {
                using (var process = System.Diagnostics.Process.GetCurrentProcess())
                {
                    process.Refresh();
                    processId = unchecked((uint)process.Id);
                    mainWindow = process.MainWindowHandle;
                }
                return processId != 0;
            }
            catch (Exception exception)
            {
                LogAtomicGeometryFallback(
                    "the current process window could not be queried: " +
                    exception.Message);
                return false;
            }
        }

        private static bool IsWindowForProcess(
            IntPtr windowHandle,
            uint expectedProcessId)
        {
            if (windowHandle == IntPtr.Zero || !IsWindow(windowHandle))
            {
                return false;
            }

            uint actualProcessId;
            return GetWindowThreadProcessId(windowHandle, out actualProcessId) != 0 &&
                actualProcessId == expectedProcessId;
        }

        private void LogAtomicGeometryFallback(string reason)
        {
            if (_loggedAtomicGeometryFallback)
            {
                return;
            }

            _loggedAtomicGeometryFallback = true;
            Debug.LogWarning(
                "Atomic side-panel geometry was unavailable because " + reason +
                ". Falling back to the compatible resize path.");
        }

        private void AnchorCameraToCurrentRenderWidth()
        {
            if (renderCamera == null || !_hasCompactCameraPosition ||
                !renderCamera.orthographic ||
                _compactWindowSize.x <= 0.0f)
            {
                return;
            }

            float renderHeightPixels = Screen.height > 0
                ? Screen.height
                : _compactWindowSize.y;
            if (renderHeightPixels <= 0.0f)
            {
                return;
            }

            float addedRenderWidth = Mathf.Max(
                0.0f,
                Screen.width - _compactWindowSize.x);
            float worldUnitsPerPixel =
                renderCamera.orthographicSize * 2.0f / renderHeightPixels;
            float worldOffset =
                addedRenderWidth * 0.5f * worldUnitsPerPixel;
            renderCamera.transform.position =
                _compactCameraPosition - renderCamera.transform.right * worldOffset;
        }

        private void TryBindRenderCamera()
        {
            if (renderCamera == null)
            {
                renderCamera = Camera.main;
                if (renderCamera == null)
                {
                    renderCamera = FindAnyObjectByType<Camera>();
                }
            }
            if (renderCamera != null && _nativeWindow.currentCamera != renderCamera)
            {
                _nativeWindow.SetCamera(renderCamera);
            }
        }

        private void PlaceAtPrimaryBottomRight()
        {
            Rect monitor = UniWindowController.GetMonitorRect(0);
            if (monitor.width <= 0.0f || monitor.height <= 0.0f)
            {
                return;
            }

            Vector2 size = _nativeWindow.windowSize;
            if (size.x <= 0.0f || size.y <= 0.0f)
            {
                size = new Vector2(windowWidth, windowHeight);
            }

            float workRight = monitor.xMax;
            float workBottom = monitor.yMin;
            NativeRect workArea;
            if (SystemParametersInfo(
                SystemParametersInfoGetWorkArea,
                0,
                out workArea,
                0))
            {
                workRight = Mathf.Min(workRight, workArea.Right);
                // Win32 work-area coordinates start at the primary monitor's
                // top-left. UniWindow starts at its bottom-left.
                workBottom = Mathf.Max(
                    workBottom,
                    monitor.yMax - workArea.Bottom);
            }
            _nativeWindow.windowPosition = new Vector2(
                workRight - size.x - screenEdgePadding,
                workBottom + screenEdgePadding);
        }
#endif

        private static void ConstrainClientSizeCore(
            int requestedWidth,
            int requestedHeight,
            int preferredDimension,
            int maximumWidth,
            int maximumHeight,
            out int constrainedWidth,
            out int constrainedHeight)
        {
            double requestedScale;
            if (preferredDimension == 1)
            {
                requestedScale = Math.Max(1, requestedWidth) /
                    (double)WindowAspectWidth;
            }
            else if (preferredDimension == 2)
            {
                requestedScale = Math.Max(1, requestedHeight) /
                    (double)WindowAspectHeight;
            }
            else
            {
                // Orthogonal projection onto width=3s,height=2s gives the
                // closest exact-aspect integer pair to an arbitrary request.
                requestedScale =
                    (WindowAspectWidth * (double)Math.Max(1, requestedWidth) +
                        WindowAspectHeight * (double)Math.Max(1, requestedHeight)) /
                    (WindowAspectWidth * WindowAspectWidth +
                        WindowAspectHeight * WindowAspectHeight);
            }

            int minimumScale = Math.Max(
                MinimumWindowWidth / WindowAspectWidth,
                MinimumWindowHeight / WindowAspectHeight);
            int maximumScale = Math.Min(
                Math.Max(MinimumWindowWidth, maximumWidth) / WindowAspectWidth,
                Math.Max(MinimumWindowHeight, maximumHeight) / WindowAspectHeight);
            maximumScale = Math.Max(minimumScale, maximumScale);
            int constrainedScale = (int)Math.Round(
                requestedScale,
                MidpointRounding.AwayFromZero);
            constrainedScale = Math.Max(
                minimumScale,
                Math.Min(maximumScale, constrainedScale));
            constrainedWidth = constrainedScale * WindowAspectWidth;
            constrainedHeight = constrainedScale * WindowAspectHeight;
        }

        private void RaiseDragStateChanged(bool dragging)
        {
            Action<bool> handler = DragStateChanged;
            if (handler != null)
            {
                handler(dragging);
            }
        }
    }
}
