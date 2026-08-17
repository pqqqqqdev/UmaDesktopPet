using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace UmaDesktopPet.Standalone.Runtime
{
    /// <summary>
    /// The small, versioned user preference that remembers which installed game
    /// data directory the desktop pet should read. It stores a path only; no game
    /// data is copied into the desktop pet's save directory.
    /// </summary>
    public sealed class GameInstallPreferences
    {
        private const int CurrentVersion = 1;
        private const string FileName = "game-install.json";

        private readonly string _persistencePath;

        public GameInstallPreferences()
            : this(Path.Combine(Application.persistentDataPath, FileName))
        {
        }

        /// <summary>
        /// Allows editor smoke tests and tools to use an isolated preference file.
        /// Runtime callers should normally use the parameterless constructor.
        /// </summary>
        public GameInstallPreferences(string persistencePath)
        {
            if (string.IsNullOrWhiteSpace(persistencePath))
            {
                throw new ArgumentException(
                    "A game-install preference path is required.",
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
            out GameInstallPreferenceSnapshot snapshot,
            out string error)
        {
            snapshot = default(GameInstallPreferenceSnapshot);
            error = null;

            if (!File.Exists(_persistencePath))
            {
                error = "No remembered game installation has been saved yet.";
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
                error = "The remembered game installation could not be read: " +
                    exception.Message;
                return false;
            }
        }

        public bool TrySave(string gameRoot, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(gameRoot))
            {
                error = "A game installation folder is required.";
                return false;
            }

            string normalizedRoot;
            try
            {
                normalizedRoot = Path.GetFullPath(gameRoot);
            }
            catch (Exception exception) when (
                exception is ArgumentException ||
                exception is NotSupportedException ||
                exception is PathTooLongException)
            {
                error = "The game installation path is not valid: " +
                    exception.Message;
                return false;
            }

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
                    gameRoot = normalizedRoot
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
                error = "The selected game installation could not be remembered: " +
                    exception.Message;
                TryDeleteTemporaryFile(temporaryPath);
                return false;
            }
        }

        public bool TryClear(out string error)
        {
            error = null;
            try
            {
                if (File.Exists(_persistencePath))
                {
                    File.Delete(_persistencePath);
                }
                TryDeleteTemporaryFile(_persistencePath + ".tmp");
                return true;
            }
            catch (Exception exception) when (
                exception is IOException ||
                exception is UnauthorizedAccessException ||
                exception is NotSupportedException)
            {
                error = "The remembered game installation could not be cleared: " +
                    exception.Message;
                return false;
            }
        }

        public static bool TryRestoreFromJson(
            string json,
            out GameInstallPreferenceSnapshot snapshot,
            out string error)
        {
            snapshot = default(GameInstallPreferenceSnapshot);
            error = null;
            if (string.IsNullOrWhiteSpace(json))
            {
                error = "The game-install preference file is empty.";
                return false;
            }

            try
            {
                SaveData save = JsonUtility.FromJson<SaveData>(json);
                if (save == null || save.version != CurrentVersion)
                {
                    error = "The game-install preference version is not supported.";
                    return false;
                }
                if (string.IsNullOrWhiteSpace(save.gameRoot))
                {
                    error = "The remembered game installation has no folder path.";
                    return false;
                }

                string normalizedRoot = Path.GetFullPath(save.gameRoot);
                snapshot = new GameInstallPreferenceSnapshot(
                    save.version,
                    normalizedRoot);
                return true;
            }
            catch (Exception exception) when (
                exception is ArgumentException ||
                exception is NotSupportedException ||
                exception is PathTooLongException)
            {
                error = "The remembered game installation path is not valid: " +
                    exception.Message;
                return false;
            }
            catch (Exception exception)
            {
                error = "The game-install preference file is not valid JSON: " +
                    exception.Message;
                return false;
            }
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
                // A stale temporary file can be safely replaced on the next save.
            }
        }

        [Serializable]
        private sealed class SaveData
        {
            public int version;
            public string gameRoot;
        }
    }

    public struct GameInstallPreferenceSnapshot
    {
        public int Version { get; private set; }
        public string GameRoot { get; private set; }

        internal GameInstallPreferenceSnapshot(int version, string gameRoot)
        {
            Version = version;
            GameRoot = gameRoot;
        }
    }
}
