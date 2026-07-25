using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.ServiceProcess;
using System.Text.Json;
using Microsoft.Win32;
using Optimax.IPC;

namespace Optimax.Core
{
    public class DebloatItemDto
    {
        public string Id { get; set; } = "";
        public string Category { get; set; } = "";
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public bool IsEnabled { get; set; }
        public bool IsSelected { get; set; }
        public bool IsRiskLow { get; set; } = true;

        public DebloatItemDto() { }

        public DebloatItemDto(string id, string category, string name, string description, bool isEnabled, bool isRiskLow = true)
        {
            Id = id;
            Category = category;
            Name = name;
            Description = description;
            IsEnabled = isEnabled;
            IsRiskLow = isRiskLow;
        }
    }

    public class DebloatReport
    {
        public bool Success { get; set; }
        public int TotalApplied { get; set; }
        public string[] Messages { get; set; } = Array.Empty<string>();
        public string? BackupId { get; set; }

        public DebloatReport() { }

        public DebloatReport(bool success, int totalApplied, string[] messages, string? backupId)
        {
            Success = success;
            TotalApplied = totalApplied;
            Messages = messages;
            BackupId = backupId;
        }
    }

    public class WindowsDebloater
    {
        public List<DebloatItemDto> GetAvailableDebloatItems()
        {
            var list = new List<DebloatItemDto>();

            // 1. Telemetry & Diagnostics
            list.Add(new DebloatItemDto(
                "telemetry",
                "Quyền Riêng Tư",
                "Tắt Telemetry & Thu Thập Dữ Liệu Windows",
                "Vô hiệu hóa DiagTrack, dmwappushservice và cờ telemetry thu thập dữ liệu về Microsoft.",
                IsTelemetryDisabled()
            ));

            // 2. Windows Copilot
            list.Add(new DebloatItemDto(
                "copilot",
                "Tính Năng Win 11",
                "Vô Hiệu Hóa Windows Copilot AI",
                "Tắt thanh trợ lý AI Windows Copilot trên Windows 11 và Registry Explorer.",
                IsCopilotDisabled()
            ));

            // 3. Bing Search in Start Menu
            list.Add(new DebloatItemDto(
                "bingsearch",
                "Giao Diện OS",
                "Tắt Tìm Kiếm Bing Web Trong Menu Start",
                "Ngăn Menu Start tự động gợi ý và tìm kiếm từ khóa trên mạng Bing, giúp mở ứng dụng nhanh hơn.",
                IsBingSearchDisabled()
            ));

            // 4. Taskbar Widgets
            list.Add(new DebloatItemDto(
                "widgets",
                "Giao Diện OS",
                "Tắt Widgets / Tin Tức Trên Thanh Taskbar",
                "Ẩn biểu tượng Widgets (News and Interests) gây hao tốn RAM và mạng nền.",
                IsWidgetsDisabled()
            ));

            // 5. Advertising ID & Tailored Experiences
            list.Add(new DebloatItemDto(
                "advertising",
                "Quyền Riêng Tư",
                "Tắt Quảng Cáo Cá Nhân Hóa & ID Tiếp Thị",
                "Tắt cờ Advertising ID và các gợi ý ứng dụng được đề xuất của Microsoft.",
                IsAdvertisingDisabled()
            ));

            // 6. Dynamic UWP Bloatware Apps (Scanned Real-time from System)
            var uwpItems = GetDynamicUwpPackages();
            if (uwpItems.Count > 0)
            {
                list.AddRange(uwpItems);
            }
            else
            {
                // Fallback item if dynamic scan returns empty
                list.Add(new DebloatItemDto(
                    "uwp_bloatware",
                    "Ứng Dụng Rác",
                    "Gỡ Ứng Dụng Bloatware UWP Mặc Định",
                    "Gỡ tự động các app rác cài sẵn: Cortana, Bing Weather, Solitaire, Xbox App, Zune, Windows Maps, v.v.",
                    false
                ));
            }

            return list;
        }

