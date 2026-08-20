using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace UmaDesktopPet.Standalone.Runtime
{
    public enum FocusSessionStatus
    {
        Idle = 0,
        Running = 1,
        Paused = 2,
        RewardReady = 3
    }

    public struct PetFocusSnapshot
    {
        public FocusSessionStatus Status { get; private set; }
        public int SessionDurationSeconds { get; private set; }
        public double RemainingSeconds { get; private set; }
        public int PendingMoni { get; private set; }
        public int Moni { get; private set; }
        public long SpentMoni { get; private set; }
        public long LifetimeMoniEarned { get; private set; }
        public long LifetimeCompletedFocusSeconds { get; private set; }
        public string EquippedDeskItemId { get; private set; }

        private readonly string[] _ownedDeskItemIds;

        internal PetFocusSnapshot(
            FocusSessionStatus status,
            int sessionDurationSeconds,
            double remainingSeconds,
            int pendingMoni,
            int moni,
            long spentMoni,
            long lifetimeMoniEarned,
            long lifetimeCompletedFocusSeconds,
            string[] ownedDeskItemIds,
            string equippedDeskItemId)
        {
            Status = status;
            SessionDurationSeconds = sessionDurationSeconds;
            RemainingSeconds = remainingSeconds;
            PendingMoni = pendingMoni;
            Moni = moni;
            SpentMoni = spentMoni;
            LifetimeMoniEarned = lifetimeMoniEarned;
            LifetimeCompletedFocusSeconds = lifetimeCompletedFocusSeconds;
            _ownedDeskItemIds = ownedDeskItemIds == null
                ? new string[0]
                : (string[])ownedDeskItemIds.Clone();
            EquippedDeskItemId = equippedDeskItemId ?? string.Empty;
        }

        public IReadOnlyList<string> OwnedDeskItemIds
        {
            get { return _ownedDeskItemIds; }
        }

        public bool IsDeskItemOwned(string itemId)
        {
            if (string.IsNullOrEmpty(itemId))
            {
                return false;
            }
            for (int index = 0; index < _ownedDeskItemIds.Length; index++)
            {
                if (string.Equals(
                    _ownedDeskItemIds[index],
                    itemId,
                    StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        public bool CarrotDeskCharmOwned
        {
            get { return IsDeskItemOwned(DeskShopCatalog.CarrotCharmId); }
        }

        internal string[] CopyOwnedDeskItemIds()
        {
            return (string[])_ownedDeskItemIds.Clone();
        }
    }

    /// <summary>
    /// Owns the deliberately small focus-with-your-pet loop. Only time while this
    /// process is running counts; reopening a saved running session pauses it at
    /// the last autosave instead of granting offline progress.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PetFocusState : MonoBehaviour
    {
        public const int ShortSessionSeconds = 25 * 60;
        public const int LongSessionSeconds = 50 * 60;
        public const int ShortSessionReward = 1;
        public const int LongSessionReward = 2;
        public const int CarrotDeskCharmCost = 1;

        private const int CurrentSaveVersion = 2;
        private const string SaveFileName = "pet-focus.json";
        private const string LegacyDeskDonutId = "desk-donut";
        private const double MaximumCountedFrameSeconds = 5.0;

        [Header("Runtime and persistence")]
        [SerializeField] private bool automaticTimeEnabled = true;
        [SerializeField] private bool loadOnAwake = true;
        [SerializeField] private bool saveOnStateChanges = true;
        [SerializeField] private bool saveOnLifecycleEvents = true;
        [SerializeField, Min(1.0f)] private float autosaveIntervalSeconds = 30.0f;

        private FocusSessionStatus _status;
        private int _sessionDurationSeconds;
        private double _remainingSeconds;
        private int _pendingMoni;
        private int _moni;
        private long _spentMoni;
        private long _lifetimeMoniEarned;
        private long _lifetimeCompletedFocusSeconds;
        private readonly HashSet<string> _ownedDeskItemIds =
            new HashSet<string>(StringComparer.Ordinal);
        private string _equippedDeskItemId = string.Empty;
        private double _lastRealtimeSeconds;
        private double _autosaveAccumulator;
        private bool _persistenceWriteBlocked;
        private bool _isQuitting;
        private bool _recordingModeEnabled;
#if UNITY_EDITOR
        private string _persistencePathOverride;
#endif

        public FocusSessionStatus Status { get { return _status; } }
        public int SessionDurationSeconds { get { return _sessionDurationSeconds; } }
        public double RemainingSeconds { get { return _remainingSeconds; } }
        public int PendingMoni { get { return _pendingMoni; } }
        public int Moni { get { return _moni; } }
        public long SpentMoni { get { return _spentMoni; } }
        public long LifetimeMoniEarned { get { return _lifetimeMoniEarned; } }
        public long LifetimeCompletedFocusSeconds
        {
            get { return _lifetimeCompletedFocusSeconds; }
        }
        public string EquippedDeskItemId { get { return _equippedDeskItemId; } }
        public int OwnedDeskItemCount { get { return _ownedDeskItemIds.Count; } }
        public bool IsRecordingMode { get { return _recordingModeEnabled; } }
        public bool CarrotDeskCharmOwned
        {
            get { return IsDeskItemOwned(DeskShopCatalog.CarrotCharmId); }
        }

        public bool IsSessionActive
        {
            get
            {
                return _status == FocusSessionStatus.Running ||
                    _status == FocusSessionStatus.Paused;
            }
        }

        public bool IsRunning
        {
            get { return _status == FocusSessionStatus.Running; }
        }

        public bool CanStartSession
        {
            get { return _status == FocusSessionStatus.Idle; }
        }

        public string PersistencePath
        {
            get
            {
#if UNITY_EDITOR
                if (!string.IsNullOrEmpty(_persistencePathOverride))
                {
                    return _persistencePathOverride;
                }
#endif
                return Path.Combine(Application.persistentDataPath, SaveFileName);
            }
        }

        public PetFocusSnapshot CurrentSnapshot
        {
            get
            {
                return new PetFocusSnapshot(
                    _status,
                    _sessionDurationSeconds,
                    _remainingSeconds,
                    _pendingMoni,
                    _moni,
                    _spentMoni,
                    _lifetimeMoniEarned,
                    _lifetimeCompletedFocusSeconds,
                    GetOwnedDeskItemIdsInCatalogOrder(),
                    _equippedDeskItemId);
            }
        }

        public event Action<PetFocusSnapshot> StateChanged;
        public event Action SessionCompleted;

        private void Awake()
        {
#if UMA_RECORDING_TOOLS
            EnterRecordingMode();
            return;
#endif
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (IsSmokeProcess())
            {
                ResetForSmokeTest();
                return;
            }
#endif
            ResetInternal();
            if (loadOnAwake)
            {
                LoadNow();
            }
            _lastRealtimeSeconds = Time.realtimeSinceStartupAsDouble;
        }

        private void OnEnable()
        {
            _lastRealtimeSeconds = Time.realtimeSinceStartupAsDouble;
            _autosaveAccumulator = 0.0;
        }

        private void Update()
        {
            double now = Time.realtimeSinceStartupAsDouble;
            double elapsedSeconds = Math.Max(0.0, now - _lastRealtimeSeconds);
            _lastRealtimeSeconds = now;

            if (!automaticTimeEnabled || !IsRunning || elapsedSeconds <= 0.0)
            {
                return;
            }

            // A long jump means Windows likely suspended the process. Focus time
            // should be intentional runtime, so preserve the session and pause.
            if (elapsedSeconds > MaximumCountedFrameSeconds)
            {
                PauseSession();
                return;
            }

            AdvanceTime(elapsedSeconds);
            _autosaveAccumulator += elapsedSeconds;
            if (_autosaveAccumulator >= Math.Max(1.0f, autosaveIntervalSeconds))
            {
                _autosaveAccumulator = 0.0;
                SaveNow();
            }
        }

        public bool StartSession(int durationSeconds)
        {
            if (!CanStartSession || !IsSupportedDuration(durationSeconds))
            {
                return false;
            }

            PetFocusSnapshot previous = CurrentSnapshot;
            _status = FocusSessionStatus.Running;
            _sessionDurationSeconds = durationSeconds;
            _remainingSeconds = durationSeconds;
            _pendingMoni = 0;
            _lastRealtimeSeconds = Time.realtimeSinceStartupAsDouble;
            _autosaveAccumulator = 0.0;
            return TryCommit(previous);
        }

        public bool PauseSession()
        {
            if (_status != FocusSessionStatus.Running)
            {
                return false;
            }

            PetFocusSnapshot previous = CurrentSnapshot;
            _status = FocusSessionStatus.Paused;
            _lastRealtimeSeconds = Time.realtimeSinceStartupAsDouble;
            return TryCommit(previous);
        }

        public bool ResumeSession()
        {
            if (_status != FocusSessionStatus.Paused)
            {
                return false;
            }

            PetFocusSnapshot previous = CurrentSnapshot;
            _status = FocusSessionStatus.Running;
            _lastRealtimeSeconds = Time.realtimeSinceStartupAsDouble;
            _autosaveAccumulator = 0.0;
            return TryCommit(previous);
        }

        public bool StopSession()
        {
            if (_status != FocusSessionStatus.Running &&
                _status != FocusSessionStatus.Paused)
            {
                return false;
            }

            PetFocusSnapshot previous = CurrentSnapshot;
            ClearSession();
            return TryCommit(previous);
        }

        public bool CollectReward()
        {
            if (_status != FocusSessionStatus.RewardReady || _pendingMoni <= 0)
            {
                return false;
            }

            if ((long)_moni + _pendingMoni > int.MaxValue)
            {
                return false;
            }
            PetFocusSnapshot previous = CurrentSnapshot;
            _moni += _pendingMoni;
            ClearSession();
            return TryCommit(previous);
        }

        public bool PurchaseCarrotDeskCharm()
        {
            return PurchaseDeskItem(DeskShopCatalog.CarrotCharmId);
        }

        public bool IsDeskItemOwned(string itemId)
        {
            return !string.IsNullOrEmpty(itemId) &&
                _ownedDeskItemIds.Contains(itemId);
        }

        public bool CanPurchaseDeskItem(string itemId)
        {
            DeskShopItem item;
            return DeskShopCatalog.TryGet(itemId, out item) &&
                !_ownedDeskItemIds.Contains(item.Id) &&
                _moni >= item.Cost;
        }

        /// <summary>
        /// Buys one permanent desk item and equips it in the same transaction.
        /// Rewards are shared across characters; placement remains character-
        /// specific through the attachment rig.
        /// </summary>
        public bool PurchaseDeskItem(string itemId)
        {
            DeskShopItem item;
            if (!DeskShopCatalog.TryGet(itemId, out item) ||
                _ownedDeskItemIds.Contains(item.Id) ||
                _moni < item.Cost)
            {
                return false;
            }

            PetFocusSnapshot previous = CurrentSnapshot;
            _moni -= item.Cost;
            _spentMoni += item.Cost;
            _ownedDeskItemIds.Add(item.Id);
            _equippedDeskItemId = item.Id;
            return TryCommit(previous);
        }

        public bool EquipDeskItem(string itemId)
        {
            if (string.IsNullOrEmpty(itemId) ||
                !_ownedDeskItemIds.Contains(itemId) ||
                string.Equals(
                    _equippedDeskItemId,
                    itemId,
                    StringComparison.Ordinal))
            {
                return false;
            }

            PetFocusSnapshot previous = CurrentSnapshot;
            _equippedDeskItemId = itemId;
            return TryCommit(previous);
        }

        public bool ClearEquippedDeskItem()
        {
            if (string.IsNullOrEmpty(_equippedDeskItemId))
            {
                return false;
            }

            PetFocusSnapshot previous = CurrentSnapshot;
            _equippedDeskItemId = string.Empty;
            return TryCommit(previous);
        }

        public void AdvanceTime(double elapsedSeconds)
        {
            ValidateElapsedSeconds(elapsedSeconds);
            if (_status != FocusSessionStatus.Running || elapsedSeconds <= 0.0)
            {
                return;
            }

            PetFocusSnapshot previous = CurrentSnapshot;
            _remainingSeconds = Math.Max(0.0, _remainingSeconds - elapsedSeconds);
            if (_remainingSeconds > 0.0)
            {
                return;
            }

            _remainingSeconds = 0.0;
            int earnedMoni = RewardForDuration(_sessionDurationSeconds);
            if ((long)_moni + earnedMoni > int.MaxValue ||
                _lifetimeMoniEarned > long.MaxValue - earnedMoni ||
                _lifetimeCompletedFocusSeconds >
                    long.MaxValue - _sessionDurationSeconds)
            {
                RestoreSnapshot(previous);
                _status = FocusSessionStatus.Paused;
                PublishStateChanged();
                Debug.LogWarning(
                    "The Moni wallet or lifetime focus counters reached their " +
                    "maximum value; " +
                    "the session was left paused instead of losing its reward.");
                return;
            }
            _pendingMoni = earnedMoni;
            _lifetimeMoniEarned += _pendingMoni;
            _lifetimeCompletedFocusSeconds += _sessionDurationSeconds;
            _status = FocusSessionStatus.RewardReady;
            if (!TryCommit(previous))
            {
                _status = FocusSessionStatus.Paused;
                PublishStateChanged();
                return;
            }
            Action completed = SessionCompleted;
            if (completed != null)
            {
                completed();
            }
        }

        public void SetAutomaticTimeEnabled(bool enabled)
        {
            automaticTimeEnabled = enabled;
            _lastRealtimeSeconds = Time.realtimeSinceStartupAsDouble;
            _autosaveAccumulator = 0.0;
        }

        public void SetPersistenceEnabled(bool enabled)
        {
            if (_recordingModeEnabled && enabled)
            {
                return;
            }
            saveOnStateChanges = enabled;
            saveOnLifecycleEvents = enabled;
        }

        internal void ResetForSmokeTest()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            _recordingModeEnabled = false;
            automaticTimeEnabled = false;
            saveOnStateChanges = false;
            saveOnLifecycleEvents = false;
            ResetInternal();
            _lastRealtimeSeconds = Time.realtimeSinceStartupAsDouble;
            PublishStateChanged();
#else
            throw new InvalidOperationException(
                "Focus smoke reset is unavailable in a release build.");
#endif
        }

        /// <summary>
        /// Starts an isolated, in-memory focus state for the local recording
        /// player. The normal save is never changed while this mode is active.
        /// </summary>
        public void EnterRecordingMode()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD || UMA_RECORDING_TOOLS
            automaticTimeEnabled = true;
            saveOnStateChanges = false;
            saveOnLifecycleEvents = false;
            ResetInternal();
            _recordingModeEnabled = true;
            _lastRealtimeSeconds = Time.realtimeSinceStartupAsDouble;
            PublishStateChanged();
#else
            throw new InvalidOperationException(
                "Recording focus controls are unavailable in this build.");
#endif
        }

        /// <summary>
        /// Stages a running 25-minute session at an exact remaining time. This
        /// is intentionally not a public-release cheat and is compiled for the
        /// local recording build only.
        /// </summary>
        public bool SetStudyRemainingForRecording(int remainingSeconds)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD || UMA_RECORDING_TOOLS
            if (!_recordingModeEnabled ||
                remainingSeconds <= 0 ||
                remainingSeconds > ShortSessionSeconds ||
                _status == FocusSessionStatus.RewardReady)
            {
                return false;
            }

            _status = FocusSessionStatus.Running;
            _sessionDurationSeconds = ShortSessionSeconds;
            _remainingSeconds = remainingSeconds;
            _pendingMoni = 0;
            _lastRealtimeSeconds = Time.realtimeSinceStartupAsDouble;
            _autosaveAccumulator = 0.0;
            PublishStateChanged();
            return true;
#else
            return false;
#endif
        }

        /// <summary>
        /// Completes the staged session through the normal completion path so
        /// desk motion, care rewards, and completion UI are exercised for real.
        /// </summary>
        public bool CompleteStudyForRecording()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD || UMA_RECORDING_TOOLS
            if (!_recordingModeEnabled ||
                (_status != FocusSessionStatus.Running &&
                 _status != FocusSessionStatus.Paused))
            {
                return false;
            }

            if (_status == FocusSessionStatus.Paused)
            {
                _status = FocusSessionStatus.Running;
            }
            AdvanceTime(_remainingSeconds);
            return _status == FocusSessionStatus.RewardReady;
#else
            return false;
#endif
        }

        /// <summary>
        /// Adds temporary Moni while preserving the focus accounting invariant.
        /// </summary>
        public bool GrantMoniForRecording(int amount)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD || UMA_RECORDING_TOOLS
            long completedSeconds = (long)amount * ShortSessionSeconds;
            if (!_recordingModeEnabled || amount <= 0 ||
                _status == FocusSessionStatus.RewardReady ||
                (long)_moni + amount > int.MaxValue ||
                _lifetimeMoniEarned > long.MaxValue - amount ||
                _lifetimeCompletedFocusSeconds >
                    long.MaxValue - completedSeconds)
            {
                return false;
            }

            _moni += amount;
            _lifetimeMoniEarned += amount;
            _lifetimeCompletedFocusSeconds += completedSeconds;
            PublishStateChanged();
            return true;
#else
            return false;
#endif
        }

        /// <summary>
        /// Clears temporary desk ownership without changing the staged wallet.
        /// </summary>
        public bool ResetDeskCollectionForRecording()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD || UMA_RECORDING_TOOLS
            if (!_recordingModeEnabled ||
                (_ownedDeskItemIds.Count == 0 &&
                 string.IsNullOrEmpty(_equippedDeskItemId)))
            {
                return false;
            }

            _ownedDeskItemIds.Clear();
            _equippedDeskItemId = string.Empty;
            PublishStateChanged();
            return true;
#else
            return false;
#endif
        }

        /// <summary>
        /// Returns all temporary focus/shop values to their recording defaults.
        /// </summary>
        public void ResetRecordingState()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD || UMA_RECORDING_TOOLS
            if (!_recordingModeEnabled)
            {
                throw new InvalidOperationException(
                    "Recording mode must be entered before resetting it.");
            }

            ResetInternal();
            _recordingModeEnabled = true;
            automaticTimeEnabled = true;
            saveOnStateChanges = false;
            saveOnLifecycleEvents = false;
            _lastRealtimeSeconds = Time.realtimeSinceStartupAsDouble;
            PublishStateChanged();
