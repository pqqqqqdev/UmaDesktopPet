using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;

namespace UmaDesktopPet.Standalone.Runtime
{
    /// <summary>
    /// Immutable, app-owned recipe for one explicitly verified installed mini
    /// character. Callers provide semantic numeric IDs, never bundle paths.
    /// </summary>
    public sealed class MiniCharacterAssemblyProfile
    {
        private const int MaximumCharacterModelId = 9999;
        private const int MaximumModelVariantId = 99;

        private readonly IReadOnlyList<string> _requiredRootLogicalNames;

        public MiniCharacterAssemblyProfile(
            string key,
            string displayName,
            int characterId,
            int? dressId,
            int costumeId,
            int headSubId,
            int skin,
            int? tailModelId,
            int? attachmentModelId)
        {
            ValidateKey(key);
            if (string.IsNullOrWhiteSpace(displayName))
            {
                throw new ArgumentException(
                    "A mini-character display name is required.",
                    "displayName");
            }
            ValidatePositiveModelId(characterId, "characterId");
            if (dressId.HasValue && dressId.Value <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    "dressId",
                    "A known master dress row must have a positive ID.");
            }
            ValidateVariant(costumeId, "costumeId");
            ValidateVariant(headSubId, "headSubId");
            ValidateVariant(skin, "skin");
            ValidateOptionalModelId(tailModelId, "tailModelId");
            ValidateOptionalModelId(attachmentModelId, "attachmentModelId");

            Key = key;
            DisplayName = displayName;
            CharacterId = characterId;
            DressId = dressId;
            CostumeId = costumeId;
            HeadSubId = headSubId;
            Skin = skin;
            TailModelId = tailModelId;
            AttachmentModelId = attachmentModelId;

            CharacterToken = characterId.ToString("0000", CultureInfo.InvariantCulture);
            CostumeToken = costumeId.ToString("00", CultureInfo.InvariantCulture);
            HeadSubToken = headSubId.ToString("00", CultureInfo.InvariantCulture);

            BodyAsset =
                "3d/chara/mini/body/mbdy" + CharacterToken + "_" + CostumeToken +
                "/pfb_mbdy" + CharacterToken + "_" + CostumeToken;
            HairAsset =
                "3d/chara/mini/head/mchr" + CharacterToken + "_" + HeadSubToken +
                "/pfb_mchr" + CharacterToken + "_" + HeadSubToken + "_hair";
            FaceAsset =
                "3d/chara/mini/head/mchr0001_00/pfb_mchr0001_00_face0";
            IdleAsset =
                "3d/motion/mini/event/body/chara/chr" + CharacterToken +
                "_00/anm_min_eve_chr" + CharacterToken + "_00_idle01_loop";
            HeadTexturePrefix =
                "3d/chara/mini/head/mchr" + CharacterToken + "_" + HeadSubToken +
                "/textures/";
            SharedFaceTexturePrefix =
                "3d/chara/mini/head/mchr0001_00/textures/" +
                "tex_mchr0001_00_face0_" +
                skin.ToString(CultureInfo.InvariantCulture);

            if (tailModelId.HasValue)
            {
                TailToken = tailModelId.Value.ToString(
                    "0000",
                    CultureInfo.InvariantCulture);
                TailAsset =
                    "3d/chara/mini/tail/mtail" + TailToken + "_00/" +
                    "pfb_mtail" + TailToken + "_00";
                TailTexturePrefix =
                    "3d/chara/mini/tail/mtail" + TailToken + "_00/textures/" +
                    "tex_mtail" + TailToken + "_00_" + CharacterToken;
            }

            var requiredRoots = new List<string>
            {
                BodyAsset,
                HairAsset,
                FaceAsset
            };
            if (TailAsset != null)
            {
                requiredRoots.Add(TailAsset);
            }
            requiredRoots.Add(IdleAsset);
            _requiredRootLogicalNames = new ReadOnlyCollection<string>(requiredRoots);
        }

        public string Key { get; }

        public string DisplayName { get; }

        public int CharacterId { get; }

        /// <summary>
        /// Optional identity of the source master dress row. It is metadata only;
        /// it is deliberately not used as an installed-asset suffix.
        /// </summary>
        public int? DressId { get; }

