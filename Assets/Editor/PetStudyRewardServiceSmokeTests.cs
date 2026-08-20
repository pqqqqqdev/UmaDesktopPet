using System;
using System.IO;
using UmaDesktopPet.Standalone.Runtime;
using UnityEngine;

namespace UmaDesktopPet.Standalone.Editor
{
    public static class PetStudyRewardServiceSmokeTests
    {
        private const string EmptyFocusSave =
            "{\"version\":2,\"status\":0," +
            "\"sessionDurationSeconds\":0,\"remainingSeconds\":0," +
            "\"pendingMoni\":0,\"moni\":0,\"spentMoni\":0," +
            "\"lifetimeMoniEarned\":0," +
            "\"lifetimeCompletedFocusSeconds\":0," +
            "\"ownedDeskItemIds\":[],\"equippedDeskItemId\":\"\"}";

        private const string RestoredShortRewardSave =
            "{\"version\":2,\"status\":3," +
            "\"sessionDurationSeconds\":1500,\"remainingSeconds\":0," +
            "\"pendingMoni\":1,\"moni\":0,\"spentMoni\":0," +
            "\"lifetimeMoniEarned\":1," +
            "\"lifetimeCompletedFocusSeconds\":1500," +
            "\"ownedDeskItemIds\":[],\"equippedDeskItemId\":\"\"}";

        public static void Run()
        {
            TestShortAndLongRewardsApplyOnce();
            TestNeedsSaveFailureKeepsRewardPending();
            TestFocusSaveFailureDoesNotDuplicateCare();
            TestRestoredRewardReconcilesOnConstruction();
            Debug.Log("Pet study reward cross-save smoke tests passed.");
        }

        private static void TestShortAndLongRewardsApplyOnce()
        {
            GameObject focusObject = null;
            GameObject needsObject = null;
            PetStudyRewardService service = null;
            try
            {
                PetFocusState focus = CreateFocus(
                    "Study reward short-long focus",
                    out focusObject);
                PetNeedsState needs = CreateNeeds(
                    "Study reward short-long needs",
                    out needsObject);
                service = new PetStudyRewardService(focus, needs);

                AssertEqual(0, service.PendingFoodQuantity, "idle pending food");
                CompleteSession(focus, PetFocusState.ShortSessionSeconds);
                AssertEqual(1, service.PendingFoodQuantity, "short pending food");
                AssertEqual(4, GetCarrotJelly(needs), "short food quantity");
                AssertNear(72.0f, needs.Energy, "short Energy");
                AssertEqual(1500L, needs.LastAppliedStudyCompletionId,
                    "short completion ID");

                Assert(service.EnsurePendingCareReward(),
                    "short care retry should succeed");
                AssertEqual(4, GetCarrotJelly(needs),
                    "short retry food quantity");
                AssertNear(72.0f, needs.Energy, "short retry Energy");
                Assert(service.TryCollectReward(),
                    "short Moni should collect");
                AssertEqual(1, focus.Moni, "short collected Moni");
                AssertEqual(0, service.PendingFoodQuantity,
                    "collected pending food");

                CompleteSession(focus, PetFocusState.LongSessionSeconds);
                AssertEqual(2, service.PendingFoodQuantity, "long pending food");
                AssertEqual(6, GetCarrotJelly(needs), "long food quantity");
                AssertNear(48.0f, needs.Energy, "long Energy");
                AssertEqual(4500L, needs.LastAppliedStudyCompletionId,
                    "long completion ID");

                Assert(service.EnsurePendingCareReward(),
                    "long care retry should succeed");
                AssertEqual(6, GetCarrotJelly(needs),
                    "long retry food quantity");
                AssertNear(48.0f, needs.Energy, "long retry Energy");
                Assert(service.TryCollectReward(),
                    "long Moni should collect");
                AssertEqual(3, focus.Moni, "total collected Moni");
            }
            finally
            {
                DisposeTestState(service, focusObject, needsObject);
            }
        }

