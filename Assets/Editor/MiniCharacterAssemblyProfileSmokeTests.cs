using System;
using UmaDesktopPet.Standalone.Runtime;
using UnityEngine;

namespace UmaDesktopPet.Standalone.Editor
{
    public static class MiniCharacterAssemblyProfileSmokeTests
    {
        public static void Run()
        {
            MiniCharacterAssemblyProfile oguri =
                MiniCharacterAssemblyProfiles.OguriDefault;
            Require(
                oguri.CharacterId == MiniCharacterAssembler.OguriCharacterId &&
                oguri.DressId == 100601 && oguri.CostumeId == 0 &&
                oguri.HeadSubId == 0 && oguri.Skin == 0 &&
                oguri.TailModelId == 1,
                "The verified Oguri assembly profile changed.");
            Require(
                oguri.BodyAsset ==
                    "3d/chara/mini/body/mbdy1006_00/pfb_mbdy1006_00" &&
                oguri.HairAsset ==
                    "3d/chara/mini/head/mchr1006_00/pfb_mchr1006_00_hair",
                "The exact Oguri assembly roots changed.");

            MiniCharacterAssemblyProfile tachyon =
                MiniCharacterAssemblyProfiles.TachyonDefault;
            Require(
                tachyon.Key == PetCharacterCatalog.Tachyon.Key &&
                tachyon.CharacterId == 1032 && tachyon.DressId == 103201 &&
                tachyon.CostumeId == 0 && tachyon.HeadSubId == 0 &&
                tachyon.Skin == 2 && tachyon.TailModelId == 1,
                "The verified Tachyon assembly profile changed.");
            Require(
                tachyon.BodyAsset ==
                    "3d/chara/mini/body/mbdy1032_00/pfb_mbdy1032_00" &&
                tachyon.HairAsset ==
                    "3d/chara/mini/head/mchr1032_00/pfb_mchr1032_00_hair" &&
                tachyon.IdleAsset ==
                    "3d/motion/mini/event/body/chara/chr1032_00/" +
                    "anm_min_eve_chr1032_00_idle01_loop" &&
                tachyon.HeadTexturePrefix ==
                    "3d/chara/mini/head/mchr1032_00/textures/" &&
                tachyon.SharedFaceTexturePrefix.EndsWith(
                    "tex_mchr0001_00_face0_2",
                    StringComparison.Ordinal) &&
                tachyon.TailTexturePrefix.EndsWith(
                    "tex_mtail0001_00_1032",
                    StringComparison.Ordinal),
                "The exact Tachyon assembly roots changed.");
            Require(
                tachyon.RequiredRootLogicalNames.Count == 5,
                "Tachyon should lease body, hair, face, tail, and idle roots.");

            MiniCharacterAssemblyProfile noTail =
                tachyon.WithInstalledAppearance(3, null, null);
            Require(
                !ReferenceEquals(tachyon, noTail) &&
                tachyon.Skin == 2 && tachyon.HasTail &&
                noTail.Skin == 3 && !noTail.HasTail &&
                noTail.TailAsset == null &&
                noTail.RequiredRootLogicalNames.Count == 4,
                "Installed appearance replacement must be immutable and tail-safe.");

            MiniCharacterAssemblyProfile resolved;
            Require(
                MiniCharacterAssemblyProfiles.TryGet(
                    PetCharacterCatalog.Tachyon.Key,
                    out resolved) && ReferenceEquals(tachyon, resolved),
                "Tachyon assembly profile should resolve by character key.");
            Require(
                !MiniCharacterAssemblyProfiles.TryGet("unknown", out resolved) &&
                resolved == null,
                "Unknown characters must not inherit an assembly profile.");

            ExpectArgumentFailure(delegate
            {
                NewProfile("Agnes/Tachyon", 1032, 0, 0, 2, 1);
            }, "Raw-path-shaped keys must be rejected.");
            ExpectArgumentFailure(delegate
            {
                NewProfile("tachyon", 0, 0, 0, 2, 1);
            }, "Zero character IDs must be rejected.");
            ExpectArgumentFailure(delegate
            {
                NewProfile("tachyon", 1032, 100, 0, 2, 1);
            }, "Three-digit body variants must be rejected.");
            ExpectArgumentFailure(delegate
            {
                NewProfile("tachyon", 1032, 0, -1, 2, 1);
            }, "Negative head variants must be rejected.");

            Debug.Log("Mini-character assembly profile smoke tests passed.");
        }

        private static MiniCharacterAssemblyProfile NewProfile(
            string key,
            int characterId,
            int costumeId,
            int headSubId,
            int skin,
            int? tailModelId)
        {
            return new MiniCharacterAssemblyProfile(
                key,
                "Test Character",
                characterId,
                null,
                costumeId,
                headSubId,
                skin,
                tailModelId,
                null);
        }

        private static void ExpectArgumentFailure(Action action, string message)
        {
            try
            {
                action();
            }
            catch (ArgumentException)
            {
                return;
            }
            throw new InvalidOperationException(message);
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }
    }
}
