using System;
using System.Collections.Generic;

namespace UmaDesktopPet.Standalone.Runtime
{
    /// <summary>
    /// App-owned identity for one explicitly supported desktop-pet character.
    /// Game assets remain in the user's installation and are not stored here.
    /// </summary>
    public sealed class PetCharacterProfile
    {
        internal PetCharacterProfile(
            string key,
            int gameCharacterId,
            string displayName,
            string shortName)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("A character key is required.", "key");
            }
            if (gameCharacterId <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    "gameCharacterId",
                    "A positive game character ID is required.");
            }
            if (string.IsNullOrWhiteSpace(displayName))
            {
                throw new ArgumentException(
                    "A character display name is required.",
                    "displayName");
            }
            if (string.IsNullOrWhiteSpace(shortName))
            {
                throw new ArgumentException(
                    "A character short name is required.",
                    "shortName");
            }

            Key = key;
            GameCharacterId = gameCharacterId;
            DisplayName = displayName;
            ShortName = shortName;
        }

        public string Key { get; private set; }

        public int GameCharacterId { get; private set; }

        public string DisplayName { get; private set; }

        public string ShortName { get; private set; }
    }

    /// <summary>
    /// Explicit allowlist of characters implemented by this app build. A row in
    /// the game's database alone never makes a character selectable.
    /// </summary>
    public static class PetCharacterCatalog
    {
        public static readonly PetCharacterProfile Oguri =
            new PetCharacterProfile("oguri-cap", 1006, "Oguri Cap", "Oguri");

        private static readonly PetCharacterProfile[] SelectableProfiles =
        {
            Oguri
        };

        public static IReadOnlyList<PetCharacterProfile> Selectable
        {
            get { return SelectableProfiles; }
        }

        public static bool TryGet(string key, out PetCharacterProfile profile)
        {
            if (!string.IsNullOrWhiteSpace(key))
            {
                for (int index = 0; index < SelectableProfiles.Length; index++)
                {
                    PetCharacterProfile candidate = SelectableProfiles[index];
                    if (string.Equals(candidate.Key, key, StringComparison.Ordinal))
                    {
                        profile = candidate;
                        return true;
                    }
                }
            }

            profile = null;
            return false;
        }

        public static PetCharacterProfile ResolveOrDefault(string key)
        {
            PetCharacterProfile profile;
            return TryGet(key, out profile) ? profile : Oguri;
        }
    }
}
