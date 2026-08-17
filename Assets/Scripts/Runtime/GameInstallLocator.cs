using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UmaDesktopPet.Standalone.Core;

namespace UmaDesktopPet.Standalone.Runtime
{
    public enum GameInstallSource
    {
        CommandLine,
        Environment,
        Remembered,
        Automatic
    }

    public sealed class GameInstallResolution
    {
        public GameInstallResolution(string gameRoot, GameInstallSource source)
        {
            GameRoot = gameRoot;
            Source = source;
        }

        public string GameRoot { get; private set; }
        public GameInstallSource Source { get; private set; }
        public bool ShouldRemember { get { return Source == GameInstallSource.Automatic; } }
    }

    public static class GameInstallLocator
    {
        private const string EnvironmentVariable = "UMA_DESKTOP_PET_GAME_ROOT";

        public static string FindGameRoot()
        {
            GameInstallResolution resolution = ResolvePreferred(
                new GameInstallPreferences());
            if (resolution != null)
            {
                return resolution.GameRoot;
            }

            throw new DirectoryNotFoundException(
                "The installed Umamusume data was not found. Choose the folder " +
                "containing meta, master\\master.mdb, and dat. No game files " +
                "were changed.");
        }

        public static GameInstallResolution ResolvePreferred(
            GameInstallPreferences preferences)
        {
            string normalized;
            if (TryNormalizeRoot(ReadCommandLineValue("--game-root"), out normalized))
            {
                return new GameInstallResolution(
                    normalized,
                    GameInstallSource.CommandLine);
            }
            if (TryNormalizeRoot(
                Environment.GetEnvironmentVariable(EnvironmentVariable),
                out normalized))
            {
                return new GameInstallResolution(
                    normalized,
                    GameInstallSource.Environment);
            }

            GameInstallPreferenceSnapshot remembered;
            string preferenceError;
            if (preferences != null &&
                preferences.TryLoad(out remembered, out preferenceError) &&
                TryNormalizeRoot(remembered.GameRoot, out normalized))
            {
                return new GameInstallResolution(
                    normalized,
                    GameInstallSource.Remembered);
            }

            IReadOnlyList<string> automatic = FindCandidates();
            return automatic.Count == 1
                ? new GameInstallResolution(
                    automatic[0],
                    GameInstallSource.Automatic)
                : null;
        }

        public static IReadOnlyList<string> FindCandidates()
        {
            var candidates = new List<string>();

            string userProfile = Environment.GetFolderPath(
                Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrWhiteSpace(userProfile))
            {
                AddInstallationCandidate(
                    candidates,
                    Path.Combine(userProfile, "AppData", "LocalLow", "Cygames"));
                AddChildrenMatching(
                    candidates,
                    Path.Combine(userProfile, "AppData", "LocalLow", "Cygames"),
                    "*uma*");
            }

            AddSteamCandidates(
                candidates,
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));
            AddSteamCandidates(
                candidates,
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));

            foreach (DriveInfo drive in GetReadyFixedDrives())
            {
                string root = drive.RootDirectory.FullName;
                AddInstallationCandidate(candidates, Path.Combine(root, "Umamusume"));
                AddInstallationCandidate(
                    candidates,
                    Path.Combine(root, "Games", "Umamusume"));
                AddSteamCommonCandidates(
                    candidates,
                    Path.Combine(root, "SteamLibrary", "steamapps", "common"));
            }

