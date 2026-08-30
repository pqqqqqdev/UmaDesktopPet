using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UmaDesktopPet.Standalone.Core;

namespace UmaDesktopPet.Standalone.Runtime
{
    public enum GameCompatibilityStatus
    {
        Compatible,
        MissingRoot,
        MissingCatalog,
        EmptyCatalog,
        CatalogUnreadable,
        CharacterUnavailable,
        RequiredFilesMissing
    }

    /// <summary>
    /// Read-only result of checking one installed Umamusume data root.
    /// Optional UI art never changes <see cref="Status"/>.
    /// </summary>
    public sealed class GameCompatibilityReport
    {
        internal GameCompatibilityReport(
            GameCompatibilityStatus status,
            string gameRoot,
            GameRegion? region,
            string message,
            string details,
            IEnumerable<string> missingRequired,
            bool moodUiAvailable,
            bool carrotUiAvailable)
        {
            Status = status;
            GameRoot = gameRoot ?? string.Empty;
            Region = region;
            Message = message ?? string.Empty;
            Details = details ?? string.Empty;
            MissingRequired = (missingRequired ?? Enumerable.Empty<string>()).ToArray();
            MoodUiAvailable = moodUiAvailable;
            CarrotUiAvailable = carrotUiAvailable;
        }

        public GameCompatibilityStatus Status { get; private set; }
        public string GameRoot { get; private set; }
        public GameRegion? Region { get; private set; }
        public string Message { get; private set; }
        public string Details { get; private set; }
        public string[] MissingRequired { get; private set; }
        public bool MoodUiAvailable { get; private set; }
        public bool CarrotUiAvailable { get; private set; }
        public bool IsCompatible { get { return Status == GameCompatibilityStatus.Compatible; } }
    }

    /// <summary>
    /// Verifies that a JP or Global install contains the local files needed by
    /// the selected desktop Uma. Nothing is written, copied, or exported.
    /// </summary>
    public static class GameCompatibilityProbe
    {
        private const string MoodAssetPrefix =
            "uianimation/flash/singlemode/statusicon/utx_ico_motivation_l_0";
        private const string CarrotJellyAsset = "item/item_icon_00035";
        private const string CarrotJellyMiniAsset = "item/item_icon_00034";

        private static readonly string[] SharedMotionAssets =
        {
            OguriPetAnimationController.PatHappyStartAsset,
            OguriPetAnimationController.PatHappyLoopAsset,
            OguriPetAnimationController.PatHappyEndAsset,
            OguriPetAnimationController.FeedResponseStartAsset,
            OguriPetAnimationController.FeedResponseLoopAsset,
            OguriPetAnimationController.FeedResponseEndAsset,
            OguriPetAnimationController.AmbientGreetingStartAsset,
            OguriPetAnimationController.AmbientGreetingLoopAsset,
            OguriPetAnimationController.AmbientGreetingEndAsset,
            OguriPetAnimationController.DragLiftAsset,
            OguriPetAnimationController.DragHoldAsset
        };

        public static GameCompatibilityReport Probe(
            string gameRoot,
            string sqlite3McLibraryPath)
        {
            return Probe(
                gameRoot,
                sqlite3McLibraryPath,
                PetCharacterCatalog.Oguri);
        }

