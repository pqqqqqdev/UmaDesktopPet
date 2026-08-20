using System;
using System.Collections.Generic;
using UnityEngine;

namespace UmaDesktopPet.Standalone.Runtime
{
    internal enum PetPanelPage
    {
        Home,
        Study,
        Shop,
        Settings
    }

    internal enum PetPanelShopSection
    {
        Shop,
        Inventory
    }

#if UMA_RECORDING_TOOLS
    internal enum PetRecordingAnimation
    {
        Tap = 1,
        Happy,
        Eating,
        Hello
    }
#endif

    internal enum PetPanelCommandType
    {
        None,
        Close,
        Navigate,
        Pat,
        Feed,
        StartStudy,
        ToggleStudyPause,
        RequestStopStudy,
        KeepStudying,
        ConfirmStopStudy,
        CollectStudyReward,
        SelectShopSection,
        PurchaseDeskItem,
        EquipDeskItem,
        ClearDeskItem,
        ToggleQuietMode,
        OpenRecordingTools,
        CloseRecordingTools,
#if UMA_RECORDING_TOOLS
        OpenRecordingAnimations,
        CloseRecordingAnimations,
        RecordingPlayAnimation,
#endif
        RecordingSetMood,
        RecordingSetStudyRemaining,
        RecordingCompleteStudy,
        RecordingGiveMoni,
        RecordingResetDeskCollection,
        RecordingResetAll,
        SelectCharacter,
        ChangeGameFiles,
        ReloadGameFiles,
        Quit
    }

    internal struct PetPanelCommand
    {
        public PetPanelCommandType Type;
        public PetPanelPage Page;
        public string Value;
        public int Number;

        public static PetPanelCommand None
        {
            get { return new PetPanelCommand(); }
        }
    }

    internal sealed class PetPanelCharacterPresentation
    {
        public string HeaderName;
        public Color Accent;
        public Color AccentSoft;
        public Color Primary;
        public Color PrimaryHover;
    }

    internal sealed class PetPanelCharacterOption
    {
        public string Key;
        public string DisplayName;
        public bool Selected;
    }

    internal sealed class PetPanelDeskItemModel
    {
        public string Id;
        public string Name;
        public int Cost;
        public bool Owned;
        public bool Equipped;
        public bool Available;
        public Texture IconTexture;
    }

    internal sealed class PetPanelModel
    {
        public PetPanelPage Page;
        public PetPanelCharacterPresentation Character;
        public int Moni;
        public float Energy;
        public Texture EnergyFillTexture;
        public PetMood Mood;
        public string MoodLabel;
        public Texture MoodTexture;
        public Texture MoodAnimationFrameTexture;
        public Texture MoodAnimationArrowTexture;
        public bool CanPat;
        public string PatLabel;
        public bool CanFeed;
        public string FeedLabel;
        public bool QuietMode;
        public FocusSessionStatus FocusStatus;
        public int SessionDurationSeconds;
        public double RemainingSeconds;
        public int PendingMoni;
        public int PendingFoodQuantity;
        public bool ConfirmStopStudy;
        public PetPanelShopSection ShopSection;
        public PetPanelDeskItemModel[] DeskItems;
        public int OwnedDeskItemCount;
        public string NextDeskItemName;
        public int NextDeskItemCost;
        public Texture DeskPreviewTexture;
        public PetPanelCharacterOption[] CharacterOptions;
        public string GameRegionLabel;
        public string GameRoot;
        public bool RecordingToolsEnabled;
        public bool RecordingToolsOpen;
        public bool RecordingAnimationsOpen;
        public bool CanPlayRecordingAnimation;
        public string RecordingAnimationStatus;
    }

    /// <summary>
    /// Rendering-only IMGUI shell for the fixed native sidecar. It receives a
    /// character-neutral snapshot and returns one command; all pet behavior stays
    /// in PetInteractionController.
    /// </summary>
    internal sealed class PetSidePanelView : IDisposable
    {
        public const float PanelWidth = DesktopWindowController.SidePanelWidth;
        public const float PanelHeight = DesktopWindowController.NativeWindowHeight;

        private const int RailWidthPixels = 72;
        private const int ContentHorizontalInsetPixels = 12;
        private const int ContentTextureWidth =
            DesktopWindowController.SidePanelWidth -
            RailWidthPixels -
            ContentHorizontalInsetPixels * 2;
        private const int FullWidthContentTextureWidth =
            DesktopWindowController.SidePanelWidth -
            ContentHorizontalInsetPixels * 2;
        private const int SegmentTextureWidth =
            (ContentTextureWidth - 4) / 2;
        private const int NavigationTopPixels = 148;
        private const int NavigationItemHeightPixels = 83;
        private const int HomeEnergyGaugeWidthPixels = 190;
        private const int HomeEnergyGaugeHeightPixels = 27;
        private const float HomeEnergyGaugeHorizontalInset = 4.0f;
        private const float HomeEnergyGaugeVerticalInset = 4.0f;
        private const int HomeEnergyGaugeAntialiasSamples = 4;
        private const float MoodPulseDurationSeconds = 0.68f;
        private const float MoodArrowLoopDurationSeconds = 1.90f;
        private const float MoodBadgeWidth = 79.0f;
        private const float MoodBadgeHeight = 29.0f;

        private const float RailWidth = RailWidthPixels;
        private const float HeaderHeight = 48.0f;
        private const float ContentLeft =
            RailWidth + ContentHorizontalInsetPixels;
        private const float ContentWidth = ContentTextureWidth;

        private static readonly Color TextColor =
            new Color(0.15f, 0.16f, 0.18f, 1.0f);
        private static readonly Color MutedTextColor =
            new Color(0.37f, 0.40f, 0.44f, 1.0f);
        private static readonly Color PanelColor =
            new Color(0.965f, 0.98f, 0.99f, 0.99f);
        private static readonly Color RailColor =
            new Color(0.94f, 0.965f, 0.98f, 1.0f);
        private static readonly Color BorderColor =
            new Color(0.72f, 0.77f, 0.81f, 1.0f);
        private static readonly Color DisabledFillColor =
            new Color(0.89f, 0.91f, 0.92f, 1.0f);
        private static readonly Color DisabledTextColor =
            new Color(0.56f, 0.58f, 0.60f, 1.0f);

        private Texture2D _panelTexture;
        private Texture2D _railTexture;
        private Texture2D _activeNavTexture;
        private Texture2D _primaryTexture;
        private Texture2D _primaryHoverTexture;
        private Texture2D _secondaryTexture;
        private Texture2D _secondaryHoverTexture;
        private Texture2D _disabledTexture;
        private Texture2D _rowTexture;
        private Texture2D _homeStatusTexture;
        private Texture2D _segmentActiveTexture;
        private Texture2D _itemIconBackgroundTexture;
        private Texture2D _energyGaugeFrameTexture;
        private Texture2D _energyGaugeTrackTexture;
        private Texture2D _progressTrackTexture;
        private Texture2D _progressFillTexture;
        private Texture2D _toggleOffTexture;
        private Texture2D _toggleOnTexture;
        private Texture2D _homeIcon;
        private Texture2D _studyIcon;
        private Texture2D _shopIcon;
        private Texture2D _settingsIcon;
        private Texture2D _closeIcon;
        private Texture2D _pauseIcon;
        private Texture2D _chevronIcon;
        private Vector2 _characterScroll;
        private int _shopPage;
        private int _inventoryPage;
        private bool _hasObservedMood;
        private PetMood _observedMood;
        private float _moodPulseStartedAt = -1.0f;
        private float _moodArrowLoopStartedAt = -1.0f;

        public PetPanelCommand Draw(PetPanelModel model)
        {
            if (model == null || model.Character == null)
            {
                return PetPanelCommand.None;
            }
            EnsureTextures(model.Character);

            Rect panel = new Rect(0.0f, 0.0f, PanelWidth, PanelHeight);
            DrawTexture(panel, _panelTexture);
            DrawTexture(
                new Rect(
                    0.0f,
                    NavigationTopPixels,
                    RailWidth,
                    PanelHeight - NavigationTopPixels),
                _railTexture);
            DrawSolid(
                new Rect(
                    RailWidth - 1.0f,
                    NavigationTopPixels,
                    1.0f,
                    PanelHeight - NavigationTopPixels),
                BorderColor);
            DrawSolid(
                new Rect(0.0f, HeaderHeight - 1.0f, PanelWidth, 1.0f),
                BorderColor);

            PetPanelCommand command = DrawHeader(model);
            if (command.Type != PetPanelCommandType.None)
            {
                return command;
            }

            command = DrawNavigation(model.Page, model.Character);
            if (command.Type != PetPanelCommandType.None)
            {
                return command;
            }

            switch (model.Page)
            {
                case PetPanelPage.Study:
                    return DrawStudy(model);
                case PetPanelPage.Shop:
                    return DrawShop(model);
                case PetPanelPage.Settings:
                    return DrawSettings(model);
                default:
                    return DrawHome(model);
            }
        }

