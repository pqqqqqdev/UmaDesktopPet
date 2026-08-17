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
        public const int SidePanelWidth = 324;
        public const int NativeWindowWidth = PetViewportWidth + SidePanelWidth;
        public const int NativeWindowHeight = 480;

        private const float SidePanelCloseTimeoutSeconds = 2.0f;
        private const float SidePanelOpenTimeoutSeconds = 2.0f;
        private const float SidePanelSizeTolerancePixels = 2.0f;
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        private const uint SystemParametersInfoGetWorkArea = 0x0030;
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

        [Flags]
        private enum SetWindowPosFlags : uint
        {
            NoZOrder = 0x0004,
            NoActivate = 0x0010,
            NoOwnerZOrder = 0x0200
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindow(IntPtr windowHandle);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(
            IntPtr windowHandle,
            out uint processId);

        [DllImport("user32.dll")]
        private static extern IntPtr GetActiveWindow();

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

            _nativeWindow.isTopmost = alwaysOnTop;
            if (placeAtPrimaryBottomRight)
            {
                PlaceAtPrimaryBottomRight();
            }

            _isReady = true;
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
        /// Toggles contextual UI in the fixed transparent side panel. Native geometry
        /// never changes, which keeps the pet and compositor surface stable.
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
            // The native surface remains permanently wide. Hidden side-panel pixels
            // are transparent and click-through, so opening contextual UI is only a
            // logical visibility change and can never expose a stale resize frame.
            _sidePanelVisible = visible;
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
            }
        }

        private void OnDisable()
        {
            EndDrag();
        }

        private void OnApplicationQuit()
        {
            _isQuitting = true;
        }

        private void OnDestroy()
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
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
