using System;
using System.IO;

namespace UmaDesktopPet.Standalone.Core
{
    /// <summary>
    /// Keeps game-data reads on a local filesystem. Rejecting UNC paths before
    /// probing them avoids an implicit SMB connection during startup.
    /// </summary>
    public static class LocalPathPolicy
    {
        public static bool TryGetLocalFullPath(string input, out string fullPath)
        {
            fullPath = null;
            if (string.IsNullOrWhiteSpace(input))
            {
                return false;
            }

            try
            {
                string candidate = Path.GetFullPath(input);
                if (!IsLocalFullPath(candidate))
                {
                    return false;
                }

                fullPath = candidate;
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (IOException)
            {
                return false;
            }
            catch (NotSupportedException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        private static bool IsLocalFullPath(string fullPath)
        {
            // Reject conventional UNC paths and extended UNC/device syntax before
            // asking Windows for any filesystem metadata.
            if (fullPath.StartsWith(@"\\", StringComparison.Ordinal) ||
                fullPath.StartsWith("//", StringComparison.Ordinal))
            {
                return false;
            }

            string root = Path.GetPathRoot(fullPath);
            if (string.IsNullOrWhiteSpace(root))
            {
                return false;
            }

            try
            {
                DriveType driveType = new DriveInfo(root).DriveType;
                return driveType != DriveType.Network &&
                    driveType != DriveType.NoRootDirectory &&
                    driveType != DriveType.Unknown;
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }
    }
}