        private PetPanelCommand DrawHeader(PetPanelModel model)
        {
            GUIStyle nameStyle = LabelStyle(18, FontStyle.Bold, TextAnchor.MiddleLeft);
            GUI.Label(
                new Rect(12.0f, 5.0f, 220.0f, 38.0f),
                model.Character.HeaderName,
                nameStyle);

            GUIStyle moniStyle = LabelStyle(12, FontStyle.Bold, TextAnchor.MiddleRight);
            GUI.Label(
                new Rect(PanelWidth - 102.0f, 7.0f, 66.0f, 34.0f),
                model.Moni + " Moni",
                moniStyle);

            Rect close = new Rect(PanelWidth - 32.0f, 9.0f, 24.0f, 28.0f);
            DrawTintedIcon(
                new Rect(close.x + 2.0f, close.y + 4.0f, 20.0f, 20.0f),
                _closeIcon,
                TextColor);
            if (GUI.Button(close, GUIContent.none, GUIStyle.none))
            {
                return Command(PetPanelCommandType.Close);
            }
            return PetPanelCommand.None;
        }

        private PetPanelCommand DrawNavigation(
            PetPanelPage selected,
            PetPanelCharacterPresentation presentation)
        {
            const float top = NavigationTopPixels;
            const float height = NavigationItemHeightPixels;
            PetPanelPage[] pages =
            {
                PetPanelPage.Home,
                PetPanelPage.Study,
                PetPanelPage.Shop,
                PetPanelPage.Settings
            };
            string[] labels = { "Home", "Study", "Shop", "Settings" };
            Texture[] icons =
            {
                _homeIcon,
                _studyIcon,
                _shopIcon,
                _settingsIcon
            };
            for (int index = 0; index < pages.Length; index++)
            {
                Rect area = new Rect(0.0f, top + index * height, RailWidth, height);
                bool active = pages[index] == selected;
                if (active)
                {
                    DrawTexture(area, _activeNavTexture);
                    DrawSolid(
                        new Rect(area.xMax - 3.0f, area.y, 3.0f, area.height),
                        presentation.Accent);
                }

                GUIStyle style = LabelStyle(
                    13,
                    active ? FontStyle.Bold : FontStyle.Normal,
                    TextAnchor.UpperCenter);
                style.normal.textColor = active ? TextColor : MutedTextColor;
                DrawTintedIcon(
                    new Rect(area.x + 20.0f, area.y + 12.0f, 32.0f, 32.0f),
                    icons[index],
                    active ? TextColor : MutedTextColor);
                GUI.Label(
                    new Rect(area.x, area.y + 49.0f, area.width, 24.0f),
                    labels[index],
                    style);
                if (GUI.Button(area, GUIContent.none, GUIStyle.none) && !active)
                {
                    return new PetPanelCommand
                    {
                        Type = PetPanelCommandType.Navigate,
                        Page = pages[index]
                    };
                }
            }
            return PetPanelCommand.None;
        }

        private PetPanelCommand DrawHome(PetPanelModel model)
        {
            const float statusTop = 88.0f;
            const float statusHeight = 54.0f;
            const float statusPadding = 8.0f;
            const float energyLabelWidth = 45.0f;
            const float moodGap = 6.0f;

            float x = ContentHorizontalInsetPixels;
            const float width = FullWidthContentTextureWidth;
            GUI.Label(
                new Rect(x, 60.0f, width, 22.0f),
                "How are we doing?",
                LabelStyle(14, FontStyle.Bold, TextAnchor.MiddleLeft));

            float statusX = x + statusPadding;
            float gaugeX = statusX + energyLabelWidth;
            float moodX = gaugeX + HomeEnergyGaugeWidthPixels + moodGap;
            Rect moodArea = GetAnimatedMoodRect(
                new Rect(
                    moodX,
                    101.0f,
                    MoodBadgeWidth,
                    MoodBadgeHeight),
                model);

            DrawTexture(
                new Rect(x, statusTop, width, statusHeight),
                _homeStatusTexture);
            GUI.Label(
                new Rect(statusX, 100.0f, energyLabelWidth, 30.0f),
                "Energy",
                LabelStyle(12, FontStyle.Bold, TextAnchor.MiddleLeft));
            DrawEnergyGauge(
                new Rect(
                    gaugeX,
                    102.0f,
                    HomeEnergyGaugeWidthPixels,
                    HomeEnergyGaugeHeightPixels),
                Mathf.Clamp01(model.Energy / 100.0f),
                model.EnergyFillTexture);

            DrawMoodBadge(moodArea, model);

            x = ContentLeft;
            PetPanelCommand command;
            bool studyActive =
                model.FocusStatus == FocusSessionStatus.Running ||
                model.FocusStatus == FocusSessionStatus.Paused;
            float studyTop = studyActive ? 194.0f : 238.0f;
            float footerTop = studyActive ? 330.0f : 374.0f;
            if (DrawButton(
                new Rect(x, 150.0f, ContentWidth, 36.0f),
                model.PatLabel,
                false,
                model.CanPat,
                out command,
                PetPanelCommandType.Pat))
            {
                return command;
            }
            if (!studyActive && DrawButton(
                new Rect(x, 194.0f, ContentWidth, 36.0f),
                model.FeedLabel,
                false,
                model.CanFeed,
                out command,
                PetPanelCommandType.Feed))
            {
                return command;
            }

            string studyTitle;
            string studyDetail;
            if (model.FocusStatus == FocusSessionStatus.Running)
            {
                studyTitle = "Studying together";
                studyDetail = FormatTime(model.RemainingSeconds);
            }
            else if (model.FocusStatus == FocusSessionStatus.Paused)
            {
                studyTitle = "Study paused";
                studyDetail = FormatTime(model.RemainingSeconds);
            }
            else if (model.FocusStatus == FocusSessionStatus.RewardReady)
            {
                studyTitle = "Session complete";
                studyDetail = "+" + model.PendingMoni +
                    " Moni ready to collect";
            }
            else
            {
                studyTitle = "Study together";
                studyDetail = "25/50 min · Moni + Jelly · uses Energy";
            }
            DrawTexture(new Rect(x, studyTop, ContentWidth, 78.0f), _rowTexture);
            GUI.Label(
                new Rect(
                    x + 10.0f,
                    studyTop + 8.0f,
                    ContentWidth - 20.0f,
                    22.0f),
                studyTitle,
                LabelStyle(12, FontStyle.Bold, TextAnchor.MiddleLeft));
            GUI.Label(
                new Rect(
                    x + 10.0f,
                    studyTop + 33.0f,
                    ContentWidth - 20.0f,
                    18.0f),
                studyDetail,
                MutedStyle(10, TextAnchor.MiddleLeft));
            if (DrawTextAction(
                new Rect(
                    x + 10.0f,
                    studyTop + 54.0f,
                    ContentWidth - 20.0f,
                    18.0f),
                model.FocusStatus == FocusSessionStatus.RewardReady
                    ? "Collect Moni"
                    : "Open study"))
            {
                return model.FocusStatus == FocusSessionStatus.RewardReady
                    ? Command(PetPanelCommandType.CollectStudyReward)
                    : Navigate(PetPanelPage.Study);
            }

            return DrawCollectionFooter(model, footerTop);
        }