        public static GameCompatibilityReport Probe(
            string gameRoot,
            string sqlite3McLibraryPath,
            PetCharacterProfile characterProfile)
        {
            characterProfile = characterProfile ?? PetCharacterCatalog.Oguri;
            string normalizedRoot;
            if (string.IsNullOrWhiteSpace(gameRoot) ||
                !LocalPathPolicy.TryGetLocalFullPath(gameRoot, out normalizedRoot))
            {
                return Failure(
                    GameCompatibilityStatus.MissingRoot,
                    gameRoot,
                    "Choose a local Umamusume game-data folder.",
                    "Network, UNC, and invalid filesystem paths are not supported.");
            }

            if (normalizedRoot.Length == 0 || !Directory.Exists(normalizedRoot))
            {
                return Failure(
                    GameCompatibilityStatus.MissingRoot,
                    normalizedRoot,
                    "Choose your Umamusume game-data folder.",
                    "The selected directory does not exist.");
            }

            string metaPath = Path.Combine(normalizedRoot, "meta");
            string masterPath = Path.Combine(normalizedRoot, "master", "master.mdb");
            string datPath = Path.Combine(normalizedRoot, "dat");
            if (!File.Exists(metaPath) ||
                !File.Exists(masterPath) ||
                !Directory.Exists(datPath))
            {
                return Failure(
                    GameCompatibilityStatus.MissingCatalog,
                    normalizedRoot,
                    "That folder is missing Umamusume game data.",
                    "Expected meta, master\\master.mdb, and dat.");
            }

            GameDataCatalog catalog;
            try
            {
                catalog = GameDataCatalog.Open(normalizedRoot, sqlite3McLibraryPath);
            }
            catch (Exception exception)
            {
                return Failure(
                    GameCompatibilityStatus.CatalogUnreadable,
                    normalizedRoot,
                    "These game files could not be read. Repair or update Umamusume.",
                    exception.ToString());
            }

            using (catalog)
            {
                if (catalog.AssetCount == 0)
                {
                    string emptyCatalogMessage = catalog.Region == GameRegion.Global
                        ? "Global's asset catalog is empty. Launch Global, finish " +
                            "any update, then use Settings > Download All. Close " +
                            "the game normally and Scan again."
                        : "The game's asset catalog is empty. Launch Umamusume, " +
                            "finish any update, close it normally, and Scan again.";
                    return new GameCompatibilityReport(
                        GameCompatibilityStatus.EmptyCatalog,
                        normalizedRoot,
                        catalog.Region,
                        emptyCatalogMessage,
                        "The encrypted meta asset table contains zero rows.",
                        null,
                        false,
                        false);
                }

                CharacterRecord character;
                MiniCharacterAssemblyProfile baseAssemblyProfile;
                PetCharacterMotionProfile motionProfile;
                PetCharacterDefinition characterDefinition;
                if (!PetCharacterDefinitions.TryGet(
                    characterProfile.Key,
                    out characterDefinition))
                {
                    return new GameCompatibilityReport(
                        GameCompatibilityStatus.CharacterUnavailable,
                        normalizedRoot,
                        catalog.Region,
                        characterProfile.DisplayName +
                            " is not supported by this build.",
                        "The selected character has no verified assembly or motion profile.",
                        null,
                        HasMoodUi(catalog),
                        HasCarrotUi(catalog));
                }
                baseAssemblyProfile = characterDefinition.Assembly;
                motionProfile = characterDefinition.Motions;
                try
                {
                    character = catalog.GetCharacter(characterProfile.GameCharacterId);
                }
                catch (KeyNotFoundException exception)
                {
                    return new GameCompatibilityReport(
                        GameCompatibilityStatus.CharacterUnavailable,
                        normalizedRoot,
                        catalog.Region,
                        characterProfile.DisplayName +
                            " is not available in this installation.",
                        exception.Message,
                        null,
                        HasMoodUi(catalog),
                        HasCarrotUi(catalog));
                }
                catch (Exception exception)
                {
                    return new GameCompatibilityReport(
                        GameCompatibilityStatus.CatalogUnreadable,
                        normalizedRoot,
                        catalog.Region,
                        "These game files could not be read. Repair or update Umamusume.",
                        exception.ToString(),
                        null,
                        HasMoodUi(catalog),
                        HasCarrotUi(catalog));
                }

                var missing = new List<string>();
                var checkedNames = new HashSet<string>(StringComparer.Ordinal);
                CheckRequiredAsset(
                    catalog,
                    BundleRepository.DefaultShaderBundleName,
                    missing,
                    checkedNames);
                int installedSkin;
                try
                {
                    installedSkin = MiniCharacterAssembler.ResolveInstalledSkin(
                        character.Skin,
                        baseAssemblyProfile.Skin);
                }
                catch (InvalidDataException exception)
                {
                    return new GameCompatibilityReport(
                        GameCompatibilityStatus.CatalogUnreadable,
                        normalizedRoot,
                        catalog.Region,
                        characterProfile.ShortName +
                            "'s installed character data is invalid. Repair or " +
                            "update Umamusume, then try again.",
                        exception.Message,
                        null,
                        HasMoodUi(catalog),
                        HasCarrotUi(catalog));
                }
                MiniCharacterAssemblyProfile assemblyProfile =
                    baseAssemblyProfile.WithInstalledAppearance(
                        installedSkin,
                        character.TailModelId > 0
                            ? (int?)character.TailModelId
                            : null,
                        null);
                CheckRequiredAsset(
                    catalog,
                    assemblyProfile.BodyAsset,
                    missing,
                    checkedNames);
                CheckRequiredAsset(
                    catalog,
                    assemblyProfile.HairAsset,
                    missing,
                    checkedNames);
                CheckRequiredAsset(
                    catalog,
                    assemblyProfile.FaceAsset,
                    missing,
                    checkedNames);
                if (assemblyProfile.HasTail)
                {
                    CheckRequiredAsset(
                        catalog,
                        assemblyProfile.TailAsset,
                        missing,
                        checkedNames);
                    CheckRequiredPrefix(
                        catalog,
                        assemblyProfile.TailTexturePrefix,
                        missing,
                        checkedNames);
                }
                CheckRequiredPrefix(
                    catalog,
                    assemblyProfile.HeadTexturePrefix,
                    missing,
                    checkedNames);
                CheckRequiredPrefix(
                    catalog,
                    assemblyProfile.SharedFaceTexturePrefix,
                    missing,
                    checkedNames);

                string[] motionAssets = SharedMotionAssets
                    .Concat(new[]
                    {
                        motionProfile.Idle.StartAsset,
                        motionProfile.Idle.LoopAsset,
                        motionProfile.Idle.EndAsset,
                        motionProfile.Tap.StartAsset,
                        motionProfile.Tap.LoopAsset,
                        motionProfile.Tap.EndAsset
                    })
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                for (int index = 0; index < motionAssets.Length; index++)
                {
                    CheckRequiredAsset(catalog, motionAssets[index], missing, checkedNames);
                }

                bool moodUiAvailable = HasMoodUi(catalog);
                bool carrotUiAvailable = HasCarrotUi(catalog);
                if (missing.Count > 0)
                {
                    string recoveryMessage = catalog.Region == GameRegion.Global
                        ? characterProfile.ShortName +
                            "'s local game files are missing. Open Umamusume, " +
                            "use Download All in Settings, then try again."
                        : "Some " + characterProfile.ShortName +
                            " game files are missing. Repair or update " +
                            "Umamusume, then try again.";
                    return new GameCompatibilityReport(
                        GameCompatibilityStatus.RequiredFilesMissing,
                        normalizedRoot,
                        catalog.Region,
                        recoveryMessage,
                        string.Join(Environment.NewLine, missing.ToArray()),
                        missing,
                        moodUiAvailable,
                        carrotUiAvailable);
                }

                return new GameCompatibilityReport(
                    GameCompatibilityStatus.Compatible,
                    normalizedRoot,
                    catalog.Region,
                    "Ready to use " + catalog.Region + " game files.",
                    string.Empty,
                    null,
                    moodUiAvailable,
                    carrotUiAvailable);
            }
        }

