using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace UmaDesktopPet.Standalone.Core
{
    public enum GameRegion
    {
        Global,
        Japan
    }

    public sealed class CharacterRecord
    {
        public int Id { get; internal set; }
        public int TailModelId { get; internal set; }
        public string Skin { get; internal set; }
    }

    public sealed class GameDataCatalog : IDisposable
    {
        private static readonly byte[] DatabaseBaseKey =
        {
            0xF1, 0x70, 0xCE, 0xA4, 0xDF, 0xCE, 0xA3,
            0xE1, 0xA5, 0xD8, 0xC7, 0x0B, 0xD1
        };

        private static readonly byte[] GlobalDatabaseKey =
        {
            0x36, 0x23, 0x6B, 0x4C, 0x2A, 0x39, 0x21, 0x75,
            0x52, 0x26, 0x32, 0x76, 0x25, 0x50, 0x3F, 0x35,
            0x5D, 0x77, 0x58, 0x6D, 0x40, 0x71, 0x38, 0x5E,
            0x4C, 0x31, 0x28, 0x74, 0x29, 0x59, 0x37, 0x24,
            0x53
        };

        private static readonly byte[] JapanDatabaseKey =
        {
            0x6D, 0x5B, 0x65, 0x33, 0x63, 0x36, 0x63, 0x25,
            0x54, 0x71, 0x2D, 0x73, 0x50, 0x53, 0x63, 0x38,
            0x6D, 0x34, 0x37, 0x7B, 0x35, 0x63, 0x70, 0x23,
            0x37, 0x34, 0x53, 0x29, 0x73, 0x43, 0x36, 0x33
        };

        private readonly Dictionary<string, GameAssetRecord> _assets;
        private readonly NativeSqlite.Connection _masterDatabase;

        private GameDataCatalog(
            string gameRoot,
            GameRegion region,
            Dictionary<string, GameAssetRecord> assets,
            NativeSqlite.Connection masterDatabase)
        {
            GameRoot = gameRoot;
            Region = region;
            _assets = assets;
            _masterDatabase = masterDatabase;
        }

        public string GameRoot { get; private set; }
        public GameRegion Region { get; private set; }
        public int AssetCount { get { return _assets.Count; } }

        public static GameDataCatalog Open(string gameRoot, string sqlite3McLibraryPath)
        {
            if (string.IsNullOrWhiteSpace(gameRoot))
            {
                throw new ArgumentException("A game-data root is required.", "gameRoot");
            }
            string normalizedRoot;
            if (!LocalPathPolicy.TryGetLocalFullPath(gameRoot, out normalizedRoot))
            {
                throw new NotSupportedException(
                    "Game data must be read from a local filesystem path.");
            }
            gameRoot = normalizedRoot;
            string metaPath = Path.Combine(gameRoot, "meta");
            string masterPath = Path.Combine(gameRoot, "master", "master.mdb");
            string datPath = Path.Combine(gameRoot, "dat");
            if (!File.Exists(metaPath) || !File.Exists(masterPath) || !Directory.Exists(datPath))
            {
                throw new DirectoryNotFoundException(
                    "Expected meta, master\\master.mdb, and dat under " + gameRoot);
            }

            NativeSqlite.LoadLibraryFrom(sqlite3McLibraryPath);
            GameRegion region;
            NativeSqlite.Connection metaDatabase = OpenMeta(metaPath, out region);
            Dictionary<string, GameAssetRecord> assets;
            using (metaDatabase)
            {
                assets = ReadAssets(gameRoot, metaDatabase);
            }

            NativeSqlite.Connection masterDatabase = NativeSqlite.OpenPlainReadOnly(masterPath);
            return new GameDataCatalog(gameRoot, region, assets, masterDatabase);
        }

        public GameAssetRecord GetRequiredAsset(string logicalName)
        {
            GameAssetRecord record;
            if (!_assets.TryGetValue(logicalName, out record))
            {
                throw new KeyNotFoundException("Asset not found in local catalog: " + logicalName);
            }
            return record;
        }

        public bool TryGetAsset(string logicalName, out GameAssetRecord record)
        {
            return _assets.TryGetValue(logicalName, out record);
        }

        public IEnumerable<GameAssetRecord> FindByPrefix(string prefix)
        {
            return _assets.Values
                .Where(record => record.Name.StartsWith(prefix, StringComparison.Ordinal))
                .OrderBy(record => record.Name, StringComparer.Ordinal);
        }

        public IEnumerable<GameAssetRecord> FindByNameFragment(string fragment)
        {
            if (string.IsNullOrWhiteSpace(fragment))
            {
                throw new ArgumentException(
                    "An asset-name fragment is required.",
                    "fragment");
            }

            return _assets.Values
                .Where(record => record.Name.IndexOf(
                    fragment,
                    StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(record => record.Name, StringComparer.Ordinal);
        }

        public CharacterRecord GetCharacter(int characterId)
        {
            CharacterRecord result = null;
            string sql =
                "SELECT id, tail_model_id, skin FROM chara_data WHERE id = " +
                characterId.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                " LIMIT 1";
            _masterDatabase.ForEachRow(
                sql,
                delegate(NativeSqlite.Row row)
                {
                    result = new CharacterRecord
                    {
                        Id = row.Int32(0),
                        TailModelId = row.Int32(1),
                        Skin = row.Text(2) ?? string.Empty
                    };
                });
            if (result == null)
            {
                throw new KeyNotFoundException(
                    "Character not found in master.mdb: " + characterId);
            }
            return result;
        }

        public IEnumerable<GameAssetRecord> ResolveLoadOrder(GameAssetRecord root)
        {
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var result = new List<GameAssetRecord>();
            Visit(root, visited, result);
            return result;
        }

        public void Dispose()
        {
            _masterDatabase.Dispose();
        }

        private void Visit(
            GameAssetRecord record,
            HashSet<string> visited,
            List<GameAssetRecord> result)
        {
            if (!visited.Add(record.Name))
            {
                return;
            }
            foreach (string dependencyName in record.EnumeratePrerequisiteNames())
            {
                GameAssetRecord dependency;
                if (!_assets.TryGetValue(dependencyName, out dependency))
                {
                    throw new InvalidDataException(
                        "Missing catalog dependency '" + dependencyName +
                        "' required by '" + record.Name + "'.");
                }
                Visit(dependency, visited, result);
            }
            result.Add(record);
        }

        private static NativeSqlite.Connection OpenMeta(string path, out GameRegion region)
        {
            string globalError;
            byte[] globalKey = DeriveDatabaseKey(GlobalDatabaseKey);
            NativeSqlite.Connection connection;
            try
            {
                connection = NativeSqlite.TryOpenEncryptedReadOnly(
                    path,
                    globalKey,
                    "chacha20",
                    out globalError);
            }
            finally
            {
                Array.Clear(globalKey, 0, globalKey.Length);
            }
            if (connection != null)
            {
                region = GameRegion.Global;
                return connection;
            }

            string japanError;
            byte[] japanKey = DeriveDatabaseKey(JapanDatabaseKey);
            try
            {
                connection = NativeSqlite.TryOpenEncryptedReadOnly(
                    path,
                    japanKey,
                    "chacha20",
                    out japanError);
            }
            finally
            {
                Array.Clear(japanKey, 0, japanKey.Length);
            }
            if (connection != null)
            {
                region = GameRegion.Japan;
                return connection;
            }

            throw new InvalidDataException(
                "The installed meta database could not be opened read-only. " +
                "Global key error: " + globalError +
                "; Japan key error: " + japanError);
        }

        private static byte[] DeriveDatabaseKey(byte[] regionalKey)
        {
            byte[] result = (byte[])regionalKey.Clone();
            for (int index = 0; index < result.Length; index++)
            {
                result[index] ^= DatabaseBaseKey[index % DatabaseBaseKey.Length];
            }
            return result;
        }

        private static Dictionary<string, GameAssetRecord> ReadAssets(
            string gameRoot,
            NativeSqlite.Connection database)
        {
            bool hasEncryptionKey = database.HasColumn("a", "e");
            string sql = hasEncryptionKey
                ? "SELECT m,n,h,c,COALESCE(d,''),COALESCE(e,0) FROM a WHERE n IS NOT NULL"
                : "SELECT m,n,h,c,COALESCE(d,''),0 FROM a WHERE n IS NOT NULL";
            var assets = new Dictionary<string, GameAssetRecord>(StringComparer.Ordinal);
            database.ForEachRow(
                sql,
                delegate(NativeSqlite.Row row)
                {
                    string name = row.Text(1);
                    string hash = row.Text(2);
                    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(hash))
                    {
                        return;
                    }
                    if (!assets.ContainsKey(name))
                    {
                        assets.Add(
                            name,
                            GameAssetRecord.CreateFromValidatedLocalRoot(
                                gameRoot,
                                row.Text(0),
                                name,
                                hash,
                                row.Text(3),
                                row.Text(4),
                                row.Int64(5)));
                    }
                });
            return assets;
        }
    }
}
