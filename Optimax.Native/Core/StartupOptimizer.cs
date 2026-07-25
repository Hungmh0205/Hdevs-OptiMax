using System;
using System.Collections.Generic;
using System.IO;
using System.ServiceProcess;
using Microsoft.Win32;
using Optimax.IPC;

namespace Optimax.Core
{
    public class StartupOptimizer
    {
        private static readonly HashSet<string> EssentialServices = new(StringComparer.OrdinalIgnoreCase)
        {
            "RpcSs", "DcomLaunch", "LSM", "SamSs", "EventLog", "PlugPlay", "Dhcp", "Dnscache", "WinDefend", "wuauserv"
        };

        public StartupOptimizerReport GetStartupAndServiceStatus()
        {
            var startupList = new List<StartupItemResult>();
            var serviceList = new List<ServiceItemResult>();

            // 1. HKCU Run
            ScanRegistryRunKey(Registry.CurrentUser, "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", "HKCU\\Run", startupList);
            // 2. HKLM Run
            ScanRegistryRunKey(Registry.LocalMachine, "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", "HKLM\\Run", startupList);
            // 3. HKLM WOW6432Node Run
            ScanRegistryRunKey(Registry.LocalMachine, "SOFTWARE\\WOW6432Node\\Microsoft\\Windows\\CurrentVersion\\Run", "HKLM\\WOW6432Node\\Run", startupList);

            // 4. Startup Folders
            ScanStartupFolder(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "User Startup Folder", startupList);
            ScanStartupFolder(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), "Common Startup Folder", startupList);

            // 5. Windows Services
            try
            {
                var services = ServiceController.GetServices();
                foreach (var sc in services)
                {
                    bool isEssential = EssentialServices.Contains(sc.ServiceName);
                    serviceList.Add(new ServiceItemResult(
                        sc.ServiceName,
                        sc.DisplayName,
                        sc.StartType.ToString(),
                        sc.Status.ToString(),
                        isEssential
                    ));
                }
            }
            catch { }

            return new StartupOptimizerReport(startupList.ToArray(), serviceList.ToArray());
        }

        public bool ToggleStartupItem(string itemId, bool enable)
        {
            var rollbackMgr = new TransactionalRollbackManager();
            var backupPkg = rollbackMgr.CreatePackage();

            try
            {
                if (itemId.StartsWith("HKCU\\Run\\"))
                {
                    string name = itemId.Substring("HKCU\\Run\\".Length);
                    rollbackMgr.SnapshotRegistryKey(backupPkg, Registry.CurrentUser, "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", name);
                    rollbackMgr.PersistPackage(backupPkg);

                    using var key = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", writable: true);
                    if (!enable)
                    {
                        var val = key?.GetValue(name);
                        if (val != null)
                        {
                            using var disKey = Registry.CurrentUser.CreateSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\RunDisabled", writable: true);
                            disKey?.SetValue(name, val);
                            key?.DeleteValue(name, false);
                        }
                    }
                    else
                    {
                        using var disKey = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\RunDisabled", writable: true);
                        var val = disKey?.GetValue(name);
                        if (val != null)
                        {
                            key?.SetValue(name, val);
                            disKey?.DeleteValue(name, false);
                        }
                    }
                    return true;
                }
                else if (itemId.StartsWith("HKLM\\Run\\"))
                {
                    string name = itemId.Substring("HKLM\\Run\\".Length);
                    rollbackMgr.SnapshotRegistryKey(backupPkg, Registry.LocalMachine, "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", name);
                    rollbackMgr.PersistPackage(backupPkg);

                    using var key = Registry.LocalMachine.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", writable: true);
                    if (!enable)
                    {
                        var val = key?.GetValue(name);
                        if (val != null)
                        {
                            using var disKey = Registry.LocalMachine.CreateSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\RunDisabled", writable: true);
                            disKey?.SetValue(name, val);
                            key?.DeleteValue(name, false);
                        }
                    }
                    else
                    {
                        using var disKey = Registry.LocalMachine.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\RunDisabled", writable: true);
                        var val = disKey?.GetValue(name);
                        if (val != null)
                        {
                            key?.SetValue(name, val);
                            disKey?.DeleteValue(name, false);
                        }
                    }
                    return true;
                }
                else if (itemId.StartsWith("Folder\\"))
                {
                    string file = itemId.Substring("Folder\\".Length);
                    if (File.Exists(file))
                    {
                        if (!enable && !file.EndsWith(".disabled"))
                        {
                            File.Move(file, file + ".disabled");
                        }
                        else if (enable && file.EndsWith(".disabled"))
                        {
                            File.Move(file, file.Substring(0, file.Length - ".disabled".Length));
                        }
                        return true;
                    }
                }
            }
            catch { }

            return false;
        }

