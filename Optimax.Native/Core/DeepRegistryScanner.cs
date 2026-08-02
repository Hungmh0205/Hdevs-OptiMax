using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;
using Optimax.IPC;

namespace Optimax.Core
{
    /// <summary>
    /// Deep Safe Registry Cleaner — scans for orphaned, invalid, and leftover registry keys.
    /// Note: Registry cleaning is performed for System Hygiene (cleaning post-uninstall clutter)
    /// rather than raw performance gains, as Microsoft confirms registry size does not impact OS speed.
    /// Includes Transactional Rollback snapshot protection and a strict system file whitelist.
    /// </summary>
    public class DeepRegistryScanner : IDeepRegistryScanner
    {
        private static readonly HashSet<string> WhitelistedSystemPaths;

        static DeepRegistryScanner()
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

        public RegistryScanReport ScanAndClean(bool isDryRun)
        {
            var items = new List<RegistryScanItemResult>();
            var rollbackMgr = new TransactionalRollbackManager();
            var backupPackage = rollbackMgr.CreatePackage();

            // 1. Scan Invalid CLSIDs
            ScanClsids(Registry.ClassesRoot, "HKEY_CLASSES_ROOT", "CLSID", items);
            ScanClsids(Registry.LocalMachine, "HKEY_LOCAL_MACHINE", "SOFTWARE\\Classes\\CLSID", items);

            // 2. Scan Missing Shared DLLs
            ScanSharedDlls(Registry.LocalMachine, "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\SharedDlls", items);

            // 3. Scan Broken MuiCache
            ScanMuiCache(Registry.CurrentUser, "SOFTWARE\\Microsoft\\Windows\\Shell\\MuiCache", items);

            // 4. Scan Orphaned Uninstall Keys
            ScanUninstallKeys(Registry.LocalMachine, "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall", items);
            ScanUninstallKeys(Registry.LocalMachine, "SOFTWARE\\WOW6432Node\\Microsoft\\Windows\\CurrentVersion\\Uninstall", items);

            // 5. Scan TypeLib Graph References
            ScanTypeLibs(Registry.ClassesRoot, "TypeLib", items);

            // 6. Scan App Paths Graph References
            ScanAppPaths(Registry.LocalMachine, "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\App Paths", items);

            // 7. Scan Fonts & Help References
            ScanFontsAndHelp(Registry.LocalMachine, items);

            string? backupId = null;

            if (!isDryRun && items.Count > 0)
            {
                // Snapshot before deletion
                foreach (var item in items)
                {
                    RegistryKey root = item.Hive switch
                    {
                        "HKEY_LOCAL_MACHINE" => Registry.LocalMachine,
                        "HKEY_CURRENT_USER" => Registry.CurrentUser,
                        "HKEY_CLASSES_ROOT" => Registry.ClassesRoot,
                        _ => Registry.LocalMachine
                    };

                    rollbackMgr.SnapshotRegistryKey(backupPackage, root, item.SubKey, item.ValueName);
                }

                backupId = rollbackMgr.PersistPackage(backupPackage);

                // Perform Cleanup
                foreach (var item in items)
                {
                    try
                    {
                        RegistryKey root = item.Hive switch
                        {
                            "HKEY_LOCAL_MACHINE" => Registry.LocalMachine,
                            "HKEY_CURRENT_USER" => Registry.CurrentUser,
                            "HKEY_CLASSES_ROOT" => Registry.ClassesRoot,
                            _ => Registry.LocalMachine
                        };

                        using var key = root.OpenSubKey(item.SubKey, writable: true);
                        if (key != null)
                        {
                            if (string.IsNullOrEmpty(item.ValueName))
                            {
                                root.DeleteSubKeyTree(item.SubKey, false);
                            }
                            else
                            {
                                key.DeleteValue(item.ValueName, false);
                            }
                        }
                    }
                    catch (Exception ex) { OptimaxLogger.Trace($"Failed to clean registry item: {item.Hive}\\{item.SubKey}\\{item.ValueName}", ex); }
                }
            }

            return new RegistryScanReport(isDryRun, items.Count, backupId, items.ToArray());
        }