        private static GameCompatibilityReport Failure(
            GameCompatibilityStatus status,
            string gameRoot,
            string message,
            string details)
        {
            return new GameCompatibilityReport(
                status,
                gameRoot,
                null,
                message,
                details,
                null,
                false,
                false);
        }

        private static void CheckRequiredPrefix(
            GameDataCatalog catalog,
            string prefix,
            List<string> missing,
            HashSet<string> checkedNames)
        {
            GameAssetRecord[] matches = catalog.FindByPrefix(prefix).ToArray();
            if (matches.Length == 0)
            {
                missing.Add("Missing catalog assets: " + prefix + "*");
                return;
            }

            for (int index = 0; index < matches.Length; index++)
            {
                CheckRequiredRecord(catalog, matches[index], missing, checkedNames);
            }
        }

        private static void CheckRequiredAsset(
            GameDataCatalog catalog,
            string logicalName,
            List<string> missing,
            HashSet<string> checkedNames)
        {
            GameAssetRecord record;
            if (!catalog.TryGetAsset(logicalName, out record))
            {
                missing.Add("Missing catalog asset: " + logicalName);
                return;
            }

            CheckRequiredRecord(catalog, record, missing, checkedNames);
        }

        private static void CheckRequiredRecord(
            GameDataCatalog catalog,
            GameAssetRecord record,
            List<string> missing,
            HashSet<string> checkedNames)
        {
            IEnumerable<GameAssetRecord> loadOrder;
            try
            {
                loadOrder = catalog.ResolveLoadOrder(record);
            }
            catch (Exception exception)
            {
                missing.Add(record.Name + ": " + exception.Message);
                return;
            }

            foreach (GameAssetRecord dependency in loadOrder)
            {
                if (!checkedNames.Add(dependency.Name))
                {
                    continue;
                }

                string filePath;
                try
                {
                    filePath = dependency.FilePath;
                }
                catch (Exception exception)
                {
                    missing.Add(dependency.Name + ": " + exception.Message);
                    continue;
                }

                if (!File.Exists(filePath))
                {
                    missing.Add("Missing local file: " + dependency.Name);
                }
            }
        }

        private static bool HasMoodUi(GameDataCatalog catalog)
        {
            for (int index = 0; index < 5; index++)
            {
                GameAssetRecord record;
                if (!catalog.TryGetAsset(MoodAssetPrefix + index, out record) ||
                    !HasLocalFile(record))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool HasCarrotUi(GameDataCatalog catalog)
        {
            GameAssetRecord record;
            return (catalog.TryGetAsset(CarrotJellyAsset, out record) &&
                    HasLocalFile(record)) ||
                (catalog.TryGetAsset(CarrotJellyMiniAsset, out record) &&
                    HasLocalFile(record));
        }

        private static bool HasLocalFile(GameAssetRecord record)
        {
            try
            {
                return record != null && File.Exists(record.FilePath);
            }
            catch
            {
                return false;
            }
        }
    }
}
