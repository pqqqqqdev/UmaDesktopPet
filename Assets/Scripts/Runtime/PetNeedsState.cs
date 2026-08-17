using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace UmaDesktopPet.Standalone.Runtime
{
    /// <summary>
    /// Matches the five ordered Mood states shown by Umamusume. Values are kept
    /// stable because they are persisted in the desktop pet's local save.
    /// </summary>
    public enum PetMood
    {
        Awful = 1,
        Bad = 2,
        Normal = 3,
        Good = 4,
        Great = 5
    }

    public enum PetCareAction
    {
        Pat,
        Feed
    }

    /// <summary>
    /// Immutable view of the pet's gentle, game-inspired state.
    /// </summary>
    public struct PetNeedsSnapshot
    {
        public PetMood Mood { get; private set; }
        public float Energy { get; private set; }
        public bool QuietMode { get; private set; }
        public double PatCooldownRemainingSeconds { get; private set; }
        public double FeedCooldownRemainingSeconds { get; private set; }

        internal PetNeedsSnapshot(
            PetMood mood,
            float energy,
            bool quietMode,
            double patCooldownRemainingSeconds,
            double feedCooldownRemainingSeconds)
        {
            Mood = mood;
            Energy = energy;
            QuietMode = quietMode;
            PatCooldownRemainingSeconds = patCooldownRemainingSeconds;
            FeedCooldownRemainingSeconds = feedCooldownRemainingSeconds;
        }
    }

    /// <summary>
    /// Owns the desktop pet's local Mood and Energy. Mood is a five-state value,
    /// while Energy is a visible 0-100 bar. Closing the app never lowers either
    /// value, and reaching zero never harms, kills, or locks the pet.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PetNeedsState : MonoBehaviour
    {
        private const int CurrentSaveVersion = 2;
        private const string SaveFileName = "pet-needs.json";
        private const string LegacyBackupFileName = "pet-needs.v1.json.bak";

        [Header("Starting values")]
        [SerializeField] private PetMood startingMood = PetMood.Normal;
        [SerializeField, Range(0.0f, 100.0f)] private float startingEnergy = 84.0f;

        [Header("Care actions")]
        [SerializeField, Range(0, 4)] private int patMoodGainSteps = 1;
        [SerializeField, Min(0.0f)] private float patReactionEnergyCost = 0.5f;
        // Care cooldowns are intentionally disabled for the current prototype.
        // Keeping the serialized fields preserves save compatibility and makes
        // it straightforward to tune them again later if the care loop needs it.
        [SerializeField, Min(0.0f)] private float patCooldownSeconds = 0.0f;
        [SerializeField, Range(0, 4)] private int feedMoodGainSteps = 1;
        [SerializeField, Min(0.0f)] private float feedEnergyGain = 18.0f;
        [SerializeField, Min(0.0f)] private float feedCooldownSeconds = 0.0f;

        [Header("Other reactions")]
        [SerializeField, Min(0.0f)] private float tapReactionEnergyCost = 0.5f;
        [SerializeField, Range(0.0f, 100.0f)] private float lowEnergyThreshold = 20.0f;

        [Header("Runtime and persistence")]
        [SerializeField] private bool automaticTimeEnabled = true;
        [SerializeField] private bool loadOnAwake = true;
        [SerializeField] private bool saveOnLifecycleEvents = true;
        [SerializeField, Min(0.1f)] private float automaticTickSeconds = 1.0f;
        [SerializeField, Min(1.0f)] private float autosaveIntervalSeconds = 60.0f;

        private PetMood _mood;
        private float _energy;
        private bool _quietMode;
        private double _patCooldownRemainingSeconds;
        private double _feedCooldownRemainingSeconds;
        private double _lastRealtimeSeconds;
        private double _automaticTimeAccumulator;
        private double _autosaveAccumulator;
        private bool _wasPaused;
        private bool _isQuitting;
        private bool _persistenceWriteBlocked;

        public PetMood Mood { get { return _mood; } }
        public string MoodLabel { get { return GetMoodLabel(_mood); } }
        public float Energy { get { return _energy; } }
        public bool QuietMode { get { return _quietMode; } }
        public bool IsLowEnergy { get { return _energy <= lowEnergyThreshold; } }

        public double PatCooldownRemainingSeconds
        {
            get { return _patCooldownRemainingSeconds; }
        }

        public double FeedCooldownRemainingSeconds
        {
            get { return _feedCooldownRemainingSeconds; }
        }

        public bool CanPat { get { return _patCooldownRemainingSeconds <= 0.0; } }
        public bool CanFeed { get { return _feedCooldownRemainingSeconds <= 0.0; } }

        public string PersistencePath
        {
            get { return Path.Combine(Application.persistentDataPath, SaveFileName); }
        }

        public PetNeedsSnapshot CurrentSnapshot
        {
            get
            {
                return new PetNeedsSnapshot(
                    _mood,
                    _energy,
                    _quietMode,
                    _patCooldownRemainingSeconds,
                    _feedCooldownRemainingSeconds);
            }
        }

        /// <summary>
        /// Raised for Mood, Energy, cooldown, or quiet-mode changes.
        /// </summary>
        public event Action<PetNeedsSnapshot> StateChanged;

        /// <summary>
        /// Raised only after a care action passes its cooldown and is accepted.
        /// </summary>
        public event Action<PetCareAction> CareActionApplied;

        private void Awake()
        {
            ResetNeedsInternal();
            if (loadOnAwake)
            {
                LoadNow();
            }
            _lastRealtimeSeconds = Time.realtimeSinceStartupAsDouble;
        }

        private void OnEnable()
        {
            _lastRealtimeSeconds = Time.realtimeSinceStartupAsDouble;
            _automaticTimeAccumulator = 0.0;
        }

        private void Update()
        {
            double now = Time.realtimeSinceStartupAsDouble;
            double elapsedSeconds = Math.Max(0.0, now - _lastRealtimeSeconds);
            _lastRealtimeSeconds = now;

            if (!automaticTimeEnabled || elapsedSeconds <= 0.0)
            {
                return;
            }

            _automaticTimeAccumulator += elapsedSeconds;
            _autosaveAccumulator += elapsedSeconds;
            if (_automaticTimeAccumulator >= Math.Max(0.1, automaticTickSeconds))
            {
                double step = _automaticTimeAccumulator;
                _automaticTimeAccumulator = 0.0;
                AdvanceTime(step);
            }

            if (_autosaveAccumulator >= Math.Max(1.0, autosaveIntervalSeconds))
            {
                _autosaveAccumulator = 0.0;
                SaveNow();
            }
        }

        /// <summary>
        /// Advances care-action cooldowns. Mood and Energy never decay with time.
        /// </summary>
        public void AdvanceTime(double elapsedSeconds)
        {
            ValidateElapsedSeconds(elapsedSeconds, "elapsedSeconds");
            ApplyElapsedTime(elapsedSeconds, true);
        }

        /// <summary>
        /// Advances cooldowns for time spent away. It deliberately does not change
        /// Mood or Energy, so returning to the pet never carries a punishment.
        /// </summary>
        public void AdvanceOfflineTime(double elapsedSeconds)
        {
            ValidateElapsedSeconds(elapsedSeconds, "elapsedSeconds");
            ApplyElapsedTime(elapsedSeconds, true);
        }

        public void RecordTapReaction()
        {
            ChangeEnergy(-Math.Max(0.0f, tapReactionEnergyCost));
        }

        public void RecordAmbientReaction()
        {
            // Ambient animation is personality, not a care action. It must not
            // silently drain Energy and eventually make the pet turn inert.
        }

        public bool TryPat()
        {
            if (!CanPat)
            {
                return false;
            }

            _mood = PromoteMood(_mood, patMoodGainSteps);
            _energy = ClampEnergy(
                _energy - Math.Max(0.0f, patReactionEnergyCost));
            _patCooldownRemainingSeconds = Math.Max(0.0, patCooldownSeconds);
            PublishStateChanged();
            RaiseCareActionApplied(PetCareAction.Pat);
            return true;
        }

        public bool TryFeed()
        {
            if (!CanFeed)
            {
                return false;
            }

            _mood = PromoteMood(_mood, feedMoodGainSteps);
            _energy = ClampEnergy(
                _energy + Math.Max(0.0f, feedEnergyGain));
            _feedCooldownRemainingSeconds = Math.Max(0.0, feedCooldownSeconds);
            PublishStateChanged();
            RaiseCareActionApplied(PetCareAction.Feed);
            return true;
        }

        public void SetQuietMode(bool quietMode)
        {
            if (_quietMode == quietMode)
            {
                return;
            }
            _quietMode = quietMode;
            PublishStateChanged();
        }

        public void SetAutomaticTimeEnabled(bool enabled)
        {
            automaticTimeEnabled = enabled;
            _lastRealtimeSeconds = Time.realtimeSinceStartupAsDouble;
            _automaticTimeAccumulator = 0.0;
        }

        /// <summary>
        /// Restores configured starting state and clears cooldowns. Quiet mode is a
        /// user preference and is intentionally preserved.
        /// </summary>
        public void ResetNeeds()
        {
            ResetNeedsInternal();
            PublishStateChanged();
        }

        public bool LoadNow()
        {
            return LoadNow(UtcNowUnixSeconds());
        }

        public bool LoadNow(long utcUnixSeconds)
        {
            ValidateUnixSeconds(utcUnixSeconds, "utcUnixSeconds");
            string path = PersistencePath;
            if (!File.Exists(path))
            {
                return false;
            }

            try
            {
                string json = File.ReadAllText(path, Encoding.UTF8);
                int saveVersion;
                if (!TryReadSaveVersion(json, out saveVersion))
                {
                    _persistenceWriteBlocked = true;
                    Debug.LogWarning("Ignoring invalid pet-state save: missing version.");
                    return false;
                }

                string error;
                if (TryRestoreFromJson(json, utcUnixSeconds, out error))
                {
                    _persistenceWriteBlocked = false;
                    if (saveVersion == 1)
                    {
                        if (!TryBackupLegacySave(json))
                        {
                            _persistenceWriteBlocked = true;
                            return true;
                        }
                        SaveNow(utcUnixSeconds);
                    }
                    return true;
                }
                _persistenceWriteBlocked = true;
                Debug.LogWarning("Ignoring invalid pet-state save: " + error);
            }
            catch (Exception exception)
            {
                _persistenceWriteBlocked = true;
                Debug.LogWarning(
                    "Could not read the pet-state save; using current values. " +
                    exception.Message);
            }
            return false;
        }

        public bool SaveNow()
        {
            return SaveNow(UtcNowUnixSeconds());
        }

        public bool SaveNow(long utcUnixSeconds)
        {
            ValidateUnixSeconds(utcUnixSeconds, "utcUnixSeconds");
            if (_persistenceWriteBlocked)
            {
                Debug.LogWarning(
                    "Pet-state saving is paused because the existing save " +
                    "could not be read. The unreadable file was left untouched.");
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
                    CreateSaveJson(utcUnixSeconds),
                    new UTF8Encoding(false));
                File.Copy(temporaryPath, path, true);
                File.Delete(temporaryPath);
                return true;
            }
            catch (Exception exception)
            {
                TryDeleteTemporaryFile(temporaryPath);
                Debug.LogWarning("Could not save pet state: " + exception.Message);
                return false;
            }
        }

        public string CreateSaveJson(long utcUnixSeconds)
        {
            ValidateUnixSeconds(utcUnixSeconds, "utcUnixSeconds");
            var data = new SaveDataV2
            {
                version = CurrentSaveVersion,
                moodState = (int)_mood,
                energy = _energy,
                quietMode = _quietMode,
                patCooldownRemainingSeconds = _patCooldownRemainingSeconds,
                feedCooldownRemainingSeconds = _feedCooldownRemainingSeconds,
                savedAtUnixSeconds = utcUnixSeconds
            };
            return JsonUtility.ToJson(data, true);
        }

        /// <summary>
        /// Restores either the original prototype save or the current v2 format.
        /// Invalid input leaves the current state untouched.
        /// </summary>
        public bool TryRestoreFromJson(
            string json,
            long utcUnixSeconds,
            out string error)
        {
            ValidateUnixSeconds(utcUnixSeconds, "utcUnixSeconds");
            if (string.IsNullOrWhiteSpace(json))
            {
                error = "The save is empty.";
                return false;
            }

            int version;
            if (!TryReadSaveVersion(json, out version))
            {
                error = "The save did not contain a valid version.";
                return false;
            }

            PetMood restoredMood;
            float restoredEnergy;
            bool restoredQuietMode;
            double restoredPatCooldown;
            double restoredFeedCooldown;
            long savedAtUnixSeconds;

            if (version == 1)
            {
                LegacySaveDataV1 legacy;
                try
                {
                    legacy = JsonUtility.FromJson<LegacySaveDataV1>(json);
                }
                catch (Exception exception)
                {
                    error = exception.Message;
                    return false;
                }

                if (legacy == null ||
                    !IsFinite(legacy.mood) ||
                    !IsFinite(legacy.fullness) ||
                    !IsFinite(legacy.energy) ||
                    !IsFinite(legacy.patCooldownRemainingSeconds) ||
                    !IsFinite(legacy.feedCooldownRemainingSeconds) ||
                    legacy.savedAtUnixSeconds < 0)
                {
                    error = "The legacy save contains invalid values.";
                    return false;
                }

                restoredMood = MoodFromLegacyValue(legacy.mood);
                restoredEnergy = ClampEnergy(legacy.energy);
                restoredQuietMode = legacy.quietMode;
                restoredPatCooldown = legacy.patCooldownRemainingSeconds;
                restoredFeedCooldown = legacy.feedCooldownRemainingSeconds;
                savedAtUnixSeconds = legacy.savedAtUnixSeconds;
            }
            else if (version == CurrentSaveVersion)
            {
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

                if (data == null ||
                    !IsValidMoodValue(data.moodState) ||
                    !IsFinite(data.energy) ||
                    !IsFinite(data.patCooldownRemainingSeconds) ||
                    !IsFinite(data.feedCooldownRemainingSeconds) ||
                    data.savedAtUnixSeconds < 0)
                {
                    error = "The save contains invalid values.";
                    return false;
                }

                restoredMood = (PetMood)data.moodState;
                restoredEnergy = ClampEnergy(data.energy);
                restoredQuietMode = data.quietMode;
                restoredPatCooldown = data.patCooldownRemainingSeconds;
                restoredFeedCooldown = data.feedCooldownRemainingSeconds;
                savedAtUnixSeconds = data.savedAtUnixSeconds;
            }
            else
            {
                error = "Unsupported save version " + version + ".";
                return false;
            }

            restoredPatCooldown = Math.Min(
                Math.Max(0.0, restoredPatCooldown),
                Math.Max(0.0, patCooldownSeconds));
            restoredFeedCooldown = Math.Min(
                Math.Max(0.0, restoredFeedCooldown),
                Math.Max(0.0, feedCooldownSeconds));
            double offlineSeconds = Math.Max(
                0.0,
                (double)utcUnixSeconds - savedAtUnixSeconds);

            _mood = restoredMood;
            _energy = restoredEnergy;
            _quietMode = restoredQuietMode;
            _patCooldownRemainingSeconds = Math.Max(
                0.0,
                restoredPatCooldown - offlineSeconds);
            _feedCooldownRemainingSeconds = Math.Max(
                0.0,
                restoredFeedCooldown - offlineSeconds);
            _lastRealtimeSeconds = Time.realtimeSinceStartupAsDouble;
            _automaticTimeAccumulator = 0.0;
            _autosaveAccumulator = 0.0;
            PublishStateChanged();
            error = null;
            return true;
        }

        public static string GetMoodLabel(PetMood mood)
        {
            switch (mood)
            {
                case PetMood.Awful:
                    return "Awful";
                case PetMood.Bad:
                    return "Bad";
                case PetMood.Normal:
                    return "Normal";
                case PetMood.Good:
                    return "Good";
                case PetMood.Great:
                    return "Great";
                default:
                    return "Unknown";
            }
        }

        private void ChangeEnergy(float delta)
        {
            float next = ClampEnergy(_energy + delta);
            if (next == _energy)
            {
                return;
            }
            _energy = next;
            PublishStateChanged();
        }

        private void ApplyElapsedTime(double elapsedSeconds, bool publish)
        {
            if (elapsedSeconds <= 0.0)
            {
                return;
            }

            double previousPatCooldown = _patCooldownRemainingSeconds;
            double previousFeedCooldown = _feedCooldownRemainingSeconds;
            _patCooldownRemainingSeconds = Math.Max(
                0.0,
                _patCooldownRemainingSeconds - elapsedSeconds);
            _feedCooldownRemainingSeconds = Math.Max(
                0.0,
                _feedCooldownRemainingSeconds - elapsedSeconds);

            bool changed =
                previousPatCooldown != _patCooldownRemainingSeconds ||
                previousFeedCooldown != _feedCooldownRemainingSeconds;
            if (publish && changed)
            {
                PublishStateChanged();
            }
        }

        private void ResetNeedsInternal()
        {
            _mood = IsValidMoodValue((int)startingMood)
                ? startingMood
                : PetMood.Normal;
            _energy = ClampEnergy(startingEnergy);
            _patCooldownRemainingSeconds = 0.0;
            _feedCooldownRemainingSeconds = 0.0;
            _automaticTimeAccumulator = 0.0;
            _autosaveAccumulator = 0.0;
        }

        private void PublishStateChanged()
        {
            Action<PetNeedsSnapshot> handler = StateChanged;
            if (handler != null)
            {
                handler(CurrentSnapshot);
            }
        }

        private void RaiseCareActionApplied(PetCareAction action)
        {
            Action<PetCareAction> handler = CareActionApplied;
            if (handler != null)
            {
                handler(action);
            }
        }

        private void OnApplicationPause(bool paused)
        {
            if (!saveOnLifecycleEvents)
            {
                _lastRealtimeSeconds = Time.realtimeSinceStartupAsDouble;
                return;
            }

            if (paused)
            {
                _wasPaused = true;
                SaveNow();
            }
            else if (_wasPaused)
            {
                _wasPaused = false;
                LoadNow();
                _lastRealtimeSeconds = Time.realtimeSinceStartupAsDouble;
            }
        }

        private void OnApplicationQuit()
        {
            _isQuitting = true;
            if (saveOnLifecycleEvents)
            {
                SaveNow();
            }
        }

        private void OnDisable()
        {
            if (!_isQuitting && saveOnLifecycleEvents && Application.isPlaying)
            {
                SaveNow();
            }
        }

        private void OnValidate()
        {
            if (!IsValidMoodValue((int)startingMood))
            {
                startingMood = PetMood.Normal;
            }
            startingEnergy = ClampEnergy(startingEnergy);
            patMoodGainSteps = Mathf.Clamp(patMoodGainSteps, 0, 4);
            patReactionEnergyCost = Math.Max(0.0f, patReactionEnergyCost);
            patCooldownSeconds = Math.Max(0.0f, patCooldownSeconds);
            feedMoodGainSteps = Mathf.Clamp(feedMoodGainSteps, 0, 4);
            feedEnergyGain = Math.Max(0.0f, feedEnergyGain);
            feedCooldownSeconds = Math.Max(0.0f, feedCooldownSeconds);
            tapReactionEnergyCost = Math.Max(0.0f, tapReactionEnergyCost);
            lowEnergyThreshold = ClampEnergy(lowEnergyThreshold);
            automaticTickSeconds = Math.Max(0.1f, automaticTickSeconds);
            autosaveIntervalSeconds = Math.Max(1.0f, autosaveIntervalSeconds);
        }

        private bool TryBackupLegacySave(string json)
        {
            try
            {
                string backupPath = Path.Combine(
                    Application.persistentDataPath,
                    LegacyBackupFileName);
                if (!File.Exists(backupPath))
                {
                    File.WriteAllText(
                        backupPath,
                        json,
                        new UTF8Encoding(false));
                }
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "Could not back up the legacy pet-state save: " +
                    exception.Message);
                return false;
            }
        }

        private static PetMood PromoteMood(PetMood mood, int steps)
        {
            int current = IsValidMoodValue((int)mood)
                ? (int)mood
                : (int)PetMood.Normal;
            return (PetMood)Mathf.Clamp(current + Math.Max(0, steps), 1, 5);
        }

        private static PetMood MoodFromLegacyValue(float mood)
        {
            float value = ClampEnergy(mood);
            if (value < 20.0f)
            {
                return PetMood.Awful;
            }
            if (value < 40.0f)
            {
                return PetMood.Bad;
            }
            if (value < 60.0f)
            {
                return PetMood.Normal;
            }
            if (value < 80.0f)
            {
                return PetMood.Good;
            }
            return PetMood.Great;
        }

        private static bool TryReadSaveVersion(string json, out int version)
        {
            version = 0;
            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            try
            {
                SaveVersionHeader header = JsonUtility.FromJson<SaveVersionHeader>(json);
                if (header == null || header.version <= 0)
                {
                    return false;
                }
                version = header.version;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static float ClampEnergy(float value)
        {
            if (!IsFinite(value))
            {
                return 0.0f;
            }
            return Mathf.Clamp(value, 0.0f, 100.0f);
        }

        private static bool IsValidMoodValue(int value)
        {
            return value >= (int)PetMood.Awful &&
                value <= (int)PetMood.Great;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static void ValidateElapsedSeconds(double value, string name)
        {
            if (!IsFinite(value) || value < 0.0)
            {
                throw new ArgumentOutOfRangeException(
                    name,
                    "Elapsed time must be a finite, non-negative number.");
            }
        }

        private static void ValidateUnixSeconds(long value, string name)
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(
                    name,
                    "UTC Unix time must be non-negative.");
            }
        }

        private static long UtcNowUnixSeconds()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeSeconds();
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
                // A stale temporary file is harmless and can be overwritten later.
            }
        }

        [Serializable]
        private sealed class SaveVersionHeader
        {
            public int version;
        }

        [Serializable]
        private sealed class LegacySaveDataV1
        {
            public int version;
            public float mood;
            public float fullness;
            public float energy;
            public bool quietMode;
            public double patCooldownRemainingSeconds;
            public double feedCooldownRemainingSeconds;
            public long savedAtUnixSeconds;
        }

        [Serializable]
        private sealed class SaveDataV2
        {
            public int version;
            public int moodState;
            public float energy;
            public bool quietMode;
            public double patCooldownRemainingSeconds;
            public double feedCooldownRemainingSeconds;
            public long savedAtUnixSeconds;
        }
    }
}
