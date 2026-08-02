using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Text.Json;
using Microsoft.Win32;
using Optimax.IPC;

namespace Optimax.Core
{
    public class TransactionalRollbackManager
    {
        private readonly string _backupRoot;

        public TransactionalRollbackManager()
        {
            _backupRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Optimax", "Backups");
            Directory.CreateDirectory(_backupRoot);
        }

        public SystemStateBackupPackage CreatePackage() => new SystemStateBackupPackage();

        public void SnapshotRegistryKey(SystemStateBackupPackage package, RegistryKey rootKey, string subKeyPath, string valueName)
        {
            using var key = rootKey.OpenSubKey(subKeyPath, writable: false);
            if (key == null)
            {
                package.RegistryEntries.Add(new RegistryStateSnapshot
                {
                    KeyPath = $"{rootKey.Name}\\{subKeyPath}",
                    ValueName = valueName,
                    Existed = false
                });
                return;
            }

            if (string.IsNullOrEmpty(valueName))
            {
                // Snapshot entire key and all subkeys recursively
                SnapshotRegistryTreeRecursive(package, rootKey, subKeyPath);
            }
            else
            {
                var snapshot = new RegistryStateSnapshot
                {
                    KeyPath = $"{rootKey.Name}\\{subKeyPath}",
                    ValueName = valueName
                };

                var val = key.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                if (val != null)
                {
                    snapshot.Existed = true;
                    snapshot.ValueKind = key.GetValueKind(valueName);
                    if (val is byte[] bytes)
                    {
                        snapshot.OriginalValue = Convert.ToBase64String(bytes);
                    }
                    else
                    {
                        snapshot.OriginalValue = val;
                    }
                }

                package.RegistryEntries.Add(snapshot);
            }
        }

        private void SnapshotRegistryTreeRecursive(SystemStateBackupPackage package, RegistryKey rootKey, string subKeyPath)
        {
            using var key = rootKey.OpenSubKey(subKeyPath, writable: false);
            if (key == null) return;

            foreach (var vName in key.GetValueNames())
            {
                var val = key.GetValue(vName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                var snap = new RegistryStateSnapshot
                {
                    KeyPath = $"{rootKey.Name}\\{subKeyPath}",
                    ValueName = vName,
                    Existed = true,
                    ValueKind = key.GetValueKind(vName)
                };
                if (val is byte[] bytes) snap.OriginalValue = Convert.ToBase64String(bytes);
                else snap.OriginalValue = val;

                package.RegistryEntries.Add(snap);
            }

            foreach (var skName in key.GetSubKeyNames())
            {
                SnapshotRegistryTreeRecursive(package, rootKey, $"{subKeyPath}\\{skName}");
            }
        }

        public void SnapshotService(SystemStateBackupPackage package, string serviceName)
        {
            try
            {
                using var sc = new ServiceController(serviceName);
                package.ServiceEntries.Add(new ServiceStateSnapshot
                {
                    ServiceName = serviceName,
                    OriginalStartMode = (int)sc.StartType,
                    OriginalStatus = (int)sc.Status
                });
            }
            catch (Exception ex) { OptimaxLogger.Warn($"Failed to snapshot service state: {serviceName}", ex); }
        }

        public string PersistPackage(SystemStateBackupPackage package)
        {
            string packageDir = Path.Combine(_backupRoot, package.BackupId);
            Directory.CreateDirectory(packageDir);
            string jsonPath = Path.Combine(packageDir, "snapshot.json");
            string hashPath = Path.Combine(packageDir, "snapshot.sha256");

            string json = JsonSerializer.Serialize(package, OptimaxJsonContext.Default.SystemStateBackupPackage);
            File.WriteAllText(jsonPath, json);

            using var sha256 = System.Security.Cryptography.SHA256.Create();
            byte[] hashBytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(json));
            string hashHex = Convert.ToHexString(hashBytes);
            File.WriteAllText(hashPath, hashHex);

            OptimaxLogger.Warn($"[AUDIT TRAIL] Created backup snapshot ID [{package.BackupId}] with SHA256 checksum: {hashHex}");
            return package.BackupId;
        }

        public bool ExecuteRollback(string backupId)
        {
            if (string.IsNullOrWhiteSpace(backupId) || backupId.Contains("..") || backupId.Contains('/') || backupId.Contains('\\')) return false;
            string jsonPath = Path.Combine(_backupRoot, backupId, "snapshot.json");
            string hashPath = Path.Combine(_backupRoot, backupId, "snapshot.sha256");

            if (!File.Exists(jsonPath)) return false;

            string jsonContent = File.ReadAllText(jsonPath);

            if (File.Exists(hashPath))
            {
                string storedHash = File.ReadAllText(hashPath).Trim();
                using var sha256 = System.Security.Cryptography.SHA256.Create();
                byte[] hashBytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(jsonContent));
                string computedHash = Convert.ToHexString(hashBytes);

                if (!string.Equals(storedHash, computedHash, StringComparison.OrdinalIgnoreCase))
                {
                    OptimaxLogger.Error($"[SECURITY AUDIT] Snapshot SHA256 integrity verification failed for Backup ID: {backupId}. Aborting rollback.", null);
                    return false;
                }
            }