        private PetPanelCommand DrawStudy(PetPanelModel model)
        {
            float x = ContentLeft;
            GUI.Label(
                new Rect(x, 62.0f, ContentWidth, 18.0f),
                model.FocusStatus == FocusSessionStatus.Paused
                    ? "study paused"
                    : model.FocusStatus == FocusSessionStatus.RewardReady
                        ? "nice work"
                        : model.FocusStatus == FocusSessionStatus.Idle
                            ? "choose a session"
                            : "studying together",
                MutedStyle(11, TextAnchor.MiddleCenter));

            PetPanelCommand command;
            if (model.FocusStatus == FocusSessionStatus.Idle)
            {
                GUI.Label(
                    new Rect(x, 88.0f, ContentWidth, 56.0f),
                    "Focus with " + model.Character.HeaderName,
                    LabelStyle(20, FontStyle.Bold, TextAnchor.MiddleCenter));
                if (DrawButton(
                    new Rect(x, 160.0f, ContentWidth, 42.0f),
                    FormatStudyStartLabel(
                        PetFocusState.ShortSessionSeconds,
                        PetFocusState.ShortSessionReward),
                    true,
                    true,
                    out command,
                    PetPanelCommandType.StartStudy,
                    PetFocusState.ShortSessionSeconds))
                {
                    return command;
                }
                if (DrawButton(
                    new Rect(x, 212.0f, ContentWidth, 42.0f),
                    FormatStudyStartLabel(
                        PetFocusState.LongSessionSeconds,
                        PetFocusState.LongSessionReward),
                    true,
                    true,
                    out command,
                    PetPanelCommandType.StartStudy,
                    PetFocusState.LongSessionSeconds))
                {
                    return command;
                }
            }
            else if (model.FocusStatus == FocusSessionStatus.RewardReady)
            {
                GUI.Label(
                    new Rect(x, 92.0f, ContentWidth, 58.0f),
                    "+" + model.PendingMoni + " Moni",
                    LabelStyle(38, FontStyle.Bold, TextAnchor.MiddleCenter));
                if (DrawButton(
                    new Rect(x, 164.0f, ContentWidth, 44.0f),
                    "Collect Moni",
                    true,
                    true,
                    out command,
                    PetPanelCommandType.CollectStudyReward))
                {
                    return command;
                }
            }
            else
            {
                GUI.Label(
                    new Rect(x, 84.0f, ContentWidth, 68.0f),
                    FormatTime(model.RemainingSeconds),
                    LabelStyle(46, FontStyle.Bold, TextAnchor.MiddleCenter));
                float progress = model.SessionDurationSeconds <= 0
                    ? 0.0f
                    : 1.0f - (float)(model.RemainingSeconds /
                        model.SessionDurationSeconds);
                DrawProgressBar(
                    new Rect(x + 8.0f, 161.0f, ContentWidth - 16.0f, 8.0f),
                    progress);
                if (DrawButton(
                    new Rect(x, 196.0f, ContentWidth, 44.0f),
                    model.FocusStatus == FocusSessionStatus.Running
                        ? "Pause"
                        : "Resume",
                    true,
                    true,
                    out command,
                    PetPanelCommandType.ToggleStudyPause,
                    0,
                    null,
                    model.FocusStatus == FocusSessionStatus.Running
                        ? _pauseIcon
                        : null))
                {
                    return command;
                }

                if (model.ConfirmStopStudy)
                {
                    GUI.Label(
                        new Rect(x, 252.0f, ContentWidth, 34.0f),
                        "Stop without earning rewards?",
                        WrappedStyle(11, TextAnchor.MiddleCenter));
                    if (DrawButton(
                        new Rect(x, 291.0f, 102.0f, 31.0f),
                        "Keep going",
                        false,
                        true,
                        out command,
                        PetPanelCommandType.KeepStudying))
                    {
                        return command;
                    }
                    if (DrawButton(
                        new Rect(x + ContentWidth - 102.0f, 291.0f, 102.0f, 31.0f),
                        "Stop",
                        false,
                        true,
                        out command,
                        PetPanelCommandType.ConfirmStopStudy))
                    {
                        return command;
                    }
                }
                else if (DrawTextAction(
                    new Rect(x, 254.0f, ContentWidth, 26.0f),
                    "Stop session"))
                {
                    return Command(PetPanelCommandType.RequestStopStudy);
                }
            }

            return DrawCollectionFooter(model, 374.0f);
        }

        private PetPanelCommand DrawShop(PetPanelModel model)
        {
            float x = ContentLeft;
            GUI.Label(
                new Rect(x, 58.0f, ContentWidth, 24.0f),
                "Desk items",
                LabelStyle(16, FontStyle.Bold, TextAnchor.MiddleLeft));

            const float tabGap = 4.0f;
            float tabWidth = (ContentWidth - tabGap) * 0.5f;
            PetPanelCommand sectionCommand;
            if (DrawShopSectionTab(
                new Rect(x, 86.0f, tabWidth, 30.0f),
                "Shop",
                model.ShopSection == PetPanelShopSection.Shop,
                PetPanelShopSection.Shop,
                out sectionCommand))
            {
                return sectionCommand;
            }
            if (DrawShopSectionTab(
                new Rect(x + tabWidth + tabGap, 86.0f, tabWidth, 30.0f),
                "Inventory",
                model.ShopSection == PetPanelShopSection.Inventory,
                PetPanelShopSection.Inventory,
                out sectionCommand))
            {
                return sectionCommand;
            }

            PetPanelDeskItemModel[] items = model.DeskItems ??
                new PetPanelDeskItemModel[0];
            var visibleItems = new List<PetPanelDeskItemModel>(items.Length);
            bool inventory = model.ShopSection == PetPanelShopSection.Inventory;
            for (int index = 0; index < items.Length; index++)
            {
                PetPanelDeskItemModel item = items[index];
                if (item != null && item.Owned == inventory)
                {
                    visibleItems.Add(item);
                }
            }

            const int itemsPerPage = 3;
            int pageCount = Mathf.Max(
                1,
                Mathf.CeilToInt(visibleItems.Count / (float)itemsPerPage));
            int currentPage = inventory ? _inventoryPage : _shopPage;
            currentPage = Mathf.Clamp(currentPage, 0, pageCount - 1);
            if (inventory)
            {
                _inventoryPage = currentPage;
            }
            else
            {
                _shopPage = currentPage;
            }

            int firstItem = currentPage * itemsPerPage;
            int lastItem = Mathf.Min(firstItem + itemsPerPage, visibleItems.Count);
            for (int index = firstItem; index < lastItem; index++)
            {
                PetPanelDeskItemModel item = visibleItems[index];
                float top = 126.0f + (index - firstItem) * 86.0f;
                Rect row = new Rect(x, top, ContentWidth, 78.0f);
                DrawTexture(row, _rowTexture);
                Rect iconBackground = new Rect(row.x + 8.0f, row.y + 13.0f, 52.0f, 52.0f);
                DrawTexture(iconBackground, _itemIconBackgroundTexture);
                if (item.IconTexture != null)
                {
                    GUI.DrawTexture(
                        new Rect(iconBackground.x + 3.0f, iconBackground.y + 3.0f,
                            iconBackground.width - 6.0f, iconBackground.height - 6.0f),
                        item.IconTexture,
                        ScaleMode.ScaleToFit,
                        true);
                }
                else
                {
                    DrawTintedIcon(
                        new Rect(iconBackground.x + 14.0f, iconBackground.y + 14.0f,
                            24.0f, 24.0f),
                        _shopIcon,
                        MutedTextColor);
                }
                GUI.Label(
                    new Rect(row.x + 68.0f, row.y + 6.0f, row.width - 78.0f, 20.0f),
                    item.Name,
                    LabelStyle(12, FontStyle.Bold, TextAnchor.MiddleLeft));
                GUI.Label(
                    new Rect(row.x + 68.0f, row.y + 27.0f, row.width - 78.0f, 16.0f),
                    inventory
                        ? item.Equipped ? "On desk" : "Owned"
                        : item.Cost + " Moni",
                    MutedStyle(10, TextAnchor.MiddleLeft));

                string actionLabel;
                bool enabled;
                PetPanelCommandType type;
                if (inventory)
                {
                    actionLabel = item.Equipped
                        ? "Put away"
                        : item.Available ? "Equip" : "Unavailable";
                    enabled = item.Equipped || item.Available;
                    type = item.Equipped
                        ? PetPanelCommandType.ClearDeskItem
                        : PetPanelCommandType.EquipDeskItem;
                }
                else
                {
                    actionLabel = !item.Available
                        ? "Unavailable"
                        : model.Moni >= item.Cost ? "Buy" : "Need " + item.Cost;
                    enabled = item.Available && model.Moni >= item.Cost;
                    type = PetPanelCommandType.PurchaseDeskItem;
                }
                PetPanelCommand command;
                if (DrawButton(
                    new Rect(row.x + 68.0f, row.y + 47.0f, row.width - 78.0f, 24.0f),
                    actionLabel,
                    inventory && item.Equipped,
                    enabled,
                    out command,
                    type,
                    0,
                    item.Id))
                {
                    return command;
                }
            }

            if (visibleItems.Count == 0)
            {
                GUI.Label(
                    new Rect(x, 178.0f, ContentWidth, 46.0f),
                    inventory ? "Nothing here yet" : "You bought everything",
                    MutedStyle(12, TextAnchor.MiddleCenter));
            }

            if (pageCount > 1)
            {
                if (currentPage > 0 && DrawTextAction(
                    new Rect(x, 382.0f, 68.0f, 24.0f),
                    "Previous"))
                {
                    currentPage--;
                }
                GUI.Label(
                    new Rect(x + 68.0f, 382.0f, ContentWidth - 136.0f, 24.0f),
                    (currentPage + 1) + "/" + pageCount,
                    MutedStyle(10, TextAnchor.MiddleCenter));
                if (currentPage < pageCount - 1 && DrawTextAction(
                    new Rect(x + ContentWidth - 68.0f, 382.0f, 68.0f, 24.0f),
                    "Next"))
                {
                    currentPage++;
                }
                if (inventory)
                {
                    _inventoryPage = currentPage;
                }
                else
                {
                    _shopPage = currentPage;
                }
            }
            GUI.Label(
                new Rect(x, 410.0f, ContentWidth, 24.0f),
                model.OwnedDeskItemCount + " of " + items.Length + " owned",
                MutedStyle(10, TextAnchor.MiddleCenter));
            return PetPanelCommand.None;
        }

