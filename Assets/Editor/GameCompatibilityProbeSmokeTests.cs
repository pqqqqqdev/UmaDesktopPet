using System;
using System.IO;
using UmaDesktopPet.Standalone.Runtime;
using UnityEngine;

namespace UmaDesktopPet.Standalone.Editor
{
    /// <summary>
    /// Dependency-free checks for compatibility failures that do not need a
    /// local game installation or the native SQLite library.
    /// </summary>
    public static class GameCompatibilityProbeSmokeTests
    {
        public static void Run()
        {
            GameCompatibilityReport noRoot = GameCompatibilityProbe.Probe(null, null);
            AssertEqual(
                GameCompatibilityStatus.MissingRoot,
                noRoot.Status,
                "empty root");
            Assert(!noRoot.IsCompatible, "empty root must not be compatible");

            GameCompatibilityReport uncRoot = GameCompatibilityProbe.Probe(
                @"\\example.invalid\share\Persistent",
                null);
            AssertEqual(
                GameCompatibilityStatus.MissingRoot,
                uncRoot.Status,
                "UNC root");
            Assert(
                uncRoot.Details.IndexOf(
                    "UNC",
                    StringComparison.OrdinalIgnoreCase) >= 0,
                "UNC rejection should explain the local-path requirement");

            string temporaryRoot = Path.Combine(
                Path.GetTempPath(),
                "UmaDesktopPetCompatibility-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(temporaryRoot);
                GameCompatibilityReport noCatalog =
                    GameCompatibilityProbe.Probe(temporaryRoot, null);
                AssertEqual(
                    GameCompatibilityStatus.MissingCatalog,
                    noCatalog.Status,
                    "empty directory");
                Assert(!noCatalog.MoodUiAvailable, "missing catalog Mood UI");
                Assert(!noCatalog.CarrotUiAvailable, "missing catalog carrot UI");
            }
            finally
            {
                if (Directory.Exists(temporaryRoot))
                {
                    Directory.Delete(temporaryRoot, true);
                }
            }

            Debug.Log("Game compatibility probe smoke tests passed.");
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }

        private static void AssertEqual<T>(T expected, T actual, string name)
        {
            if (!Equals(expected, actual))
            {
                throw new InvalidOperationException(
                    name + " expected " + expected + " but was " + actual + ".");
            }
        }
    }
}