            var package = JsonSerializer.Deserialize(jsonContent, OptimaxJsonContext.Default.SystemStateBackupPackage);
            if (package == null) return false;

            bool hasError = false;

            // 1. Restore Registry Entries
            foreach (var reg in package.RegistryEntries)
            {
                try
                {
                    string[] parts = reg.KeyPath.Split('\\', 2);
                    RegistryKey root = parts[0] switch
                    {
                        "HKEY_LOCAL_MACHINE" => Registry.LocalMachine,
                        "HKEY_CURRENT_USER" => Registry.CurrentUser,
                        "HKEY_CLASSES_ROOT" => Registry.ClassesRoot,
                        _ => Registry.LocalMachine
                    };

                    if (reg.Existed)
                    {
                        using var key = root.CreateSubKey(parts[1], writable: true);
                        if (key != null && reg.OriginalValue != null)
                        {
                            object targetVal = ConvertJsonValueToRegistryType(reg.OriginalValue, reg.ValueKind);
                            key.SetValue(reg.ValueName, targetVal, reg.ValueKind);
                        }
                    }
                    else
                    {
                        if (!string.IsNullOrEmpty(reg.ValueName))
                        {
                            using var key = root.OpenSubKey(parts[1], writable: true);
                            key?.DeleteValue(reg.ValueName, false);
                        }
                        else
                        {
                            try { root.DeleteSubKeyTree(parts[1], false); } catch (Exception ex) { OptimaxLogger.Error($"Failed to delete registry subtree: {parts[1]}", ex); }
                        }
                    }
                }
                catch (Exception ex) 
                { 
                    hasError = true;
                    OptimaxLogger.Error($"Failed to rollback registry key: {reg.KeyPath}\\{reg.ValueName}", ex); 
                }
            }

            // 2. Restore Services via Win32 SCM
            foreach (var svc in package.ServiceEntries)
            {
                try
                {
                    ScmServiceManager.RestoreServiceState(svc.ServiceName, (ServiceStartMode)svc.OriginalStartMode, (ServiceControllerStatus)svc.OriginalStatus);
                }
                catch (Exception ex) 
                { 
                    hasError = true;
                    OptimaxLogger.Error($"Failed to rollback service: {svc.ServiceName}", ex); 
                }
            }
            return !hasError;
        }

        public List<BackupItemDto> GetAvailableBackups()
        {
            var list = new List<BackupItemDto>();
            try
            {
                if (!Directory.Exists(_backupRoot)) return list;

                foreach (var dir in Directory.GetDirectories(_backupRoot))
                {
                    string jsonPath = Path.Combine(dir, "snapshot.json");
                    if (File.Exists(jsonPath))
                    {
                        try
                        {
                            string json = File.ReadAllText(jsonPath);
                            var pkg = JsonSerializer.Deserialize(json, OptimaxJsonContext.Default.SystemStateBackupPackage);
                            if (pkg != null)
                            {
                                list.Add(new BackupItemDto(
                                    pkg.BackupId,
                                    pkg.Timestamp,
                                    pkg.RegistryEntries?.Count ?? 0,
                                    pkg.ServiceEntries?.Count ?? 0
                                ));
                            }
                        }
                        catch (Exception ex) { OptimaxLogger.Trace($"Failed to parse backup snapshot: {jsonPath}", ex); }
                    }
                }
            }
            catch (Exception ex) { OptimaxLogger.Warn("Failed to enumerate backup directory", ex); }

            list.Sort((a, b) => b.Timestamp.CompareTo(a.Timestamp));
            return list;
        }

