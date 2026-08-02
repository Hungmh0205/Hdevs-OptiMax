using System;
using System.Collections.Generic;
using System.IO;

namespace Optimax.Core
{
    public static class SafeRegistryChecker
    {
        private static readonly HashSet<string> WhitelistedSystemPaths;

        static SafeRegistryChecker()
        {
            string winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (string.IsNullOrEmpty(winDir)) winDir = "C:\\Windows";

            WhitelistedSystemPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                Path.Combine(winDir, "System32"),
                Path.Combine(winDir, "SysWOW64"),
                Path.Combine(winDir, "WinSxS"),
                Path.Combine(winDir, "SystemResources"),
                "explorer.exe",
                "svchost.exe",
                "rundll32.exe",
                "cmd.exe",
                "powershell.exe"
            };
        }

        /// <summary>
        /// Safely evaluates whether a registry file path reference is truly orphaned.
        /// Protects against deleting valid registry keys when external/secondary drives are unmounted or disconnected.
        /// </summary>
        public static bool IsPathOrphaned(string rawPath)
        {
            if (string.IsNullOrWhiteSpace(rawPath)) return false;

            string path = Environment.ExpandEnvironmentVariables(rawPath).Trim();

            // Extract actual file/executable path if arguments or quotes are present
            if (path.StartsWith("\""))
            {
                int endQuote = path.IndexOf('"', 1);
                if (endQuote > 1) path = path.Substring(1, endQuote - 1);
            }
            else
            {
                int exeIdx = path.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
                if (exeIdx > 0 && exeIdx + 4 < path.Length && (path[exeIdx + 4] == ' ' || path[exeIdx + 4] == '/' || path[exeIdx + 4] == '-'))
                {
                    path = path.Substring(0, exeIdx + 4);
                }
            }

            path = path.Trim('"', ' ', '\'');

            // Whitelist System paths
            foreach (var safePath in WhitelistedSystemPaths)
            {
                if (path.StartsWith(safePath, StringComparison.OrdinalIgnoreCase)) return false;
            }

            // Must look like an absolute Windows drive path (e.g. C:\...)
            if (path.Length >= 3 && path[1] == ':' && (path[2] == '\\' || path[2] == '/'))
            {
                string? rootDrive = Path.GetPathRoot(path);
                if (!string.IsNullOrEmpty(rootDrive))
                {
                    try
                    {
                        var drive = new DriveInfo(rootDrive);
                        // ABSOLUTE PROTECTION: If the drive is NOT ready (unplugged SSD/USB or unmounted), DO NOT DELETE REGISTRY!
                        if (!drive.IsReady)
                        {
                            return false;
                        }
                    }
                    catch (Exception ex)
                    {
                        OptimaxLogger.Trace($"Drive readiness check failed for drive root '{rootDrive}'", ex);
                        return false; // Skip deletion on drive inspection failure
                    }
                }

                // Confirm orphaned status ONLY when the drive IS READY but the file/directory is missing
                return !File.Exists(path) && !Directory.Exists(path);
            }

            return false;
        }
    }
}
