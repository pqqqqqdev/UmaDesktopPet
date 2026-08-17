using System;
using System.IO;
using UmaDesktopPet.Standalone.Core;
using UmaDesktopPet.Standalone.Runtime;
using UnityEngine;

namespace UmaDesktopPet.Standalone.Editor
{
    public static class GameInstallSmokeTests
    {
        public static void Run()
        {
            string temporaryDirectory = Path.Combine(
                Path.GetTempPath(),
                "UmaDesktopPetInstall-" + Guid.NewGuid().ToString("N"));
            try
            {
                string installDirectory = Path.Combine(temporaryDirectory, "Umamusume");
                string dataDirectory = Path.Combine(
                    installDirectory,
                    "umamusume_Data");
                string gameRoot = Path.Combine(dataDirectory, "Persistent");
                Directory.CreateDirectory(Path.Combine(gameRoot, "master"));
                Directory.CreateDirectory(Path.Combine(gameRoot, "dat"));
                File.WriteAllBytes(Path.Combine(gameRoot, "meta"), new byte[0]);
                File.WriteAllBytes(
                    Path.Combine(gameRoot, "master", "master.mdb"),
                    new byte[0]);
                string executable = Path.Combine(installDirectory, "umamusume.exe");
                File.WriteAllBytes(executable, new byte[0]);

                AssertNormalized(gameRoot, gameRoot, "Persistent root");
                AssertNormalized(dataDirectory, gameRoot, "*_Data folder");
                AssertNormalized(installDirectory, gameRoot, "install folder");
                AssertNormalized(executable, gameRoot, "game executable");

                string ignoredRoot;
                Assert(
                    !GameInstallLocator.TryNormalizeRoot(
                        @"\\example.invalid\share\Persistent",
                        out ignoredRoot),
                    "UNC game roots must be rejected before filesystem probing");
                Assert(
                    !GameInstallLocator.TryNormalizeRoot(
                        @"\\?\UNC\example.invalid\share\Persistent",
                        out ignoredRoot),
                    "extended UNC game roots must be rejected");

                const string safeHash = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
                var safeAsset = new GameAssetRecord(
                    gameRoot,
                    "3d",
                    "safe",
                    safeHash,
                    string.Empty,
                    string.Empty,
                    0);
                AssertEqual(
                    Path.GetFullPath(Path.Combine(gameRoot, "dat", "AB", safeHash)),
                    safeAsset.FilePath,
                    "contained asset path");
                AssertInvalidAssetHash(gameRoot, @"C:\Windows\win.ini");
                AssertInvalidAssetHash(gameRoot, "../ABCDEFGHIJKLMNOPQRSTUVWXYZ2345");
                AssertInvalidAssetHash(gameRoot, "abcdefghijklmnopqrstuvwxyz234567");
                AssertInvalidAssetHash(gameRoot, "ABCDEFGHIJKLMNOPQRSTUVWXYZ234560");

                string preferencePath = Path.Combine(
                    temporaryDirectory,
                    "preferences",
                    "game-install.json");
                var preferences = new GameInstallPreferences(preferencePath);
                string error;
                Assert(preferences.TrySave(gameRoot, out error), error);
                Assert(preferences.Exists, "preference file should exist");

                GameInstallPreferenceSnapshot snapshot;
                Assert(preferences.TryLoad(out snapshot, out error), error);
                AssertEqual(
                    Path.GetFullPath(gameRoot),
                    snapshot.GameRoot,
                    "remembered game root");
                AssertEqual(1, snapshot.Version, "preference version");

                Assert(preferences.TryClear(out error), error);
                Assert(!preferences.Exists, "preference file should be removed");

                Assert(
                    !GameInstallPreferences.TryRestoreFromJson(
                        "{\"version\":99,\"gameRoot\":\"C:/invalid\"}",
                        out snapshot,
                        out error),
                    "unsupported preference version should fail");
                Debug.Log("Game-install discovery and preference smoke tests passed.");
            }
            finally
            {
                if (Directory.Exists(temporaryDirectory))
                {
                    Directory.Delete(temporaryDirectory, true);
                }
            }
        }

        private static void AssertInvalidAssetHash(string gameRoot, string hash)
        {
            try
            {
                new GameAssetRecord(
                    gameRoot,
                    "3d",
                    "malicious",
                    hash,
                    string.Empty,
                    string.Empty,
                    0);
            }
            catch (InvalidDataException)
            {
                return;
            }

            throw new InvalidOperationException(
                "Invalid asset hash should have been rejected: " + hash);
        }

        private static void AssertNormalized(
            string input,
            string expected,
            string name)
        {
            string actual;
            Assert(
                GameInstallLocator.TryNormalizeRoot(input, out actual),
                name + " should normalize");
            AssertEqual(Path.GetFullPath(expected), actual, name);
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