        private static void TestNeedsSaveFailureKeepsRewardPending()
        {
            GameObject focusObject = null;
            GameObject needsObject = null;
            PetStudyRewardService service = null;
            string blockedNeedsPath = null;
            try
            {
                PetFocusState focus = CreateFocus(
                    "Study reward blocked-needs focus",
                    out focusObject);
                PetNeedsState needs = CreateNeeds(
                    "Study reward blocked-needs needs",
                    out needsObject);

                blockedNeedsPath = CreateBlockedPath("needs");
                needs.SetPersistencePathForSmokeTest(blockedNeedsPath);
                needs.SetPersistenceEnabled(true);
                service = new PetStudyRewardService(focus, needs);

                CompleteSession(focus, PetFocusState.ShortSessionSeconds);
                AssertEqual(FocusSessionStatus.RewardReady, focus.Status,
                    "blocked needs focus status");
                AssertEqual(1, focus.PendingMoni, "blocked needs pending Moni");
                AssertEqual(0, focus.Moni, "blocked needs collected Moni");
                AssertEqual(1, service.PendingFoodQuantity,
                    "blocked needs pending food");
                AssertEqual(3, GetCarrotJelly(needs),
                    "blocked needs food rollback");
                AssertNear(84.0f, needs.Energy,
                    "blocked needs Energy rollback");
                AssertEqual(0L, needs.LastAppliedStudyCompletionId,
                    "blocked needs completion rollback");

                Assert(!service.TryCollectReward(),
                    "collection should fail while needs cannot save");
                AssertEqual(FocusSessionStatus.RewardReady, focus.Status,
                    "failed collection focus status");
                AssertEqual(1, focus.PendingMoni,
                    "failed collection pending Moni");
                AssertEqual(3, GetCarrotJelly(needs),
                    "failed collection food rollback");
                AssertNear(84.0f, needs.Energy,
                    "failed collection Energy rollback");

                needs.SetPersistenceEnabled(false);
                Assert(service.TryCollectReward(),
                    "collection should recover after needs saving recovers");
                AssertEqual(4, GetCarrotJelly(needs),
                    "recovered food quantity");
                AssertNear(72.0f, needs.Energy, "recovered Energy");
                AssertEqual(1, focus.Moni, "recovered collected Moni");
            }
            finally
            {
                DisposeTestState(service, focusObject, needsObject);
                TryDeleteOwnedPath(blockedNeedsPath);
            }
        }

        private static void TestFocusSaveFailureDoesNotDuplicateCare()
        {
            GameObject focusObject = null;
            GameObject needsObject = null;
            PetStudyRewardService service = null;
            string needsSavePath = null;
            string blockedFocusPath = null;
            try
            {
                PetFocusState focus = CreateFocus(
                    "Study reward blocked-focus focus",
                    out focusObject);
                PetNeedsState needs = CreateNeeds(
                    "Study reward blocked-focus needs",
                    out needsObject);

                needsSavePath = Path.Combine(
                    Path.GetTempPath(),
                    "UmaDesktopPet-study-needs-" +
                    Guid.NewGuid().ToString("N") + ".json");
                needs.SetPersistencePathForSmokeTest(needsSavePath);
                needs.SetPersistenceEnabled(true);
                service = new PetStudyRewardService(focus, needs);

                CompleteSession(focus, PetFocusState.ShortSessionSeconds);
                Assert(File.Exists(needsSavePath),
                    "care reward should be durably saved first");
                AssertEqual(4, GetCarrotJelly(needs),
                    "pre-collection food quantity");
                AssertNear(72.0f, needs.Energy,
                    "pre-collection Energy");

                blockedFocusPath = CreateBlockedPath("focus");
                focus.SetPersistencePathForSmokeTest(blockedFocusPath);
                focus.SetPersistenceEnabled(true);
                Assert(!service.TryCollectReward(),
                    "collection should fail when focus cannot save");
                AssertEqual(FocusSessionStatus.RewardReady, focus.Status,
                    "focus rollback status");
                AssertEqual(1, focus.PendingMoni, "focus rollback pending Moni");
                AssertEqual(0, focus.Moni, "focus rollback collected Moni");
                AssertEqual(4, GetCarrotJelly(needs),
                    "failed focus save must not duplicate food");
                AssertNear(72.0f, needs.Energy,
                    "failed focus save must not duplicate Energy cost");
                AssertEqual(1500L, needs.LastAppliedStudyCompletionId,
                    "failed focus save completion ID");

                focus.SetPersistenceEnabled(false);
                Assert(service.TryCollectReward(),
                    "collection retry should succeed");
                AssertEqual(FocusSessionStatus.Idle, focus.Status,
                    "collection retry status");
                AssertEqual(1, focus.Moni, "collection retry Moni");
                AssertEqual(4, GetCarrotJelly(needs),
                    "collection retry must not duplicate food");
                AssertNear(72.0f, needs.Energy,
                    "collection retry must not duplicate Energy cost");
            }
            finally
            {
                DisposeTestState(service, focusObject, needsObject);
                TryDeleteOwnedPath(needsSavePath);
                TryDeleteOwnedPath(blockedFocusPath);
            }
        }

