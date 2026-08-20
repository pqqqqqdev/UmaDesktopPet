using System;
using System.IO;
using System.Text;
using UmaDesktopPet.Standalone.Runtime;
using UnityEngine;

namespace UmaDesktopPet.Standalone.Editor
{
    public static class PetRecordingModeSmokeTests
    {
        private const string FocusSentinel =
            "{\"version\":2,\"status\":0,\"sessionDurationSeconds\":0," +
            "\"remainingSeconds\":0,\"pendingMoni\":0,\"moni\":0," +
            "\"spentMoni\":0,\"lifetimeMoniEarned\":0," +
            "\"lifetimeCompletedFocusSeconds\":0," +
            "\"ownedDeskItemIds\":[],\"equippedDeskItemId\":\"\"}";

        private const string NeedsSentinel =
            "{\"version\":3,\"moodState\":3,\"energy\":84," +
            "\"quietMode\":false,\"patCooldownRemainingSeconds\":0," +
            "\"feedCooldownRemainingSeconds\":0," +
            "\"foodStacks\":[{\"foodId\":\"carrot-jelly\",\"quantity\":3}]," +
            "\"lastAppliedStudyCompletionId\":0," +
            "\"savedAtUnixSeconds\":100}";

        public static void Run()
        {
            string directory = Path.Combine(
                Path.GetTempPath(),
                "UmaDesktopPetRecordingMode-" + Guid.NewGuid().ToString("N"));
            string focusPath = Path.Combine(directory, "pet-focus.json");
            string needsPath = Path.Combine(directory, "pet-needs.json");
            GameObject focusObject = null;
            GameObject needsObject = null;
            PetStudyRewardService rewards = null;
            try
            {
                Directory.CreateDirectory(directory);
                File.WriteAllText(
                    focusPath,
                    FocusSentinel,
                    new UTF8Encoding(false));
                File.WriteAllText(
                    needsPath,
                    NeedsSentinel,
                    new UTF8Encoding(false));

                focusObject = new GameObject("Recording focus state");
                focusObject.SetActive(false);
                PetFocusState focus = focusObject.AddComponent<PetFocusState>();
                focus.SetPersistencePathForSmokeTest(focusPath);
                focusObject.SetActive(true);

                needsObject = new GameObject("Recording care state");
                needsObject.SetActive(false);
                PetNeedsState needs = needsObject.AddComponent<PetNeedsState>();
                needs.SetPersistencePathForSmokeTest(needsPath);
                needsObject.SetActive(true);

                focus.EnterRecordingMode();
                needs.EnterRecordingMode();
                rewards = new PetStudyRewardService(focus, needs);

                Assert(focus.IsRecordingMode, "focus recording mode");
                Assert(needs.IsRecordingMode, "care recording mode");
                AssertEqual(FocusSessionStatus.Idle, focus.Status, "initial focus");
                AssertEqual(0, focus.Moni, "initial Moni");
                AssertEqual(PetMood.Normal, needs.Mood, "initial Mood");
                AssertNear(84.0, needs.Energy, "initial Energy");
                AssertEqual(
                    PetNeedsState.StarterCarrotJellyQuantity,
                    needs.GetFoodQuantity(FoodCatalog.CarrotJellyId),
                    "initial Jelly");

                PetMood[] moods =
                {
                    PetMood.Awful,
                    PetMood.Bad,
                    PetMood.Normal,
                    PetMood.Good,
                    PetMood.Great
                };
                for (int index = 0; index < moods.Length; index++)
                {
                    Assert(needs.SetMoodForRecording(moods[index]),
                        "set recording Mood " + moods[index]);
                    AssertEqual(moods[index], needs.Mood,
                        "recording Mood " + moods[index]);
                }
                Assert(!needs.SetMoodForRecording((PetMood)99),
                    "invalid recording Mood");

                Assert(focus.SetStudyRemainingForRecording(60),
                    "stage one minute");
                AssertEqual(FocusSessionStatus.Running, focus.Status,
                    "staged status");
                AssertEqual(PetFocusState.ShortSessionSeconds,
                    focus.SessionDurationSeconds, "staged duration");
                AssertNear(60.0, focus.RemainingSeconds, "staged minute");
                Assert(focus.SetStudyRemainingForRecording(10),
                    "stage ten seconds");
                AssertNear(10.0, focus.RemainingSeconds, "staged seconds");

                int completionEvents = 0;
                focus.SessionCompleted += delegate { completionEvents++; };
                Assert(focus.CompleteStudyForRecording(),
                    "complete recording session");
                AssertEqual(FocusSessionStatus.RewardReady, focus.Status,
                    "reward-ready status");
                AssertEqual(1, completionEvents, "completion events");
                AssertEqual(4,
                    needs.GetFoodQuantity(FoodCatalog.CarrotJellyId),
                    "completion Jelly");
                AssertNear(72.0, needs.Energy, "completion Energy");
                Assert(!focus.GrantMoniForRecording(10),
                    "grant while reward-ready");
                Assert(rewards.TryCollectReward(), "collect recording reward");
                AssertEqual(1, focus.Moni, "collected Moni");
                AssertEqual(4,
                    needs.GetFoodQuantity(FoodCatalog.CarrotJellyId),
                    "collect does not duplicate Jelly");
                AssertNear(72.0, needs.Energy,
                    "collect does not duplicate Energy cost");

                Assert(focus.GrantMoniForRecording(10), "grant recording Moni");
                AssertEqual(11, focus.Moni, "granted Moni");
                Assert(focus.PurchaseDeskItem(DeskShopCatalog.CarrotCharmId),
                    "buy carrot charm");
                Assert(focus.PurchaseDeskItem(DeskShopCatalog.TazunaRedPenId),
                    "buy red pen");
                Assert(focus.PurchaseDeskItem(DeskShopCatalog.DerbyTrophyId),
                    "buy trophy");
                AssertEqual(3, focus.OwnedDeskItemCount,
                    "completed desk collection");
                Assert(focus.ResetDeskCollectionForRecording(),
                    "reset desk collection");
                AssertEqual(0, focus.OwnedDeskItemCount,
                    "reset owned count");
                AssertEqual(string.Empty, focus.EquippedDeskItemId,
                    "reset equipped item");
                AssertEqual(5, focus.Moni, "collection reset keeps Moni");
                AssertEqual(6L, focus.SpentMoni,
                    "collection reset keeps accounting");

                needs.SetQuietMode(true);
                focus.ResetRecordingState();
                needs.ResetRecordingState();
                AssertEqual(FocusSessionStatus.Idle, focus.Status,
                    "reset focus status");
                AssertEqual(0, focus.Moni, "reset Moni");
                AssertEqual(0, focus.OwnedDeskItemCount,
                    "reset collection");
                AssertEqual(PetMood.Normal, needs.Mood, "reset Mood");
                Assert(!needs.QuietMode, "reset Quiet Mode");
                AssertNear(84.0, needs.Energy, "reset Energy");
                AssertEqual(3,
                    needs.GetFoodQuantity(FoodCatalog.CarrotJellyId),
                    "reset Jelly");
                AssertEqual(0L, needs.LastAppliedStudyCompletionId,
                    "reset completion receipt");

                Assert(!focus.LoadNow(), "recording focus load is blocked");
                Assert(!needs.LoadNow(200), "recording care load is blocked");
                Assert(focus.SaveNow(), "recording focus save is a no-op");
                Assert(needs.SaveNow(200), "recording care save is a no-op");
                focus.SetPersistenceEnabled(true);
                needs.SetPersistenceEnabled(true);
                focusObject.SetActive(false);
                needsObject.SetActive(false);
                rewards.Dispose();
                rewards = null;
                UnityEngine.Object.DestroyImmediate(focusObject);
                focusObject = null;
                UnityEngine.Object.DestroyImmediate(needsObject);
                needsObject = null;
                AssertEqual(FocusSentinel,
                    File.ReadAllText(focusPath, Encoding.UTF8),
                    "focus sentinel after lifecycle teardown");
                AssertEqual(NeedsSentinel,
                    File.ReadAllText(needsPath, Encoding.UTF8),
                    "care sentinel after lifecycle teardown");
                AssertEqual(
                    2,
                    Directory.GetFiles(directory).Length,
                    "recording mode should not create temporary or backup files");
            }
            finally
            {
                if (rewards != null)
                {
                    rewards.Dispose();
                }
                if (focusObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(focusObject);
                }
                if (needsObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(needsObject);
                }
                TryDelete(directory);
            }

            Debug.Log("Pet recording-mode smoke tests passed.");
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                }
            }
            catch
            {
            }
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }

        private static void AssertNear(double expected, double actual, string name)
        {
            if (Math.Abs(expected - actual) > 0.001)
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
