using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace UmaDesktopPet.Standalone.Core
{
    internal static class NativeSqlite
    {
        private const string LibraryName = "sqlite3mc_x64";
        private const int SqliteOk = 0;
        private const int SqliteRow = 100;
        private const int SqliteDone = 101;
        private const int SqliteOpenReadOnly = 0x00000001;
        private const int ErrorFilenameExceededRange = 206;
        private static IntPtr _loadedLibrary;

        public static void LoadLibraryFrom(string path)
        {
            if (_loadedLibrary != IntPtr.Zero)
            {
                return;
            }
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("A SQLite3MC library path is required.", "path");
            }

            _loadedLibrary = LoadLibraryW(path);
            int loadError = Marshal.GetLastWin32Error();
            if (_loadedLibrary == IntPtr.Zero && loadError == ErrorFilenameExceededRange)
            {
                string extendedPath = ToExtendedWindowsPath(path);
                _loadedLibrary = LoadLibraryW(extendedPath);
                loadError = Marshal.GetLastWin32Error();
            }
            if (_loadedLibrary == IntPtr.Zero)
            {
                throw new Win32Exception(
                    loadError,
                    "Could not load SQLite3MC from " + path);
            }
        }

        internal static string ToExtendedWindowsPath(string path)
        {
            string fullPath = Path.GetFullPath(path);
            if (fullPath.StartsWith(@"\\?\", StringComparison.Ordinal))
            {
                return fullPath;
            }
            if (fullPath.StartsWith(@"\\", StringComparison.Ordinal))
            {
                return @"\\?\UNC\" + fullPath.Substring(2);
            }
            return @"\\?\" + fullPath;
        }

        public static Connection OpenPlainReadOnly(string path)
        {
            return Connection.Open(path, null, -1);
        }

        public static Connection TryOpenEncryptedReadOnly(
            string path,
            byte[] key,
            string cipherName,
            out string error)
        {
            try
            {
                Connection connection = Connection.Open(path, key, cipherName);
                error = null;
                return connection;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return null;
            }
        }

        public sealed class Connection : IDisposable
        {
            private IntPtr _database;

            private Connection(IntPtr database)
            {
                _database = database;
            }

            internal static Connection Open(string path, byte[] key, int cipherIndex)
            {
                return Open(path, key, cipherIndex < 0 ? null : cipherIndex.ToString());
            }

            internal static Connection Open(string path, byte[] key, string cipherName)
            {
                IntPtr database;
                byte[] pathUtf8 = Utf8NullTerminated(path);
                int result = sqlite3_open_v2(
                    pathUtf8,
                    out database,
                    SqliteOpenReadOnly,
                    IntPtr.Zero);
                if (result != SqliteOk || database == IntPtr.Zero)
                {
                    string message = database == IntPtr.Zero
                        ? "no database handle"
                        : GetError(database);
                    if (database != IntPtr.Zero)
                    {
                        sqlite3_close(database);
                    }
                    throw new InvalidOperationException(
                        "Could not open SQLite database read-only: " + message);
                }

                Connection connection = new Connection(database);
                try
                {
                    if (key != null)
                    {
                        if (string.IsNullOrWhiteSpace(cipherName))
                        {
                            throw new InvalidOperationException(
                                "An encrypted database requires a named SQLite3MC cipher.");
                        }
                        byte[] cipherUtf8 = Utf8NullTerminated(cipherName);
                        int cipherIndex = sqlite3mc_cipher_index(cipherUtf8);
                        if (cipherIndex < 0)
                        {
                            throw new InvalidOperationException(
                                "SQLite3MC does not provide the requested cipher: " + cipherName);
                        }
                        int configured = sqlite3mc_config(
                            database,
                            Utf8NullTerminated("cipher"),
                            cipherIndex);
                        if (configured != cipherIndex)
                        {
                            throw new InvalidOperationException(
                                "SQLite3MC did not select cipher '" + cipherName +
                                "' (expected " + cipherIndex + ", got " + configured + ").");
                        }
                        result = sqlite3_key(database, key, key.Length);
                        if (result != SqliteOk)
                        {
                            throw new InvalidOperationException(
                                "SQLite3MC rejected the key: " + GetError(database));
                        }
                    }

                    connection.ForEachRow(
                        "SELECT name FROM sqlite_master LIMIT 1",
                        delegate(Row row) { });
                    connection.ExecuteNoRows("PRAGMA query_only=ON");
                    return connection;
                }
                catch
                {
                    connection.Dispose();
                    throw;
                }
            }

            public void ForEachRow(string sql, Action<Row> visit)
            {
                if (_database == IntPtr.Zero)
                {
                    throw new ObjectDisposedException("Connection");
                }
                if (visit == null)
                {
                    throw new ArgumentNullException("visit");
                }

                IntPtr statement;
                byte[] sqlUtf8 = Utf8NullTerminated(sql);
                int result = sqlite3_prepare_v2(
                    _database,
                    sqlUtf8,
                    -1,
                    out statement,
                    IntPtr.Zero);
                if (result != SqliteOk)
                {
                    throw new InvalidOperationException(
                        "SQLite prepare failed: " + GetError(_database) + " SQL=" + sql);
                }

                try
                {
                    while (true)
                    {
                        result = sqlite3_step(statement);
                        if (result == SqliteRow)
                        {
                            visit(new Row(statement));
                            continue;
                        }
                        if (result == SqliteDone)
                        {
                            break;
                        }
                        throw new InvalidOperationException(
                            "SQLite query failed: " + GetError(_database) + " SQL=" + sql);
                    }
                }
                finally
                {
                    sqlite3_finalize(statement);
                }
            }

            private void ExecuteNoRows(string sql)
            {
                ForEachRow(sql, delegate(Row row) { });
            }

            public bool HasColumn(string table, string column)
            {
                bool found = false;
                ForEachRow(
                    "PRAGMA table_info(" + table + ")",
                    delegate(Row row)
                    {
                        if (string.Equals(row.Text(1), column, StringComparison.OrdinalIgnoreCase))
                        {
                            found = true;
                        }
                    });
                return found;
            }

            public void Dispose()
            {
                IntPtr database = _database;
                _database = IntPtr.Zero;
                if (database != IntPtr.Zero)
                {
                    sqlite3_close(database);
                }
            }
        }

        public struct Row
        {
            private readonly IntPtr _statement;

            internal Row(IntPtr statement)
            {
                _statement = statement;
            }

            public string Text(int column)
            {
                IntPtr pointer = sqlite3_column_text(_statement, column);
                return PointerToUtf8(pointer);
            }

            public int Int32(int column)
            {
                return sqlite3_column_int(_statement, column);
            }

            public long Int64(int column)
            {
                return sqlite3_column_int64(_statement, column);
            }
        }

        private static string GetError(IntPtr database)
        {
            return PointerToUtf8(sqlite3_errmsg(database)) ?? "unknown SQLite error";
        }

        private static string PointerToUtf8(IntPtr pointer)
        {
            if (pointer == IntPtr.Zero)
            {
                return null;
            }
            int length = 0;
            while (Marshal.ReadByte(pointer, length) != 0)
            {
                length++;
            }
            if (length == 0)
            {
                return string.Empty;
            }
            byte[] bytes = new byte[length];
            Marshal.Copy(pointer, bytes, 0, length);
            return Encoding.UTF8.GetString(bytes);
        }

        private static byte[] Utf8NullTerminated(string value)
        {
            byte[] encoded = Encoding.UTF8.GetBytes(value ?? string.Empty);
            byte[] terminated = new byte[encoded.Length + 1];
            Buffer.BlockCopy(encoded, 0, terminated, 0, encoded.Length);
            return terminated;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibraryW(string path);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_open_v2(
            byte[] filename,
            out IntPtr database,
            int flags,
            IntPtr vfs);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_close(IntPtr database);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr sqlite3_errmsg(IntPtr database);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3mc_config(
            IntPtr database,
            byte[] name,
            int value);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3mc_cipher_index(byte[] name);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_key(IntPtr database, byte[] key, int keyLength);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_prepare_v2(
            IntPtr database,
            byte[] sql,
            int byteCount,
            out IntPtr statement,
            IntPtr tail);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_step(IntPtr statement);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_finalize(IntPtr statement);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr sqlite3_column_text(IntPtr statement, int column);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_column_int(IntPtr statement, int column);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        private static extern long sqlite3_column_int64(IntPtr statement, int column);
    }
}
