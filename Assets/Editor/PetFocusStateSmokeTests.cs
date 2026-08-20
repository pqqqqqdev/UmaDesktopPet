using System;
using System.IO;
using UmaDesktopPet.Standalone.Runtime;
using UnityEngine;

namespace UmaDesktopPet.Standalone.Editor
{
    public static class PetFocusStateSmokeTests
    {
        private const string EmptySave =
            "{\"version\":1,\"status\":0,\"sessionDurationSeconds\":0," +
            "\"remainingSeconds\":0,\"pendingMoni\":0,\"moni\":0," +
            "\"carrotDeskCharmOwned\":false}";

        public static void Run()
        {
            GameObject firstObject = null;
            GameObject secondObject = null;
            string blockedSavePath = null;
            try
            {
                firstObject = new GameObject("Focus state smoke source");
                PetFocusState state = firstObject.AddComponent<PetFocusState>();
                PrepareForTest(state);

                AssertEqual(FocusSessionStatus.Idle, state.Status, "default status");
                AssertEqual(0, state.Moni, "default Moni");
                Assert(!state.CarrotDeskCharmOwned, "default desk charm");
                PetFocusSnapshot beforeInsufficientPurchase =
                    state.CurrentSnapshot;
                Assert(
                    !state.PurchaseDeskItem(DeskShopCatalog.CarrotCharmId),
                    "known desk item should not purchase without enough Moni");
                AssertSnapshotEqual(
                    beforeInsufficientPurchase,
                    state.CurrentSnapshot,
                    "insufficient-funds purchase");
                PetFocusSnapshot beforeUnownedEquip = state.CurrentSnapshot;
                Assert(
                    !state.EquipDeskItem(DeskShopCatalog.CarrotCharmId),
                    "known but unowned desk item should not equip");
                AssertSnapshotEqual(
                    beforeUnownedEquip,
                    state.CurrentSnapshot,
                    "unowned equip");
                Assert(!state.StartSession(60), "unsupported duration should fail");
                AssertEqual(
                    1,
                    PetFocusState.RewardForDuration(PetFocusState.ShortSessionSeconds),
                    "25-minute reward");
                AssertEqual(
                    2,
                    PetFocusState.RewardForDuration(PetFocusState.LongSessionSeconds),
                    "50-minute reward");

                Assert(
                    state.StartSession(PetFocusState.ShortSessionSeconds),
                    "25-minute session should start");
                int completedEvents = 0;
                state.SessionCompleted += delegate { completedEvents++; };
                state.AdvanceTime(600.0);
                AssertNear(900.0, state.RemainingSeconds, "split elapsed first step");
                Assert(state.PauseSession(), "running session should pause");
                state.AdvanceTime(400.0);
                AssertNear(900.0, state.RemainingSeconds, "paused elapsed");
                Assert(state.ResumeSession(), "paused session should resume");
                state.AdvanceTime(899.5);
                AssertEqual(FocusSessionStatus.Running, state.Status, "near-complete status");
                state.AdvanceTime(0.5);
                AssertEqual(
                    FocusSessionStatus.RewardReady,
                    state.Status,
                    "completed status");
                AssertEqual(1, state.PendingMoni, "pending reward");
                AssertEqual(1L, state.LifetimeMoniEarned, "lifetime Moni");
                AssertEqual(
                    (long)PetFocusState.ShortSessionSeconds,
                    state.LifetimeCompletedFocusSeconds,
                    "lifetime focus seconds");
                AssertEqual(1, completedEvents, "completion event count");
                state.AdvanceTime(9999.0);
                AssertEqual(1, state.PendingMoni, "completion should be exactly once");
                AssertEqual(1, completedEvents, "completion should fire exactly once");
                Assert(state.CollectReward(), "reward should collect");
                AssertEqual(1, state.Moni, "collected Moni");
                Assert(!state.CollectReward(), "reward should not collect twice");
                Assert(state.PurchaseCarrotDeskCharm(), "desk charm should purchase");
                AssertEqual(0, state.Moni, "desk charm cost");
                AssertEqual(1L, state.SpentMoni, "desk charm spending ledger");
                Assert(state.CarrotDeskCharmOwned, "desk charm ownership");
                Assert(
                    !state.PurchaseCarrotDeskCharm(),
                    "desk charm should not purchase twice");

                Assert(
                    state.StartSession(PetFocusState.LongSessionSeconds),
                    "50-minute session should start");
                state.AdvanceTime(125.25);
                string runningJson = state.CreateSaveJson();

                secondObject = new GameObject("Focus state smoke target");
                PetFocusState restored = secondObject.AddComponent<PetFocusState>();
                restored.SetAutomaticTimeEnabled(false);
                restored.SetPersistenceEnabled(false);
                string error;
                Assert(
                    restored.TryRestoreFromJson(runningJson, out error),
                    "running restore failed: " + error);
                AssertEqual(
                    FocusSessionStatus.Paused,
                    restored.Status,
                    "running save should restore paused");
                AssertNear(
                    state.RemainingSeconds,
                    restored.RemainingSeconds,
                    "restored remaining time");
                Assert(restored.StopSession(), "paused session should stop");
                AssertEqual(FocusSessionStatus.Idle, restored.Status, "stopped status");
                AssertEqual(0, restored.PendingMoni, "stopped reward");
                AssertEqual(1L, restored.LifetimeMoniEarned, "restored lifetime Moni");
                Assert(
                    restored.IsDeskItemOwned(DeskShopCatalog.CarrotCharmId),
                    "restored carrot ownership");
                AssertEqual(
                    DeskShopCatalog.CarrotCharmId,
                    restored.EquippedDeskItemId,
                    "restored equipped carrot");

                CompleteAndCollect(restored, PetFocusState.LongSessionSeconds);
                Assert(
                    restored.PurchaseDeskItem(DeskShopCatalog.TazunaRedPenId),
                    "Tazuna red pen should purchase");
                AssertEqual(
                    DeskShopCatalog.TazunaRedPenId,
                    restored.EquippedDeskItemId,
                    "Tazuna red pen auto-equip");
                CompleteAndCollect(restored, PetFocusState.LongSessionSeconds);
                CompleteAndCollect(restored, PetFocusState.ShortSessionSeconds);
                Assert(
                    restored.PurchaseDeskItem(DeskShopCatalog.DerbyTrophyId),
                    "trophy should purchase");
                AssertEqual(3, restored.OwnedDeskItemCount, "owned desk items");
                AssertEqual(6L, restored.LifetimeMoniEarned, "catalog lifetime Moni");
                AssertEqual(6L, restored.SpentMoni, "catalog spending ledger");
                AssertEqual(0, restored.Moni, "catalog wallet");
                Assert(
                    restored.EquipDeskItem(DeskShopCatalog.CarrotCharmId),
                    "owned carrot should equip");
                PetFocusSnapshot multiItemSnapshot = restored.CurrentSnapshot;
                string multiItemJson = restored.CreateSaveJson();
                Assert(
                    state.TryRestoreFromJson(multiItemJson, out error),
                    "multi-item v2 round-trip failed: " + error);
                AssertSnapshotEqual(
                    multiItemSnapshot,
                    state.CurrentSnapshot,
                    "multi-item v2 round-trip");
                Assert(
                    !restored.EquipDeskItem("not-a-real-item"),
                    "unknown item should not equip");
                Assert(restored.ClearEquippedDeskItem(), "equipped item should clear");
                AssertEqual(string.Empty, restored.EquippedDeskItemId, "cleared item");

                PetFocusSnapshot beforeInvalid = restored.CurrentSnapshot;
                Assert(
                    !restored.TryRestoreFromJson(
                        "{\"version\":2,\"status\":0," +
                        "\"sessionDurationSeconds\":0," +
                        "\"remainingSeconds\":0,\"pendingMoni\":0," +
                        "\"moni\":0,\"spentMoni\":1," +
                        "\"lifetimeMoniEarned\":1," +
                        "\"lifetimeCompletedFocusSeconds\":1500," +
                        "\"ownedDeskItemIds\":[\"carrot-charm\"]," +
                        "\"equippedDeskItemId\":\"tazuna-red-pen\"}",
                        out error),
                    "equipped-but-unowned v2 save should fail");
                AssertSnapshotEqual(
                    beforeInvalid,
                    restored.CurrentSnapshot,
                    "equipped-but-unowned save");
                Assert(
                    !restored.TryRestoreFromJson(
                        "{\"version\":1,\"status\":3," +
                        "\"sessionDurationSeconds\":1500," +
                        "\"remainingSeconds\":20,\"pendingMoni\":1," +
                        "\"moni\":0}",
                        out error),
                    "inconsistent reward save should fail");
                AssertSnapshotEqual(
                    beforeInvalid,
                    restored.CurrentSnapshot,
                    "invalid save");

                Assert(
                    !restored.TryRestoreFromJson(
                        "{\"version\":1,\"status\":3," +
                        "\"sessionDurationSeconds\":1500," +
                        "\"remainingSeconds\":0,\"pendingMoni\":1," +
                        "\"moni\":2147483647," +
                        "\"carrotDeskCharmOwned\":false}",
                        out error),
                    "overflowing reward save should fail");
                AssertSnapshotEqual(
                    beforeInvalid,
                    restored.CurrentSnapshot,
                    "overflow save");

                Assert(
                    !restored.TryRestoreFromJson(
                        "{\"version\":2,\"status\":0," +
                        "\"sessionDurationSeconds\":0,\"remainingSeconds\":0," +
                        "\"pendingMoni\":0,\"moni\":0," +
                        "\"lifetimeMoniEarned\":1," +
                        "\"lifetimeCompletedFocusSeconds\":1500," +
                        "\"ownedDeskItemIds\":[\"desk-donut\",\"desk-donut\"]," +
                        "\"equippedDeskItemId\":\"desk-donut\"}",
                        out error),
                    "duplicate ownership should fail");
                AssertSnapshotEqual(
                    beforeInvalid,
                    restored.CurrentSnapshot,
                    "duplicate ownership save");

                Assert(
                    restored.TryRestoreFromJson(
                        "{\"version\":2,\"status\":0," +
                        "\"sessionDurationSeconds\":0,\"remainingSeconds\":0," +
                        "\"pendingMoni\":0,\"moni\":0,\"spentMoni\":2," +
                        "\"lifetimeMoniEarned\":2," +
                        "\"lifetimeCompletedFocusSeconds\":3000," +
                        "\"ownedDeskItemIds\":[\"desk-donut\"]," +
                        "\"equippedDeskItemId\":\"desk-donut\"}",
                        out error),
                    "legacy desk donut migration failed: " + error);
                Assert(
                    restored.IsDeskItemOwned(DeskShopCatalog.TazunaRedPenId),
                    "legacy desk donut should migrate to Tazuna red pen ownership");
                Assert(
                    !restored.IsDeskItemOwned("desk-donut"),
                    "legacy desk donut ID should not remain owned");
                AssertEqual(
                    DeskShopCatalog.TazunaRedPenId,
                    restored.EquippedDeskItemId,
                    "legacy desk donut equipped migration");
                AssertEqual(2L, restored.SpentMoni, "legacy reward spending ledger");
                string migratedRewardJson = restored.CreateSaveJson();
                Assert(
                    migratedRewardJson.Contains("\"tazuna-red-pen\""),
                    "migrated reward save should emit the Tazuna red pen ID");
                Assert(
                    !migratedRewardJson.Contains("\"desk-donut\""),
                    "migrated reward save should drop the legacy desk donut ID");

                Assert(
                    restored.TryRestoreFromJson(
                        "{\"version\":2,\"status\":0," +
                        "\"sessionDurationSeconds\":0,\"remainingSeconds\":0," +
                        "\"pendingMoni\":0,\"moni\":0,\"spentMoni\":5," +
                        "\"lifetimeMoniEarned\":5," +
                        "\"lifetimeCompletedFocusSeconds\":7500," +
                        "\"ownedDeskItemIds\":[\"carrot-charm\"]," +
                        "\"equippedDeskItemId\":\"carrot-charm\"}",
                        out error),
                    "stored purchase price should survive catalog retuning: " + error);
                AssertEqual(5L, restored.SpentMoni, "stored purchase price ledger");

                Assert(
                    restored.TryRestoreFromJson(
                        "{\"version\":1,\"status\":0," +
                        "\"sessionDurationSeconds\":0,\"remainingSeconds\":0," +
                        "\"pendingMoni\":0,\"moni\":2," +
                        "\"carrotDeskCharmOwned\":true}",
                        out error),
                    "v1 carrot migration failed: " + error);
                AssertEqual(3L, restored.LifetimeMoniEarned, "v1 lifetime migration");
                AssertEqual(1L, restored.SpentMoni, "v1 spending migration");
                AssertEqual(4500L, restored.LifetimeCompletedFocusSeconds,
                    "v1 focus migration");
                Assert(restored.CarrotDeskCharmOwned, "v1 carrot migration ownership");
                AssertEqual(DeskShopCatalog.CarrotCharmId,
                    restored.EquippedDeskItemId, "v1 equipped migration");
                Assert(
                    restored.CreateSaveJson().Contains("\"version\": 2"),
                    "migrated save should emit v2");

                Assert(
                    restored.TryRestoreFromJson(EmptySave, out error),
                    "could not reset before persistence failures: " + error);

                blockedSavePath = Path.Combine(
                    Path.GetTempPath(),
                    "UmaDesktopPet-focus-blocked-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(blockedSavePath);
                restored.SetPersistencePathForSmokeTest(blockedSavePath);
                restored.SetPersistenceEnabled(true);
                PetFocusSnapshot beforeFailedStart = restored.CurrentSnapshot;
                Assert(
                    !restored.StartSession(PetFocusState.ShortSessionSeconds),
                    "start should fail when persistence fails");
                AssertSnapshotEqual(
                    beforeFailedStart,
                    restored.CurrentSnapshot,
                    "failed start rollback");

                restored.SetPersistenceEnabled(false);
                Assert(
                    restored.StartSession(PetFocusState.ShortSessionSeconds),
                    "setup session for failed completion");
                restored.AdvanceTime(PetFocusState.ShortSessionSeconds - 1.0);
                int failedCompletionEvents = 0;
                restored.SessionCompleted += delegate { failedCompletionEvents++; };
                restored.SetPersistenceEnabled(true);
                restored.AdvanceTime(1.0);
                AssertEqual(
                    FocusSessionStatus.Paused,
                    restored.Status,
                    "failed completion should pause");
                AssertNear(
                    1.0,
                    restored.RemainingSeconds,
                    "failed completion should restore remaining time");
                AssertEqual(0, restored.PendingMoni, "failed completion reward");
                AssertEqual(0, failedCompletionEvents, "failed completion event count");

                restored.SetPersistenceEnabled(false);
                Assert(
                    restored.TryRestoreFromJson(EmptySave, out error),
                    "could not reset after failed completion: " + error);
                Assert(
                    restored.StartSession(PetFocusState.ShortSessionSeconds),
                    "setup session for failed collect");
                restored.AdvanceTime(PetFocusState.ShortSessionSeconds);
                PetFocusSnapshot beforeFailedCollect = restored.CurrentSnapshot;
                restored.SetPersistenceEnabled(true);
                Assert(
                    !restored.CollectReward(),
                    "collect should fail when persistence fails");
                AssertSnapshotEqual(
                    beforeFailedCollect,
                    restored.CurrentSnapshot,
                    "failed collect rollback");

                restored.SetPersistenceEnabled(false);
                Assert(restored.CollectReward(), "setup Moni after failed collect");
                PetFocusSnapshot beforeFailedPurchase = restored.CurrentSnapshot;
                restored.SetPersistenceEnabled(true);
                Assert(
                    !restored.PurchaseCarrotDeskCharm(),
                    "purchase should fail when persistence fails");
                AssertSnapshotEqual(
                    beforeFailedPurchase,
                    restored.CurrentSnapshot,
                    "failed purchase rollback");
                restored.SetPersistenceEnabled(false);

                Assert(
                    restored.PurchaseDeskItem(DeskShopCatalog.CarrotCharmId),
                    "setup carrot for failed equip");
                CompleteAndCollect(restored, PetFocusState.LongSessionSeconds);
                Assert(
                    restored.PurchaseDeskItem(DeskShopCatalog.TazunaRedPenId),
                    "setup Tazuna red pen for failed equip");
                AssertEqual(
                    DeskShopCatalog.TazunaRedPenId,
                    restored.EquippedDeskItemId,
                    "failed-equip setup equipped item");
                PetFocusSnapshot beforeFailedEquip = restored.CurrentSnapshot;
                restored.SetPersistenceEnabled(true);
                Assert(
                    !restored.EquipDeskItem(DeskShopCatalog.CarrotCharmId),
                    "equip should fail when persistence fails");
                AssertSnapshotEqual(
                    beforeFailedEquip,
                    restored.CurrentSnapshot,
                    "failed equip rollback");

                restored.SetPersistenceEnabled(false);
                Assert(
                    restored.EquipDeskItem(DeskShopCatalog.CarrotCharmId),
                    "setup equipped carrot for failed clear");
                PetFocusSnapshot beforeFailedClear = restored.CurrentSnapshot;
                restored.SetPersistenceEnabled(true);
                Assert(
                    !restored.ClearEquippedDeskItem(),
                    "clear should fail when persistence fails");
                AssertSnapshotEqual(
                    beforeFailedClear,
                    restored.CurrentSnapshot,
                    "failed clear rollback");
                restored.SetPersistenceEnabled(false);

                long fullWalletFocusSeconds =
                    (long)int.MaxValue * PetFocusState.ShortSessionSeconds;
                Assert(
                    restored.TryRestoreFromJson(
                        "{\"version\":2,\"status\":0," +
                        "\"sessionDurationSeconds\":0,\"remainingSeconds\":0," +
                        "\"pendingMoni\":0,\"moni\":2147483647," +
                        "\"spentMoni\":0,\"lifetimeMoniEarned\":2147483647," +
                        "\"lifetimeCompletedFocusSeconds\":" +
                        fullWalletFocusSeconds + ",\"ownedDeskItemIds\":[]," +
                        "\"equippedDeskItemId\":\"\"}",
                        out error),
                    "full-wallet setup restore failed: " + error);
                int fullWalletCompletionEvents = 0;
                restored.SessionCompleted +=
                    delegate { fullWalletCompletionEvents++; };
                Assert(
                    restored.StartSession(PetFocusState.ShortSessionSeconds),
                    "full-wallet session should start");
                restored.AdvanceTime(PetFocusState.ShortSessionSeconds);
                AssertEqual(
                    FocusSessionStatus.Paused,
                    restored.Status,
                    "full-wallet completion should pause");
                AssertEqual(0, restored.PendingMoni, "full-wallet pending reward");
                AssertEqual(int.MaxValue, restored.Moni, "full-wallet balance");
                AssertEqual(
                    (long)int.MaxValue,
                    restored.LifetimeMoniEarned,
                    "full-wallet lifetime Moni");
                AssertEqual(
                    fullWalletFocusSeconds,
                    restored.LifetimeCompletedFocusSeconds,
                    "full-wallet focus seconds");
                AssertEqual(
                    0,
                    fullWalletCompletionEvents,
                    "full-wallet completion event count");

                AssertThrows(
                    delegate { restored.AdvanceTime(-1.0); },
                    "negative elapsed time");
                AssertThrows(
                    delegate { restored.AdvanceTime(double.NaN); },
                    "non-finite elapsed time");

                Debug.Log("Focus session and Moni smoke tests passed.");
            }
            finally
            {
                if (!string.IsNullOrEmpty(blockedSavePath))
                {
                    TryDelete(blockedSavePath + ".tmp");
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

        private static void PrepareForTest(PetFocusState state)
        {
            state.SetAutomaticTimeEnabled(false);
            state.SetPersistenceEnabled(false);
            string error;
            Assert(
                state.TryRestoreFromJson(EmptySave, out error),
                "could not reset focus state: " + error);
        }

        private static void CompleteAndCollect(
            PetFocusState state,
            int durationSeconds)
        {
            Assert(state.StartSession(durationSeconds), "session should start");
            state.AdvanceTime(durationSeconds);
            AssertEqual(
                FocusSessionStatus.RewardReady,
                state.Status,
                "completed helper status");
            Assert(state.CollectReward(), "completed reward should collect");
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

        private static void AssertSnapshotEqual(
            PetFocusSnapshot expected,
            PetFocusSnapshot actual,
            string name)
        {
            AssertEqual(expected.Status, actual.Status, name + " status");
            AssertEqual(
                expected.SessionDurationSeconds,
                actual.SessionDurationSeconds,
                name + " duration");
            AssertNear(
                expected.RemainingSeconds,
                actual.RemainingSeconds,
                name + " remaining");
            AssertEqual(expected.PendingMoni, actual.PendingMoni, name + " pending");
            AssertEqual(expected.Moni, actual.Moni, name + " Moni");
            AssertEqual(expected.SpentMoni, actual.SpentMoni, name + " spent Moni");
            AssertEqual(
                expected.LifetimeMoniEarned,
                actual.LifetimeMoniEarned,
                name + " lifetime Moni");
            AssertEqual(
                expected.LifetimeCompletedFocusSeconds,
                actual.LifetimeCompletedFocusSeconds,
                name + " lifetime focus seconds");
            AssertEqual(
                expected.CarrotDeskCharmOwned,
                actual.CarrotDeskCharmOwned,
                name + " charm");
            AssertEqual(
                expected.EquippedDeskItemId,
                actual.EquippedDeskItemId,
                name + " equipped item");
            AssertEqual(
                expected.OwnedDeskItemIds.Count,
                actual.OwnedDeskItemIds.Count,
                name + " owned item count");
            for (int index = 0; index < expected.OwnedDeskItemIds.Count; index++)
            {
                AssertEqual(
                    expected.OwnedDeskItemIds[index],
                    actual.OwnedDeskItemIds[index],
                    name + " owned item " + index);
            }
        }

        private static void TryDelete(string path)
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

        private static void AssertThrows(Action action, string name)
        {
            try
            {
                action();
            }
            catch (ArgumentOutOfRangeException)
            {
                return;
            }
            throw new InvalidOperationException(name + " should throw.");
        }
    }
}