        private static void ScanClsids(RegistryKey rootKey, string hiveName, string relSubKey, List<RegistryScanItemResult> results)
        {
            try
            {
                using var clsidKey = rootKey.OpenSubKey(relSubKey);
                if (clsidKey == null) return;

                foreach (var clsid in clsidKey.GetSubKeyNames())
                {
                    using var subKey = clsidKey.OpenSubKey(clsid);
                    if (subKey == null) continue;

                    string[] serverTypes = { "InprocServer32", "LocalServer32" };
                    foreach (var srvType in serverTypes)
                    {
                        using var srvKey = subKey.OpenSubKey(srvType);
                        if (srvKey == null) continue;

                        string? path = srvKey.GetValue("") as string;
                        if (!string.IsNullOrWhiteSpace(path) && IsInvalidFilePath(path))
                        {
                            results.Add(new RegistryScanItemResult(
                                hiveName,
                                $"{relSubKey}\\{clsid}\\{srvType}",
                                "",
                                "Invalid CLSID Server",
                                path,
                                "DeleteRegistryKey"
                            ));
                        }
                    }
                }
            }
            catch (Exception ex) { OptimaxLogger.Trace($"CLSID scan failed on {hiveName}\\{relSubKey}", ex); }
        }

        private static void ScanSharedDlls(RegistryKey rootKey, string subKeyPath, List<RegistryScanItemResult> results)
        {
            try
            {
                using var key = rootKey.OpenSubKey(subKeyPath);
                if (key == null) return;

                foreach (var valName in key.GetValueNames())
                {
                    if (IsInvalidFilePath(valName))
                    {
                        results.Add(new RegistryScanItemResult(
                            "HKEY_LOCAL_MACHINE",
                            subKeyPath,
                            valName,
                            "Missing Shared DLL",
                            valName,
                            "DeleteRegistryValue"
                        ));
                    }
                }
            }
            catch (Exception ex) { OptimaxLogger.Trace($"SharedDLLs scan failed: {subKeyPath}", ex); }
        }

        private static void ScanMuiCache(RegistryKey rootKey, string subKeyPath, List<RegistryScanItemResult> results)
        {
            try
            {
                using var key = rootKey.OpenSubKey(subKeyPath);
                if (key == null) return;

                foreach (var valName in key.GetValueNames())
                {
                    string path = valName;
                    int pipeIdx = path.IndexOf(".FriendlyAppName");
                    if (pipeIdx > 0) path = path.Substring(0, pipeIdx);

                    if (IsInvalidFilePath(path))
                    {
                        results.Add(new RegistryScanItemResult(
                            "HKEY_CURRENT_USER",
                            subKeyPath,
                            valName,
                            "Broken MuiCache Entry",
                            path,
                            "DeleteRegistryValue"
                        ));
                    }
                }
            }
            catch (Exception ex) { OptimaxLogger.Trace($"MuiCache scan failed: {subKeyPath}", ex); }
        }

        private static void ScanUninstallKeys(RegistryKey rootKey, string subKeyPath, List<RegistryScanItemResult> results)
        {
            try
            {
                using var key = rootKey.OpenSubKey(subKeyPath);
                if (key == null) return;

                foreach (var appKeyName in key.GetSubKeyNames())
                {
                    using var appKey = key.OpenSubKey(appKeyName);
                    if (appKey == null) continue;

                    string? installLoc = appKey.GetValue("InstallLocation") as string;
                    if (!string.IsNullOrWhiteSpace(installLoc) && IsInvalidFilePath(installLoc))
                    {
                        results.Add(new RegistryScanItemResult(
                            "HKEY_LOCAL_MACHINE",
                            $"{subKeyPath}\\{appKeyName}",
                            "InstallLocation",
                            "Invalid Uninstall Location Value",
                            installLoc,
                            "DeleteRegistryValue"
                        ));
                    }
                }
            }
            catch (Exception ex) { OptimaxLogger.Trace($"Uninstall keys scan failed: {subKeyPath}", ex); }
        }

