using System;
using System.IO;
using UmaDesktopPet.Standalone.Runtime;
using UnityEngine;

namespace UmaDesktopPet.Standalone.Editor
{
    public static class DesktopPetPreferencesSmokeTests
    {
        public static void Run()
        {
            string temporaryDirectory = Path.Combine(
                Path.GetTempPath(),
                "UmaDesktopPetSettings-" + Guid.NewGuid().ToString("N"));
            try
            {
                string preferencePath = Path.Combine(
                    temporaryDirectory,
                    "isolated",
                    "pet-settings.json");
                var preferences = new DesktopPetPreferences(preferencePath);

                Assert(!preferences.Exists, "settings should initially be absent");

                DesktopPetPreferenceSnapshot snapshot;
                string error;
                Assert(
                    !preferences.TryLoad(out snapshot, out error),
                    "missing settings should report that no file was loaded");
                AssertEqual(
                    PetCharacterCatalog.Oguri.Key,
                    snapshot.SelectedCharacterKey,
                    "missing-file fallback");
                Assert(
                    !snapshot.HasSeenInteractionHint,
                    "missing settings should show the first-run hint");

                Assert(
                    preferences.TrySave(PetCharacterCatalog.Oguri.Key, out error),
                    error);
                Assert(preferences.Exists, "settings file should exist after saving");
                Assert(preferences.TryLoad(out snapshot, out error), error);
                AssertEqual(2, snapshot.Version, "settings version");
                AssertEqual(
                    PetCharacterCatalog.Oguri.Key,
                    snapshot.SelectedCharacterKey,
                    "round-trip selected character");
                Assert(
                    !snapshot.HasSeenInteractionHint,
                    "ordinary saves should preserve an unseen hint");

                Assert(
                    preferences.TryMarkInteractionHintShown(
                        PetCharacterCatalog.Oguri.Key,
                        out error),
                    error);
                Assert(preferences.TryLoad(out snapshot, out error), error);
                Assert(
                    snapshot.HasSeenInteractionHint,
                    "the first-run hint should be remembered");

                Assert(
                    preferences.TrySave(PetCharacterCatalog.Oguri.Key, out error),
                    error);
                Assert(preferences.TryLoad(out snapshot, out error), error);
                Assert(
                    snapshot.HasSeenInteractionHint,
                    "changing character should preserve the hint state");

                Assert(
                    DesktopPetPreferences.TryRestoreFromJson(
                        "{\"version\":1,\"selectedCharacterKey\":\"not-supported\"}",
                        out snapshot,
                        out error),
                    error);
                AssertEqual(
                    PetCharacterCatalog.Oguri.Key,
                    snapshot.SelectedCharacterKey,
                    "unknown-key fallback");
                Assert(
                    !snapshot.HasSeenInteractionHint,
                    "v1 settings should migrate with an unseen hint");

                Assert(
                    DesktopPetPreferences.TryRestoreFromJson(
                        "{\"version\":1,\"selectedCharacterKey\":\"\"}",
                        out snapshot,
                        out error),
                    error);
                AssertEqual(
                    PetCharacterCatalog.Oguri.Key,
                    snapshot.SelectedCharacterKey,
                    "empty-key fallback");

                File.WriteAllText(preferencePath, "{ definitely not JSON");
                Assert(
                    !preferences.TryLoad(out snapshot, out error),
                    "corrupt settings should not load");
                AssertEqual(
                    PetCharacterCatalog.Oguri.Key,
                    snapshot.SelectedCharacterKey,
                    "corrupt-file fallback");

                AssertEqual(1, PetCharacterCatalog.Selectable.Count, "selectable count");
                AssertEqual(
                    PetCharacterCatalog.Oguri,
                    PetCharacterCatalog.Selectable[0],
                    "only selectable character");
                AssertEqual(1006, PetCharacterCatalog.Oguri.GameCharacterId, "Oguri ID");
                AssertEqual("Oguri Cap", PetCharacterCatalog.Oguri.DisplayName, "display name");
                AssertEqual("Oguri", PetCharacterCatalog.Oguri.ShortName, "short name");

                PetCharacterProfile profile;
                Assert(
                    PetCharacterCatalog.TryGet("oguri-cap", out profile),
                    "Oguri should resolve by key");
                AssertEqual(PetCharacterCatalog.Oguri, profile, "resolved Oguri profile");
                Assert(
                    !PetCharacterCatalog.TryGet("not-supported", out profile),
                    "an unsupported key should not resolve");
                AssertEqual(
                    PetCharacterCatalog.Oguri,
                    PetCharacterCatalog.ResolveOrDefault("not-supported"),
                    "catalog fallback");

                Debug.Log("Desktop-pet preference smoke tests passed.");
            }
            finally
            {
                if (Directory.Exists(temporaryDirectory))
                {
                    Directory.Delete(temporaryDirectory, true);
                }
            }
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