        private static void TestRestoredRewardReconcilesOnConstruction()
        {
            GameObject focusObject = null;
            GameObject needsObject = null;
            PetStudyRewardService service = null;
            try
            {
                PetFocusState focus = CreateFocus(
                    "Study reward restored focus",
                    out focusObject);
                PetNeedsState needs = CreateNeeds(
                    "Study reward restored needs",
                    out needsObject);
                string error;
                Assert(
                    focus.TryRestoreFromJson(RestoredShortRewardSave, out error),
                    "reward-ready restore failed: " + error);

                service = new PetStudyRewardService(focus, needs);
                AssertEqual(FocusSessionStatus.RewardReady, focus.Status,
                    "restored reward status");
                AssertEqual(4, GetCarrotJelly(needs),
                    "restored reward food quantity");
                AssertNear(72.0f, needs.Energy,
                    "restored reward Energy");
                AssertEqual(1500L, needs.LastAppliedStudyCompletionId,
                    "restored reward completion ID");

                Assert(service.EnsurePendingCareReward(),
                    "restored reward reconciliation retry should succeed");
                AssertEqual(4, GetCarrotJelly(needs),
                    "restored retry must not duplicate food");
                AssertNear(72.0f, needs.Energy,
                    "restored retry must not duplicate Energy cost");
                Assert(service.TryCollectReward(),
                    "restored reward should collect");
                AssertEqual(1, focus.Moni, "restored collected Moni");
            }
            finally
            {
                DisposeTestState(service, focusObject, needsObject);
            }
        }

        private static PetFocusState CreateFocus(
            string objectName,
            out GameObject gameObject)
        {
            gameObject = new GameObject(objectName);
            PetFocusState focus = gameObject.AddComponent<PetFocusState>();
            focus.SetAutomaticTimeEnabled(false);
            focus.SetPersistenceEnabled(false);
            string error;
            Assert(focus.TryRestoreFromJson(EmptyFocusSave, out error),
                "could not reset focus state: " + error);
            return focus;
        }

        private static PetNeedsState CreateNeeds(
            string objectName,
            out GameObject gameObject)
        {
            gameObject = new GameObject(objectName);
            PetNeedsState needs = gameObject.AddComponent<PetNeedsState>();
            needs.SetAutomaticTimeEnabled(false);
            needs.SetPersistenceEnabled(false);
            needs.ResetNeeds();
            return needs;
        }

        private static void CompleteSession(
            PetFocusState focus,
            int durationSeconds)
        {
            Assert(focus.StartSession(durationSeconds),
                "study session should start");
            focus.AdvanceTime(durationSeconds);
            AssertEqual(FocusSessionStatus.RewardReady, focus.Status,
                "completed study status");
        }

        private static int GetCarrotJelly(PetNeedsState needs)
        {
            return needs.GetFoodQuantity(FoodCatalog.CarrotJellyId);
        }

        private static string CreateBlockedPath(string label)
        {
            string path = Path.Combine(
                Path.GetTempPath(),
                "UmaDesktopPet-study-blocked-" + label + "-" +
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        private static void DisposeTestState(
            PetStudyRewardService service,
            GameObject focusObject,
            GameObject needsObject)
        {
            if (service != null)
            {
                service.Dispose();
            }
            if (focusObject != null)
            {
                PetFocusState focus = focusObject.GetComponent<PetFocusState>();
                if (focus != null)
                {
                    focus.SetPersistenceEnabled(false);
                }
            }
            if (needsObject != null)
            {
                PetNeedsState needs = needsObject.GetComponent<PetNeedsState>();
                if (needs != null)
                {
                    needs.SetPersistenceEnabled(false);
                }
            }
            if (needsObject != null)
            {
                UnityEngine.Object.DestroyImmediate(needsObject);
            }
            if (focusObject != null)
            {
                UnityEngine.Object.DestroyImmediate(focusObject);
            }
        }

        private static void TryDeleteOwnedPath(string path)
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
                if (File.Exists(path + ".tmp"))
                {
                    File.Delete(path + ".tmp");
                }
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