        /// <summary>Two-digit installed body asset variant.</summary>
        public int CostumeId { get; }

        /// <summary>Two-digit head asset variant, independent of the costume.</summary>
        public int HeadSubId { get; }

        public int Skin { get; }

        public int? TailModelId { get; }

        public int? AttachmentModelId { get; }

        public bool HasTail { get { return TailModelId.HasValue; } }

        public bool HasSeparateModelAttachment
        {
            get { return AttachmentModelId.HasValue; }
        }

        public string CharacterToken { get; }

        public string CostumeToken { get; }

        public string HeadSubToken { get; }

        public string TailToken { get; }

        public string BodyAsset { get; }

        public string HairAsset { get; }

        public string FaceAsset { get; }

        public string IdleAsset { get; }

        public string HeadTexturePrefix { get; }

        public string SharedFaceTexturePrefix { get; }

        public string TailAsset { get; }

        public string TailTexturePrefix { get; }

        public IReadOnlyList<string> RequiredRootLogicalNames
        {
            get { return _requiredRootLogicalNames; }
        }

        /// <summary>
        /// Returns a new profile with installed chara_data appearance metadata.
        /// Asset identity, dress identity, and body/head variants stay unchanged.
        /// </summary>
        public MiniCharacterAssemblyProfile WithInstalledAppearance(
            int skin,
            int? tailModelId,
            int? attachmentModelId)
        {
            return new MiniCharacterAssemblyProfile(
                Key,
                DisplayName,
                CharacterId,
                DressId,
                CostumeId,
                HeadSubId,
                skin,
                tailModelId,
                attachmentModelId);
        }

        private static void ValidateKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException(
                    "A mini-character key is required.",
                    "key");
            }

            for (int index = 0; index < key.Length; index++)
            {
                char value = key[index];
                bool valid =
                    (value >= 'a' && value <= 'z') ||
                    (value >= '0' && value <= '9') ||
                    value == '-';
                if (!valid)
                {
                    throw new ArgumentException(
                        "Mini-character keys may contain only lowercase ASCII " +
                        "letters, digits, and hyphens.",
                        "key");
                }
            }
        }

        private static void ValidatePositiveModelId(int value, string parameterName)
        {
            if (value <= 0 || value > MaximumCharacterModelId)
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    "Mini-character model IDs must be between 1 and 9999.");
            }
        }

        private static void ValidateOptionalModelId(
            int? value,
            string parameterName)
        {
            if (value.HasValue)
            {
                ValidatePositiveModelId(value.Value, parameterName);
            }
        }

        private static void ValidateVariant(int value, string parameterName)
        {
            if (value < 0 || value > MaximumModelVariantId)
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    "Mini-character variants must be between 0 and 99.");
            }
        }
    }

    /// <summary>
    /// Small allowlist of assembly profiles whose installed naming conventions
    /// have been verified. It contains IDs and logical-name rules, never game art.
    /// </summary>
    public static class MiniCharacterAssemblyProfiles
    {
        public static readonly MiniCharacterAssemblyProfile OguriDefault =
            new MiniCharacterAssemblyProfile(
                "oguri-cap",
                "Oguri Cap",
                1006,
                100601,
                0,
                0,
                0,
                1,
                null);

        public static readonly MiniCharacterAssemblyProfile TachyonDefault =
            new MiniCharacterAssemblyProfile(
                "agnes-tachyon",
                "Agnes Tachyon",
                1032,
                103201,
                0,
                0,
                2,
                1,
                null);

        private static readonly IReadOnlyDictionary<string, MiniCharacterAssemblyProfile>
            Profiles = new ReadOnlyDictionary<string, MiniCharacterAssemblyProfile>(
                new Dictionary<string, MiniCharacterAssemblyProfile>(
                    StringComparer.Ordinal)
                {
                    { OguriDefault.Key, OguriDefault },
                    { TachyonDefault.Key, TachyonDefault }
                });

        public static bool TryGet(
            string characterKey,
            out MiniCharacterAssemblyProfile profile)
        {
            if (string.IsNullOrWhiteSpace(characterKey))
            {
                profile = null;
                return false;
            }

            return Profiles.TryGetValue(characterKey.Trim(), out profile);
        }
    }
}
