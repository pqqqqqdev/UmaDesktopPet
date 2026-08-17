using System;
using UnityEngine;
using UmaDesktopPet.Standalone.Core;

namespace UmaDesktopPet.Standalone.Runtime
{
    /// <summary>
    /// Separates a short click, a stationary hold-to-pat, and a mouse drag. The
    /// right-click menu exposes care actions and settings without a permanent HUD.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PetInteractionController : MonoBehaviour
    {
        private const float DragThresholdPixels = 7.0f;
        private const float HoldToPatSeconds = 0.58f;
        private const float MaximumTapSeconds = 0.75f;
        private const float MenuWidth = 308.0f;
        private const float MenuHeight = 368.0f;
        private const int MenuSidecarWidth = DesktopWindowController.SidePanelWidth;
        private const float ToastSeconds = 1.8f;
        private const float CarrotSize = 72.0f;
        private const float CarrotMouthSize = 42.0f;
        private const float CarrotBiteSize = 30.0f;
        private const float CarrotDeliverySeconds = 0.18f;
        private const float CarrotBiteShrinkSeconds = 0.32f;

        private static readonly Vector2 CarrotMouthOffset =
            new Vector2(19.0f, 11.0f);

        private static readonly Color GameTextColor =
            new Color(0.31f, 0.22f, 0.18f, 1.0f);

        private DesktopWindowController _window;
        private OguriPetAnimationController _motions;
        private PetNeedsState _needs;
        private InstalledCareUiAssets _careUiAssets;
        private Camera _camera;
        private Renderer _mouthRenderer;
        private Transform _mouthTransform;
        private PetCharacterProfile _characterProfile;
        private GameRegion _gameRegion;
        private string _gameRoot;
        private Action<string> _requestCharacterChange;
        private Action _requestGameInstallChange;
        private Action _requestGameFilesReload;
        private bool _initialized;
        private bool _pressActive;
        private bool _dragging;
        private bool _holdConsumed;
        private bool _showMenu;
        private MenuPage _menuPage;
        private Vector2 _pressPointerPosition;
        private Vector2 _menuPosition;
        private Vector2 _characterScroll;
        private float _pressStartedAt;
        private Rect _menuRect;
        private Rect _carrotPickupRect;
        private CarrotFeedPhase _carrotFeedPhase;
        private bool _carrotHovering;
        private bool _carrotVisible;
        private bool _feedApplied;
        private Vector2 _carrotReleasePosition;
        private Vector2 _mouthGuiPosition;
        private bool _hasMouthGuiPosition;
        private float _carrotDeliveryStartedAt;
        private float _carrotBiteStartedAt = -1.0f;
        private string _toast;
        private float _hideToastAt;
        private Texture2D _menuPanelTexture;
        private Texture2D _statusPanelTexture;
        private Texture2D _energyFrameTexture;
        private Texture2D _energyTrackTexture;
        private Texture2D _energyGradientTexture;
        private Texture2D _primaryButtonTexture;
        private Texture2D _primaryButtonHoverTexture;
        private Texture2D _secondaryButtonTexture;
        private Texture2D _secondaryButtonHoverTexture;
        private Texture2D _toggleOffTexture;
        private Texture2D _toggleOnTexture;
        private Texture _carrotTexture;
        private Texture2D _fallbackCarrotTexture;
        private Texture2D _carrotCardTexture;

        public bool IsMenuOpen
        {
            get { return _showMenu; }
        }

        internal int MenuSidecarWidthForSmokeTest
        {
            get { return MenuSidecarWidth; }
        }

        private string PetName
        {
            get
            {
                return _characterProfile != null
                    ? _characterProfile.ShortName
                    : "Oguri";
            }
        }

        public bool IsCareInteractionActive
        {
            get
            {
                return _showMenu ||
                    _carrotFeedPhase != CarrotFeedPhase.None;
            }
        }

        /// <summary>
        /// True while the user is pressing, carrying, or using any care UI.
        /// Autonomous reactions wait for this to clear so they cannot steal a
        /// hold-to-pat gesture or start under the cursor during a drag.
        /// </summary>
        public bool IsUserInteractionActive
        {
            get
            {
                return _pressActive ||
                    _dragging ||
                    IsCareInteractionActive;
            }
        }

        public void Initialize(
            DesktopWindowController window,
            OguriPetAnimationController motions,
            PetNeedsState needs,
            InstalledCareUiAssets careUiAssets,
            Camera camera,
            Transform characterRoot,
            PetCharacterProfile characterProfile,
            GameRegion gameRegion,
            string gameRoot,
            Action<string> requestCharacterChange = null,
            Action requestGameInstallChange = null,
            Action requestGameFilesReload = null)
        {
            if (_initialized)
            {
                throw new InvalidOperationException(
                    "The pet interaction controller is already initialized.");
            }
            if (window == null)
            {
                throw new ArgumentNullException("window");
            }
            if (motions == null)
            {
                throw new ArgumentNullException("motions");
            }
            if (needs == null)
            {
                throw new ArgumentNullException("needs");
            }
            if (characterProfile == null)
            {
                throw new ArgumentNullException("characterProfile");
            }

            _window = window;
            _motions = motions;
            _needs = needs;
            _careUiAssets = careUiAssets;
            _camera = camera;
            _characterProfile = characterProfile;
            _gameRegion = gameRegion;
            _gameRoot = gameRoot ?? string.Empty;
            _mouthTransform = FindDescendant(characterRoot, "M_Mouth");
            if (_mouthTransform != null)
            {
                _mouthRenderer = _mouthTransform.GetComponent<Renderer>();
            }
            _requestCharacterChange = requestCharacterChange;
            _requestGameInstallChange = requestGameInstallChange;
            _requestGameFilesReload = requestGameFilesReload;
            _motions.FeedBiteStarted += HandleFeedBiteStarted;
            _motions.FeedBiteCommitted += HandleFeedBiteCommitted;
            _motions.FeedResponseCompleted += HandleFeedResponseCompleted;
            CreateMenuTextures();
            _initialized = true;
        }

        public void OpenMenuForSmokeTest()
        {
            if (!_initialized)
            {
                return;
            }

            OpenMenu();
        }

        public void CloseMenuForSmokeTest()
        {
            if (!_initialized)
            {
                return;
            }

            CloseMenu();
        }

        public void OpenSettingsForSmokeTest()
        {
            if (!_initialized)
            {
                return;
            }

            OpenMenu();
            _menuPage = MenuPage.Settings;
        }

        public void OpenCarrotFeedForSmokeTest()
        {
            if (!_initialized)
            {
                return;
            }

            OpenMenu();
            BeginCarrotFeed();
        }

        public void OpenCarrotEatingForSmokeTest()
        {
            if (!_initialized)
            {
                return;
            }

            OpenMenu();
            BeginCarrotFeed();
            if (_carrotFeedPhase != CarrotFeedPhase.Ready)
            {
                return;
            }

            StartCarrotResponse(GetPetMouthPoint(GetPetFeedTarget()));
        }

        private void Update()
        {
            if (!_initialized)
            {
                return;
            }

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (_carrotFeedPhase == CarrotFeedPhase.Reacting)
                {
                    return;
                }
                if (_carrotFeedPhase != CarrotFeedPhase.None)
                {
                    CancelCarrotFeed(true);
                }
                else if (_showMenu)
                {
                    CloseMenu();
                }
                else
                {
                    Quit();
                }
                return;
            }

            if (Input.GetMouseButtonDown(1))
            {
                if (_carrotFeedPhase == CarrotFeedPhase.Reacting)
                {
                    return;
                }
                if (_carrotFeedPhase != CarrotFeedPhase.None)
                {
                    CancelCarrotFeed(true);
                }
                else if (_showMenu)
                {
                    CloseMenu();
                }
                else
                {
                    OpenMenu();
                }
            }

            if (_carrotFeedPhase == CarrotFeedPhase.Reacting)
            {
                return;
            }

            if (_showMenu)
            {
                return;
            }

            if (Input.GetMouseButtonDown(0))
            {
                _pressActive = true;
                _dragging = false;
                _holdConsumed = false;
                _pressStartedAt = Time.unscaledTime;
                _pressPointerPosition = GetPointerPosition();
            }

            if (_pressActive && Input.GetMouseButton(0))
            {
                Vector2 pointer = GetPointerPosition();
                float distance = Vector2.Distance(pointer, _pressPointerPosition);
                float duration = Time.unscaledTime - _pressStartedAt;

                if (!_dragging && !_holdConsumed &&
                    distance > DragThresholdPixels)
                {
                    _dragging = _window.BeginDrag();
                    if (_dragging)
                    {
                        _motions.BeginDragReaction();
                    }
                }
                else if (!_dragging && !_holdConsumed &&
                    duration >= HoldToPatSeconds)
                {
                    _holdConsumed = true;
                    TryPat();
                }
            }

            if (_pressActive &&
                (Input.GetMouseButtonUp(0) || !Input.GetMouseButton(0)))
            {
                Vector2 releasedAt = GetPointerPosition();
                float distance = Vector2.Distance(
                    releasedAt,
                    _pressPointerPosition);
                float duration = Time.unscaledTime - _pressStartedAt;
                if (_dragging)
                {
                    _window.EndDrag();
                    _motions.EndDragReaction();
                }
                _pressActive = false;
                _dragging = false;

                if (!_holdConsumed &&
                    distance <= DragThresholdPixels &&
                    duration <= MaximumTapSeconds)
                {
                    if (_motions.TriggerTapReaction())
                    {
                        _needs.RecordTapReaction();
                    }
                    else
                    {
                        ShowToast(PetName + " is busy right now.");
                    }
                }
            }
        }