#else
            throw new InvalidOperationException(
                "Recording focus controls are unavailable in this build.");
#endif
        }

#if UNITY_EDITOR
        public void SetPersistencePathForSmokeTest(string path)
        {
            _persistencePathOverride = path;
            _persistenceWriteBlocked = false;
        }
#endif

        public bool LoadNow()
        {
            if (_recordingModeEnabled)
            {
                return false;
            }
            string path = PersistencePath;
            if (!File.Exists(path))
            {
                return false;
            }

            try
            {
                string json = File.ReadAllText(path, Encoding.UTF8);
                string error;
                if (TryRestoreFromJson(json, out error))
                {
                    _persistenceWriteBlocked = false;
                    return true;
                }
                _persistenceWriteBlocked = true;
                Debug.LogWarning("Ignoring invalid focus save: " + error);
            }
            catch (Exception exception)
            {
                _persistenceWriteBlocked = true;
                Debug.LogWarning(
                    "Could not read the focus save; using current values. " +
                    exception.Message);
            }
            return false;
        }

        public bool SaveNow()
        {
            if (_recordingModeEnabled)
            {
                return true;
            }
            if (_persistenceWriteBlocked)
            {
                Debug.LogWarning(
                    "Focus saving is paused because the existing save could not " +
                    "be read. The unreadable file was left untouched.");
                return false;
            }

            string path = PersistencePath;
            string temporaryPath = path + ".tmp";
            try
            {
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                File.WriteAllText(
                    temporaryPath,
                    CreateSaveJson(),
                    new UTF8Encoding(false));
                File.Copy(temporaryPath, path, true);
                // The durable copy already succeeded. A stale temporary file is
                // harmless and should not roll the in-memory transaction back.
                TryDeleteTemporaryFile(temporaryPath);
                return true;
            }
            catch (Exception exception)
            {
                TryDeleteTemporaryFile(temporaryPath);
                Debug.LogWarning("Could not save focus state: " + exception.Message);
                return false;
            }
        }

        public string CreateSaveJson()
        {
            var data = new SaveDataV2
            {
                version = CurrentSaveVersion,
                status = (int)_status,
                sessionDurationSeconds = _sessionDurationSeconds,
                remainingSeconds = _remainingSeconds,
                pendingMoni = _pendingMoni,
                moni = _moni,
                spentMoni = _spentMoni,
                lifetimeMoniEarned = _lifetimeMoniEarned,
                lifetimeCompletedFocusSeconds =
                    _lifetimeCompletedFocusSeconds,
                ownedDeskItemIds = GetOwnedDeskItemIdsInCatalogOrder(),
                equippedDeskItemId = _equippedDeskItemId
            };
            return JsonUtility.ToJson(data, true);
        }

        public bool TryRestoreFromJson(string json, out string error)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                error = "The save is empty.";
                return false;
            }

            SaveVersionHeader header;
            try
            {
                header = JsonUtility.FromJson<SaveVersionHeader>(json);
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }

            if (header == null)
            {
                error = "The save contains no version.";
                return false;
            }

            if (header.version == 1)
            {
                return TryRestoreV1(json, out error);
            }
            if (header.version == CurrentSaveVersion)
            {
                return TryRestoreV2(json, out error);
            }

            error = "The focus save version is not supported.";
            return false;
        }

        private bool TryRestoreV1(string json, out string error)
        {
            error = null;
            SaveDataV1 data;
            try
            {
                data = JsonUtility.FromJson<SaveDataV1>(json);
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }

            if (data == null || data.version != 1 ||
                !ValidateSessionValues(
                    data.status,
                    data.sessionDurationSeconds,
                    data.remainingSeconds,
                    data.pendingMoni,
                    data.moni,
                    out error))
            {
                if (string.IsNullOrEmpty(error))
                {
                    error = "The version 1 save contains invalid values.";
                }
                return false;
            }

            long spent = data.carrotDeskCharmOwned
                ? DeskShopCatalog.CarrotCharm.Cost
                : 0;
            long lifetimeMoni = (long)data.moni + data.pendingMoni + spent;
            if (lifetimeMoni < 0 ||
                lifetimeMoni > long.MaxValue / ShortSessionSeconds)
            {
                error = "The version 1 lifetime totals overflow.";
                return false;
            }

            string[] owned = data.carrotDeskCharmOwned
                ? new[] { DeskShopCatalog.CarrotCharmId }
                : new string[0];
            string equipped = data.carrotDeskCharmOwned
                ? DeskShopCatalog.CarrotCharmId
                : string.Empty;
            ApplyRestoredState(
                data.status,
                data.sessionDurationSeconds,
                data.remainingSeconds,
                data.pendingMoni,
                data.moni,
                spent,
                lifetimeMoni,
                lifetimeMoni * ShortSessionSeconds,
                owned,
                equipped);
            error = null;
            return true;
        }

        private bool TryRestoreV2(string json, out string error)
        {
            error = null;
            SaveDataV2 data;
            try
            {
                data = JsonUtility.FromJson<SaveDataV2>(json);
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }

            if (data == null || data.version != CurrentSaveVersion ||
                !ValidateSessionValues(
                    data.status,
                    data.sessionDurationSeconds,
                    data.remainingSeconds,
                    data.pendingMoni,
                    data.moni,
                    out error) ||
                data.lifetimeMoniEarned < 0 ||
                data.spentMoni < 0 ||
                data.lifetimeCompletedFocusSeconds < 0)
            {
                if (string.IsNullOrEmpty(error))
                {
                    error = "The version 2 save contains invalid values.";
                }
                return false;
            }

            string[] savedOwned = data.ownedDeskItemIds ?? new string[0];
            var owned = new string[savedOwned.Length];
            var uniqueOwned = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < savedOwned.Length; index++)
            {
                string id = MigrateLegacyDeskItemId(savedOwned[index]);
                DeskShopItem item;
                if (!DeskShopCatalog.TryGet(id, out item) ||
                    !uniqueOwned.Add(id))
                {
                    error = "The save contains an unknown or duplicate desk item.";
                    return false;
                }
                owned[index] = id;
            }

            string equipped = MigrateLegacyDeskItemId(
                data.equippedDeskItemId ?? string.Empty);
            if (!string.IsNullOrEmpty(equipped) &&
                !uniqueOwned.Contains(equipped))
            {
                error = "The equipped desk item is not owned.";
                return false;
            }

            long accountedMoni =
                (long)data.moni + data.pendingMoni + data.spentMoni;
            if (accountedMoni != data.lifetimeMoniEarned ||
                data.lifetimeMoniEarned >
                    long.MaxValue / ShortSessionSeconds ||
                data.lifetimeCompletedFocusSeconds !=
                    data.lifetimeMoniEarned * ShortSessionSeconds)
            {
                error = "The save contains inconsistent lifetime totals.";
                return false;
            }

            ApplyRestoredState(
                data.status,
                data.sessionDurationSeconds,
                data.remainingSeconds,
                data.pendingMoni,
                data.moni,
                data.spentMoni,
                data.lifetimeMoniEarned,
                data.lifetimeCompletedFocusSeconds,
                owned,
                equipped);
            error = null;
            return true;
        }

        private static string MigrateLegacyDeskItemId(string itemId)
        {
            // Unreleased local builds briefly saved the cost-two reward as a
            // desk donut. Preserve ownership/equip state while replacing it
            // with the cost-two Tazuna pen; the stored accounting is unchanged.
            return string.Equals(
                itemId,
                LegacyDeskDonutId,
                StringComparison.Ordinal)
                ? DeskShopCatalog.TazunaRedPenId
                : itemId;
        }

        private static bool ValidateSessionValues(
            int statusValue,
            int durationSeconds,
            double remainingSeconds,
            int pendingMoni,
            int moni,
            out string error)
        {
            if (!Enum.IsDefined(typeof(FocusSessionStatus), statusValue) ||
                moni < 0 || pendingMoni < 0 ||
                (long)moni + pendingMoni > int.MaxValue ||
                !IsFinite(remainingSeconds))
            {
                error = "The save contains invalid session values.";
                return false;
            }

            if (!IsConsistent(
                (FocusSessionStatus)statusValue,
                durationSeconds,
                remainingSeconds,
                pendingMoni))
            {
                error = "The save contains an inconsistent session.";
                return false;
            }

            error = null;
            return true;
        }

        private void ApplyRestoredState(
            int statusValue,
            int durationSeconds,
            double remainingSeconds,
            int pendingMoni,
            int moni,
            long spentMoni,
            long lifetimeMoniEarned,
            long lifetimeCompletedFocusSeconds,
            string[] ownedDeskItemIds,
            string equippedDeskItemId)
        {
            FocusSessionStatus restoredStatus =
                (FocusSessionStatus)statusValue;
            _status = restoredStatus == FocusSessionStatus.Running
                ? FocusSessionStatus.Paused
                : restoredStatus;
            _sessionDurationSeconds = durationSeconds;
            _remainingSeconds = remainingSeconds;
            _pendingMoni = pendingMoni;
            _moni = moni;
            _spentMoni = spentMoni;
            _lifetimeMoniEarned = lifetimeMoniEarned;
            _lifetimeCompletedFocusSeconds = lifetimeCompletedFocusSeconds;
            _ownedDeskItemIds.Clear();
            if (ownedDeskItemIds != null)
            {
                for (int index = 0; index < ownedDeskItemIds.Length; index++)
                {
                    _ownedDeskItemIds.Add(ownedDeskItemIds[index]);
                }
            }
            _equippedDeskItemId = equippedDeskItemId ?? string.Empty;
            _lastRealtimeSeconds = Time.realtimeSinceStartupAsDouble;
            _autosaveAccumulator = 0.0;
            PublishStateChanged();
        }

        public static int RewardForDuration(int durationSeconds)
        {
            if (durationSeconds == ShortSessionSeconds)
            {
                return ShortSessionReward;
            }
            if (durationSeconds == LongSessionSeconds)
            {
                return LongSessionReward;
            }
            throw new ArgumentOutOfRangeException(
                "durationSeconds",
                "Only 25- and 50-minute focus sessions are supported.");
        }

        private static bool IsSupportedDuration(int durationSeconds)
        {
            return durationSeconds == ShortSessionSeconds ||
                durationSeconds == LongSessionSeconds;
        }

        private static bool IsConsistent(
            FocusSessionStatus status,
            int durationSeconds,
            double remainingSeconds,
            int pendingMoni)
        {
            if (status == FocusSessionStatus.Idle)
            {
                return durationSeconds == 0 && remainingSeconds == 0.0 &&
                    pendingMoni == 0;
            }
            if (!IsSupportedDuration(durationSeconds))
            {
                return false;
            }
            if (status == FocusSessionStatus.Running ||
                status == FocusSessionStatus.Paused)
            {
                return remainingSeconds > 0.0 &&
                    remainingSeconds <= durationSeconds && pendingMoni == 0;
            }
            return status == FocusSessionStatus.RewardReady &&
                remainingSeconds == 0.0 &&
                pendingMoni == RewardForDuration(durationSeconds);
        }

        private void ClearSession()
        {
            _status = FocusSessionStatus.Idle;
            _sessionDurationSeconds = 0;
            _remainingSeconds = 0.0;
            _pendingMoni = 0;
            _lastRealtimeSeconds = Time.realtimeSinceStartupAsDouble;
            _autosaveAccumulator = 0.0;
        }

        private void ResetInternal()
        {
            _status = FocusSessionStatus.Idle;
            _sessionDurationSeconds = 0;
            _remainingSeconds = 0.0;
            _pendingMoni = 0;
            _moni = 0;
            _spentMoni = 0;
            _lifetimeMoniEarned = 0;
            _lifetimeCompletedFocusSeconds = 0;
            _ownedDeskItemIds.Clear();
            _equippedDeskItemId = string.Empty;
            _autosaveAccumulator = 0.0;
            _persistenceWriteBlocked = false;
        }

        private bool TryCommit(PetFocusSnapshot previous)
        {
            if (saveOnStateChanges && !SaveNow())
            {
                RestoreSnapshot(previous);
                return false;
            }
            PublishStateChanged();
            return true;
        }

        private void RestoreSnapshot(PetFocusSnapshot snapshot)
        {
            _status = snapshot.Status;
            _sessionDurationSeconds = snapshot.SessionDurationSeconds;
            _remainingSeconds = snapshot.RemainingSeconds;
            _pendingMoni = snapshot.PendingMoni;
            _moni = snapshot.Moni;
            _spentMoni = snapshot.SpentMoni;
            _lifetimeMoniEarned = snapshot.LifetimeMoniEarned;
            _lifetimeCompletedFocusSeconds =
                snapshot.LifetimeCompletedFocusSeconds;
            _ownedDeskItemIds.Clear();
            string[] owned = snapshot.CopyOwnedDeskItemIds();
            for (int index = 0; index < owned.Length; index++)
            {
                _ownedDeskItemIds.Add(owned[index]);
            }
            _equippedDeskItemId = snapshot.EquippedDeskItemId;
            _lastRealtimeSeconds = Time.realtimeSinceStartupAsDouble;
            _autosaveAccumulator = 0.0;
        }

        private void PublishStateChanged()
        {
            Action<PetFocusSnapshot> changed = StateChanged;
            if (changed != null)
            {
                changed(CurrentSnapshot);
            }
        }

        private static void ValidateElapsedSeconds(double elapsedSeconds)
        {
            if (!IsFinite(elapsedSeconds) || elapsedSeconds < 0.0)
            {
                throw new ArgumentOutOfRangeException(
                    "elapsedSeconds",
                    "Elapsed time must be finite and non-negative.");
            }
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static bool IsSmokeProcess()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            string[] arguments = Environment.GetCommandLineArgs();
            for (int index = 0; index < arguments.Length; index++)
            {
                string argument = arguments[index];
                if (argument != null &&
                    argument.StartsWith(
                        "--smoke-",
                        StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
#endif
            return false;
        }

        private static void TryDeleteTemporaryFile(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // Keep the original save error as the useful diagnostic.
            }
        }

        private string[] GetOwnedDeskItemIdsInCatalogOrder()
        {
            var result = new List<string>(_ownedDeskItemIds.Count);
            IReadOnlyList<DeskShopItem> items = DeskShopCatalog.Items;
            for (int index = 0; index < items.Count; index++)
            {
                if (_ownedDeskItemIds.Contains(items[index].Id))
                {
                    result.Add(items[index].Id);
                }
            }
            return result.ToArray();
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused)
            {
                PauseAndSaveForLifecycle();
            }
        }

        private void OnApplicationQuit()
        {
            _isQuitting = true;
            PauseAndSaveForLifecycle();
        }

        private void OnDisable()
        {
            if (!_isQuitting)
            {
                PauseAndSaveForLifecycle();
            }
        }

        private void PauseAndSaveForLifecycle()
        {
            if (!saveOnLifecycleEvents)
            {
                return;
            }
            if (_status == FocusSessionStatus.Running)
            {
                _status = FocusSessionStatus.Paused;
                PublishStateChanged();
            }
            SaveNow();
        }

        [Serializable]
        private sealed class SaveVersionHeader
        {
            public int version;
        }

        [Serializable]
        private sealed class SaveDataV1
        {
            public int version;
            public int status;
            public int sessionDurationSeconds;
            public double remainingSeconds;
            public int pendingMoni;
            public int moni;
            public bool carrotDeskCharmOwned;
        }

        [Serializable]
        private sealed class SaveDataV2
        {
            public int version;
            public int status;
            public int sessionDurationSeconds;
            public double remainingSeconds;
            public int pendingMoni;
            public int moni;
            public long spentMoni;
            public long lifetimeMoniEarned;
            public long lifetimeCompletedFocusSeconds;
            public string[] ownedDeskItemIds;
            public string equippedDeskItemId;
        }
    }
}