        private bool DrawShopSectionTab(
            Rect area,
            string label,
            bool selected,
            PetPanelShopSection section,
            out PetPanelCommand command)
        {
            DrawTexture(area, selected ? _segmentActiveTexture : _secondaryTexture);
            GUIStyle style = LabelStyle(11, FontStyle.Bold, TextAnchor.MiddleCenter);
            style.normal.textColor = TextColor;
            GUI.Label(area, label, style);
            if (!selected && GUI.Button(area, GUIContent.none, GUIStyle.none))
            {
                command = new PetPanelCommand
                {
                    Type = PetPanelCommandType.SelectShopSection,
                    Number = (int)section
                };
                return true;
            }

            command = PetPanelCommand.None;
            return false;
        }

        private PetPanelCommand DrawSettings(PetPanelModel model)
        {
#if UMA_RECORDING_TOOLS
            if (model.RecordingToolsEnabled && model.RecordingAnimationsOpen)
            {
                return DrawRecordingAnimations(model);
            }
#endif
            if (model.RecordingToolsEnabled && model.RecordingToolsOpen)
            {
                return DrawRecordingTools(model);
            }

            float x = ContentLeft;
            GUI.Label(
                new Rect(x, 59.0f, ContentWidth, 22.0f),
                "Settings",
                LabelStyle(16, FontStyle.Bold, TextAnchor.MiddleLeft));
            GUI.Label(
                new Rect(x, 86.0f, ContentWidth, 18.0f),
                "Desktop Uma",
                LabelStyle(11, FontStyle.Bold, TextAnchor.MiddleLeft));

            Rect characterArea = new Rect(x, 108.0f, ContentWidth, 70.0f);
            PetPanelCharacterOption[] options = model.CharacterOptions ??
                new PetPanelCharacterOption[0];
            Rect content = new Rect(
                0.0f,
                0.0f,
                characterArea.width - 16.0f,
                Mathf.Max(characterArea.height, options.Length * 30.0f));
            _characterScroll = GUI.BeginScrollView(
                characterArea,
                _characterScroll,
                content);
            for (int index = 0; index < options.Length; index++)
            {
                PetPanelCharacterOption option = options[index];
                PetPanelCommand command;
                if (DrawButton(
                    new Rect(0.0f, index * 30.0f, content.width, 26.0f),
                    option.DisplayName + (option.Selected ? "  · selected" : string.Empty),
                    option.Selected,
                    !option.Selected,
                    out command,
                    PetPanelCommandType.SelectCharacter,
                    0,
                    option.Key))
                {
                    GUI.EndScrollView();
                    return command;
                }
            }
            GUI.EndScrollView();

            if (DrawToggle(
                new Rect(x, 188.0f, ContentWidth, 28.0f),
                model.QuietMode,
                "Quiet mode"))
            {
                return Command(PetPanelCommandType.ToggleQuietMode);
            }

            GUI.Label(
                new Rect(x, 226.0f, ContentWidth, 18.0f),
                model.GameRegionLabel + " game files",
                LabelStyle(11, FontStyle.Bold, TextAnchor.MiddleLeft));
            GUI.Label(
                new Rect(x, 246.0f, ContentWidth, 28.0f),
                CompactPath(model.GameRoot),
                WrappedStyle(9, TextAnchor.UpperLeft));

            PetPanelCommand result;
            if (DrawButton(
                new Rect(x, 282.0f, ContentWidth, 28.0f),
                "Change game files",
                false,
                true,
                out result,
                PetPanelCommandType.ChangeGameFiles))
            {
                return result;
            }
            if (DrawButton(
                new Rect(x, 317.0f, ContentWidth, 28.0f),
                "Reload game files",
                false,
                true,
                out result,
                PetPanelCommandType.ReloadGameFiles))
            {
                return result;
            }
            PetPanelCommand quitCommand;
            if (DrawButton(
                new Rect(x, 365.0f, ContentWidth, 36.0f),
                "Quit desktop pet",
                false,
                true,
                out quitCommand,
                PetPanelCommandType.Quit))
            {
                return quitCommand;
            }
            if (model.RecordingToolsEnabled && DrawButton(
                new Rect(x, 409.0f, ContentWidth, 30.0f),
                "Recording tools",
                true,
                true,
                out result,
                PetPanelCommandType.OpenRecordingTools))
            {
                return result;
            }
            return PetPanelCommand.None;
        }