        private List<DebloatItemDto> GetDynamicUwpPackages()
        {
            var uwpList = new List<DebloatItemDto>();
            try
            {
                string psCmd = "Get-AppxPackage | Where-Object { -not $_.IsFramework -and -not $_.NonRemovable } | Select-Object -Property Name, PackageFullName | ConvertTo-Json";
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{psCmd}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(psi);
                string json = proc?.StandardOutput.ReadToEnd() ?? "";
                proc?.WaitForExit(4000);

                if (!string.IsNullOrWhiteSpace(json))
                {
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var elem in doc.RootElement.EnumerateArray())
                        {
                            string name = elem.TryGetProperty("Name", out var n) ? n.GetString() ?? "" : "";
                            string fullName = elem.TryGetProperty("PackageFullName", out var fn) ? fn.GetString() ?? "" : "";

                            if (!string.IsNullOrEmpty(name) && IsRemovableUwpPackage(name))
                            {
                                string cleanTitle = FormatUwpDisplayName(name);
                                uwpList.Add(new DebloatItemDto(
                                    $"uwp_{name}",
                                    "Ứng Dụng UWP (Quét Động)",
                                    $"Gỡ UWP App: {cleanTitle}",
                                    $"Gỡ ứng dụng UWP '{fullName}' thực tế trên máy.",
                                    false
                                ));
                            }
                        }
                    }
                }
            }
            catch { }
            return uwpList;
        }

        private static bool IsRemovableUwpPackage(string name)
        {
            string[] bloatPatterns = new[]
            {
                "Cortana", "BingNews", "BingWeather", "GetHelp", "Getstarted", "MicrosoftSolitaireCollection",
                "People", "YourPhone", "ZuneMusic", "ZuneVideo", "WindowsMaps", "Xbox", "3DBuilder",
                "SkypeApp", "OfficeHub", "OneNote", "MixedReality", "FeedbackHub", "SoundRecorder",
                "Spotify", "TikTok", "Disney", "CandyCrush", "Netflix"
            };

            foreach (var p in bloatPatterns)
            {
                if (name.Contains(p, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private static string FormatUwpDisplayName(string name)
        {
            if (name.StartsWith("Microsoft.")) return name.Substring("Microsoft.".Length);
            return name;
        }

        public DebloatReport ApplyDebloatItems(string[] targetItemIds, bool isDryRun)
        {
            if (targetItemIds == null || targetItemIds.Length == 0)
            {
                return new DebloatReport(true, 0, new[] { "Không có mục debloat nào được chọn." }, null);
            }

            var messages = new List<string>();
            int count = 0;

            var rollbackMgr = new TransactionalRollbackManager();
            var backupPkg = rollbackMgr.CreatePackage();

            // Snapshot critical registry keys before tweaking
            rollbackMgr.SnapshotRegistryKey(backupPkg, Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry");
            rollbackMgr.SnapshotRegistryKey(backupPkg, Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\WindowsCopilot", "TurnOffWindowsCopilot");
            rollbackMgr.SnapshotRegistryKey(backupPkg, Registry.CurrentUser, @"Software\Policies\Microsoft\Windows\Explorer", "DisableSearchBoxSuggestions");
            rollbackMgr.SnapshotRegistryKey(backupPkg, Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Search", "BingSearchEnabled");
            rollbackMgr.SnapshotRegistryKey(backupPkg, Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarDa");

            string? backupId = null;
            if (!isDryRun)
            {
                backupId = rollbackMgr.PersistPackage(backupPkg);
            }

            foreach (var id in targetItemIds)
            {
                try
                {
                    switch (id.ToLowerInvariant())
                    {
                        case "telemetry":
                            if (isDryRun)
                            {
                                messages.Add("[DRY-RUN] Sẽ tắt dịch vụ DiagTrack và thiết lập AllowTelemetry = 0.");
                            }
                            else
                            {
                                DisableTelemetryInternal();
                                messages.Add("Đã tắt Telemetry & Diagnostics.");
                            }
                            count++;
                            break;

                        case "copilot":
                            if (isDryRun)
                            {
                                messages.Add("[DRY-RUN] Sẽ thiết lập TurnOffWindowsCopilot = 1.");
                            }
                            else
                            {
                                DisableCopilotInternal();
                                messages.Add("Đã tắt Windows Copilot AI.");
                            }
                            count++;
                            break;

                        case "bingsearch":
                            if (isDryRun)
                            {
                                messages.Add("[DRY-RUN] Sẽ tắt tìm kiếm Bing trong Start Menu.");
                            }
                            else
                            {
                                DisableBingSearchInternal();
                                messages.Add("Đã tắt Bing Search trong Menu Start.");
                            }
                            count++;
                            break;

                        case "widgets":
                            if (isDryRun)
                            {
                                messages.Add("[DRY-RUN] Sẽ ẩn Taskbar Widgets (TaskbarDa = 0).");
                            }
                            else
                            {
                                DisableWidgetsInternal();
                                messages.Add("Đã tắt Taskbar Widgets.");
                            }
                            count++;
                            break;

                        case "advertising":
                            if (isDryRun)
                            {
                                messages.Add("[DRY-RUN] Sẽ tắt Advertising ID và gợi ý tiếp thị.");
                            }
                            else
                            {
                                DisableAdvertisingInternal();
                                messages.Add("Đã tắt Quảng cáo & ID tiếp thị.");
                            }
                            count++;
                            break;

                        case "uwp_bloatware":
                            if (isDryRun)
                            {
                                messages.Add("[DRY-RUN] Sẽ gỡ các UWP Appx Packages: Cortana, Weather, Solitaire, Xbox, Zune...");
                            }
                            else
                            {
                                int removed = RemoveUwpBloatwareInternal();
                                messages.Add($"Đã gỡ bỏ {removed} gói ứng dụng UWP Bloatware.");
                            }
                            count++;
                            break;

                        default:
                            if (id.StartsWith("uwp_", StringComparison.OrdinalIgnoreCase))
                            {
                                string pkgName = id.Substring(4);
                                if (isDryRun)
                                {
                                    messages.Add($"[DRY-RUN] Sẽ gỡ ứng dụng UWP quét động '{pkgName}'.");
                                }
                                else
                                {
                                    bool ok = RemoveSpecificUwpPackageInternal(pkgName);
                                    messages.Add(ok ? $"Đã gỡ bỏ thành công ứng dụng UWP '{pkgName}'." : $"Không thể gỡ ứng dụng UWP '{pkgName}'.");
                                }
                                count++;
                            }
                            break;
                    }
                }
                catch (Exception ex)
                {
                    messages.Add($"Lỗi xử lý mục '{id}': {ex.Message}");
                }
            }

            return new DebloatReport(true, count, messages.ToArray(), backupId);
        }

        #region Internal Implementations & Status Checks

        private bool IsTelemetryDisabled()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows\DataCollection");
                if (key != null)
                {
                    var val = key.GetValue("AllowTelemetry");
                    if (val is int i && i == 0) return true;
                }
            }
            catch { }
            return false;
        }

        private void DisableTelemetryInternal()
        {
            try
            {
                using var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Microsoft\Windows\DataCollection", true);
                key.SetValue("AllowTelemetry", 0, RegistryValueKind.DWord);
            }
            catch { }

            // Disable DiagTrack & dmwappushservice
            SetServiceDisabled("DiagTrack");
            SetServiceDisabled("dmwappushservice");
        }

        private bool IsCopilotDisabled()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows\WindowsCopilot");
                if (key != null && key.GetValue("TurnOffWindowsCopilot") is int i && i == 1) return true;
            }
            catch { }
            return false;
        }

        private void DisableCopilotInternal()
        {
            try
            {
                using var key1 = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Microsoft\Windows\WindowsCopilot", true);
                key1.SetValue("TurnOffWindowsCopilot", 1, RegistryValueKind.DWord);

                using var key2 = Registry.CurrentUser.CreateSubKey(@"Software\Policies\Microsoft\Windows\WindowsCopilot", true);
                key2.SetValue("TurnOffWindowsCopilot", 1, RegistryValueKind.DWord);
            }
            catch { }
        }

        private bool IsBingSearchDisabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Search");
                if (key != null && key.GetValue("BingSearchEnabled") is int i && i == 0) return true;
            }
            catch { }
            return false;
        }

        private void DisableBingSearchInternal()
        {
            try
            {
                using var key1 = Registry.CurrentUser.CreateSubKey(@"Software\Policies\Microsoft\Windows\Explorer", true);
                key1.SetValue("DisableSearchBoxSuggestions", 1, RegistryValueKind.DWord);

                using var key2 = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Search", true);
                key2.SetValue("BingSearchEnabled", 0, RegistryValueKind.DWord);
            }
            catch { }
        }

        private bool IsWidgetsDisabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced");
                if (key != null && key.GetValue("TaskbarDa") is int i && i == 0) return true;
            }
            catch { }
            return false;
        }

        private void DisableWidgetsInternal()
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", true);
                key.SetValue("TaskbarDa", 0, RegistryValueKind.DWord);
            }
            catch { }
        }

        private bool IsAdvertisingDisabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo");
                if (key != null && key.GetValue("Enabled") is int i && i == 0) return true;
            }
            catch { }
            return false;
        }

        private void DisableAdvertisingInternal()
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo", true);
                key.SetValue("Enabled", 0, RegistryValueKind.DWord);
            }
            catch { }
        }

        private int RemoveUwpBloatwareInternal()
        {
            string[] packages = new[]
            {
                "*Cortana*",
                "*BingNews*",
                "*BingWeather*",
                "*GetHelp*",
                "*Getstarted*",
                "*MicrosoftSolitaireCollection*",
                "*People*",
                "*YourPhone*",
                "*ZuneMusic*",
                "*ZuneVideo*",
                "*WindowsMaps*",
                "*XboxApp*",
                "*XboxGameOverlay*",
                "*XboxGamingOverlay*"
            };

            int count = 0;
            foreach (var pkg in packages)
            {
                try
                {
                    string psCommand = $"Get-AppxPackage -Name '{pkg}' | Remove-AppxPackage -ErrorAction SilentlyContinue";
                    var psi = new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{psCommand}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    using var proc = Process.Start(psi);
                    proc?.WaitForExit(20000);
                    count++;
                }
                catch { }
            }
            return count;
        }

        private static bool RemoveSpecificUwpPackageInternal(string packageName)
        {
            if (string.IsNullOrWhiteSpace(packageName)) return false;
            try
            {
                string psCommand = $"Get-AppxPackage -Name '*{packageName}*' | Remove-AppxPackage -ErrorAction SilentlyContinue";
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{psCommand}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(psi);
                bool exited = proc?.WaitForExit(20000) ?? false;
                return exited;
            }
            catch
            {
                return false;
            }
        }

        private static void SetServiceDisabled(string serviceName)
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{serviceName}", writable: true);
                if (key != null)
                {
                    key.SetValue("Start", 4, RegistryValueKind.DWord); // 4 = Disabled
                }
            }
            catch { }

            try
            {
                using var sc = new ServiceController(serviceName);
                if (sc.Status != ServiceControllerStatus.Stopped)
                {
                    sc.Stop();
                }
            }
            catch { }
        }

        #endregion
    }
}
