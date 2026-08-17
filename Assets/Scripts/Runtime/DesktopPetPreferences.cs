using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace UmaDesktopPet.Standalone.Runtime
{
    /// <summary>
    /// Versioned app preferences that are independent of the selected game-data
    /// directory. Unknown character keys safely resolve to the supported default.
    /// </summary>
    public sealed class DesktopPetPreferences
    {
        private const int CurrentVersion = 2;
        private const string FileName = "pet-settings.json";

        private readonly string _persistencePath;

        public DesktopPetPreferences()
            : this(Path.Combine(Application.persistentDataPath, FileName))
        {
        }

        /// <summary>
        /// Allows editor smoke tests and tools to use an isolated preference file.
        /// Runtime callers should normally use the parameterless constructor.
        /// </summary>
        public DesktopPetPreferences(string persistencePath)
        {
            if (string.IsNullOrWhiteSpace(persistencePath))
            {
                throw new ArgumentException(
                    "A desktop-pet preference path is required.",
                    "persistencePath");
            }

            _persistencePath = Path.GetFullPath(persistencePath);
        }

        public string PersistencePath
        {
            get { return _persistencePath; }
        }

        public bool Exists
        {
            get { return File.Exists(_persistencePath); }
        }

        public bool TryLoad(
            out DesktopPetPreferenceSnapshot snapshot,
            out string error)
        {
            snapshot = CreateDefaultSnapshot();
            error = null;

            if (!File.Exists(_persistencePath))
            {
                error = "No desktop-pet settings have been saved yet.";
                return false;
            }

            try
            {
                string json = File.ReadAllText(_persistencePath, Encoding.UTF8);
                return TryRestoreFromJson(json, out snapshot, out error);
            }
            catch (Exception exception) when (
                exception is IOException ||
                exception is UnauthorizedAccessException ||
                exception is NotSupportedException)
            {
                error = "The desktop-pet settings could not be read: " +
                    exception.Message;
                return false;
            }
        }

        public bool TrySave(string selectedCharacterKey, out string error)
        {
            DesktopPetPreferenceSnapshot existing;
            string ignoredError;
            bool hasSeenInteractionHint =
                TryLoad(out existing, out ignoredError) &&
                existing.HasSeenInteractionHint;
            return TrySaveSnapshot(
                selectedCharacterKey,
                hasSeenInteractionHint,
                out error);
        }

        public bool TryMarkInteractionHintShown(
            string selectedCharacterKey,
            out string error)
        {
            return TrySaveSnapshot(selectedCharacterKey, true, out error);
        }

        private bool TrySaveSnapshot(
            string selectedCharacterKey,
            bool hasSeenInteractionHint,
            out string error)
        {
            error = null;
            PetCharacterProfile selected =
                PetCharacterCatalog.ResolveOrDefault(selectedCharacterKey);
            string directory = Path.GetDirectoryName(_persistencePath);
            string temporaryPath = _persistencePath + ".tmp";

            try
            {
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var save = new SaveData
                {
                    version = CurrentVersion,
                    selectedCharacterKey = selected.Key,
                    hasSeenInteractionHint = hasSeenInteractionHint
                };
                File.WriteAllText(
                    temporaryPath,
                    JsonUtility.ToJson(save, true),
                    new UTF8Encoding(false));

                if (File.Exists(_persistencePath))
                {
                    File.Replace(temporaryPath, _persistencePath, null);
                }
                else
                {
                    File.Move(temporaryPath, _persistencePath);
                }
                return true;
            }
            catch (Exception exception) when (
                exception is IOException ||
                exception is UnauthorizedAccessException ||
                exception is NotSupportedException)
            {
                error = "The desktop-pet settings could not be saved: " +
                    exception.Message;
                TryDeleteTemporaryFile(temporaryPath);
                return false;
            }
        }

        public static bool TryRestoreFromJson(
            string json,
            out DesktopPetPreferenceSnapshot snapshot,
            out string error)
        {
            snapshot = CreateDefaultSnapshot();
            error = null;
            if (string.IsNullOrWhiteSpace(json))
            {
                error = "The desktop-pet settings file is empty.";
                return false;
            }

            try
            {
                SaveData save = JsonUtility.FromJson<SaveData>(json);
                if (save == null ||
                    (save.version != 1 && save.version != CurrentVersion))
                {
                    error = "The desktop-pet settings version is not supported.";
                    return false;
                }

                PetCharacterProfile selected =
                    PetCharacterCatalog.ResolveOrDefault(save.selectedCharacterKey);
                snapshot = new DesktopPetPreferenceSnapshot(
                    save.version,
                    selected.Key,
                    save.version >= 2 && save.hasSeenInteractionHint);
                return true;
            }
            catch (Exception exception)
            {
                error = "The desktop-pet settings file is not valid JSON: " +
                    exception.Message;
                return false;
            }
        }

        private static DesktopPetPreferenceSnapshot CreateDefaultSnapshot()
        {
            return new DesktopPetPreferenceSnapshot(
                CurrentVersion,
                PetCharacterCatalog.Oguri.Key,
                false);
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
                // A stale temporary file can be replaced on the next save.
            }
        }

        [Serializable]
        private sealed class SaveData
        {
            public int version;
            public string selectedCharacterKey;
            public bool hasSeenInteractionHint;
        }
    }

    public struct DesktopPetPreferenceSnapshot
    {
        public int Version { get; private set; }
        public string SelectedCharacterKey { get; private set; }
        public bool HasSeenInteractionHint { get; private set; }

        internal DesktopPetPreferenceSnapshot(
            int version,
            string selectedCharacterKey,
            bool hasSeenInteractionHint)
        {
            Version = version;
            SelectedCharacterKey = selectedCharacterKey;
            HasSeenInteractionHint = hasSeenInteractionHint;
        }
    }
}