        private PetPanelCommand DrawRecordingTools(PetPanelModel model)
        {
            float x = ContentLeft;
            GUI.Label(
                new Rect(x, 59.0f, ContentWidth - 112.0f, 22.0f),
                "Recording tools",
                LabelStyle(16, FontStyle.Bold, TextAnchor.MiddleLeft));
#if UMA_RECORDING_TOOLS
            if (DrawTextAction(
                new Rect(x + ContentWidth - 104.0f, 57.0f, 104.0f, 26.0f),
                "Animations  ›"))
            {
                return Command(PetPanelCommandType.OpenRecordingAnimations);
            }
#endif

            DrawTexture(
                new Rect(x, 86.0f, ContentWidth, 42.0f),
                _rowTexture);
            GUI.Label(
                new Rect(x + 10.0f, 89.0f, ContentWidth - 20.0f, 18.0f),
                "RECORDING MODE",
                LabelStyle(10, FontStyle.Bold, TextAnchor.MiddleLeft));
            GUI.Label(
                new Rect(x + 10.0f, 106.0f, ContentWidth - 20.0f, 18.0f),
                "Temporary changes aren't saved.",
                MutedStyle(10, TextAnchor.MiddleLeft));

            GUI.Label(
                new Rect(x, 142.0f, ContentWidth, 18.0f),
                "Mood",
                LabelStyle(11, FontStyle.Bold, TextAnchor.MiddleLeft));
            PetMood[] moods =
            {
                PetMood.Awful,
                PetMood.Bad,
                PetMood.Normal,
                PetMood.Good,
                PetMood.Great
            };
            string[] moodLabels = { "Awful", "Bad", "Normal", "Good", "Great" };
            const float moodGap = 3.0f;
            float moodWidth = (ContentWidth - moodGap * 4.0f) / 5.0f;
            for (int index = 0; index < moods.Length; index++)
            {
                PetPanelCommand moodCommand;
                if (DrawButton(
                    new Rect(
                        x + index * (moodWidth + moodGap),
                        164.0f,
                        moodWidth,
                        30.0f),
                    moodLabels[index],
                    model.Mood == moods[index],
                    true,
                    out moodCommand,
                    PetPanelCommandType.RecordingSetMood,
                    (int)moods[index]))
                {
                    return moodCommand;
                }
            }

            GUI.Label(
                new Rect(x, 208.0f, ContentWidth, 18.0f),
                "Study time left",
                LabelStyle(11, FontStyle.Bold, TextAnchor.MiddleLeft));
            string studyStatus;
            if (model.FocusStatus == FocusSessionStatus.Running)
            {
                studyStatus = "Running · " + FormatTime(model.RemainingSeconds);
            }
            else if (model.FocusStatus == FocusSessionStatus.Paused)
            {
                studyStatus = "Paused · " + FormatTime(model.RemainingSeconds);
            }
            else if (model.FocusStatus == FocusSessionStatus.RewardReady)
            {
                studyStatus = "Reward ready · collect or reset all";
            }
            else
            {
                studyStatus = "No active session";
            }
            GUI.Label(
                new Rect(x, 228.0f, ContentWidth, 18.0f),
                studyStatus,
                MutedStyle(10, TextAnchor.MiddleLeft));

            const float actionGap = 4.0f;
            float actionWidth = (ContentWidth - actionGap * 2.0f) / 3.0f;
            bool canStageStudy =
                model.FocusStatus != FocusSessionStatus.RewardReady;
            PetPanelCommand result;
            if (DrawButton(
                new Rect(x, 249.0f, actionWidth, 32.0f),
                "1 min",
                false,
                canStageStudy,
                out result,
                PetPanelCommandType.RecordingSetStudyRemaining,
                60))
            {
                return result;
            }
            if (DrawButton(
                new Rect(
                    x + actionWidth + actionGap,
                    249.0f,
                    actionWidth,
                    32.0f),
                "10 sec",
                false,
                canStageStudy,
                out result,
                PetPanelCommandType.RecordingSetStudyRemaining,
                10))
            {
                return result;
            }
            bool studyActive =
                model.FocusStatus == FocusSessionStatus.Running ||
                model.FocusStatus == FocusSessionStatus.Paused;
            if (DrawButton(
                new Rect(
                    x + (actionWidth + actionGap) * 2.0f,
                    249.0f,
                    actionWidth,
                    32.0f),
                studyActive ? "Finish now" : "25 min",
                studyActive,
                studyActive || canStageStudy,
                out result,
                studyActive
                    ? PetPanelCommandType.RecordingCompleteStudy
                    : PetPanelCommandType.RecordingSetStudyRemaining,
                studyActive ? 0 : PetFocusState.ShortSessionSeconds))
            {
                return result;
            }

            GUI.Label(
                new Rect(x, 298.0f, ContentWidth, 18.0f),
                "Moni · " + model.Moni,
                LabelStyle(11, FontStyle.Bold, TextAnchor.MiddleLeft));
            float halfWidth = (ContentWidth - actionGap) * 0.5f;
            bool canGiveMoni =
                model.FocusStatus != FocusSessionStatus.RewardReady;
            if (DrawButton(
                new Rect(x, 320.0f, halfWidth, 32.0f),
                "+1 Moni",
                false,
                canGiveMoni,
                out result,
                PetPanelCommandType.RecordingGiveMoni,
                1))
            {
                return result;
            }
            if (DrawButton(
                new Rect(
                    x + halfWidth + actionGap,
                    320.0f,
                    halfWidth,
                    32.0f),
                "+10 Moni",
                false,
                canGiveMoni,
                out result,
                PetPanelCommandType.RecordingGiveMoni,
                10))
            {
                return result;
            }

            int deskTotal = model.DeskItems == null ? 0 : model.DeskItems.Length;
            GUI.Label(
                new Rect(x, 369.0f, ContentWidth, 18.0f),
                "Desk collection · " + model.OwnedDeskItemCount + "/" + deskTotal,
                LabelStyle(11, FontStyle.Bold, TextAnchor.MiddleLeft));
            if (DrawButton(
                new Rect(x, 392.0f, halfWidth, 32.0f),
                "Reset collection",
                false,
                model.OwnedDeskItemCount > 0,
                out result,
                PetPanelCommandType.RecordingResetDeskCollection))
            {
                return result;
            }
            if (DrawButton(
                new Rect(
                    x + halfWidth + actionGap,
                    392.0f,
                    halfWidth,
                    32.0f),
                "Reset all",
                false,
                true,
                out result,
                PetPanelCommandType.RecordingResetAll))
            {
                return result;
            }
            if (DrawButton(
                new Rect(x, 437.0f, ContentWidth, 26.0f),
                "Back to settings",
                false,
                true,
                out result,
                PetPanelCommandType.CloseRecordingTools))
            {
                return result;
            }
            return PetPanelCommand.None;
        }

#if UMA_RECORDING_TOOLS
        private PetPanelCommand DrawRecordingAnimations(PetPanelModel model)
        {
            float x = ContentLeft;
            GUI.Label(
                new Rect(x, 59.0f, ContentWidth, 22.0f),
                "Animations",
                LabelStyle(16, FontStyle.Bold, TextAnchor.MiddleLeft));

            DrawTexture(
                new Rect(x, 86.0f, ContentWidth, 42.0f),
                _rowTexture);
            GUI.Label(
                new Rect(x + 10.0f, 89.0f, ContentWidth - 20.0f, 18.0f),
                "VISUAL ONLY",
                LabelStyle(10, FontStyle.Bold, TextAnchor.MiddleLeft));
            GUI.Label(
                new Rect(x + 10.0f, 106.0f, ContentWidth - 20.0f, 18.0f),
                model.RecordingAnimationStatus ?? "Ready",
                MutedStyle(10, TextAnchor.MiddleLeft));

            GUI.Label(
                new Rect(x, 145.0f, ContentWidth, 18.0f),
                "Reactions",
                LabelStyle(11, FontStyle.Bold, TextAnchor.MiddleLeft));

            const float gap = 6.0f;
            float buttonWidth = (ContentWidth - gap) * 0.5f;
            PetPanelCommand result;
            if (DrawButton(
                new Rect(x, 168.0f, buttonWidth, 38.0f),
                "Tap",
                true,
                model.CanPlayRecordingAnimation,
                out result,
                PetPanelCommandType.RecordingPlayAnimation,
                (int)PetRecordingAnimation.Tap))
            {
                return result;
            }
            if (DrawButton(
                new Rect(x + buttonWidth + gap, 168.0f, buttonWidth, 38.0f),
                "Happy",
                true,
                model.CanPlayRecordingAnimation,
                out result,
                PetPanelCommandType.RecordingPlayAnimation,
                (int)PetRecordingAnimation.Happy))
            {
                return result;
            }
            if (DrawButton(
                new Rect(x, 216.0f, buttonWidth, 38.0f),
                "Eating",
                true,
                model.CanPlayRecordingAnimation,
                out result,
                PetPanelCommandType.RecordingPlayAnimation,
                (int)PetRecordingAnimation.Eating))
            {
                return result;
            }
            if (DrawButton(
                new Rect(x + buttonWidth + gap, 216.0f, buttonWidth, 38.0f),
                "Hello",
                true,
                model.CanPlayRecordingAnimation,
                out result,
                PetPanelCommandType.RecordingPlayAnimation,
                (int)PetRecordingAnimation.Hello))
            {
                return result;
            }

            GUI.Label(
                new Rect(x, 270.0f, ContentWidth, 18.0f),
                "Study: timer  ·  Drag: move Oguri",
                MutedStyle(10, TextAnchor.MiddleLeft));

            if (DrawButton(
                new Rect(x, 437.0f, ContentWidth, 26.0f),
                "Back to recording tools",
                false,
                true,
                out result,
                PetPanelCommandType.CloseRecordingAnimations))
            {
                return result;
            }
            return PetPanelCommand.None;
        }
#endif