        public string CreateSystemSnapshot()
        {
            var pkg = CreatePackage();

            // Snapshot critical system registry keys
            SnapshotRegistryKey(pkg, Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\RunMRU", "");
            SnapshotRegistryKey(pkg, Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry");
            SnapshotRegistryKey(pkg, Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\WindowsCopilot", "TurnOffWindowsCopilot");
            SnapshotRegistryKey(pkg, Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Search", "BingSearchEnabled");
            SnapshotRegistryKey(pkg, Registry.LocalMachine, @"System\CurrentControlSet\Control\DeviceGuard", "EnableVirtualizationBasedSecurity");
            SnapshotRegistryKey(pkg, Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\Dwm", "OverlayTestMode");

            // Snapshot key Windows services
            SnapshotService(pkg, "WSearch");
            SnapshotService(pkg, "Spooler");
            SnapshotService(pkg, "SysMain");
            SnapshotService(pkg, "DiagTrack");
            SnapshotService(pkg, "dmwappushservice");

            return PersistPackage(pkg);
        }

        public bool DeleteBackup(string backupId)
        {
            if (string.IsNullOrWhiteSpace(backupId) || backupId.Contains("..") || backupId.Contains('/') || backupId.Contains('\\')) return false;
            try
            {
                string dir = Path.Combine(_backupRoot, backupId);
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, true);
                    return true;
                }
            }
            catch (Exception ex) { OptimaxLogger.Warn($"Failed to delete backup: {backupId}", ex); }
            return false;
        }

        /// <summary>
        /// Delete ALL backup snapshots stored in the backup repository directory.
        /// Returns the number of successfully deleted backup packages.
        /// </summary>
        public int DeleteAllBackups()
        {
            int deletedCount = 0;
            try
            {
                if (Directory.Exists(_backupRoot))
                {
                    foreach (var subDir in Directory.GetDirectories(_backupRoot))
                    {
                        try
                        {
                            Directory.Delete(subDir, true);
                            deletedCount++;
                        }
                        catch (Exception ex)
                        {
                            OptimaxLogger.Warn($"Failed to delete backup directory: {subDir}", ex);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                OptimaxLogger.Warn("Failed to delete all backups from repository", ex);
            }
            return deletedCount;
        }

        private static object ConvertJsonValueToRegistryType(object originalVal, RegistryValueKind valueKind)
        {
            if (originalVal is JsonElement elem)
            {
                switch (valueKind)
                {
                    case RegistryValueKind.DWord:
                        if (elem.ValueKind == JsonValueKind.Number && elem.TryGetInt64(out long dL)) return (int)dL;
                        if (int.TryParse(elem.ToString(), out int dI)) return dI;
                        return 0;

                    case RegistryValueKind.QWord:
                        if (elem.ValueKind == JsonValueKind.Number && elem.TryGetInt64(out long qL)) return qL;
                        if (long.TryParse(elem.ToString(), out long qVal)) return qVal;
                        return 0L;

                    case RegistryValueKind.Binary:
                        if (elem.ValueKind == JsonValueKind.String)
                        {
                            try { return Convert.FromBase64String(elem.GetString()!); } catch { }
                        }
                        return Array.Empty<byte>();

                    case RegistryValueKind.MultiString:
                        if (elem.ValueKind == JsonValueKind.Array)
                        {
                            var list = new List<string>();
                            foreach (var item in elem.EnumerateArray())
                            {
                                list.Add(item.ValueKind == JsonValueKind.String ? (item.GetString() ?? "") : item.ToString());
                            }
                            return list.ToArray();
                        }
                        return new[] { elem.ValueKind == JsonValueKind.String ? (elem.GetString() ?? "") : elem.ToString() };

                    case RegistryValueKind.String:
                    case RegistryValueKind.ExpandString:
                    default:
                        if (elem.ValueKind == JsonValueKind.String) return elem.GetString() ?? "";
                        return elem.ToString();
                }
            }

            return originalVal;
        }
    }

    public class TransactionalScope : IDisposable
    {
        private readonly TransactionalRollbackManager _rollbackManager;
        private readonly SystemStateBackupPackage _package;
        private bool _isCommitted = false;

        public string BackupId => _package.BackupId;
        public SystemStateBackupPackage Package => _package;

        public TransactionalScope(TransactionalRollbackManager rollbackManager)
        {
            _rollbackManager = rollbackManager ?? throw new ArgumentNullException(nameof(rollbackManager));
            _package = _rollbackManager.CreatePackage();
        }

        public void SnapshotRegistryKey(RegistryKey rootKey, string subKeyPath, string valueName)
        {
            _rollbackManager.SnapshotRegistryKey(_package, rootKey, subKeyPath, valueName);
        }

        public void SnapshotService(string serviceName)
        {
            _rollbackManager.SnapshotService(_package, serviceName);
        }

        public void Commit()
        {
            _rollbackManager.PersistPackage(_package);
            _isCommitted = true;
        }

        public void Dispose()
        {
            if (!_isCommitted)
            {
                OptimaxLogger.Warn($"[TRANSACTION ROLLBACK] Transaction scope disposed without explicit Commit. Triggering auto-rollback for Backup ID: {_package.BackupId}");
                try
                {
                    _rollbackManager.PersistPackage(_package);
                    _rollbackManager.ExecuteRollback(_package.BackupId);
                }
                catch (Exception ex)
                {
                    OptimaxLogger.Error($"Failed auto-rollback for Backup ID: {_package.BackupId}", ex);
                }
            }
        }
    }
}