        private static void ScanTypeLibs(RegistryKey rootKey, string subKeyPath, List<RegistryScanItemResult> results)
        {
            try
            {
                using var typeLibKey = rootKey.OpenSubKey(subKeyPath);
                if (typeLibKey == null) return;

                foreach (var guid in typeLibKey.GetSubKeyNames())
                {
                    using var guidKey = typeLibKey.OpenSubKey(guid);
                    if (guidKey == null) continue;

                    foreach (var ver in guidKey.GetSubKeyNames())
                    {
                        using var verKey = guidKey.OpenSubKey(ver);
                        if (verKey == null) continue;

                        string[] archKeys = { "0\\win32", "0\\win64", "0\\win64\\0" };
                        foreach (var arch in archKeys)
                        {
                            using var archKey = verKey.OpenSubKey(arch);
                            if (archKey == null) continue;

                            string? path = archKey.GetValue("") as string;
                            if (!string.IsNullOrWhiteSpace(path) && IsInvalidFilePath(path))
                            {
                                results.Add(new RegistryScanItemResult(
                                    "HKEY_CLASSES_ROOT",
                                    $"{subKeyPath}\\{guid}\\{ver}\\{arch}",
                                    "",
                                    "Orphaned TypeLib Reference",
                                    path,
                                    "DeleteRegistryKey"
                                ));
                            }
                        }
                    }
                }
            }
            catch (Exception ex) { OptimaxLogger.Trace($"TypeLib scan failed: {subKeyPath}", ex); }
        }

        private static void ScanAppPaths(RegistryKey rootKey, string subKeyPath, List<RegistryScanItemResult> results)
        {
            try
            {
                using var appPathsKey = rootKey.OpenSubKey(subKeyPath);
                if (appPathsKey == null) return;

                foreach (var exeName in appPathsKey.GetSubKeyNames())
                {
                    using var exeKey = appPathsKey.OpenSubKey(exeName);
                    if (exeKey == null) continue;

                    string? defaultVal = exeKey.GetValue("") as string;
                    if (!string.IsNullOrWhiteSpace(defaultVal) && IsInvalidFilePath(defaultVal))
                    {
                        results.Add(new RegistryScanItemResult(
                            "HKEY_LOCAL_MACHINE",
                            $"{subKeyPath}\\{exeName}",
                            "",
                            "Invalid App Path Entry",
                            defaultVal,
                            "DeleteRegistryKey"
                        ));
                    }
                }
            }
            catch (Exception ex) { OptimaxLogger.Trace($"AppPaths scan failed: {subKeyPath}", ex); }
        }

        private static void ScanFontsAndHelp(RegistryKey rootKey, List<RegistryScanItemResult> results)
        {
            // Fonts
            try
            {
                string fontSubKey = "SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Fonts";
                using var fontKey = rootKey.OpenSubKey(fontSubKey);
                if (fontKey != null)
                {
                    string fontDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts");
                    foreach (var valName in fontKey.GetValueNames())
                    {
                        string? fontFileName = fontKey.GetValue(valName) as string;
                        if (!string.IsNullOrWhiteSpace(fontFileName))
                        {
                            string fullPath = Path.IsPathRooted(fontFileName) ? fontFileName : Path.Combine(fontDir, fontFileName);
                            if (IsInvalidFilePath(fullPath))
                            {
                                results.Add(new RegistryScanItemResult(
                                    "HKEY_LOCAL_MACHINE",
                                    fontSubKey,
                                    valName,
                                    "Missing Font Reference",
                                    fullPath,
                                    "DeleteRegistryValue"
                                ));
                            }
                        }
                    }
                }
            }
            catch (Exception ex) { OptimaxLogger.Trace("Fonts registry scan failed", ex); }

            // Help Files
            try
            {
                string helpSubKey = "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Help";
                using var helpKey = rootKey.OpenSubKey(helpSubKey);
                if (helpKey != null)
                {
                    foreach (var valName in helpKey.GetValueNames())
                    {
                        string? helpDir = helpKey.GetValue(valName) as string;
                        if (!string.IsNullOrWhiteSpace(helpDir) && IsInvalidFilePath(helpDir))
                        {
                            results.Add(new RegistryScanItemResult(
                                "HKEY_LOCAL_MACHINE",
                                helpSubKey,
                                valName,
                                "Invalid Help Folder Reference",
                                helpDir,
                                "DeleteRegistryValue"
                            ));
                        }
                    }
                }
            }
            catch (Exception ex) { OptimaxLogger.Trace("Help files registry scan failed", ex); }
        }

        private static bool IsInvalidFilePath(string rawPath)
        {
            return SafeRegistryChecker.IsPathOrphaned(rawPath);
        }
    }
}
