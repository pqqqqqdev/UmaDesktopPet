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
        private const float MenuWidth = PetSidePanelView.PanelWidth;
        private const float MenuHeight = PetSidePanelView.PanelHeight;
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
        private PetFocusState _focus;
        private PetStudyRewardService _studyRewards;
        private StudyDeskPresenter _studyDeskPresenter;
        private PetSidePanelView _sidePanelView;
        private InstalledCareUiAssets _careUiAssets;
        private InstalledShopUiAssets _shopUiAssets;
        private InstalledFoodUiAssets _foodUiAssets;
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
        private bool _dragUsesPetReaction;
        private bool _holdConsumed;
        private bool _showMenu;
        private MenuPage _menuPage;
        private PetPanelShopSection _shopSection;
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
        private bool _confirmStopStudy;
        private bool _playStudyCompletionHappy;
        private bool _recordingToolsEnabled;
        private bool _recordingToolsOpen;
        private bool _recordingAnimationsOpen;
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
        private PetFoodDefinition _selectedFood;

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
                    : "Pet";
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
                    IsCareInteractionActive ||
                    (_focus != null && _focus.IsSessionActive);
            }
        }

        public void Initialize(
            DesktopWindowController window,
            OguriPetAnimationController motions,
            PetNeedsState needs,
            PetFocusState focus,
            PetStudyRewardService studyRewards,
            StudyDeskPresenter studyDeskPresenter,
            InstalledCareUiAssets careUiAssets,
            InstalledShopUiAssets shopUiAssets,
            InstalledFoodUiAssets foodUiAssets,
            Camera camera,
            Transform characterRoot,
            PetCharacterProfile characterProfile,
            GameRegion gameRegion,
            string gameRoot,
            bool recordingToolsEnabled,
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
            if (focus == null)
            {
                throw new ArgumentNullException("focus");
            }
            if (studyRewards == null)
            {
                throw new ArgumentNullException("studyRewards");
            }
            if (studyDeskPresenter == null)
            {
                throw new ArgumentNullException("studyDeskPresenter");
            }

            _window = window;
            _motions = motions;
            _needs = needs;
            _focus = focus;
            _studyRewards = studyRewards;
            _studyDeskPresenter = studyDeskPresenter;
            _sidePanelView = new PetSidePanelView();
            _careUiAssets = careUiAssets;
            _shopUiAssets = shopUiAssets;
            _foodUiAssets = foodUiAssets;
            _camera = camera;
            _characterProfile = characterProfile;
            _gameRegion = gameRegion;
            _gameRoot = gameRoot ?? string.Empty;
            _recordingToolsEnabled = recordingToolsEnabled;
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
            _focus.SessionCompleted += HandleStudyCompleted;
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

        public void OpenRecordingTools()
        {
            if (!_initialized || !_recordingToolsEnabled)
            {
                return;
            }

            OpenMenu();
            _menuPage = MenuPage.Settings;
            _recordingToolsOpen = true;
        }

        public void OpenStudyForSmokeTest()
        {
            if (!_initialized)
            {
                return;
            }

            OpenMenu();
            _menuPage = MenuPage.Study;
        }

        public void OpenShopForSmokeTest()
        {
            if (!_initialized)
            {
                return;
            }

            OpenMenu();
            _menuPage = MenuPage.Shop;
            _shopSection = PetPanelShopSection.Shop;
        }

        public void OpenInventoryForSmokeTest()
        {
            if (!_initialized)
            {
                return;
            }

            OpenMenu();
            _menuPage = MenuPage.Shop;
            _shopSection = PetPanelShopSection.Inventory;
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

            if (_playStudyCompletionHappy && !_motions.IsBusy &&
                !_focus.IsSessionActive && !_pressActive && !_dragging &&
                !IsCareInteractionActive && !Input.GetMouseButton(0))
            {
                _playStudyCompletionHappy = false;
                _motions.TriggerAmbientHappy();
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
                float dragThreshold =
                    DesktopWindowLayout.LogicalLengthToPhysical(
                        DragThresholdPixels);

                if (!_dragging && !_holdConsumed &&
                    distance > dragThreshold)
                {
                    _dragging = _window.BeginDrag();
                    if (_dragging)
                    {
                        _dragUsesPetReaction = !_focus.IsSessionActive &&
                            _motions.BeginDragReaction();
                        if (!_dragUsesPetReaction && !_focus.IsSessionActive)
                        {
                            ShowToast(PetName + " is busy right now.");
                        }
                    }
                }
                else if (!_dragging && !_holdConsumed &&
                    duration >= HoldToPatSeconds)
                {
                    _holdConsumed = true;
                    if (_focus.IsSessionActive)
                    {
                        ShowToast(PetName + " is studying.");
                    }
                    else
                    {
                        TryPat();
                    }
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
                float dragThreshold =
                    DesktopWindowLayout.LogicalLengthToPhysical(
                        DragThresholdPixels);
                if (_dragging)
                {
                    _window.EndDrag();
                    if (_dragUsesPetReaction)
                    {
                        _motions.EndDragReaction();
                    }
                }
                _pressActive = false;
                _dragging = false;
                _dragUsesPetReaction = false;

                if (!_holdConsumed &&
                    distance <= dragThreshold &&
                    duration <= MaximumTapSeconds)
                {
                    if (_focus.IsSessionActive)
                    {
                        ShowToast(PetName + " is studying.");
                    }
                    else if (_motions.TriggerTapReaction())
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
            Matrix4x4 previousMatrix = DesktopWindowLayout.BeginGui();
            try
            {
                DrawGui();
            }
            finally
            {
                DesktopWindowLayout.EndGui(previousMatrix);
            }
        }

        private void DrawGui()
        {
            if (!_showMenu)
            {
                DrawActiveCarrot();
                DrawFocusBadge();
                DrawToast();
                return;
            }
            if (_window != null && !_window.IsSidePanelRenderReady)
            {
                return;
            }

            _menuRect = new Rect(0.0f, 0.0f, MenuWidth, MenuHeight);
            if (_carrotFeedPhase != CarrotFeedPhase.None)
            {
                DrawTexture(_menuRect, _menuPanelTexture);
                DrawCarrotFeedMode(0.0f, 0.0f);
                DrawToast();
                return;
            }

            PetPanelCommand command = _sidePanelView.Draw(BuildPanelModel());
            HandlePanelCommand(command);
            HandleOutsideMenuClick();
            DrawToast();
        }

        private PetPanelModel BuildPanelModel()
        {
            Texture moodTexture = null;
            Texture moodAnimationFrameTexture = null;
            Texture moodAnimationArrowTexture = null;
            if (_careUiAssets != null)
            {
                _careUiAssets.TryGetMoodTexture(_needs.Mood, out moodTexture);
                _careUiAssets.TryGetMoodAnimationTextures(
                    _needs.Mood,
                    out moodAnimationFrameTexture,
                    out moodAnimationArrowTexture);
            }

            PetPanelDeskItemModel[] deskItems =
                new PetPanelDeskItemModel[DeskShopCatalog.Items.Count];
            string nextName = string.Empty;
            int nextCost = 0;
            Texture nextItemPreview = null;
            Texture equippedItemPreview = null;
            bool hasEquippedItem = false;
            for (int index = 0; index < DeskShopCatalog.Items.Count; index++)
            {
                DeskShopItem item = DeskShopCatalog.Items[index];
                bool owned = _focus.IsDeskItemOwned(item.Id);
                bool equipped = string.Equals(
                    _focus.EquippedDeskItemId,
                    item.Id,
                    StringComparison.Ordinal);
                Texture itemIcon = null;
                if (_shopUiAssets != null)
                {
                    _shopUiAssets.TryGetTexture(item.Id, out itemIcon);
                }
                deskItems[index] = new PetPanelDeskItemModel
                {
                    Id = item.Id,
                    Name = item.DisplayName,
                    Cost = item.Cost,
                    Owned = owned,
                    Equipped = equipped,
                    Available = _studyDeskPresenter.CanPresentDeskItem(item.Id),
                    IconTexture = itemIcon
                };
                if (!owned && string.IsNullOrEmpty(nextName))
                {
                    nextName = item.ShortName;
                    nextCost = item.Cost;
                    nextItemPreview = itemIcon;
                }
                if (equipped)
                {
                    hasEquippedItem = true;
                    equippedItemPreview = itemIcon;
                }
            }

            Texture deskPreview = hasEquippedItem
                ? equippedItemPreview
                : nextItemPreview;
            if (deskPreview == null && _careUiAssets != null)
            {
                _careUiAssets.TryGetCarrotTexture(out deskPreview);
            }

            PetPanelCharacterOption[] characterOptions =
                new PetPanelCharacterOption[PetCharacterCatalog.Selectable.Count];
            for (int index = 0; index < PetCharacterCatalog.Selectable.Count; index++)
            {
                PetCharacterProfile profile = PetCharacterCatalog.Selectable[index];
                characterOptions[index] = new PetPanelCharacterOption
                {
                    Key = profile.Key,
                    DisplayName = profile.DisplayName,
                    Selected = string.Equals(
                        profile.Key,
                        _characterProfile.Key,
                        StringComparison.Ordinal)
                };
            }

            bool studyActive = _focus.IsSessionActive;
            int carrotJellyQuantity = _needs.GetFoodQuantity(
                FoodCatalog.CarrotJellyId);
            return new PetPanelModel
            {
                Page = ToPanelPage(_menuPage),
                Character = new PetPanelCharacterPresentation
                {
                    HeaderName = PetName,
                    Accent = _characterProfile.Theme.Accent,
                    AccentSoft = _characterProfile.Theme.AccentSoft,
                    Primary = _characterProfile.Theme.Primary,
                    PrimaryHover = _characterProfile.Theme.PrimaryHover
                },
                Moni = _focus.Moni,
                Energy = _needs.Energy,
                EnergyFillTexture = _energyGradientTexture,
                Mood = _needs.Mood,
                MoodLabel = _needs.MoodLabel,
                MoodTexture = moodTexture,
                MoodAnimationFrameTexture = moodAnimationFrameTexture,
                MoodAnimationArrowTexture = moodAnimationArrowTexture,
                CanPat = !studyActive && _needs.CanPat,
                PatLabel = studyActive
                    ? PetName + " is studying"
                    : _needs.CanPat
                        ? "Pat " + PetName
                        : "Pat (" + FormatCooldown(
                            _needs.PatCooldownRemainingSeconds) + ")",
                CanFeed = !studyActive &&
                    _needs.CanFeed &&
                    carrotJellyQuantity > 0,
                FeedLabel = studyActive
                    ? "Oguri is studying"
                    : carrotJellyQuantity <= 0
                        ? "Study to earn Carrot Jelly"
                        : _needs.CanFeed
                            ? "Feed Carrot Jelly  ·  " +
                                carrotJellyQuantity
                            : "Carrot Jelly (" + FormatCooldown(
                                _needs.FeedCooldownRemainingSeconds) + ")",
                QuietMode = _needs.QuietMode,
                FocusStatus = _focus.Status,
                SessionDurationSeconds = _focus.SessionDurationSeconds,
                RemainingSeconds = _focus.RemainingSeconds,
                PendingMoni = _focus.PendingMoni,
                PendingFoodQuantity = _studyRewards.PendingFoodQuantity,
                ConfirmStopStudy = _confirmStopStudy,
                ShopSection = _shopSection,
                DeskItems = deskItems,
                OwnedDeskItemCount = _focus.OwnedDeskItemCount,
                NextDeskItemName = nextName,
                NextDeskItemCost = nextCost,
                DeskPreviewTexture = deskPreview,
                CharacterOptions = characterOptions,
                GameRegionLabel = _gameRegion == GameRegion.Japan
                    ? "JP"
                    : "Global",
                GameRoot = _gameRoot,
                RecordingToolsEnabled = _recordingToolsEnabled,
                RecordingToolsOpen = _recordingToolsOpen,
#if UMA_RECORDING_TOOLS
                RecordingAnimationsOpen = _recordingAnimationsOpen,
                CanPlayRecordingAnimation = CanPlayRecordingAnimation(),
                RecordingAnimationStatus = GetRecordingAnimationStatus()
#endif
            };
        }

        private void HandlePanelCommand(PetPanelCommand command)
        {
            switch (command.Type)
            {
                case PetPanelCommandType.Close:
                    CloseMenu();
                    break;
                case PetPanelCommandType.Navigate:
                    MenuPage nextPage = ToMenuPage(command.Page);
                    _menuPage = nextPage;
                    _recordingToolsOpen = false;
#if UMA_RECORDING_TOOLS
                    _recordingAnimationsOpen = false;
#endif
                    _confirmStopStudy = false;
                    break;
                case PetPanelCommandType.OpenRecordingTools:
                    if (_recordingToolsEnabled)
                    {
                        _recordingToolsOpen = true;
#if UMA_RECORDING_TOOLS
                        _recordingAnimationsOpen = false;
#endif
                    }
                    break;
                case PetPanelCommandType.CloseRecordingTools:
                    _recordingToolsOpen = false;
#if UMA_RECORDING_TOOLS
                    _recordingAnimationsOpen = false;
#endif
                    break;
#if UMA_RECORDING_TOOLS
                case PetPanelCommandType.OpenRecordingAnimations:
                    if (CanUseRecordingTools())
                    {
                        _recordingAnimationsOpen = true;
                    }
                    break;
                case PetPanelCommandType.CloseRecordingAnimations:
                    _recordingAnimationsOpen = false;
                    break;
                case PetPanelCommandType.RecordingPlayAnimation:
                    if (Enum.IsDefined(
                        typeof(PetRecordingAnimation),
                        command.Number))
                    {
                        TryPlayRecordingAnimation(
                            (PetRecordingAnimation)command.Number);
                    }
                    break;
#endif
                case PetPanelCommandType.RecordingSetMood:
                    if (CanUseRecordingTools() &&
                        Enum.IsDefined(typeof(PetMood), command.Number) &&
                        _needs.SetMoodForRecording((PetMood)command.Number))
                    {
                        ShowToast(
                            "Mood set to " +
                            PetNeedsState.GetMoodLabel(
                                (PetMood)command.Number) + ".");
                    }
                    break;
                case PetPanelCommandType.RecordingSetStudyRemaining:
                    if (CanUseRecordingTools() &&
                        _focus.SetStudyRemainingForRecording(command.Number))
                    {
                        ShowToast(
                            "Study timer set to " +
                            FormatFocusTime(command.Number) + ".");
                    }
                    break;
                case PetPanelCommandType.RecordingCompleteStudy:
                    if (CanUseRecordingTools())
                    {
                        _focus.CompleteStudyForRecording();
                    }
                    break;
                case PetPanelCommandType.RecordingGiveMoni:
                    if (CanUseRecordingTools())
                    {
                        ShowToast(_focus.GrantMoniForRecording(command.Number)
                            ? "Added " + command.Number + " Moni."
                            : "Collect the pending Moni first.");
                    }
                    break;
                case PetPanelCommandType.RecordingResetDeskCollection:
                    if (CanUseRecordingTools() &&
                        _focus.ResetDeskCollectionForRecording())
                    {
                        ShowToast("Desk collection reset.");
                    }
                    break;
                case PetPanelCommandType.RecordingResetAll:
                    if (CanUseRecordingTools())
                    {
                        _playStudyCompletionHappy = false;
                        _confirmStopStudy = false;
                        _focus.ResetRecordingState();
                        _needs.ResetRecordingState();
                        ShowToast("Recording state reset.");
                    }
                    break;
                case PetPanelCommandType.Pat:
                    TryPat();
                    break;
                case PetPanelCommandType.Feed:
                    BeginCarrotFeed();
                    break;
                case PetPanelCommandType.StartStudy:
                    StartStudySession(command.Number);
                    break;
                case PetPanelCommandType.ToggleStudyPause:
                    if (_focus.Status == FocusSessionStatus.Running)
                    {
                        if (!_focus.PauseSession())
                        {
                            ShowToast("Couldn't save the paused timer.");
                        }
                    }
                    else if (_focus.Status == FocusSessionStatus.Paused &&
                        !_focus.ResumeSession())
                    {
                        ShowToast("Couldn't save the timer.");
                    }
                    break;
                case PetPanelCommandType.RequestStopStudy:
                    _confirmStopStudy = true;
                    break;
                case PetPanelCommandType.KeepStudying:
                    _confirmStopStudy = false;
                    break;
                case PetPanelCommandType.ConfirmStopStudy:
                    if (_focus.StopSession())
                    {
                        _confirmStopStudy = false;
                        ShowToast("Study session stopped.");
                    }
                    else
                    {
                        ShowToast("Couldn't save. The timer is still there.");
                    }
                    break;
                case PetPanelCommandType.CollectStudyReward:
                    ShowToast(_studyRewards.TryCollectReward()
                        ? "Moni collected!"
                        : "Couldn't save the rewards. Try again.");
                    break;
                case PetPanelCommandType.SelectShopSection:
                    if (Enum.IsDefined(
                        typeof(PetPanelShopSection),
                        command.Number))
                    {
                        _shopSection = (PetPanelShopSection)command.Number;
                    }
                    break;
                case PetPanelCommandType.PurchaseDeskItem:
                    HandleDeskItemPurchase(command.Value);
                    break;
                case PetPanelCommandType.EquipDeskItem:
                    HandleDeskItemEquip(command.Value);
                    break;
                case PetPanelCommandType.ClearDeskItem:
                    ShowToast(_focus.ClearEquippedDeskItem()
                        ? "Desk item put away."
                        : "Couldn't put that desk item away.");
                    break;
                case PetPanelCommandType.ToggleQuietMode:
                    _needs.SetQuietMode(!_needs.QuietMode);
                    ShowToast(_needs.QuietMode
                        ? "Quiet mode is on."
                        : "Quiet mode is off.");
                    break;
                case PetPanelCommandType.SelectCharacter:
                    if (_requestCharacterChange != null)
                    {
                        CloseMenu();
                        _requestCharacterChange(command.Value);
                    }
                    break;
                case PetPanelCommandType.ChangeGameFiles:
                    CloseMenu();
                    if (_requestGameInstallChange != null)
                    {
                        _requestGameInstallChange();
                    }
                    break;
                case PetPanelCommandType.ReloadGameFiles:
                    CloseMenu();
                    if (_requestGameFilesReload != null)
                    {
                        _requestGameFilesReload();
                    }
                    break;
                case PetPanelCommandType.Quit:
                    Quit();
                    break;
            }
        }

        private void HandleDeskItemPurchase(string itemId)
        {
            DeskShopItem item;
            if (!DeskShopCatalog.TryGet(itemId, out item))
            {
                ShowToast("That desk item isn't available.");
                return;
            }
            if (!_studyDeskPresenter.CanPresentDeskItem(item.Id))
            {
                ShowToast("That item isn't available with these game files.");
                return;
            }
            if (_focus.PurchaseDeskItem(item.Id))
            {
                ShowToast(item.ShortName + " unlocked!");
                return;
            }
            ShowToast(_focus.Moni < item.Cost
                ? "You need " + item.Cost + " Moni."
                : "Couldn't save the purchase. Try again.");
        }

        private void HandleDeskItemEquip(string itemId)
        {
            DeskShopItem item;
            if (!DeskShopCatalog.TryGet(itemId, out item) ||
                !_studyDeskPresenter.CanPresentDeskItem(item.Id))
            {
                ShowToast("That item isn't available with these game files.");
                return;
            }
            ShowToast(_focus.EquipDeskItem(item.Id)
                ? item.ShortName + " equipped."
                : "Couldn't equip that desk item.");
        }

        private bool CanUseRecordingTools()
        {
            return _recordingToolsEnabled &&
                _focus != null &&
                _needs != null &&
                _focus.IsRecordingMode &&
                _needs.IsRecordingMode;
        }

#if UMA_RECORDING_TOOLS
        private bool CanPlayRecordingAnimation()
        {
            return CanUseRecordingTools() &&
                _motions != null &&
                !_motions.IsBusy &&
                !_focus.IsSessionActive &&
                _carrotFeedPhase == CarrotFeedPhase.None &&
                !_pressActive &&
                !_dragging;
        }

        private string GetRecordingAnimationStatus()
        {
            if (_focus != null && _focus.IsSessionActive)
            {
                return "Study is active";
            }
            if (_carrotFeedPhase != CarrotFeedPhase.None)
            {
                return "Feeding is active";
            }
            if (_motions == null || !_motions.IsBusy)
            {
                return "Ready";
            }

            switch (_motions.CurrentAction)
            {
                case "TapReaction":
                    return "Playing · Tap";
                case "PatHappy":
                    return "Playing · Happy";
                case "FeedResponse":
                    return "Playing · Eating";
                case "AmbientGreeting":
                    return "Playing · Hello";
                case "Study":
                    return "Study is active";
                default:
                    return "Returning to idle";
            }
        }

        private bool TryPlayRecordingAnimation(PetRecordingAnimation animation)
        {
            if (!CanPlayRecordingAnimation())
            {
                return false;
            }

            bool started;
            switch (animation)
            {
                case PetRecordingAnimation.Tap:
                    started = _motions.TriggerTapReaction();
                    break;
                case PetRecordingAnimation.Happy:
                    started = _motions.TriggerAmbientHappy();
                    break;
                case PetRecordingAnimation.Eating:
                    // Feed callbacks are intentionally ignored because recording
                    // previews never enter the coordinated carrot-feed phase.
                    started = _motions.TriggerFeedResponse();
                    break;
                case PetRecordingAnimation.Hello:
                    started = _motions.TriggerAmbientGreeting();
                    break;
                default:
                    return false;
            }

            if (started)
            {
                _playStudyCompletionHappy = false;
                Debug.Log("Recording animation preview: " + animation + ".");
            }
            return started;
        }
#endif

        private static PetPanelPage ToPanelPage(MenuPage page)
        {
            switch (page)
            {
                case MenuPage.Study:
                    return PetPanelPage.Study;
                case MenuPage.Shop:
                    return PetPanelPage.Shop;
                case MenuPage.Settings:
                    return PetPanelPage.Settings;
                default:
                    return PetPanelPage.Home;
            }
        }

        private static MenuPage ToMenuPage(PetPanelPage page)
        {
            switch (page)
            {
                case PetPanelPage.Study:
                    return MenuPage.Study;
                case PetPanelPage.Shop:
                    return MenuPage.Shop;
                case PetPanelPage.Settings:
                    return MenuPage.Settings;
                default:
                    return MenuPage.Main;
            }
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

        private void DrawStudyPage(float x, float y, GUIStyle headingStyle)
        {
            GUI.Label(
                new Rect(x + 16.0f, y + 10.0f, MenuWidth - 32.0f, 25.0f),
                "Study with " + PetName,
                headingStyle);

            var moniStyle = new GUIStyle(GUI.skin.label)
            {
                fontStyle = FontStyle.Bold,
                fontSize = 12,
                alignment = TextAnchor.MiddleRight
            };
            moniStyle.normal.textColor = GameTextColor;
            GUI.Label(
                new Rect(x + 150.0f, y + 10.0f, MenuWidth - 166.0f, 25.0f),
                _focus.Moni + " Moni",
                moniStyle);

            var bodyStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true
            };
            bodyStyle.normal.textColor = GameTextColor;

            if (_focus.Status == FocusSessionStatus.Idle)
            {
                GUI.Label(
                    new Rect(x + 20.0f, y + 41.0f, MenuWidth - 40.0f, 38.0f),
                    "Pick a timer and focus while Oguri studies with you.",
                    bodyStyle);

                if (DrawGameButton(
                    new Rect(x + 14.0f, y + 87.0f, MenuWidth - 28.0f, 38.0f),
                    "25 min · +1 Moni · +1 Jelly · -12 Energy",
                    true))
                {
                    StartStudySession(PetFocusState.ShortSessionSeconds);
                }
                if (DrawGameButton(
                    new Rect(x + 14.0f, y + 133.0f, MenuWidth - 28.0f, 38.0f),
                    "50 min · +2 Moni · +2 Jelly · -24 Energy",
                    true))
                {
                    StartStudySession(PetFocusState.LongSessionSeconds);
                }
                GUI.Label(
                    new Rect(x + 20.0f, y + 175.0f, MenuWidth - 40.0f, 30.0f),
                    "Closing the app pauses the timer.",
                    bodyStyle);
            }
            else if (_focus.Status == FocusSessionStatus.RewardReady)
            {
                GUI.Label(
                    new Rect(x + 20.0f, y + 43.0f, MenuWidth - 40.0f, 34.0f),
                    "Jelly and Energy update before Moni.",
                    bodyStyle);
                if (DrawGameButton(
                    new Rect(x + 14.0f, y + 87.0f, MenuWidth - 28.0f, 42.0f),
                    "Collect Moni",
                    true))
                {
                    if (_studyRewards.TryCollectReward())
                    {
                        ShowToast("Moni collected!");
                    }
                    else
                    {
                        ShowToast("Couldn't save the rewards. Try again.");
                    }
                }
            }
            else
            {
                var timerStyle = new GUIStyle(GUI.skin.label)
                {
                    fontStyle = FontStyle.Bold,
                    fontSize = 30,
                    alignment = TextAnchor.MiddleCenter
                };
                timerStyle.normal.textColor = GameTextColor;
                GUI.Label(
                    new Rect(x + 14.0f, y + 42.0f, MenuWidth - 28.0f, 48.0f),
                    FormatFocusTime(_focus.RemainingSeconds),
                    timerStyle);

                string sessionState = _focus.Status == FocusSessionStatus.Running
                    ? PetName + " is studying with you."
                    : "Paused. Continue whenever you're ready.";
                GUI.Label(
                    new Rect(x + 20.0f, y + 91.0f, MenuWidth - 40.0f, 34.0f),
                    sessionState,
                    bodyStyle);

                string toggleLabel = _focus.Status == FocusSessionStatus.Running
                    ? "Pause"
                    : "Resume";
                if (DrawGameButton(
                    new Rect(x + 14.0f, y + 135.0f, MenuWidth - 28.0f, 34.0f),
                    toggleLabel,
                    true))
                {
                    if (_focus.Status == FocusSessionStatus.Running)
                    {
                        if (!_focus.PauseSession())
                        {
                            ShowToast("Couldn't save the paused timer.");
                        }
                    }
                    else
                    {
                        if (!_focus.ResumeSession())
                        {
                            ShowToast("Couldn't save the timer.");
                        }
                    }
                }

                if (_confirmStopStudy)
                {
                    GUI.Label(
                        new Rect(x + 20.0f, y + 176.0f, MenuWidth - 40.0f, 26.0f),
                        "Stop this session? No Moni is earned.",
                        bodyStyle);
                    if (DrawGameButton(
                        new Rect(x + 14.0f, y + 204.0f, 134.0f, 28.0f),
                        "Keep studying",
                        false))
                    {
                        _confirmStopStudy = false;
                    }
                    if (DrawGameButton(
                        new Rect(x + 160.0f, y + 204.0f, 134.0f, 28.0f),
                        "Stop",
                        false))
                    {
                        if (_focus.StopSession())
                        {
                            _confirmStopStudy = false;
                            ShowToast("Study session stopped.");
                        }
                        else
                        {
                            ShowToast("Couldn't save. The timer is still there.");
                        }
                    }
                }
                else if (DrawGameButton(
                    new Rect(x + 14.0f, y + 181.0f, MenuWidth - 28.0f, 28.0f),
                    "Stop session",
                    false))
                {
                    _confirmStopStudy = true;
                }
            }

            DrawDeskReward(x, y, bodyStyle);

            if (DrawGameButton(
                new Rect(x + 14.0f, y + 306.0f, MenuWidth - 28.0f, 24.0f),
                "Back",
                false))
            {
                _confirmStopStudy = false;
                _menuPage = MenuPage.Main;
            }
            if (DrawGameButton(
                new Rect(x + 14.0f, y + 336.0f, MenuWidth - 28.0f, 24.0f),
                "Close menu",
                false))
            {
                CloseMenu();
            }
        }

        private void DrawDeskReward(float x, float y, GUIStyle bodyStyle)
        {
            float top = y + 239.0f;
            var labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontStyle = FontStyle.Bold,
                fontSize = 12,
                alignment = TextAnchor.MiddleLeft
            };
            labelStyle.normal.textColor = GameTextColor;
            GUI.Label(
                new Rect(x + 18.0f, top, 110.0f, 24.0f),
                "Desk rewards",
                labelStyle);

            string rewardLabel = _focus.CarrotDeskCharmOwned
                ? "Carrot desk charm  ✓"
                : "Carrot desk charm  ·  1 Moni";
            GUI.Label(
                new Rect(x + 18.0f, top + 25.0f, 184.0f, 30.0f),
                rewardLabel,
                bodyStyle);

            if (!_focus.CarrotDeskCharmOwned && DrawGameButton(
                new Rect(x + 207.0f, top + 26.0f, 87.0f, 28.0f),
                _focus.Moni >= PetFocusState.CarrotDeskCharmCost
                    ? "Get"
                    : "Need 1",
                false))
            {
                if (_focus.PurchaseCarrotDeskCharm())
                {
                    ShowToast("Carrot desk charm unlocked!");
                }
                else
                {
                    ShowToast(
                        _focus.Moni < PetFocusState.CarrotDeskCharmCost
                            ? "Finish a study session first."
                            : "Couldn't save the desk charm. Try again.");
                }
            }
        }

        private void StartStudySession(int durationSeconds)
        {
            _playStudyCompletionHappy = false;
            if (_focus.StartSession(durationSeconds))
            {
                _confirmStopStudy = false;
                ShowToast("Study timer started.");
            }
            else
            {
                ShowToast("Couldn't save the study timer.");
            }
        }

        private void HandleStudyCompleted()
        {
            _confirmStopStudy = false;
            _playStudyCompletionHappy = true;
            ShowToast("Study session complete!");
        }

        private void DrawFocusBadge()
        {
            if (_focus == null || _focus.Status == FocusSessionStatus.Idle)
            {
                return;
            }

            const float width = 216.0f;
            const float height = 36.0f;
            float x = DesktopWindowController.SidePanelWidth +
                (DesktopWindowController.PetViewportWidth - width) * 0.5f;
            Rect area = new Rect(x, 12.0f, width, height);
            DrawDarkPanel(area);

            string label;
            if (_focus.Status == FocusSessionStatus.Running)
            {
                label = "studying  " + FormatFocusTime(_focus.RemainingSeconds);
            }
            else if (_focus.Status == FocusSessionStatus.Paused)
            {
                label = "study paused  " + FormatFocusTime(_focus.RemainingSeconds);
            }
            else
            {
                label = "Moni ready to collect";
            }

            var style = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                fontSize = 13
            };
            style.normal.textColor = Color.white;
            GUI.Label(area, label, style);
        }

        private void HandleOutsideMenuClick()
        {
            Event current = Event.current;
            if (current != null &&
                current.type == EventType.MouseDown &&
                current.button == 0 &&
                !_menuRect.Contains(
                    DesktopWindowLayout.EventMouseToCurrentGui(current)))
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
            PetFoodDefinition food = FoodCatalog.CarrotJelly;
            if (_needs.GetFoodQuantity(food.Id) <= 0)
            {
                ShowToast("Study together to earn more Carrot Jelly.");
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
                if (_foodUiAssets != null &&
                    _foodUiAssets.TryGetTexture(food.Id, out installedCarrot))
                {
                    _carrotTexture = installedCarrot;
                }
                else
                {
                    _fallbackCarrotTexture = ProceduralCarrotTexture.Create();
                    _carrotTexture = _fallbackCarrotTexture;
                }
            }
            _selectedFood = food;
            _carrotFeedPhase = CarrotFeedPhase.Ready;
            _carrotHovering = false;
            _carrotVisible = false;
            _feedApplied = false;
            _carrotBiteStartedAt = -1.0f;
            ShowToast("Drag the Carrot Jelly to " + PetName + ".");
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
                "Drag the Carrot Jelly to " + PetName,
                titleStyle);

            _carrotPickupRect = new Rect(
                card.center.x - CarrotSize * 0.5f,
                card.y + 48.0f,
                CarrotSize,
                CarrotSize);

            Event current = Event.current;
            Vector2 pointer = current != null
                ? DesktopWindowLayout.EventMouseToCurrentGui(current)
                : DesktopWindowLayout.InputMouseToLogicalGui(
                    Input.mousePosition);
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
                    : "Carry it over to " + PetName;
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
                "Food accepted; the icon is visible while " + PetName +
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
            bool applied = _selectedFood != null &&
                _needs.TryFeed(_selectedFood.Id);
            ShowToast(
                applied
                    ? PetName + " got the Carrot Jelly!"
                    : "Couldn't save; the food wasn't used.");
            Debug.Log(
                "Food bite committed; icon hidden and care state applied=" +
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
            _selectedFood = null;
            Debug.Log("Food response completed.");
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
            _selectedFood = null;
            if (showToast)
            {
                ShowToast("Food put away.");
            }
        }

        private static Rect GetPetFeedTarget()
        {
            return new Rect(
                Mathf.Max(
                    0.0f,
                    DesktopWindowLayout.LogicalWidth - 310.0f),
                40.0f,
                245.0f,
                Mathf.Max(
                    240.0f,
                    DesktopWindowLayout.LogicalHeight - 74.0f));
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
            float y = DesktopWindowLayout.LogicalHeight - height - 16.0f;
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
            bool hovered = current != null && area.Contains(
                DesktopWindowLayout.EventMouseToCurrentGui(current));
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
                (int)MenuWidth,
                (int)MenuHeight,
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

        private static string FormatFocusTime(double seconds)
        {
            int remaining = Math.Max(0, (int)Math.Ceiling(seconds));
            int minutes = remaining / 60;
            int secondsPart = remaining % 60;
            return minutes.ToString("00") + ":" + secondsPart.ToString("00");
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

            Vector2 physicalSize = DesktopWindowLayout.CurrentPhysicalSize;
            _mouthGuiPosition = DesktopWindowLayout.PhysicalGuiToLogical(
                new Vector2(
                    screenPosition.x,
                    physicalSize.y - screenPosition.y),
                physicalSize);
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
            if (_focus != null)
            {
                _focus.SessionCompleted -= HandleStudyCompleted;
            }
            if (_sidePanelView != null)
            {
                _sidePanelView.Dispose();
                _sidePanelView = null;
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
            _menuPosition = Vector2.zero;
            _showMenu = true;
            _menuPage = MenuPage.Main;
            _recordingToolsOpen = false;
#if UMA_RECORDING_TOOLS
            _recordingAnimationsOpen = false;
#endif
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
            _recordingToolsOpen = false;
#if UMA_RECORDING_TOOLS
            _recordingAnimationsOpen = false;
#endif
            _window.SetSidePanelVisible(false, MenuSidecarWidth);
        }

        private void CancelPress()
        {
            bool wasDragging = _dragging;
            bool usedPetReaction = _dragUsesPetReaction;
            if (_window != null)
            {
                _window.EndDrag();
            }
            if (wasDragging && usedPetReaction && _motions != null)
            {
                _motions.EndDragReaction();
            }
            _pressActive = false;
            _dragging = false;
            _dragUsesPetReaction = false;
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
            Settings,
            Study,
            Shop
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