            candidates.Sort(StringComparer.OrdinalIgnoreCase);
            return candidates;
        }

        public static bool TryNormalizeRoot(string input, out string gameRoot)
        {
            gameRoot = null;
            if (string.IsNullOrWhiteSpace(input))
            {
                return false;
            }

            string path;
            try
            {
                string expanded = Environment.ExpandEnvironmentVariables(
                    input.Trim().Trim('"'));
                if (!LocalPathPolicy.TryGetLocalFullPath(expanded, out path))
                {
                    return false;
                }
                if (File.Exists(path))
                {
                    path = Path.GetDirectoryName(path);
                }
            }
            catch (Exception exception) when (
                exception is ArgumentException ||
                exception is NotSupportedException ||
                exception is PathTooLongException)
            {
                return false;
            }

            var candidates = new List<string>();
            AddPathShapeCandidates(candidates, path);

            DirectoryInfo current = Directory.Exists(path)
                ? new DirectoryInfo(path)
                : null;
            for (int depth = 0; current != null && depth < 4; depth++)
            {
                AddPathShapeCandidates(candidates, current.FullName);
                current = current.Parent;
            }

            foreach (string candidate in candidates)
            {
                if (IsGameRoot(candidate))
                {
                    gameRoot = Path.GetFullPath(candidate);
                    return true;
                }
            }
            return false;
        }

        public static bool IsGameRoot(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            try
            {
                string fullPath;
                if (!LocalPathPolicy.TryGetLocalFullPath(path, out fullPath))
                {
                    return false;
                }
                return File.Exists(Path.Combine(fullPath, "meta")) &&
                    File.Exists(Path.Combine(fullPath, "master", "master.mdb")) &&
                    Directory.Exists(Path.Combine(fullPath, "dat"));
            }
            catch (Exception exception) when (
                exception is ArgumentException ||
                exception is NotSupportedException ||
                exception is PathTooLongException)
            {
                return false;
            }
        }

        private static IEnumerable<DriveInfo> GetReadyFixedDrives()
        {
            try
            {
                return DriveInfo.GetDrives()
                    .Where(item => item.DriveType == DriveType.Fixed && item.IsReady)
                    .ToArray();
            }
            catch
            {
                return new DriveInfo[0];
            }
        }

        private static void AddSteamCandidates(
            ICollection<string> candidates,
            string programFiles)
        {
            if (string.IsNullOrWhiteSpace(programFiles))
            {
                return;
            }
            AddSteamCommonCandidates(
                candidates,
                Path.Combine(programFiles, "Steam", "steamapps", "common"));
        }

        private static void AddSteamCommonCandidates(
            ICollection<string> candidates,
            string commonDirectory)
        {
            AddInstallationCandidate(
                candidates,
                Path.Combine(commonDirectory, "Umamusume Pretty Derby"));
            AddChildrenMatching(candidates, commonDirectory, "*Umamusume*");
        }

        private static void AddChildrenMatching(
            ICollection<string> candidates,
            string parent,
            string searchPattern)
        {
            try
            {
                if (!Directory.Exists(parent))
                {
                    return;
                }
                foreach (string child in Directory.EnumerateDirectories(
                    parent,
                    searchPattern,
                    SearchOption.TopDirectoryOnly))
                {
                    AddInstallationCandidate(candidates, child);
                }
            }
            catch (Exception exception) when (
                exception is IOException ||
                exception is UnauthorizedAccessException ||
                exception is ArgumentException ||
                exception is PathTooLongException)
            {
                // A protected library is simply unavailable to auto-detection.
            }
        }

        private static void AddInstallationCandidate(
            ICollection<string> candidates,
            string candidate)
        {
            string normalized;
            if (TryNormalizeRoot(candidate, out normalized) &&
                !candidates.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            {
                candidates.Add(normalized);
            }
        }

        private static void AddPathShapeCandidates(
            ICollection<string> candidates,
            string path)
        {
            AddCandidate(candidates, path);
            AddCandidate(candidates, Path.Combine(path, "Persistent"));
            AddCandidate(
                candidates,
                Path.Combine(path, "umamusume_Data", "Persistent"));
            AddCandidate(
                candidates,
                Path.Combine(path, "Umamusume_Data", "Persistent"));
            AddCandidate(
                candidates,
                Path.Combine(path, "UmamusumePrettyDerby_Data", "Persistent"));

            try
            {
                if (!Directory.Exists(path))
                {
                    return;
                }
                foreach (string dataDirectory in Directory.EnumerateDirectories(
                    path,
                    "*_Data",
                    SearchOption.TopDirectoryOnly))
                {
                    AddCandidate(
                        candidates,
                        Path.Combine(dataDirectory, "Persistent"));
                }
            }
            catch (Exception exception) when (
                exception is IOException ||
                exception is UnauthorizedAccessException ||
                exception is ArgumentException ||
                exception is PathTooLongException)
            {
                // Normalization can still succeed through the explicit shapes above.
            }
        }

        private static string ReadCommandLineValue(string name)
        {
            string[] arguments = Environment.GetCommandLineArgs();
            for (int index = 0; index < arguments.Length - 1; index++)
            {
                if (string.Equals(
                    arguments[index],
                    name,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return arguments[index + 1];
                }
            }
            return null;
        }

        private static void AddCandidate(
            ICollection<string> candidates,
            string candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                return;
            }
            if (!candidates.Contains(candidate, StringComparer.OrdinalIgnoreCase))
            {
                candidates.Add(candidate);
            }
        }
    }
}