        private PetPanelCommand DrawCollectionFooter(PetPanelModel model, float top)
        {
            DrawSolid(
                new Rect(RailWidth, top - 1.0f, PanelWidth - RailWidth, 1.0f),
                BorderColor);
            if (model.DeskPreviewTexture != null)
            {
                GUI.DrawTexture(
                    new Rect(ContentLeft, top + 10.0f, 42.0f, 42.0f),
                    model.DeskPreviewTexture,
                    ScaleMode.ScaleToFit,
                    true);
            }
            float labelX = model.DeskPreviewTexture == null
                ? ContentLeft
                : ContentLeft + 50.0f;
            GUI.Label(
                new Rect(labelX, top + 7.0f, PanelWidth - labelX - 12.0f, 21.0f),
                "Desk collection " + model.OwnedDeskItemCount + "/" +
                    ((model.DeskItems == null) ? 0 : model.DeskItems.Length),
                LabelStyle(11, FontStyle.Bold, TextAnchor.MiddleLeft));
            string next = string.IsNullOrEmpty(model.NextDeskItemName)
                ? "Collection complete"
                : "Next: " + model.NextDeskItemName + " · " +
                    model.NextDeskItemCost + " Moni";
            GUI.Label(
                new Rect(labelX, top + 29.0f, PanelWidth - labelX - 12.0f, 19.0f),
                next,
                MutedStyle(9, TextAnchor.MiddleLeft));
            DrawTintedIcon(
                new Rect(PanelWidth - 31.0f, top + 16.0f, 20.0f, 20.0f),
                _chevronIcon,
                MutedTextColor);
            Rect hit = new Rect(RailWidth, top, PanelWidth - RailWidth, PanelHeight - top);
            return GUI.Button(hit, GUIContent.none, GUIStyle.none)
                ? Navigate(PetPanelPage.Shop)
                : PetPanelCommand.None;
        }

        private void EnsureTextures(PetPanelCharacterPresentation presentation)
        {
            if (_panelTexture != null)
            {
                return;
            }

            _panelTexture = CreateRoundedTexture(
                DesktopWindowController.SidePanelWidth,
                DesktopWindowController.NativeWindowHeight,
                8.0f,
                PanelColor,
                BorderColor,
                1);
            _railTexture = CreateRoundedTexture(
                RailWidthPixels,
                DesktopWindowController.NativeWindowHeight - NavigationTopPixels,
                8.0f,
                RailColor,
                Color.clear,
                0);
            _activeNavTexture = CreateRoundedTexture(
                RailWidthPixels, NavigationItemHeightPixels, 0.0f,
                presentation.AccentSoft, Color.clear, 0);
            _primaryTexture = CreateRoundedTexture(
                ContentTextureWidth, 44, 6.0f, presentation.Primary,
                new Color(0.13f, 0.43f, 0.20f, 1.0f), 1);
            _primaryHoverTexture = CreateRoundedTexture(
                ContentTextureWidth, 44, 6.0f, presentation.PrimaryHover,
                new Color(0.13f, 0.43f, 0.20f, 1.0f), 1);
            _secondaryTexture = CreateRoundedTexture(
                ContentTextureWidth, 36, 6.0f, Color.white, BorderColor, 1);
            _secondaryHoverTexture = CreateRoundedTexture(
                ContentTextureWidth, 36, 6.0f,
                new Color(1.0f, 0.98f, 0.88f, 1.0f),
                presentation.Accent, 1);
            _disabledTexture = CreateRoundedTexture(
                ContentTextureWidth, 36, 6.0f,
                DisabledFillColor, BorderColor, 1);
            _rowTexture = CreateRoundedTexture(
                ContentTextureWidth, 78, 6.0f, Color.white, BorderColor, 1);
            _homeStatusTexture = CreateRoundedTexture(
                FullWidthContentTextureWidth, 54, 6.0f,
                Color.white, BorderColor, 1);
            _segmentActiveTexture = CreateRoundedTexture(
                SegmentTextureWidth, 30, 6.0f, presentation.AccentSoft,
                presentation.Accent, 1);
            _itemIconBackgroundTexture = CreateRoundedTexture(
                52, 52, 6.0f, presentation.AccentSoft,
                Color.clear, 0);
            _energyGaugeFrameTexture = CreateAntialiasedRoundedTexture(
                HomeEnergyGaugeWidthPixels,
                HomeEnergyGaugeHeightPixels,
                HomeEnergyGaugeHeightPixels * 0.5f,
                Color.white,
                new Color32(58, 61, 58, 255),
                2.0f,
                HomeEnergyGaugeAntialiasSamples);
            int energyTrackWidth = HomeEnergyGaugeWidthPixels -
                Mathf.RoundToInt(HomeEnergyGaugeHorizontalInset * 2.0f);
            int energyTrackHeight = HomeEnergyGaugeHeightPixels -
                Mathf.RoundToInt(HomeEnergyGaugeVerticalInset * 2.0f);
            _energyGaugeTrackTexture = CreateAntialiasedRoundedTexture(
                energyTrackWidth,
                energyTrackHeight,
                energyTrackHeight * 0.5f,
                new Color32(118, 117, 118, 255),
                Color.clear,
                0.0f,
                HomeEnergyGaugeAntialiasSamples);
            _progressTrackTexture = CreateRoundedTexture(
                ContentTextureWidth, 12, 6.0f,
                new Color(0.84f, 0.87f, 0.89f, 1.0f),
                Color.clear, 0);
            _progressFillTexture = CreateRoundedTexture(
                ContentTextureWidth, 12, 6.0f,
                presentation.Accent, Color.clear, 0);
            _toggleOffTexture = CreateRoundedTexture(
                36, 20, 10.0f, new Color(0.77f, 0.79f, 0.81f, 1.0f),
                Color.clear, 0);
            _toggleOnTexture = CreateRoundedTexture(
                36, 20, 10.0f, presentation.Primary, Color.clear, 0);
            _homeIcon = Resources.Load<Texture2D>("Ui/Icons/house");
            _studyIcon = Resources.Load<Texture2D>("Ui/Icons/book");
            _shopIcon = Resources.Load<Texture2D>("Ui/Icons/bag");
            _settingsIcon = Resources.Load<Texture2D>("Ui/Icons/gear");
            _closeIcon = Resources.Load<Texture2D>("Ui/Icons/x-lg");
            _pauseIcon = Resources.Load<Texture2D>("Ui/Icons/pause-fill");
            _chevronIcon = Resources.Load<Texture2D>("Ui/Icons/chevron-right");
        }

        private bool DrawButton(
            Rect area,
            string label,
            bool primary,
            bool enabled,
            out PetPanelCommand command,
            PetPanelCommandType commandType,
            int number = 0,
            string value = null,
            Texture icon = null)
        {
            bool hovered = enabled && Event.current != null &&
                area.Contains(
                    DesktopWindowLayout.EventMouseToCurrentGui(
                        Event.current));
            Texture2D texture = !enabled
                ? _disabledTexture
                : primary
                    ? hovered ? _primaryHoverTexture : _primaryTexture
                    : hovered ? _secondaryHoverTexture : _secondaryTexture;
            DrawTexture(area, texture);
            GUIStyle style = LabelStyle(12, FontStyle.Bold, TextAnchor.MiddleCenter);
            style.normal.textColor = !enabled
                ? DisabledTextColor
                : primary ? Color.white : TextColor;
            Rect labelArea = area;
            if (icon != null)
            {
                float iconX = area.center.x - 44.0f;
                DrawTintedIcon(
                    new Rect(iconX - 2.0f, area.center.y - 10.0f, 20.0f, 20.0f),
                    icon,
                    primary ? Color.white : TextColor);
                labelArea.x += 14.0f;
                labelArea.width -= 14.0f;
            }
            GUI.Label(labelArea, label, style);
            if (enabled && GUI.Button(area, GUIContent.none, GUIStyle.none))
            {
                command = new PetPanelCommand
                {
                    Type = commandType,
                    Number = number,
                    Value = value
                };
                return true;
            }
            command = PetPanelCommand.None;
            return false;
        }

        private bool DrawTextAction(Rect area, string label)
        {
            GUIStyle style = LabelStyle(11, FontStyle.Normal, TextAnchor.MiddleCenter);
            style.normal.textColor = MutedTextColor;
            GUI.Label(area, label, style);
            return GUI.Button(area, GUIContent.none, GUIStyle.none);
        }

        private bool DrawToggle(Rect area, bool enabled, string label)
        {
            GUI.Label(
                new Rect(area.x, area.y, area.width - 48.0f, area.height),
                label,
                LabelStyle(11, FontStyle.Normal, TextAnchor.MiddleLeft));
            Rect track = new Rect(area.xMax - 40.0f, area.y + 4.0f, 36.0f, 20.0f);
            DrawTexture(track, enabled ? _toggleOnTexture : _toggleOffTexture);
            float knobX = enabled ? track.xMax - 17.0f : track.x + 3.0f;
            DrawSolid(new Rect(knobX, track.y + 3.0f, 14.0f, 14.0f), Color.white);
            return GUI.Button(area, GUIContent.none, GUIStyle.none);
        }

