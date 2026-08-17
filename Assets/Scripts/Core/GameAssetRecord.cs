using System;
using System.Collections.Generic;
using System.IO;

namespace UmaDesktopPet.Standalone.Core
{
    public sealed class GameAssetRecord
    {
        private const int AssetHashLength = 32;
        private readonly string _gameRoot;

        public GameAssetRecord(
            string gameRoot,
            string type,
            string name,
            string hash,
            string checksum,
            string prerequisites,
            long encryptionKey)
            : this(
                gameRoot,
                type,
                name,
                hash,
                checksum,
                prerequisites,
                encryptionKey,
                false)
        {
        }

        internal static GameAssetRecord CreateFromValidatedLocalRoot(
            string gameRoot,
            string type,
            string name,
            string hash,
            string checksum,
            string prerequisites,
            long encryptionKey)
        {
            return new GameAssetRecord(
                gameRoot,
                type,
                name,
                hash,
                checksum,
                prerequisites,
                encryptionKey,
                true);
        }

        private GameAssetRecord(
            string gameRoot,
            string type,
            string name,
            string hash,
            string checksum,
            string prerequisites,
            long encryptionKey,
            bool gameRootAlreadyValidated)
        {
            string normalizedRoot;
            if (gameRootAlreadyValidated)
            {
                normalizedRoot = gameRoot;
            }
            else if (!LocalPathPolicy.TryGetLocalFullPath(gameRoot, out normalizedRoot))
            {
                throw new NotSupportedException(
                    "Game assets must be read from a local filesystem path.");
            }
            if (!IsValidAssetHash(hash))
            {
                throw new InvalidDataException(
                    "The asset catalog contains an invalid content hash: " +
                    (name ?? string.Empty));
            }

            _gameRoot = normalizedRoot;
            Type = type ?? string.Empty;
            Name = name ?? string.Empty;
            Hash = hash;
            Checksum = checksum ?? string.Empty;
            Prerequisites = prerequisites ?? string.Empty;
            EncryptionKey = encryptionKey;
        }

        public string Type { get; private set; }
        public string Name { get; private set; }
        public string Hash { get; private set; }
        public string Checksum { get; private set; }
        public string Prerequisites { get; private set; }
        public long EncryptionKey { get; private set; }

        public string FilePath
        {
            get
            {
                string dataRoot = Path.GetFullPath(Path.Combine(_gameRoot, "dat"));
                string dataPrefix = dataRoot.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                string candidate = Path.GetFullPath(Path.Combine(
                    dataRoot,
                    Hash.Substring(0, 2),
                    Hash));
                if (!candidate.StartsWith(
                    dataPrefix,
                    StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        "Asset path escaped the installed game's dat directory: " + Name);
                }
                return candidate;
            }
        }

        public IEnumerable<string> EnumeratePrerequisiteNames()
        {
            if (string.IsNullOrWhiteSpace(Prerequisites))
            {
                yield break;
            }

            string[] parts = Prerequisites.Split(
                new[] { ',', ';' },
                StringSplitOptions.RemoveEmptyEntries);
            foreach (string part in parts)
            {
                string name = part.Trim();
                if (name.Length > 0)
                {
                    yield return name;
                }
            }
        }

        public Stream OpenRead()
        {
            return new EncryptedAssetStream(FilePath, EncryptionKey);
        }

        private static bool IsValidAssetHash(string hash)
        {
            if (string.IsNullOrEmpty(hash) || hash.Length != AssetHashLength)
            {
                return false;
            }

            // Umamusume content hashes are SHA-1 values encoded with the standard
            // unpadded Base32 alphabet. This also excludes path syntax entirely.
            for (int index = 0; index < hash.Length; index++)
            {
                char value = hash[index];
                if ((value < 'A' || value > 'Z') &&
                    (value < '2' || value > '7'))
                {
                    return false;
                }
            }
            return true;
        }
    }
}
