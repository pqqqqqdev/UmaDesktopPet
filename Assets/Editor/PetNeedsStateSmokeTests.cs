using System;
using UmaDesktopPet.Standalone.Runtime;
using UnityEngine;

namespace UmaDesktopPet.Standalone.Editor
{
    /// <summary>
    /// Dependency-free editor checks for the state model and v1 migration.
    /// Run with Unity's -executeMethod option in CI or local verification.
    /// </summary>
    public static class PetNeedsStateSmokeTests
    {
        public static void Run()
        {
            GameObject firstObject = null;
            GameObject secondObject = null;
            Texture2D carrot = null;
            try
            {
                firstObject = new GameObject("Pet state smoke source");
                PetNeedsState state = firstObject.AddComponent<PetNeedsState>();
                state.SetAutomaticTimeEnabled(false);
                state.ResetNeeds();

                AssertEqual(PetMood.Normal, state.Mood, "starting Mood");
                AssertNear(84.0f, state.Energy, "starting Energy");

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
                Assert(state.CanFeed, "carrot should have no cooldown");
                Assert(state.TryFeed(), "immediate second carrot should be accepted");

                PetMood beforeOfflineMood = state.Mood;
                float beforeOfflineEnergy = state.Energy;
                state.AdvanceOfflineTime(24.0 * 60.0 * 60.0);
                AssertEqual(beforeOfflineMood, state.Mood, "offline Mood");
                AssertNear(beforeOfflineEnergy, state.Energy, "offline Energy");
                Assert(state.CanPat, "offline time should clear the pat cooldown");
                Assert(state.CanFeed, "offline time should clear the carrot cooldown");

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

                state.ResetNeeds();
                state.SetQuietMode(true);
                Assert(state.TryPat(), "round-trip pat should be accepted");
                string v2Json = state.CreateSaveJson(2000);

                secondObject = new GameObject("Pet state smoke target");
                PetNeedsState restored =
                    secondObject.AddComponent<PetNeedsState>();
                restored.SetAutomaticTimeEnabled(false);
                restored.ResetNeeds();
                Assert(
                    restored.TryRestoreFromJson(v2Json, 2000, out error),
                    "v2 restore failed: " + error);
                AssertEqual(state.Mood, restored.Mood, "round-trip Mood");
                AssertNear(state.Energy, restored.Energy, "round-trip Energy");
                Assert(restored.QuietMode, "round-trip quiet mode");

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