        private void DrawProgressBar(
            Rect area,
            float progress,
            Texture fillTexture = null,
            Texture trackTexture = null)
        {
            DrawTexture(area, trackTexture ?? _progressTrackTexture);
            float width = area.width * Mathf.Clamp01(progress);
            if (width <= 0.0f)
            {
                return;
            }
            GUI.BeginGroup(new Rect(area.x, area.y, width, area.height));
            DrawTexture(
                new Rect(0.0f, 0.0f, area.width, area.height),
                fillTexture ?? _progressFillTexture);
            GUI.EndGroup();
        }

        private void DrawEnergyGauge(
            Rect area,
            float progress,
            Texture fillTexture)
        {
            DrawTexture(area, _energyGaugeFrameTexture);
            DrawProgressBar(
                new Rect(
                    area.x + HomeEnergyGaugeHorizontalInset,
                    area.y + HomeEnergyGaugeVerticalInset,
                    area.width - HomeEnergyGaugeHorizontalInset * 2.0f,
                    area.height - HomeEnergyGaugeVerticalInset * 2.0f),
                progress,
                fillTexture,
                _energyGaugeTrackTexture);
        }

        private void DrawMoodBadge(Rect area, PetPanelModel model)
        {
            if (model.MoodAnimationFrameTexture != null &&
                model.MoodAnimationArrowTexture != null)
            {
                GUI.DrawTexture(
                    area,
                    model.MoodAnimationFrameTexture,
                    ScaleMode.StretchToFill,
                    true);
                DrawAnimatedMoodArrow(area, model);
                return;
            }

            if (model.MoodTexture != null)
            {
                GUI.DrawTexture(
                    area,
                    model.MoodTexture,
                    ScaleMode.StretchToFill,
                    true);
                return;
            }

            GUI.Label(
                area,
                model.MoodLabel,
                LabelStyle(11, FontStyle.Bold, TextAnchor.MiddleCenter));
        }

        private void DrawAnimatedMoodArrow(Rect badgeArea, PetPanelModel model)
        {
            float scaleX = badgeArea.width / MoodBadgeWidth;
            float scaleY = badgeArea.height / MoodBadgeHeight;
            Rect arrowArea = new Rect(
                6.0f * scaleX,
                3.0f * scaleY,
                24.0f * scaleX,
                24.0f * scaleY);

            Vector2 displacement = Vector2.zero;
            Vector2 arrowScale = Vector2.one;
            if (!model.QuietMode)
            {
                if (_moodArrowLoopStartedAt < 0.0f)
                {
                    _moodArrowLoopStartedAt = Time.unscaledTime;
                }
                float loopTime = Mathf.Repeat(
                    Time.unscaledTime - _moodArrowLoopStartedAt,
                    MoodArrowLoopDurationSeconds);
                GetMoodArrowMotion(
                    model.Mood,
                    loopTime,
                    out displacement,
                    out arrowScale);
                displacement.x *= scaleX;
                displacement.y *= scaleY;
            }
            else
            {
                _moodArrowLoopStartedAt = -1.0f;
            }

            Vector2 center = arrowArea.center + displacement;
            // The source arrow points up, so width is perpendicular to its
            // travel axis and height is on the travel axis. Applying the squash
            // before rotating preserves that relationship for all five moods.
            arrowArea.width *= arrowScale.x;
            arrowArea.height *= arrowScale.y;
            arrowArea.center = center;

            GUI.BeginGroup(badgeArea);
            Matrix4x4 previousMatrix = GUI.matrix;
            try
            {
                // GUIUtility.RotateAroundPivot pre-multiplies GUI.matrix in screen
                // space. That loses the outer DesktopWindowLayout scale whenever the
                // native window is not at 100%, sending the arrow outside its badge.
                // Compose on the right so the result still passes through the
                // shared window transform. GUI.matrix remains in the enclosing
                // coordinate space after BeginGroup, so unclip the local center
                // by adding the badge origin before building the rotation.
                Vector2 matrixPivot = badgeArea.position + center;
                Matrix4x4 moveToPivot = Matrix4x4.Translate(
                    new Vector3(matrixPivot.x, matrixPivot.y, 0.0f));
                Matrix4x4 rotate = Matrix4x4.Rotate(
                    Quaternion.Euler(
                        0.0f,
                        0.0f,
                        GetMoodArrowClockwiseRotation(model.Mood)));
                Matrix4x4 moveFromPivot = Matrix4x4.Translate(
                    new Vector3(-matrixPivot.x, -matrixPivot.y, 0.0f));
                GUI.matrix = previousMatrix *
                    moveToPivot * rotate * moveFromPivot;
                GUI.DrawTexture(
                    arrowArea,
                    model.MoodAnimationArrowTexture,
                    ScaleMode.StretchToFill,
                    true);
            }
            finally
            {
                GUI.matrix = previousMatrix;
                GUI.EndGroup();
            }
        }

        private static void GetMoodArrowMotion(
            PetMood mood,
            float loopTime,
            out Vector2 displacement,
            out Vector2 scale)
        {
            displacement = Vector2.zero;
            scale = Vector2.one;
            if (loopTime < 0.60f)
            {
                return;
            }

            if (loopTime < 0.80f)
            {
                float progress = (loopTime - 0.60f) / 0.20f;
                scale = new Vector2(
                    Mathf.SmoothStep(1.0f, 1.017f, progress),
                    Mathf.SmoothStep(1.0f, 0.843f, progress));
                return;
            }

            Vector2 peak = GetMoodArrowPeakDisplacement(mood);
            if (loopTime < 1.02f)
            {
                float progress = (loopTime - 0.80f) / 0.22f;
                displacement = peak * Mathf.SmoothStep(0.0f, 1.0f, progress);
                scale = new Vector2(
                    Mathf.SmoothStep(1.017f, 1.0f, progress),
                    Mathf.SmoothStep(0.843f, 1.0f, progress));
                return;
            }

            if (loopTime < 1.233f)
            {
                float progress = (loopTime - 1.02f) / 0.213f;
                displacement = peak * Mathf.SmoothStep(1.0f, 0.0f, progress);
                return;
            }

            if (loopTime < 1.30f)
            {
                scale.y = Mathf.SmoothStep(
                    1.0f,
                    0.851f,
                    (loopTime - 1.233f) / 0.067f);
                return;
            }

            if (loopTime < 1.50f)
            {
                scale.y = Mathf.SmoothStep(
                    0.851f,
                    1.034f,
                    (loopTime - 1.30f) / 0.20f);
                return;
            }

            if (loopTime < 1.70f)
            {
                scale.y = Mathf.SmoothStep(
                    1.034f,
                    1.0f,
                    (loopTime - 1.50f) / 0.20f);
            }
        }

        private static Vector2 GetMoodArrowPeakDisplacement(PetMood mood)
        {
            switch (mood)
            {
                case PetMood.Awful:
                    return new Vector2(0.0f, 3.1f);
                case PetMood.Bad:
                    return new Vector2(2.2f, 2.2f);
                case PetMood.Normal:
                    return new Vector2(4.0f, 0.0f);
                case PetMood.Good:
                    return new Vector2(2.5f, -2.5f);
                case PetMood.Great:
                    // The source Flash stage has room above the badge. The
                    // desktop sidecar does not, so keep the visible tip inside
                    // the fixed pill while retaining the same upward motion.
                    return new Vector2(0.0f, -3.0f);
                default:
                    return Vector2.zero;
            }
        }

        private static float GetMoodArrowClockwiseRotation(PetMood mood)
        {
            switch (mood)
            {
                case PetMood.Awful:
                    return 180.0f;
                case PetMood.Bad:
                    return 135.0f;
                case PetMood.Normal:
                    return 90.0f;
                case PetMood.Good:
                    return 45.0f;
                case PetMood.Great:
                    return 0.0f;
                default:
                    return 0.0f;
            }
        }