        public bool SetServiceStartMode(string serviceName, ServiceStartMode newMode)
        {
            if (EssentialServices.Contains(serviceName)) return false;

            var rollbackMgr = new TransactionalRollbackManager();
            var backupPkg = rollbackMgr.CreatePackage();
            rollbackMgr.SnapshotService(backupPkg, serviceName);
            rollbackMgr.PersistPackage(backupPkg);

            try
            {
                using var sc = new ServiceController(serviceName);
                ServiceControllerStatus status = sc.Status;

                // Call TransactionalRollbackManager.ExecuteRollback or RestoreServiceState via SCM
                return RestoreServiceState(serviceName, newMode, status);
            }
            catch
            {
                return false;
            }
        }

        private static void ScanRegistryRunKey(RegistryKey root, string subKeyPath, string locationName, List<StartupItemResult> list)
        {
            try
            {
                using var key = root.OpenSubKey(subKeyPath);
                if (key == null) return;

                foreach (var valName in key.GetValueNames())
                {
                    string cmd = key.GetValue(valName)?.ToString() ?? "";
                    string risk = cmd.Contains("temp", StringComparison.OrdinalIgnoreCase) || cmd.Contains("powershell", StringComparison.OrdinalIgnoreCase) ? "Medium" : "Low";
                    list.Add(new StartupItemResult($"{locationName}\\{valName}", valName, cmd, locationName, true, risk));
                }

                // Check disabled subkey
                using var disKey = root.OpenSubKey(subKeyPath + "Disabled");
                if (disKey != null)
                {
                    foreach (var valName in disKey.GetValueNames())
                    {
                        string cmd = disKey.GetValue(valName)?.ToString() ?? "";
                        list.Add(new StartupItemResult($"{locationName}\\{valName}", valName, cmd, locationName + " (Disabled)", false, "Low"));
                    }
                }
            }
            catch { }
        }

        private static void ScanStartupFolder(string folderPath, string locationName, List<StartupItemResult> list)
        {
            if (!Directory.Exists(folderPath)) return;

            try
            {
                foreach (var file in Directory.GetFiles(folderPath, "*"))
                {
                    string name = Path.GetFileName(file);
                    bool isEnabled = !name.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);
                    list.Add(new StartupItemResult($"Folder\\{file}", name, file, locationName, isEnabled, "Low"));
                }
            }
            catch { }
        }

        private static bool RestoreServiceState(string serviceName, ServiceStartMode startMode, ServiceControllerStatus status)
        {
            // Use Win32 SCM P/Invoke directly
            return ScmServiceManager.SetServiceConfig(serviceName, startMode);
        }
    }

    internal static class ScmServiceManager
    {

        [System.Runtime.InteropServices.DllImport("advapi32.dll", EntryPoint = "OpenSCManagerW", ExactSpelling = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr OpenSCManager(string? machineName, string? databaseName, uint dwDesiredAccess);

        [System.Runtime.InteropServices.DllImport("advapi32.dll", EntryPoint = "OpenServiceW", ExactSpelling = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr OpenService(IntPtr hSCManager, string serviceName, uint dwDesiredAccess);

        [System.Runtime.InteropServices.DllImport("advapi32.dll", EntryPoint = "ChangeServiceConfigW", ExactSpelling = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
        private static extern bool ChangeServiceConfig(IntPtr hService, uint dwServiceType, uint dwStartType, uint dwErrorControl, string? binaryPathName, string? loadOrderGroup, IntPtr tagId, string? dependencies, string? serviceStartName, string? password, string? displayName);

        [System.Runtime.InteropServices.DllImport("advapi32.dll", EntryPoint = "CloseServiceHandle", ExactSpelling = true, SetLastError = true)]
        private static extern bool CloseServiceHandle(IntPtr hSCObject);

        public static bool SetServiceConfig(string serviceName, ServiceStartMode startMode)
        {
            IntPtr hSCM = OpenSCManager(null, null, 0xF003F);
            if (hSCM == IntPtr.Zero) return false;

            try
            {
                IntPtr hSvc = OpenService(hSCM, serviceName, 0xF01FF);
                if (hSvc == IntPtr.Zero) return false;

                try
                {
                    uint winStartType = startMode switch
                    {
                        ServiceStartMode.Automatic => 0x00000002,
                        ServiceStartMode.Manual => 0x00000003,
                        ServiceStartMode.Disabled => 0x00000004,
                        _ => 0x00000003
                    };

                    return ChangeServiceConfig(hSvc, 0xFFFFFFFF, winStartType, 0xFFFFFFFF, null, null, IntPtr.Zero, null, null, null, null);
                }
                finally
                {
                    CloseServiceHandle(hSvc);
                }
            }
            finally
            {
                CloseServiceHandle(hSCM);
            }
        }
    }
}