        private void OnGUI()
        {
            if (!_showMenu)
            {
                DrawActiveCarrot();
                DrawToast();
                return;
            }
            if (_window != null && !_window.IsSidePanelRenderReady)
            {
                return;
            }

            float x = Mathf.Clamp(
                _menuPosition.x,
                8.0f,
                Mathf.Max(8.0f, Screen.width - MenuWidth - 8.0f));
            float y = Mathf.Clamp(
                _menuPosition.y,
                8.0f,
                Mathf.Max(8.0f, Screen.height - MenuHeight - 8.0f));
            _menuRect = new Rect(x, y, MenuWidth, MenuHeight);

            DrawTexture(_menuRect, _menuPanelTexture);
            var nameStyle = new GUIStyle(GUI.skin.label)
            {
                fontStyle = FontStyle.Bold,
                fontSize = 17
            };
            nameStyle.normal.textColor = GameTextColor;
            if (_menuPage == MenuPage.Settings)
            {
                DrawSettingsPage(x, y, nameStyle);
                HandleOutsideMenuClick();
                DrawToast();
                return;
            }
            GUI.Label(
                new Rect(x + 16.0f, y + 10.0f, MenuWidth - 32.0f, 25.0f),
                PetName,
                nameStyle);

            DrawStatusStrip(
                new Rect(x + 10.0f, y + 40.0f, MenuWidth - 20.0f, 64.0f),
                _needs.Energy);

            if (_carrotFeedPhase != CarrotFeedPhase.None)
            {
                DrawCarrotFeedMode(x, y);
                DrawToast();
                return;
            }

            string patLabel = _needs.CanPat
                ? "Pat " + PetName
                : "Pat (" + FormatCooldown(_needs.PatCooldownRemainingSeconds) + ")";
            if (DrawGameButton(
                new Rect(x + 14.0f, y + 112.0f, MenuWidth - 28.0f, 32.0f),
                patLabel,
                true))
            {
                TryPat();
            }

            string feedLabel = _needs.CanFeed
                ? "Feed a carrot"
                : "Carrot (" + FormatCooldown(_needs.FeedCooldownRemainingSeconds) + ")";
            if (DrawGameButton(
                new Rect(x + 14.0f, y + 150.0f, MenuWidth - 28.0f, 32.0f),
                feedLabel,
                true))
            {
                BeginCarrotFeed();
            }

            bool quietMode = DrawGameToggle(
                new Rect(x + 18.0f, y + 194.0f, MenuWidth - 36.0f, 22.0f),
                _needs.QuietMode,
                "Quiet mode (no greetings)");
            if (quietMode != _needs.QuietMode)
            {
                _needs.SetQuietMode(quietMode);
                ShowToast(quietMode ? "Quiet mode is on." : "Quiet mode is off.");
            }

            var helpStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = GameTextColor }
            };
            GUI.Label(
                new Rect(x + 14.0f, y + 222.0f, MenuWidth - 28.0f, 42.0f),
                "Click: react     Hold: pat\nDrag: move " + PetName,
                helpStyle);

            if (DrawGameButton(
                new Rect(x + 14.0f, y + 276.0f, MenuWidth - 28.0f, 24.0f),
                "Settings...",
                false))
            {
                _menuPage = MenuPage.Settings;
            }
            if (DrawGameButton(
                new Rect(x + 14.0f, y + 306.0f, MenuWidth - 28.0f, 24.0f),
                "Close menu",
                false))
            {
                CloseMenu();
            }
            if (DrawGameButton(
                new Rect(x + 14.0f, y + 336.0f, MenuWidth - 28.0f, 24.0f),
                "Quit desktop pet",
                false))
            {
                Quit();
            }

            HandleOutsideMenuClick();

            DrawToast();
        }

        private void DrawSettingsPage(float x, float y, GUIStyle headingStyle)
        {
            GUI.Label(
                new Rect(x + 16.0f, y + 10.0f, MenuWidth - 32.0f, 25.0f),
                "Settings",
                headingStyle);

            var sectionStyle = new GUIStyle(GUI.skin.label)
            {
                fontStyle = FontStyle.Bold,
                fontSize = 13
            };
            sectionStyle.normal.textColor = GameTextColor;
            GUI.Label(
                new Rect(x + 16.0f, y + 40.0f, MenuWidth - 32.0f, 20.0f),
                "Desktop Uma",
                sectionStyle);

            Rect characterArea = new Rect(
                x + 14.0f,
                y + 62.0f,
                MenuWidth - 28.0f,
                40.0f);
            Rect characterContent = new Rect(
                0.0f,
                0.0f,
                characterArea.width - 16.0f,
                Mathf.Max(36.0f, PetCharacterCatalog.Selectable.Count * 36.0f));
            _characterScroll = GUI.BeginScrollView(
                characterArea,
                _characterScroll,
                characterContent);
            for (int index = 0; index < PetCharacterCatalog.Selectable.Count; index++)
            {
                PetCharacterProfile profile = PetCharacterCatalog.Selectable[index];
                bool selected = _characterProfile != null &&
                    string.Equals(
                        profile.Key,
                        _characterProfile.Key,
                        StringComparison.Ordinal);
                string label = selected
                    ? profile.DisplayName + "  (Selected)"
                    : profile.DisplayName;
                if (DrawGameButton(
                    new Rect(0.0f, index * 36.0f, characterContent.width, 32.0f),
                    label,
                    selected) &&
                    !selected &&
                    _requestCharacterChange != null)
                {
                    GUI.EndScrollView();
                    CloseMenu();
                    _requestCharacterChange(profile.Key);
                    return;
                }
            }
            GUI.EndScrollView();

            var noteStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                alignment = TextAnchor.UpperLeft,
                wordWrap = true
            };
            noteStyle.normal.textColor = GameTextColor;
            GUI.Label(
                new Rect(x + 16.0f, y + 105.0f, MenuWidth - 32.0f, 32.0f),
                "Only Oguri is supported in this build.",
                noteStyle);

            GUI.Label(
                new Rect(x + 16.0f, y + 141.0f, MenuWidth - 32.0f, 20.0f),
                "Game installation",
                sectionStyle);
            Rect installCard = new Rect(
                x + 10.0f,
                y + 163.0f,
                MenuWidth - 20.0f,
                58.0f);
            DrawTexture(installCard, _statusPanelTexture);

            var installTitleStyle = new GUIStyle(GUI.skin.label)
            {
                fontStyle = FontStyle.Bold,
                fontSize = 12
            };
            installTitleStyle.normal.textColor = GameTextColor;
            GUI.Label(
                new Rect(
                    installCard.x + 10.0f,
                    installCard.y + 6.0f,
                    installCard.width - 20.0f,
                    20.0f),
                (_gameRegion == GameRegion.Japan ? "JP" : "Global") +
                    " game files",
                installTitleStyle);

            var pathStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 10,
                clipping = TextClipping.Clip
            };
            pathStyle.normal.textColor = GameTextColor;
            Rect pathRect = new Rect(
                installCard.x + 10.0f,
                installCard.y + 29.0f,
                installCard.width - 20.0f,
                20.0f);
            GUI.Label(
                pathRect,
                new GUIContent(
                    ShortenToWidth(_gameRoot, pathRect.width, pathStyle),
                    _gameRoot),
                pathStyle);

            if (DrawGameButton(
                new Rect(x + 14.0f, y + 229.0f, MenuWidth - 28.0f, 28.0f),
                "Change game files...",
                false))
            {
                CloseMenu();
                if (_requestGameInstallChange != null)
                {
                    _requestGameInstallChange();
                }
                return;
            }
            if (DrawGameButton(
                new Rect(x + 14.0f, y + 263.0f, MenuWidth - 28.0f, 28.0f),
                "Reload game files",
                false))
            {
                CloseMenu();
                if (_requestGameFilesReload != null)
                {
                    _requestGameFilesReload();
                }
                return;
            }
            if (DrawGameButton(
                new Rect(x + 14.0f, y + 299.0f, MenuWidth - 28.0f, 24.0f),
                "Back",
                false))
            {
                _menuPage = MenuPage.Main;
            }
            if (DrawGameButton(
                new Rect(x + 14.0f, y + 331.0f, MenuWidth - 28.0f, 24.0f),
                "Close menu",
                false))
            {
                CloseMenu();
            }
        }

        private void HandleOutsideMenuClick()
        {
            Event current = Event.current;
            if (current != null &&
                current.type == EventType.MouseDown &&
                current.button == 0 &&
                !_menuRect.Contains(current.mousePosition))
            {
                CloseMenu();
                current.Use();
            }
        }

        private static string ShortenToWidth(
            string value,
            float maxWidth,
            GUIStyle style)
        {
            if (string.IsNullOrEmpty(value) ||
                style.CalcSize(new GUIContent(value)).x <= maxWidth)
            {
                return value;
            }

            const string Ellipsis = "...";
            string best = Ellipsis;
            int low = 0;
            int high = value.Length;
            while (low <= high)
            {
                int kept = (low + high) / 2;
                int prefix = (kept + 1) / 2;
                int suffix = kept / 2;
                string candidate = value.Substring(0, prefix) +
                    Ellipsis +
                    value.Substring(value.Length - suffix, suffix);
                if (style.CalcSize(new GUIContent(candidate)).x <= maxWidth)
                {
                    best = candidate;
                    low = kept + 1;
                }
                else
                {
                    high = kept - 1;
                }
            }
            return best;
        }

        private void TryPat()
        {
            if (!_needs.CanPat)
            {
                ShowToast(
                    "Pat again in " +
                    FormatCooldown(_needs.PatCooldownRemainingSeconds) + ".");
                return;
            }
            if (!_motions.TriggerPatHappy())
            {
                ShowToast(PetName + " is busy right now.");
                return;
            }
            if (_needs.TryPat())
            {
                ShowToast(PetName + " looks happy!");
            }
        }

        private void BeginCarrotFeed()
        {
            if (_carrotFeedPhase != CarrotFeedPhase.None)
            {
                return;
            }
            if (!_needs.CanFeed)
            {
                ShowToast(
                    "Another carrot in " +
                    FormatCooldown(_needs.FeedCooldownRemainingSeconds) + ".");
                return;
            }
            if (_motions.IsBusy)
            {
                ShowToast(PetName + " is busy right now.");
                return;
            }

            if (_carrotTexture == null)
            {
                Texture installedCarrot;
                if (_careUiAssets != null &&
                    _careUiAssets.TryGetCarrotTexture(out installedCarrot))
                {
                    _carrotTexture = installedCarrot;
                }
                else
                {
                    _fallbackCarrotTexture = ProceduralCarrotTexture.Create();
                    _carrotTexture = _fallbackCarrotTexture;
                }
            }
            _carrotFeedPhase = CarrotFeedPhase.Ready;
            _carrotHovering = false;
            _carrotVisible = false;
            _feedApplied = false;
            _carrotBiteStartedAt = -1.0f;
            ShowToast("Drag the carrot to " + PetName + ".");
        }

        private void DrawCarrotFeedMode(float x, float y)
        {
            Rect card = new Rect(
                x + 14.0f,
                y + 112.0f,
                MenuWidth - 28.0f,
                158.0f);
            DrawTexture(card, _carrotCardTexture);

            var titleStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.UpperCenter,
                fontStyle = FontStyle.Bold,
                fontSize = 14
            };
            titleStyle.normal.textColor = GameTextColor;
            GUI.Label(
                new Rect(card.x + 10.0f, card.y + 9.0f, card.width - 20.0f, 24.0f),
                "Drag the carrot to " + PetName,
                titleStyle);

            _carrotPickupRect = new Rect(
                card.center.x - CarrotSize * 0.5f,
                card.y + 48.0f,
                CarrotSize,
                CarrotSize);

            Event current = Event.current;
            Vector2 pointer = current != null
                ? current.mousePosition
                : new Vector2(
                    Input.mousePosition.x,
                    Screen.height - Input.mousePosition.y);
            Rect petTarget = GetPetFeedTarget();

            if (_carrotFeedPhase == CarrotFeedPhase.Ready &&
                current != null && current.type == EventType.MouseDown &&
                current.button == 0 && _carrotPickupRect.Contains(pointer))
            {
                _carrotFeedPhase = CarrotFeedPhase.Dragging;
                current.Use();
            }

            if (_carrotFeedPhase == CarrotFeedPhase.Dragging)
            {
                SetCarrotHovering(petTarget.Contains(pointer));
                DrawPetFeedTarget(petTarget, _carrotHovering);

                if (current != null && current.type == EventType.MouseDrag &&
                    current.button == 0)
                {
                    current.Use();
                }
                else if (current != null &&
                    current.type == EventType.MouseUp && current.button == 0)
                {
                    if (_carrotHovering)
                    {
                        StartCarrotResponse(pointer);
                    }
                    else
                    {
                        SetCarrotHovering(false);
                        _carrotFeedPhase = CarrotFeedPhase.Ready;
                        ShowToast("Bring it over to " + PetName + ".");
                    }
                    current.Use();
                }
            }

            Rect carrotArea = _carrotPickupRect;
            if (_carrotFeedPhase == CarrotFeedPhase.Dragging)
            {
                carrotArea = new Rect(
                    pointer.x - CarrotSize * 0.5f,
                    pointer.y - CarrotSize * 0.5f,
                    CarrotSize,
                    CarrotSize);
            }
            DrawCarrotTexture(carrotArea);

            var helpStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 12
            };
            helpStyle.normal.textColor = GameTextColor;
            string help = _carrotFeedPhase == CarrotFeedPhase.Ready
                ? "Pick it up"
                : _carrotHovering
                    ? "Release to feed"
                    : "Carry it over to her";
            GUI.Label(
                new Rect(card.x + 10.0f, card.y + 130.0f, card.width - 20.0f, 22.0f),
                help,
                helpStyle);

            if (DrawGameButton(
                    new Rect(x + 14.0f, y + 306.0f, MenuWidth - 28.0f, 24.0f),
                    "Cancel",
                    false))
            {
                CancelCarrotFeed(false);
            }
        }

        private void SetCarrotHovering(bool hovering)
        {
            _carrotHovering = hovering;
        }

        private bool StartCarrotResponse(Vector2 releasePosition)
        {
            if (_carrotFeedPhase != CarrotFeedPhase.Ready &&
                _carrotFeedPhase != CarrotFeedPhase.Dragging)
            {
                return false;
            }

            if (!_motions.TriggerFeedResponse())
            {
                _carrotFeedPhase = CarrotFeedPhase.Ready;
                _carrotHovering = false;
                ShowToast(PetName + " is busy right now.");
                return false;
            }

            _carrotFeedPhase = CarrotFeedPhase.Reacting;
            _carrotHovering = false;
            _carrotVisible = true;
            _feedApplied = false;
            _carrotReleasePosition = releasePosition;
            _carrotDeliveryStartedAt = Time.unscaledTime;
            _carrotBiteStartedAt = -1.0f;
            _showMenu = false;
            _window.SetSidePanelVisible(false, MenuSidecarWidth);
            Debug.Log(
                "Carrot feed accepted; prop is visible while " + PetName +
                " approaches the bite.");
            return true;
        }

        private void HandleFeedBiteStarted()
        {
            if (_carrotFeedPhase != CarrotFeedPhase.Reacting)
            {
                return;
            }

            _carrotVisible = true;
            _carrotDeliveryStartedAt =
                Time.unscaledTime - CarrotDeliverySeconds;
            _carrotBiteStartedAt = Time.unscaledTime;
            Debug.Log("Carrot bite started; Eating face and visible prop are synchronized.");
        }

        private void HandleFeedBiteCommitted()
        {
            if (_carrotFeedPhase != CarrotFeedPhase.Reacting)
            {
                return;
            }

            _carrotVisible = false;
            if (_feedApplied)
            {
                return;
            }

            _feedApplied = true;
            bool applied = _needs.TryFeed();
            ShowToast(
                applied
                    ? PetName + " got the carrot!"
                    : PetName + " already had a carrot.");
            Debug.Log(
                "Carrot bite committed; prop hidden and care state applied=" +
                applied + ".");
        }

        private void HandleFeedResponseCompleted()
        {
            if (_carrotFeedPhase != CarrotFeedPhase.Reacting)
            {
                return;
            }

            _carrotVisible = false;
            _carrotFeedPhase = CarrotFeedPhase.None;
            _carrotBiteStartedAt = -1.0f;
            Debug.Log("Carrot feed response completed.");
        }

        private void CancelCarrotFeed(bool showToast)
        {
            if (_carrotFeedPhase == CarrotFeedPhase.None ||
                _carrotFeedPhase == CarrotFeedPhase.Reacting)
            {
                return;
            }

            _carrotFeedPhase = CarrotFeedPhase.None;
            _carrotHovering = false;
            _carrotVisible = false;
            _feedApplied = false;
            _carrotBiteStartedAt = -1.0f;
            if (showToast)
            {
                ShowToast("Carrot put away.");
            }
        }

        private static Rect GetPetFeedTarget()
        {
            return new Rect(
                Mathf.Max(0.0f, Screen.width - 310.0f),
                40.0f,
                245.0f,
                Mathf.Max(240.0f, Screen.height - 74.0f));
        }

        private Vector2 GetPetMouthPoint(Rect petTarget)
        {
            if (_hasMouthGuiPosition)
            {
                return _mouthGuiPosition;
            }

            return new Vector2(
                petTarget.center.x,
                petTarget.y + petTarget.height * 0.55f);
        }

        private void DrawActiveCarrot()
        {
            if (_carrotFeedPhase != CarrotFeedPhase.Reacting ||
                !_carrotVisible || _carrotTexture == null)
            {
                return;
            }

            Vector2 mouth =
                GetPetMouthPoint(GetPetFeedTarget()) + CarrotMouthOffset;
            float deliveryProgress = Mathf.Clamp01(
                (Time.unscaledTime - _carrotDeliveryStartedAt) /
                CarrotDeliverySeconds);
            deliveryProgress = deliveryProgress * deliveryProgress *
                (3.0f - 2.0f * deliveryProgress);
            Vector2 center = Vector2.Lerp(
                _carrotReleasePosition,
                mouth,
                deliveryProgress);
            float size = Mathf.Lerp(
                CarrotSize,
                CarrotMouthSize,
                deliveryProgress);
            if (_carrotBiteStartedAt >= 0.0f)
            {
                float biteProgress = Mathf.Clamp01(
                    (Time.unscaledTime - _carrotBiteStartedAt) /
                    CarrotBiteShrinkSeconds);
                size = Mathf.Lerp(CarrotMouthSize, CarrotBiteSize, biteProgress);
            }

            DrawCarrotTexture(new Rect(
                center.x - size * 0.5f,
                center.y - size * 0.5f,
                size,
                size));
        }

        private void DrawCarrotTexture(Rect area)
        {
            if (_carrotTexture == null)
            {
                return;
            }

            Color previous = GUI.color;
            GUI.color = Color.white;
            GUI.DrawTexture(
                area,
                _carrotTexture,
                ScaleMode.ScaleToFit,
                true);
            GUI.color = previous;
        }

        private static void DrawPetFeedTarget(Rect area, bool active)
        {
            Color previous = GUI.color;
            GUI.color = active
                ? new Color(1.0f, 0.80f, 0.18f, 0.92f)
                : new Color(1.0f, 1.0f, 1.0f, 0.45f);
            const float border = 3.0f;
            GUI.DrawTexture(
                new Rect(area.x, area.y, area.width, border),
                Texture2D.whiteTexture);
            GUI.DrawTexture(
                new Rect(area.x, area.yMax - border, area.width, border),
                Texture2D.whiteTexture);
            GUI.DrawTexture(
                new Rect(area.x, area.y, border, area.height),
                Texture2D.whiteTexture);
            GUI.DrawTexture(
                new Rect(area.xMax - border, area.y, border, area.height),
                Texture2D.whiteTexture);
            GUI.color = previous;

            var style = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                fontSize = 12
            };
            style.normal.textColor = active
                ? new Color(1.0f, 0.78f, 0.08f, 1.0f)
                : Color.white;
            GUI.Label(
                new Rect(area.x, area.y + 5.0f, area.width, 24.0f),
                active ? "Release here" : "Bring it here",
                style);
        }

        private void ShowToast(string message)
        {
            _toast = message;
            _hideToastAt = Time.unscaledTime + ToastSeconds;
        }

        private void DrawToast()
        {
            if (string.IsNullOrEmpty(_toast) || Time.unscaledTime >= _hideToastAt)
            {
                return;
            }

            const float width = 220.0f;
            const float height = 34.0f;
            float x = DesktopWindowController.SidePanelWidth +
                (DesktopWindowController.PetViewportWidth - width) * 0.5f;
            float y = Screen.height - height - 16.0f;
            Rect area = new Rect(x, y, width, height);
            DrawDarkPanel(area);
            GUI.Box(area, GUIContent.none);
            var style = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter
            };
            GUI.Label(area, _toast, style);
        }

        private static void DrawDarkPanel(Rect area)
        {
            Color previous = GUI.color;
            GUI.color = new Color(0.035f, 0.035f, 0.045f, 0.96f);
            GUI.DrawTexture(area, Texture2D.whiteTexture, ScaleMode.StretchToFill);
            GUI.color = previous;
        }

        private void DrawStatusStrip(
            Rect area,
            float energy)
        {
            Rect backing = new Rect(
                area.x,
                area.y + 11.0f,
                204.0f,
                42.0f);
            DrawTexture(backing, _statusPanelTexture);

            var energyLabelStyle = new GUIStyle(GUI.skin.label)
            {
                fontStyle = FontStyle.Bold,
                fontSize = 12,
                alignment = TextAnchor.MiddleLeft
            };
            energyLabelStyle.normal.textColor = GameTextColor;
            GUI.Label(
                new Rect(backing.x + 10.0f, backing.y, 50.0f, backing.height),
                "Energy",
                energyLabelStyle);

            DrawEnergyGauge(
                new Rect(
                    backing.x + 58.0f,
                    backing.y + 7.0f,
                    backing.width - 64.0f,
                    28.0f),
                energy);

            DrawMoodIndicator(
                new Rect(
                    backing.x + backing.width + 8.0f,
                    area.y + 17.0f,
                    76.0f,
                    30.0f));
        }

        private void DrawMoodIndicator(Rect area)
        {
            Texture texture;
            if (_careUiAssets != null &&
                _careUiAssets.TryGetMoodTexture(_needs.Mood, out texture))
            {
                Color previousColor = GUI.color;
                Color previousContentColor = GUI.contentColor;
                GUI.color = Color.white;
                GUI.contentColor = Color.white;
                GUI.DrawTexture(
                    area,
                    texture,
                    ScaleMode.StretchToFill,
                    true);
                GUI.color = previousColor;
                GUI.contentColor = previousContentColor;
                return;
            }

            var fallbackStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold
            };
            fallbackStyle.normal.textColor = GameTextColor;
            GUI.Label(
                area,
                _needs.MoodLabel,
                fallbackStyle);
        }

        private void DrawEnergyGauge(
            Rect area,
            float energy)
        {
            float value = Mathf.Clamp(energy, 0.0f, 100.0f);
            DrawTexture(area, _energyFrameTexture);

            Rect track = new Rect(
                area.x + 5.0f,
                area.y + 5.0f,
                area.width - 10.0f,
                area.height - 10.0f);
            DrawTexture(track, _energyTrackTexture);

            if (value <= 0.0f)
            {
                return;
            }

            float fillWidth = track.width * value / 100.0f;
            Rect clippedArea = new Rect(
                track.x,
                track.y,
                fillWidth,
                track.height);
            GUI.BeginGroup(clippedArea);
            DrawTexture(
                new Rect(0.0f, 0.0f, track.width, track.height),
                _energyGradientTexture);
            GUI.EndGroup();
        }

        private bool DrawGameButton(Rect area, string label, bool primary)
        {
            Event current = Event.current;
            bool hovered = current != null && area.Contains(current.mousePosition);
            Texture2D background = primary
                ? hovered
                    ? _primaryButtonHoverTexture
                    : _primaryButtonTexture
                : hovered
                    ? _secondaryButtonHoverTexture
                    : _secondaryButtonTexture;
            DrawTexture(area, background);

            var labelStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                fontSize = 12
            };
            labelStyle.normal.textColor = primary ? Color.white : GameTextColor;
            GUI.Label(area, label, labelStyle);
            return GUI.Button(area, GUIContent.none, GUIStyle.none);
        }

        private bool DrawGameToggle(Rect area, bool value, string label)
        {
            Rect box = new Rect(area.x, area.y + 2.0f, 18.0f, 18.0f);
            DrawTexture(box, value ? _toggleOnTexture : _toggleOffTexture);

            var labelStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleLeft,
                fontSize = 12
            };
            labelStyle.normal.textColor = GameTextColor;
            GUI.Label(
                new Rect(area.x + 25.0f, area.y, area.width - 25.0f, area.height),
                label,
                labelStyle);

            return GUI.Button(area, GUIContent.none, GUIStyle.none)
                ? !value
                : value;
        }

        private static void DrawTexture(Rect area, Texture texture)
        {
            if (texture == null)
            {
                return;
            }

            Color previous = GUI.color;
            GUI.color = Color.white;
            GUI.DrawTexture(
                area,
                texture,
                ScaleMode.StretchToFill,
                true);
            GUI.color = previous;
        }

        private void CreateMenuTextures()
        {
            if (_menuPanelTexture != null)
            {
                return;
            }

            _menuPanelTexture = CreateRoundedRectTexture(
                308,
                338,
                12.0f,
                new Color(0.91f, 0.97f, 1.0f, 0.98f),
                new Color(0.36f, 0.48f, 0.60f, 1.0f),
                2);
            _statusPanelTexture = CreateRoundedRectTexture(
                204,
                42,
                20.0f,
                new Color(0.99f, 0.98f, 0.94f, 1.0f),
                new Color(0.70f, 0.66f, 0.61f, 1.0f),
                2);
            _energyFrameTexture = CreateRoundedRectTexture(
                142,
                28,
                14.0f,
                new Color(0.97f, 0.96f, 0.93f, 1.0f),
                new Color(0.40f, 0.35f, 0.31f, 1.0f),
                2);
            _energyTrackTexture = CreateRoundedRectTexture(
                132,
                18,
                9.0f,
                new Color(0.49f, 0.49f, 0.49f, 1.0f),
                Color.clear,
                0);
            _energyGradientTexture = CreateEnergyGradientTexture(132, 18, 9.0f);
            _primaryButtonTexture = CreateRoundedRectTexture(
                280,
                32,
                16.0f,
                new Color(0.38f, 0.73f, 0.12f, 1.0f),
                new Color(0.23f, 0.47f, 0.06f, 1.0f),
                2);
            _primaryButtonHoverTexture = CreateRoundedRectTexture(
                280,
                32,
                16.0f,
                new Color(0.49f, 0.82f, 0.18f, 1.0f),
                new Color(0.23f, 0.47f, 0.06f, 1.0f),
                2);
            _secondaryButtonTexture = CreateRoundedRectTexture(
                280,
                24,
                12.0f,
                new Color(0.98f, 0.98f, 0.96f, 1.0f),
                new Color(0.44f, 0.43f, 0.42f, 1.0f),
                2);
            _secondaryButtonHoverTexture = CreateRoundedRectTexture(
                280,
                24,
                12.0f,
                new Color(1.0f, 0.94f, 0.77f, 1.0f),
                new Color(0.44f, 0.43f, 0.42f, 1.0f),
                2);
            _toggleOffTexture = CreateRoundedRectTexture(
                18,
                18,
                5.0f,
                Color.white,
                new Color(0.44f, 0.43f, 0.42f, 1.0f),
                2);
            _toggleOnTexture = CreateRoundedRectTexture(
                18,
                18,
                5.0f,
                new Color(0.38f, 0.73f, 0.12f, 1.0f),
                new Color(0.23f, 0.47f, 0.06f, 1.0f),
                2);
            _carrotCardTexture = CreateRoundedRectTexture(
                280,
                158,
                18.0f,
                new Color(1.0f, 0.97f, 0.86f, 1.0f),
                new Color(0.90f, 0.67f, 0.22f, 1.0f),
                2);
        }

        private static Texture2D CreateRoundedRectTexture(
            int width,
            int height,
            float radius,
            Color fill,
            Color border,
            int borderWidth)
        {
            var texture = new Texture2D(
                width,
                height,
                TextureFormat.RGBA32,
                false,
                false)
            {
                name = "Uma desktop pet rounded UI",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave
            };

            var pixels = new Color[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float pixelX = x + 0.5f;
                    float pixelY = y + 0.5f;
                    if (!IsInsideRoundedRect(
                        pixelX,
                        pixelY,
                        width,
                        height,
                        radius))
                    {
                        pixels[y * width + x] = Color.clear;
                        continue;
                    }

                    bool isBorder = borderWidth > 0 &&
                        !IsInsideRoundedRect(
                            pixelX - borderWidth,
                            pixelY - borderWidth,
                            width - borderWidth * 2,
                            height - borderWidth * 2,
                            Mathf.Max(0.0f, radius - borderWidth));
                    pixels[y * width + x] = isBorder ? border : fill;
                }
            }

            texture.SetPixels(pixels);
            texture.Apply(false, true);
            return texture;
        }

        private static Texture2D CreateEnergyGradientTexture(
            int width,
            int height,
            float radius)
        {
            var texture = new Texture2D(
                width,
                height,
                TextureFormat.RGBA32,
                false,
                false)
            {
                name = "Uma desktop pet Energy gradient",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave
            };

            var pixels = new Color[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float pixelX = x + 0.5f;
                    float pixelY = y + 0.5f;
                    if (!IsInsideRoundedRect(
                        pixelX,
                        pixelY,
                        width,
                        height,
                        radius))
                    {
                        pixels[y * width + x] = Color.clear;
                        continue;
                    }

                    float progress = width <= 1
                        ? 1.0f
                        : (float)x / (width - 1);
                    Color color = GetEnergyGradientColor(progress);
                    float highlight = Mathf.Lerp(
                        0.15f,
                        0.0f,
                        height <= 1 ? 1.0f : (float)y / (height - 1));
                    pixels[y * width + x] = Color.Lerp(
                        color,
                        Color.white,
                        highlight);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply(false, true);
            return texture;
        }

        private static Color GetEnergyGradientColor(float progress)
        {
            float value = Mathf.Clamp01(progress);
            Color cyan = new Color(0.20f, 0.78f, 0.95f, 1.0f);
            Color green = new Color(0.20f, 0.88f, 0.28f, 1.0f);
            Color yellow = new Color(0.96f, 0.88f, 0.10f, 1.0f);
            Color orange = new Color(1.0f, 0.56f, 0.10f, 1.0f);
            Color red = new Color(0.98f, 0.31f, 0.28f, 1.0f);

            if (value < 0.38f)
            {
                return Color.Lerp(cyan, green, value / 0.38f);
            }
            if (value < 0.64f)
            {
                return Color.Lerp(green, yellow, (value - 0.38f) / 0.26f);
            }
            if (value < 0.82f)
            {
                return Color.Lerp(yellow, orange, (value - 0.64f) / 0.18f);
            }
            return Color.Lerp(orange, red, (value - 0.82f) / 0.18f);
        }

        private static bool IsInsideRoundedRect(
            float x,
            float y,
            float width,
            float height,
            float radius)
        {
            if (width <= 0.0f || height <= 0.0f ||
                x < 0.0f || y < 0.0f || x > width || y > height)
            {
                return false;
            }

            float clampedRadius = Mathf.Clamp(
                radius,
                0.0f,
                Mathf.Min(width, height) * 0.5f);
            float nearestX = Mathf.Clamp(
                x,
                clampedRadius,
                width - clampedRadius);
            float nearestY = Mathf.Clamp(
                y,
                clampedRadius,
                height - clampedRadius);
            float deltaX = x - nearestX;
            float deltaY = y - nearestY;
            return deltaX * deltaX + deltaY * deltaY <=
                clampedRadius * clampedRadius;
        }

        private static string FormatCooldown(double seconds)
        {
            return Math.Max(1, (int)Math.Ceiling(seconds)) + "s";
        }

        private Vector2 GetPointerPosition()
        {
            if (_window != null && _window.IsSupported)
            {
                return _window.CursorPosition;
            }
            return Input.mousePosition;
        }

        private void LateUpdate()
        {
            _hasMouthGuiPosition = false;
            if (!_initialized || _camera == null || _mouthTransform == null)
            {
                return;
            }

            Vector3 worldPosition = _mouthRenderer != null
                ? _mouthRenderer.bounds.center
                : _mouthTransform.position;
            Vector3 screenPosition = _camera.WorldToScreenPoint(worldPosition);
            if (screenPosition.z <= 0.0f)
            {
                return;
            }

            _mouthGuiPosition = new Vector2(
                screenPosition.x,
                Screen.height - screenPosition.y);
            _hasMouthGuiPosition = true;
        }

        private static Transform FindDescendant(Transform root, string name)
        {
            if (root == null || string.IsNullOrEmpty(name))
            {
                return null;
            }

            Transform[] descendants = root.GetComponentsInChildren<Transform>(true);
            for (int index = 0; index < descendants.Length; index++)
            {
                Transform descendant = descendants[index];
                if (descendant != null && string.Equals(
                    descendant.name,
                    name,
                    StringComparison.Ordinal))
                {
                    return descendant;
                }
            }
            return null;
        }

        private void OnApplicationFocus(bool focused)
        {
            if (!focused)
            {
                CancelPress();
                if (_carrotFeedPhase != CarrotFeedPhase.Reacting)
                {
                    CancelCarrotFeed(false);
                }
            }
        }

        private void OnDisable()
        {
            CloseMenu();
            CancelPress();
            _carrotVisible = false;
        }

        private void OnDestroy()
        {
            if (_motions != null)
            {
                _motions.FeedBiteStarted -= HandleFeedBiteStarted;
                _motions.FeedBiteCommitted -= HandleFeedBiteCommitted;
                _motions.FeedResponseCompleted -= HandleFeedResponseCompleted;
            }
            DestroyMenuTexture(ref _menuPanelTexture);
            DestroyMenuTexture(ref _statusPanelTexture);
            DestroyMenuTexture(ref _energyFrameTexture);
            DestroyMenuTexture(ref _energyTrackTexture);
            DestroyMenuTexture(ref _energyGradientTexture);
            DestroyMenuTexture(ref _primaryButtonTexture);
            DestroyMenuTexture(ref _primaryButtonHoverTexture);
            DestroyMenuTexture(ref _secondaryButtonTexture);
            DestroyMenuTexture(ref _secondaryButtonHoverTexture);
            DestroyMenuTexture(ref _toggleOffTexture);
            DestroyMenuTexture(ref _toggleOnTexture);
            DestroyMenuTexture(ref _carrotCardTexture);
            ProceduralCarrotTexture.Destroy(_fallbackCarrotTexture);
            _fallbackCarrotTexture = null;
            _carrotTexture = null;
        }

        private static void DestroyMenuTexture(ref Texture2D texture)
        {
            if (texture == null)
            {
                return;
            }

            UnityEngine.Object.Destroy(texture);
            texture = null;
        }

        private void OpenMenu()
        {
            if (_window.IsSupported && !_window.IsReady)
            {
                return;
            }

            _window.SetSidePanelVisible(true, MenuSidecarWidth);
            _menuPosition = new Vector2(
                8.0f,
                Mathf.Max(8.0f, (Screen.height - MenuHeight) * 0.5f));
            _showMenu = true;
            _menuPage = MenuPage.Main;
            CancelPress();
        }

        private void CloseMenu()
        {
            if (!_showMenu)
            {
                return;
            }

            CancelCarrotFeed(false);
            _showMenu = false;
            _menuPage = MenuPage.Main;
            _window.SetSidePanelVisible(false, MenuSidecarWidth);
        }

        private void CancelPress()
        {
            bool wasDragging = _dragging;
            if (_window != null)
            {
                _window.EndDrag();
            }
            if (wasDragging && _motions != null)
            {
                _motions.EndDragReaction();
            }
            _pressActive = false;
            _dragging = false;
            _holdConsumed = false;
        }

        private static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private enum MenuPage
        {
            Main,
            Settings
        }

        private enum CarrotFeedPhase
        {
            None,
            Ready,
            Dragging,
            Reacting
        }
    }
}