        private Rect GetAnimatedMoodRect(Rect area, PetPanelModel model)
        {
            if (!_hasObservedMood)
            {
                _hasObservedMood = true;
                _observedMood = model.Mood;
            }
            else if (_observedMood != model.Mood)
            {
                _observedMood = model.Mood;
                _moodArrowLoopStartedAt = -1.0f;
                _moodPulseStartedAt = model.QuietMode
                    ? -1.0f
                    : Time.unscaledTime;
            }

            if (model.QuietMode || _moodPulseStartedAt < 0.0f)
            {
                if (model.QuietMode)
                {
                    _moodPulseStartedAt = -1.0f;
                }
                return area;
            }

            float elapsed = Time.unscaledTime - _moodPulseStartedAt;
            if (elapsed < 0.0f || elapsed >= MoodPulseDurationSeconds)
            {
                _moodPulseStartedAt = -1.0f;
                return area;
            }

            float progress = Mathf.Clamp01(elapsed / MoodPulseDurationSeconds);
            float scale;
            if (progress < 0.30f)
            {
                scale = Mathf.SmoothStep(0.82f, 1.12f, progress / 0.30f);
            }
            else if (progress < 0.62f)
            {
                scale = Mathf.SmoothStep(
                    1.12f,
                    0.965f,
                    (progress - 0.30f) / 0.32f);
            }
            else
            {
                scale = Mathf.SmoothStep(
                    0.965f,
                    1.0f,
                    (progress - 0.62f) / 0.38f);
            }

            Vector2 center = area.center;
            center.y -= Mathf.Sin(progress * Mathf.PI) * 3.0f;
            float width = area.width * scale;
            float height = area.height * scale;
            return new Rect(
                center.x - width * 0.5f,
                center.y - height * 0.5f,
                width,
                height);
        }

        private static GUIStyle LabelStyle(
            int size,
            FontStyle fontStyle,
            TextAnchor alignment)
        {
            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = size,
                fontStyle = fontStyle,
                alignment = alignment,
                wordWrap = false,
                clipping = TextClipping.Clip
            };
            style.normal.textColor = TextColor;
            return style;
        }

        private static GUIStyle MutedStyle(int size, TextAnchor alignment)
        {
            GUIStyle style = LabelStyle(size, FontStyle.Normal, alignment);
            style.normal.textColor = MutedTextColor;
            return style;
        }

        private static GUIStyle WrappedStyle(int size, TextAnchor alignment)
        {
            GUIStyle style = MutedStyle(size, alignment);
            style.wordWrap = true;
            style.clipping = TextClipping.Overflow;
            return style;
        }

        private static PetPanelCommand Command(PetPanelCommandType type)
        {
            return new PetPanelCommand { Type = type };
        }

        private static PetPanelCommand Navigate(PetPanelPage page)
        {
            return new PetPanelCommand
            {
                Type = PetPanelCommandType.Navigate,
                Page = page
            };
        }

        private static string FormatTime(double seconds)
        {
            int remaining = Math.Max(0, (int)Math.Ceiling(seconds));
            return (remaining / 60).ToString("00") + ":" +
                (remaining % 60).ToString("00");
        }

        private static string FormatStudyStartLabel(
            int durationSeconds,
            int moniReward)
        {
            return (durationSeconds / 60) + " min · +" + moniReward +
                " Moni · +" +
                PetStudyRewardService.FoodQuantityForDuration(durationSeconds) +
                " Jelly · -" +
                Mathf.RoundToInt(
                    PetStudyRewardService.EnergyCostForDuration(durationSeconds)) +
                " Energy";
        }

        private static string CompactPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || path.Length <= 30)
            {
                return path ?? string.Empty;
            }
            string normalized = path.TrimEnd('\\', '/');
            int last = normalized.LastIndexOfAny(new[] { '\\', '/' });
            if (last <= 0)
            {
                return normalized;
            }
            int previous = normalized.LastIndexOfAny(
                new[] { '\\', '/' },
                last - 1);
            return previous >= 0
                ? "..." + normalized.Substring(previous)
                : normalized;
        }

        private static void DrawTexture(Rect area, Texture texture)
        {
            if (texture == null)
            {
                return;
            }
            Color previous = GUI.color;
            GUI.color = Color.white;
            GUI.DrawTexture(area, texture, ScaleMode.StretchToFill, true);
            GUI.color = previous;
        }

        private static void DrawSolid(Rect area, Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(area, Texture2D.whiteTexture);
            GUI.color = previous;
        }

        private static void DrawTintedIcon(
            Rect area,
            Texture texture,
            Color color)
        {
            if (texture == null)
            {
                return;
            }
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(area, texture, ScaleMode.ScaleToFit, true);
            GUI.color = previous;
        }

        private static Texture2D CreateRoundedTexture(
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
                name = "Uma desktop pet side panel surface",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave
            };
            var pixels = new Color[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    bool inside = IsInsideRoundedRect(
                        x + 0.5f, y + 0.5f, width, height, radius);
                    if (!inside)
                    {
                        pixels[y * width + x] = Color.clear;
                        continue;
                    }
                    bool isBorder = borderWidth > 0 &&
                        !IsInsideRoundedRect(
                            x + 0.5f - borderWidth,
                            y + 0.5f - borderWidth,
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

        private static Texture2D CreateAntialiasedRoundedTexture(
            int width,
            int height,
            float radius,
            Color fill,
            Color border,
            float borderWidth,
            int samplesPerAxis)
        {
            var texture = new Texture2D(
                width,
                height,
                TextureFormat.RGBA32,
                false,
                false)
            {
                name = "Uma desktop pet antialiased rounded UI",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave
            };
            var pixels = new Color[width * height];
            int samples = Mathf.Max(1, samplesPerAxis);
            int sampleCount = samples * samples;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float alphaSum = 0.0f;
                    float redSum = 0.0f;
                    float greenSum = 0.0f;
                    float blueSum = 0.0f;
                    for (int sampleY = 0; sampleY < samples; sampleY++)
                    {
                        for (int sampleX = 0; sampleX < samples; sampleX++)
                        {
                            float pixelX = x + (sampleX + 0.5f) / samples;
                            float pixelY = y + (sampleY + 0.5f) / samples;
                            if (!IsInsideRoundedRect(
                                pixelX,
                                pixelY,
                                width,
                                height,
                                radius))
                            {
                                continue;
                            }

                            bool isBorder = borderWidth > 0.0f &&
                                !IsInsideRoundedRect(
                                    pixelX - borderWidth,
                                    pixelY - borderWidth,
                                    width - borderWidth * 2.0f,
                                    height - borderWidth * 2.0f,
                                    Mathf.Max(0.0f, radius - borderWidth));
                            Color sample = isBorder ? border : fill;
                            alphaSum += sample.a;
                            redSum += sample.r * sample.a;
                            greenSum += sample.g * sample.a;
                            blueSum += sample.b * sample.a;
                        }
                    }

                    if (alphaSum <= 0.0f)
                    {
                        pixels[y * width + x] = Color.clear;
                        continue;
                    }
                    pixels[y * width + x] = new Color(
                        redSum / alphaSum,
                        greenSum / alphaSum,
                        blueSum / alphaSum,
                        alphaSum / sampleCount);
                }
            }
            texture.SetPixels(pixels);
            texture.Apply(false, true);
            return texture;
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
            float r = Mathf.Clamp(radius, 0.0f, Mathf.Min(width, height) * 0.5f);
            float nearestX = Mathf.Clamp(x, r, width - r);
            float nearestY = Mathf.Clamp(y, r, height - r);
            float dx = x - nearestX;
            float dy = y - nearestY;
            return dx * dx + dy * dy <= r * r;
        }

        public void Dispose()
        {
            DestroyTexture(ref _panelTexture);
            DestroyTexture(ref _railTexture);
            DestroyTexture(ref _activeNavTexture);
            DestroyTexture(ref _primaryTexture);
            DestroyTexture(ref _primaryHoverTexture);
            DestroyTexture(ref _secondaryTexture);
            DestroyTexture(ref _secondaryHoverTexture);
            DestroyTexture(ref _disabledTexture);
            DestroyTexture(ref _rowTexture);
            DestroyTexture(ref _homeStatusTexture);
            DestroyTexture(ref _segmentActiveTexture);
            DestroyTexture(ref _itemIconBackgroundTexture);
            DestroyTexture(ref _energyGaugeFrameTexture);
            DestroyTexture(ref _energyGaugeTrackTexture);
            DestroyTexture(ref _progressTrackTexture);
            DestroyTexture(ref _progressFillTexture);
            DestroyTexture(ref _toggleOffTexture);
            DestroyTexture(ref _toggleOnTexture);
            _homeIcon = null;
            _studyIcon = null;
            _shopIcon = null;
            _settingsIcon = null;
            _closeIcon = null;
            _pauseIcon = null;
            _chevronIcon = null;
        }

        private static void DestroyTexture(ref Texture2D texture)
        {
            if (texture == null)
            {
                return;
            }
            UnityEngine.Object.Destroy(texture);
            texture = null;
        }
    }
}
