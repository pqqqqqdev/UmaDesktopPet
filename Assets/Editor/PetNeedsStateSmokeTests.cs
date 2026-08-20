using System;
using System.IO;
using UmaDesktopPet.Standalone.Runtime;
using UnityEngine;

namespace UmaDesktopPet.Standalone.Editor
{
    /// <summary>
    /// Dependency-free editor checks for care state, pantry transactions, and
    /// save migrations.
    /// Run with Unity's -executeMethod option in CI or local verification.
    /// </summary>
    public static class PetNeedsStateSmokeTests
    {
        public static void Run()
        {
            GameObject firstObject = null;
            GameObject secondObject = null;
            Texture2D carrot = null;
            string migrationSavePath = null;
            string blockedSavePath = null;
            try
            {
                firstObject = new GameObject("Pet state smoke source");
                PetNeedsState state = firstObject.AddComponent<PetNeedsState>();
                state.SetAutomaticTimeEnabled(false);
                state.SetPersistenceEnabled(false);
                state.ResetNeeds();

                AssertEqual(PetMood.Normal, state.Mood, "starting Mood");
                AssertNear(84.0f, state.Energy, "starting Energy");
                AssertEqual(
                    PetNeedsState.StarterCarrotJellyQuantity,
                    state.GetFoodQuantity(FoodCatalog.CarrotJellyId),
                    "starting Carrot Jelly");
                AssertEqual(
                    18.0f,
                    FoodCatalog.CarrotJelly.EnergyGain,
                    "Carrot Jelly Energy effect");
                AssertEqual(
                    1,
                    FoodCatalog.CarrotJelly.MoodGainSteps,
                    "Carrot Jelly Mood effect");

                state.RecordTapReaction();
                AssertNear(83.5f, state.Energy, "tap Energy cost");
                state.RecordAmbientReaction();
                AssertNear(
                    83.5f,
                    state.Energy,
                    "ambient reaction should not drain Energy");
                Assert(state.TryPat(), "first pat should be accepted");
                AssertEqual(PetMood.Good, state.Mood, "pat Mood");
                AssertNear(83.0f, state.Energy, "pat Energy cost");
                Assert(state.CanPat, "pat should have no cooldown");
                Assert(state.TryPat(), "immediate second pat should be accepted");
                AssertEqual(PetMood.Great, state.Mood, "second pat Mood");
                AssertNear(82.5f, state.Energy, "second pat Energy cost");
                Assert(state.TryFeed(), "first carrot should be accepted");
                AssertEqual(PetMood.Great, state.Mood, "carrot Mood");
                AssertNear(100.0f, state.Energy, "carrot Energy clamp");
                AssertEqual(
                    2,
                    state.GetFoodQuantity(FoodCatalog.CarrotJellyId),
                    "first carrot consumption");
                Assert(state.CanFeed, "carrot should have no cooldown");
                Assert(state.TryFeed(), "immediate second carrot should be accepted");
                AssertEqual(
                    1,
                    state.GetFoodQuantity(FoodCatalog.CarrotJellyId),
                    "second carrot consumption");

                PetMood beforeOfflineMood = state.Mood;
                float beforeOfflineEnergy = state.Energy;
                state.AdvanceOfflineTime(24.0 * 60.0 * 60.0);
                AssertEqual(beforeOfflineMood, state.Mood, "offline Mood");
                AssertNear(beforeOfflineEnergy, state.Energy, "offline Energy");
                Assert(state.CanPat, "offline time should clear the pat cooldown");
                Assert(state.CanFeed, "offline time should clear the carrot cooldown");
                Assert(state.TryFeed(), "last starter carrot should be accepted");
                AssertEqual(
                    0,
                    state.GetFoodQuantity(FoodCatalog.CarrotJellyId),
                    "empty Carrot Jelly stack");
                Assert(!state.CanFeed, "empty pantry should disable default feed");
                Assert(!state.TryFeed(), "empty pantry should reject feeding");

                const string legacyJson =
                    "{\"version\":1,\"mood\":82,\"fullness\":76," +
                    "\"energy\":84,\"quietMode\":true," +
                    "\"patCooldownRemainingSeconds\":5," +
                    "\"feedCooldownRemainingSeconds\":20," +
                    "\"savedAtUnixSeconds\":900}";
                string error;
                Assert(
                    state.TryRestoreFromJson(legacyJson, 1000, out error),
                    "v1 migration failed: " + error);
                AssertEqual(PetMood.Great, state.Mood, "migrated Mood");
                AssertNear(84.0f, state.Energy, "migrated Energy");
                Assert(state.QuietMode, "migrated quiet mode");
                Assert(state.CanPat, "migrated pat cooldown");
                Assert(state.CanFeed, "migrated carrot cooldown");
                AssertEqual(
                    PetNeedsState.StarterCarrotJellyQuantity,
                    state.GetFoodQuantity(FoodCatalog.CarrotJellyId),
                    "v1 starter migration");

                const string version2Json =
                    "{\"version\":2,\"moodState\":3,\"energy\":64," +
                    "\"quietMode\":false," +
                    "\"patCooldownRemainingSeconds\":0," +
                    "\"feedCooldownRemainingSeconds\":0," +
                    "\"savedAtUnixSeconds\":1000}";
                Assert(
                    state.TryRestoreFromJson(version2Json, 1000, out error),
                    "v2 migration failed: " + error);
                AssertEqual(
                    PetNeedsState.StarterCarrotJellyQuantity,
                    state.GetFoodQuantity(FoodCatalog.CarrotJellyId),
                    "v2 starter migration");
                AssertEqual(
                    0L,
                    state.LastAppliedStudyCompletionId,
                    "v2 completion migration");
                Assert(
                    state.CreateSaveJson(1000).Contains("\"version\": 3"),
                    "v2 migration should emit v3");

                state.ResetNeeds();
                state.SetQuietMode(true);
                Assert(state.TryPat(), "round-trip pat should be accepted");
                Assert(
                    state.TryApplyStudyCompletion(1500, 1, 12.0f),
                    "short study completion should apply");
                string v3Json = state.CreateSaveJson(2000);

                secondObject = new GameObject("Pet state smoke target");
                PetNeedsState restored =
                    secondObject.AddComponent<PetNeedsState>();
                restored.SetAutomaticTimeEnabled(false);
                restored.SetPersistenceEnabled(false);
                restored.ResetNeeds();
                Assert(
                    restored.TryRestoreFromJson(v3Json, 2000, out error),
                    "v3 restore failed: " + error);
                AssertEqual(state.Mood, restored.Mood, "round-trip Mood");
                AssertNear(state.Energy, restored.Energy, "round-trip Energy");
                Assert(restored.QuietMode, "round-trip quiet mode");
                AssertEqual(
                    state.GetFoodQuantity(FoodCatalog.CarrotJellyId),
                    restored.GetFoodQuantity(FoodCatalog.CarrotJellyId),
                    "round-trip Carrot Jelly");
                AssertEqual(
                    state.LastAppliedStudyCompletionId,
                    restored.LastAppliedStudyCompletionId,
                    "round-trip completion ID");

                PetMood unchangedMood = restored.Mood;
                float unchangedEnergy = restored.Energy;
                Assert(
                    !restored.TryRestoreFromJson(
                        "{\"version\":2,\"moodState\":0,\"energy\":50," +
                        "\"savedAtUnixSeconds\":2000}",
                        2000,
                        out error),
                    "invalid Mood 0 should be rejected");
                AssertEqual(unchangedMood, restored.Mood, "state after invalid save");
                AssertNear(
                    unchangedEnergy,
                    restored.Energy,
                    "Energy after invalid save");

                PetNeedsSnapshot beforeInvalidFood = restored.CurrentSnapshot;
                Assert(
                    !restored.TryRestoreFromJson(
                        CreateV3Json(
                            "[{\"foodId\":\"not-a-food\",\"quantity\":1}]",
                            1500),
                        2000,
                        out error),
                    "unknown food ID should be rejected");
                AssertSnapshotEqual(
                    beforeInvalidFood,
                    restored.CurrentSnapshot,
                    "unknown food save");
                Assert(
                    !restored.TryRestoreFromJson(
                        CreateV3Json(
                            "[{\"foodId\":\"carrot-jelly\",\"quantity\":1}," +
                            "{\"foodId\":\"carrot-jelly\",\"quantity\":2}]",
                            1500),
                        2000,
                        out error),
                    "duplicate food stack should be rejected");
                AssertSnapshotEqual(
                    beforeInvalidFood,
                    restored.CurrentSnapshot,
                    "duplicate food save");
                Assert(
                    !restored.TryRestoreFromJson(
                        CreateV3Json(
                            "[{\"foodId\":\"carrot-jelly\",\"quantity\":100}]",
                            1500),
                        2000,
                        out error),
                    "overflowing food stack should be rejected");
                AssertSnapshotEqual(
                    beforeInvalidFood,
                    restored.CurrentSnapshot,
                    "overflowing food save");
                Assert(
                    !restored.TryRestoreFromJson(
                        CreateV3Json("[]", -1),
                        2000,
                        out error),
                    "negative completion ID should be rejected");
                AssertSnapshotEqual(
                    beforeInvalidFood,
                    restored.CurrentSnapshot,
                    "negative completion save");

                restored.ResetNeeds();
                Assert(
                    restored.TryApplyStudyCompletion(1500, 1, 12.0f),
                    "short completion should apply");
                AssertEqual(
                    4,
                    restored.GetFoodQuantity(FoodCatalog.CarrotJellyId),
                    "short completion food");
                AssertNear(72.0f, restored.Energy, "short completion Energy");
                PetNeedsSnapshot afterShort = restored.CurrentSnapshot;
                Assert(
                    restored.TryApplyStudyCompletion(1500, 99, 99.0f),
                    "same completion should be an idempotent success");
                AssertSnapshotEqual(
                    afterShort,
                    restored.CurrentSnapshot,
                    "same completion retry");
                Assert(
                    restored.TryApplyStudyCompletion(1000, 99, 99.0f),
                    "older completion should be an idempotent success");
                AssertSnapshotEqual(
                    afterShort,
                    restored.CurrentSnapshot,
                    "older completion retry");

                Assert(
                    restored.TryApplyStudyCompletion(4500, 2, 24.0f),
                    "long completion should apply");
                AssertEqual(
                    6,
                    restored.GetFoodQuantity(FoodCatalog.CarrotJellyId),
                    "long completion food");
                AssertNear(48.0f, restored.Energy, "long completion Energy");
                PetNeedsSnapshot afterLong = restored.CurrentSnapshot;
                Assert(
                    restored.TryApplyStudyCompletion(4500, 2, 24.0f),
                    "long completion retry should succeed");
                AssertSnapshotEqual(
                    afterLong,
                    restored.CurrentSnapshot,
                    "long completion retry");
                Assert(
                    !restored.TryApplyStudyCompletion(5000, -1, 1.0f),
                    "negative food reward should fail");
                AssertSnapshotEqual(
                    afterLong,
                    restored.CurrentSnapshot,
                    "negative food reward");

                Assert(
                    restored.TryApplyStudyCompletion(6000, int.MaxValue, 0.0f),
                    "large reward should clamp to stack capacity");
                AssertEqual(
                    FoodCatalog.CarrotJelly.MaxStack,
                    restored.GetFoodQuantity(FoodCatalog.CarrotJellyId),
                    "bounded food stack");
                float beforeFullStackEnergy = restored.Energy;
                Assert(
                    restored.TryApplyStudyCompletion(7500, 2, 12.0f),
                    "a full stack must not block study completion");
                AssertEqual(
                    FoodCatalog.CarrotJelly.MaxStack,
                    restored.GetFoodQuantity(FoodCatalog.CarrotJellyId),
                    "full stack reward");
                AssertNear(
                    beforeFullStackEnergy - 12.0f,
                    restored.Energy,
                    "full stack Energy cost");
                AssertEqual(
                    7500L,
                    restored.LastAppliedStudyCompletionId,
                    "full stack completion ID");

                migrationSavePath = Path.Combine(
                    Path.GetTempPath(),
                    "UmaDesktopPet-needs-migration-" +
                    Guid.NewGuid().ToString("N") + ".json");
                File.WriteAllText(migrationSavePath, version2Json);
                restored.SetPersistencePathForSmokeTest(migrationSavePath);
                restored.SetPersistenceEnabled(true);
                Assert(restored.LoadNow(1000), "v2 file migration should load");
                AssertEqual(
                    PetNeedsState.StarterCarrotJellyQuantity,
                    restored.GetFoodQuantity(FoodCatalog.CarrotJellyId),
                    "persisted v2 starter migration");
                Assert(
                    File.ReadAllText(migrationSavePath).Contains("\"version\": 3"),
                    "v2 file migration should persist v3");
                Assert(restored.LoadNow(1000), "migrated v3 file should reload");
                AssertEqual(
                    PetNeedsState.StarterCarrotJellyQuantity,
                    restored.GetFoodQuantity(FoodCatalog.CarrotJellyId),
                    "starter migration must run exactly once");

                blockedSavePath = Path.Combine(
                    Path.GetTempPath(),
                    "UmaDesktopPet-needs-blocked-" +
                    Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(blockedSavePath);
                restored.SetPersistencePathForSmokeTest(blockedSavePath);
                restored.ResetNeeds();
                int failedStateEvents = 0;
                int failedCareEvents = 0;
                restored.StateChanged += delegate { failedStateEvents++; };
                restored.CareActionApplied += delegate { failedCareEvents++; };
                PetNeedsSnapshot beforeFailedFeed = restored.CurrentSnapshot;
                Assert(
                    !restored.TryFeed(FoodCatalog.CarrotJellyId),
                    "feed should fail when its durable save fails");
                AssertSnapshotEqual(
                    beforeFailedFeed,
                    restored.CurrentSnapshot,
                    "failed feed rollback");
                AssertEqual(0, failedStateEvents, "failed feed state events");
                AssertEqual(0, failedCareEvents, "failed feed care events");

                PetNeedsSnapshot beforeFailedStudy = restored.CurrentSnapshot;
                Assert(
                    !restored.TryApplyStudyCompletion(1500, 1, 12.0f),
                    "study should fail when its durable save fails");
                AssertSnapshotEqual(
                    beforeFailedStudy,
                    restored.CurrentSnapshot,
                    "failed study rollback");
                AssertEqual(0, failedStateEvents, "failed study state events");
                AssertEqual(0, failedCareEvents, "failed study care events");
                restored.SetPersistenceEnabled(false);

                AssertEqual("Awful", PetNeedsState.GetMoodLabel(PetMood.Awful), "Awful label");
                AssertEqual("Bad", PetNeedsState.GetMoodLabel(PetMood.Bad), "Bad label");
                AssertEqual("Normal", PetNeedsState.GetMoodLabel(PetMood.Normal), "Normal label");
                AssertEqual("Good", PetNeedsState.GetMoodLabel(PetMood.Good), "Good label");
                AssertEqual("Great", PetNeedsState.GetMoodLabel(PetMood.Great), "Great label");

                carrot = ProceduralCarrotTexture.Create();
                AssertEqual(96, carrot.width, "carrot texture width");
                AssertEqual(96, carrot.height, "carrot texture height");
                Color32[] carrotPixels = carrot.GetPixels32();
                Assert(
                    Array.Exists(carrotPixels, pixel => pixel.a == 0),
                    "carrot texture should retain transparent pixels");
                Assert(
                    Array.Exists(carrotPixels, pixel => pixel.a >= 250),
                    "carrot texture should contain opaque artwork");

                Debug.Log("Pet state and procedural carrot smoke tests passed.");
            }
            finally
            {
                if (secondObject != null)
                {
                    PetNeedsState state =
                        secondObject.GetComponent<PetNeedsState>();
                    if (state != null)
                    {
                        state.SetPersistenceEnabled(false);
                    }
                }
                if (!string.IsNullOrEmpty(migrationSavePath))
                {
                    TryDelete(migrationSavePath);
                    TryDelete(migrationSavePath + ".tmp");
                }
                if (!string.IsNullOrEmpty(blockedSavePath))
                {
                    TryDelete(blockedSavePath + ".tmp");
                }
                if (!string.IsNullOrEmpty(blockedSavePath))
                {
                    try
                    {
                        if (Directory.Exists(blockedSavePath))
                        {
                            Directory.Delete(blockedSavePath, true);
                        }
                    }
                    catch
                    {
                    }
                }
                if (carrot != null)
                {
                    ProceduralCarrotTexture.Destroy(carrot);
                }
                if (secondObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(secondObject);
                }
                if (firstObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(firstObject);
                }
            }
        }

        private static string CreateV3Json(
            string foodStacksJson,
            long completionId)
        {
            return
                "{\"version\":3,\"moodState\":3,\"energy\":50," +
                "\"quietMode\":false," +
                "\"patCooldownRemainingSeconds\":0," +
                "\"feedCooldownRemainingSeconds\":0," +
                "\"foodStacks\":" + foodStacksJson + "," +
                "\"lastAppliedStudyCompletionId\":" + completionId + "," +
                "\"savedAtUnixSeconds\":2000}";
        }

        private static void AssertSnapshotEqual(
            PetNeedsSnapshot expected,
            PetNeedsSnapshot actual,
            string name)
        {
            AssertEqual(expected.Mood, actual.Mood, name + " Mood");
            AssertNear(expected.Energy, actual.Energy, name + " Energy");
            AssertEqual(
                expected.QuietMode,
                actual.QuietMode,
                name + " quiet mode");
            AssertNear(
                expected.PatCooldownRemainingSeconds,
                actual.PatCooldownRemainingSeconds,
                name + " pat cooldown");
            AssertNear(
                expected.FeedCooldownRemainingSeconds,
                actual.FeedCooldownRemainingSeconds,
                name + " feed cooldown");
            AssertEqual(
                expected.LastAppliedStudyCompletionId,
                actual.LastAppliedStudyCompletionId,
                name + " completion ID");
            AssertEqual(
                expected.FoodStacks.Count,
                actual.FoodStacks.Count,
                name + " food stack count");
            for (int index = 0; index < expected.FoodStacks.Count; index++)
            {
                AssertEqual(
                    expected.FoodStacks[index].FoodId,
                    actual.FoodStacks[index].FoodId,
                    name + " food ID " + index);
                AssertEqual(
                    expected.FoodStacks[index].Quantity,
                    actual.FoodStacks[index].Quantity,
                    name + " food quantity " + index);
            }
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }

        private static void AssertNear(float expected, float actual, string name)
        {
            if (Mathf.Abs(expected - actual) > 0.001f)
            {
                throw new InvalidOperationException(
                    name + " expected " + expected + " but was " + actual + ".");
            }
        }

        private static void AssertNear(
            double expected,
            double actual,
            string name)
        {
            if (Math.Abs(expected - actual) > 0.001)
            {
                throw new InvalidOperationException(
                    name + " expected " + expected + " but was " + actual + ".");
            }
        }

        private static void TryDelete(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return;
            }
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
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
