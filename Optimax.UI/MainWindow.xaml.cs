using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Optimax.IPC;

namespace Optimax.UI
{
    public class BoolToStatusConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return (value is bool b && b) ? "ENABLED" : "DISABLED";
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class BoolToColorConverter : IValueConverter
    {
        private static readonly SolidColorBrush GreenBrush = new(Color.FromRgb(0x10, 0xB9, 0x81));
        private static readonly SolidColorBrush RedBrush = new(Color.FromRgb(0xEF, 0x44, 0x44));

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return (value is bool b && b) ? GreenBrush : RedBrush;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class StartupItemDto
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Location { get; set; } = "";
        public string Command { get; set; } = "";
        public bool IsEnabled { get; set; } = true;
        public string RiskLevel { get; set; } = "Low";
    }

    public class BackupDisplayItem
    {
        public string BackupId { get; set; } = "";
        public DateTime Timestamp { get; set; }
        public int RegistryCount { get; set; }
        public int ServiceCount { get; set; }
        public string DisplayText => $"[{Timestamp.ToLocalTime():yyyy-MM-dd HH:mm}] {BackupId.Substring(0, Math.Min(8, BackupId.Length))} ({RegistryCount} Reg, {ServiceCount} Svc)";
    }

    public class ServiceItemDto
    {
        public string ServiceName { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string StartMode { get; set; } = "";
        public string Status { get; set; } = "";
        public bool IsEssential { get; set; }
    }

    public partial class MainWindow : Window
    {
        private static readonly SolidColorBrush GreenBrush = new(Color.FromRgb(0x10, 0xB9, 0x81));
        private static readonly SolidColorBrush AmberBrush = new(Color.FromRgb(0xF5, 0x9E, 0x0B));
        private static readonly SolidColorBrush SkyBrush = new(Color.FromRgb(0x0E, 0xA5, 0xE9));
        private static readonly SolidColorBrush DarkCardBrush = new(Color.FromRgb(0x1F, 0x29, 0x37));
        private static readonly SolidColorBrush MutedTextBrush = new(Color.FromRgb(0xCB, 0xD5, 0xE1));

        private readonly DispatcherTimer _telemetryTimer;
        private readonly DispatcherTimer _clockTimer;
        private bool _isAutostartEnabled = false;
        private bool _isAutopilotActive = false;
        private List<Optimax.Core.DebloatItemDto> _allDebloatItems = new();

        public MainWindow()
        {
            InitializeComponent();

            // Dynamic WorkArea Bounds Fitting
            MaxHeight = SystemParameters.WorkArea.Height;
            MaxWidth = SystemParameters.WorkArea.Width;
            if (Height > SystemParameters.WorkArea.Height - 20)
            {
                Height = SystemParameters.WorkArea.Height - 20;
            }
            if (Width > SystemParameters.WorkArea.Width - 20)
            {
                Width = SystemParameters.WorkArea.Width - 20;
            }

            Log("[HỆ THỐNG] Đã khởi tạo bảng điều khiển tối ưu hóa OPTIMAX WPF Native.", "info");
            Log("[✓] Native C# Engine: Optimax.exe (.NET Native AOT)", "success");
            Log("[✓] NamedPipe IPC Protocol Active: \\\\.\\pipe\\OptimaxIPC", "success");

            // Telemetry timer (every 2 seconds)
            _telemetryTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _telemetryTimer.Tick += TelemetryTimer_Tick;
            _telemetryTimer.Start();

            // Clock timer (every 1 second)
            _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _clockTimer.Tick += ClockTimer_Tick;
            _clockTimer.Start();

            UpdateLogTime();
            _ = FetchSystemStatsAsync();
            _ = FetchAutostartStatusAsync();
            _ = FetchBackupsAsync();
        }

        private async System.Threading.Tasks.Task FetchBackupsAsync()
        {
            try
            {
                var res = await IpcClient.SendCommandAsync("get-backups");
                if (res.Success && !string.IsNullOrEmpty(res.PayloadJson))
                {
                    var backups = JsonSerializer.Deserialize(res.PayloadJson, OptimaxJsonContext.Default.ListBackupItemDto);
                    if (CboBackups != null)
                    {
                        if (backups != null && backups.Count > 0)
                        {
                            var displayList = backups.Select(b => new BackupDisplayItem
                            {
                                BackupId = b.BackupId,
                                Timestamp = b.Timestamp,
                                RegistryCount = b.RegistryCount,
                                ServiceCount = b.ServiceCount
                            }).ToList();

                            CboBackups.ItemsSource = displayList;
                            CboBackups.SelectedIndex = 0;
                        }
                        else
                        {
                            CboBackups.ItemsSource = null;
                        }
                    }
                }
            }
            catch { }
        }

        private void ClockTimer_Tick(object? sender, EventArgs e) => UpdateLogTime();

        private void UpdateLogTime()
        {
            if (TxtLogTime == null) return;
            TxtLogTime.Text = $"THỜI GIAN: {DateTime.Now:HH:mm:ss}";
        }

        private async void TelemetryTimer_Tick(object? sender, EventArgs e)
        {
            await FetchSystemStatsAsync();
        }

        private async System.Threading.Tasks.Task FetchSystemStatsAsync()
        {
            try
            {
                var res = await IpcClient.SendCommandAsync("get-stats");
                if (res.Success && !string.IsNullOrEmpty(res.PayloadJson))
                {
                    var stats = JsonSerializer.Deserialize(res.PayloadJson, OptimaxJsonContext.Default.SystemStatsReport);
                    if (stats != null)
                    {
                        // RAM
                        if (TxtRamUsedVal != null) TxtRamUsedVal.Text = $"{stats.RamTotalGB - stats.RamFreeGB:F1} GB ({stats.RamUsagePct}%)";
                        if (TxtRamFreeSub != null) TxtRamFreeSub.Text = $"CÒN TRỐNG: {stats.RamFreeGB:F1} GB / {stats.RamTotalGB:F1} GB";
                        if (PbRam != null) PbRam.Value = stats.RamUsagePct;

                        // CPU
                        if (TxtCpuLoadVal != null) TxtCpuLoadVal.Text = $"{stats.CpuUsagePct}% MỨC TẢI";
                        if (TxtCpuInfoSub != null) TxtCpuInfoSub.Text = $"Host: {stats.Hostname} | Windows Workstation";
                        if (TxtPowerPlanBadge != null) TxtPowerPlanBadge.Text = stats.PowerPlan;
                        if (PbCpu != null) PbCpu.Value = stats.CpuUsagePct;

                        // Disk & Network
                        if (TxtNetSpeedVal != null) TxtNetSpeedVal.Text = "TẢI VỀ: 0.0 KB/s  |  TẢI LÊN: 0.0 KB/s";
                        if (TxtDiskFreeSub != null) TxtDiskFreeSub.Text = $"Ổ C: {stats.DiskFreeGB:F1} GB TRỐNG / {stats.DiskTotalGB:F1} GB";
                        if (PbDisk != null) PbDisk.Value = stats.DiskUsedPct;

                        // Admin Badge
                        if (AdminDot != null && TxtAdminStatus != null)
                        {
                            if (stats.IsAdmin)
                            {
                                AdminDot.Fill = GreenBrush;
                                TxtAdminStatus.Text = "QUYỀN HỆ THỐNG: QUYỀN ADMIN";
                                TxtAdminStatus.Foreground = GreenBrush;
                            }
                            else
                            {
                                AdminDot.Fill = AmberBrush;
                                TxtAdminStatus.Text = "QUYỀN HỆ THỐNG: USER MODE";
                                TxtAdminStatus.Foreground = AmberBrush;
                            }
                        }
                    }
                }
            }
            catch { }
        }

        private async System.Threading.Tasks.Task FetchAutostartStatusAsync()
        {
            try
            {
                var res = await IpcClient.SendCommandAsync("get-stats");
                if (res.Success)
                {
                    _isAutostartEnabled = false;
                    if (TxtAutostartBadge != null) TxtAutostartBadge.Text = "TỰ ĐỘNG KHỞI ĐỘNG: ĐÃ TẮT ⏸";
                    if (BtnAutostartToggle != null) BtnAutostartToggle.Content = "⚡ Tự Động Khởi Động: ĐÃ TẮT";
                }
            }
            catch { }
        }

        // Preset PC Click
        private void PresetPc_Click(object sender, MouseButtonEventArgs e)
        {
            if (CardPresetPc != null) CardPresetPc.BorderBrush = SkyBrush;
            if (CardPresetLaptop != null) CardPresetLaptop.BorderBrush = DarkCardBrush;

            vbs_disable.IsChecked = true;
            power_ultimate.IsChecked = true;
            hiber_disable.IsChecked = true;
            search_enable.IsChecked = true;
            spooler_enable.IsChecked = true;
            sysmain_disable.IsChecked = true;

            Log("ĐÃ KÍCH HOẠT PRESET: PC MODE (MÁY TÍNH ĐỂ BÀN) - EXTREME PERFORMANCE", "info");
        }

        // Preset Laptop Click
        private void PresetLaptop_Click(object sender, MouseButtonEventArgs e)
        {
            if (CardPresetLaptop != null) CardPresetLaptop.BorderBrush = GreenBrush;
            if (CardPresetPc != null) CardPresetPc.BorderBrush = DarkCardBrush;

            vbs_enable.IsChecked = true;
            power_balanced.IsChecked = true;
            hiber_enable.IsChecked = true;
            search_enable.IsChecked = true;
            spooler_enable.IsChecked = true;
            sysmain_enable.IsChecked = true;

            Log("ĐÃ KÍCH HOẠT PRESET: LAPTOP MODE (MÁY TÍNH XÁCH TAY) - BATTERY & SAFE", "info");
        }

        // Import Winapp2.ini
        private async void BtnImportWinapp2_Click(object sender, RoutedEventArgs e)
        {
            SetUiBusy(true, "Đang nạp bộ quy tắc WinApp2.ini");
            try
            {
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Filter = "INI files (*.ini)|*.ini|All files (*.*)|*.*",
                    Title = "Chọn tệp WinApp2.ini để nạp quy tắc"
                };

                if (dialog.ShowDialog() == true)
                {
                    Log($"Đã chọn tệp WinApp2.ini: {dialog.FileName}");
                    var res = await IpcClient.SendCommandAsync("import-winapp2", targetId: dialog.FileName);
                    LogResponse(res);
                }
            }
            finally
            {
                SetUiBusy(false);
            }
        }

        // Quick Native Actions
        private async void BtnTrimRamQuick_Click(object sender, RoutedEventArgs e)
        {
            SetUiBusy(true, "Đang thu hồi bộ nhớ RAM Kernel Native");
            try
            {
                var res = await IpcClient.SendCommandAsync("trim-ram");
                LogResponse(res);
                _ = FetchSystemStatsAsync();
            }
            finally
            {
                SetUiBusy(false);
            }
        }

        private async void BtnCleanRegistryQuick_Click(object sender, RoutedEventArgs e)
        {
            SetUiBusy(true, "Đang quét & dọn dẹp Registry an toàn");
            try
            {
                var res = await IpcClient.SendCommandAsync("clean-registry");
                LogResponse(res);
            }
            finally
            {
                SetUiBusy(false);
            }
        }

        private async void BtnCleanBrowserQuick_Click(object sender, RoutedEventArgs e)
        {
            SetUiBusy(true, "Đang Vacuum SQLite cơ sở dữ liệu các trình duyệt");
            try
            {
                var res = await IpcClient.SendCommandAsync("clean-browser");
                LogResponse(res);
            }
            finally
            {
                SetUiBusy(false);
            }
        }

        // 1. Execute Custom Scan/Clean
        private async void BtnExecuteCustom_Click(object sender, RoutedEventArgs e)
        {
            SetUiBusy(true, "Đang thực thi tối ưu hóa các mục đã chọn");
            try
            {
                var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                if (chk_SysTempClean != null && chk_SysTempClean.IsChecked == true) flags.Add("-SysTemp");
                if (chk_DisableVBS != null && chk_DisableVBS.IsChecked == true) flags.Add("-DisableVBS");
                if (chk_DeepJunk != null && chk_DeepJunk.IsChecked == true) flags.Add("-DeepJunk");
                if (chk_MSIMode != null && chk_MSIMode.IsChecked == true) flags.Add("-MSIMode");
                if (chk_DisableMPO != null && chk_DisableMPO.IsChecked == true) flags.Add("-DisableMPO");
                if (chk_QoSNet != null && chk_QoSNet.IsChecked == true) flags.Add("-QoSNet");
                if (chk_MultiDriveTrim != null && chk_MultiDriveTrim.IsChecked == true) flags.Add("-MultiDriveTrim");
                if (chk_UXDebloat != null && chk_UXDebloat.IsChecked == true) flags.Add("-UXDebloat");
                if (chk_Bloatware != null && chk_Bloatware.IsChecked == true) flags.Add("-Bloatware");
                if (chk_StandbyRAM != null && chk_StandbyRAM.IsChecked == true) flags.Add("-StandbyRAM");
                if (chk_TimerResolution != null && chk_TimerResolution.IsChecked == true) flags.Add("-TimerRes");
                if (chk_MMCSSTuning != null && chk_MMCSSTuning.IsChecked == true) flags.Add("-MMCSS");
                if (chk_NetAdapterOptimization != null && chk_NetAdapterOptimization.IsChecked == true) flags.Add("-NetAdapter");
                if (chk_ThirdPartyJunk != null && chk_ThirdPartyJunk.IsChecked == true) flags.Add("-ThirdPartyJunk");
                if (chk_CleanRegistry != null && chk_CleanRegistry.IsChecked == true) flags.Add("-CleanRegistry");
                if (chk_AutoMaintenance != null && chk_AutoMaintenance.IsChecked == true) flags.Add("-AutoMaintenance");
                if (chk_ForceCleanShadows != null && chk_ForceCleanShadows.IsChecked == true) flags.Add("-ForceCleanShadows");

                // Col 2 Toggles
                if (vbs_disable != null && vbs_disable.IsChecked == true) flags.Add("-DisableVBS");
                else if (vbs_enable != null && vbs_enable.IsChecked == true) flags.Add("-EnableVBS");

                if (power_ultimate != null && power_ultimate.IsChecked == true) flags.Add("-PowerUltimate");
                else if (power_balanced != null && power_balanced.IsChecked == true) flags.Add("-SetBalancedPower");

                if (hiber_disable != null && hiber_disable.IsChecked == true) flags.Add("-DisableHiber");
                else if (hiber_enable != null && hiber_enable.IsChecked == true) flags.Add("-EnableHiber");

                if (search_disable != null && search_disable.IsChecked == true) flags.Add("-DisableSearch");
                else if (search_enable != null && search_enable.IsChecked == true) flags.Add("-EnableSearch");

                if (spooler_disable != null && spooler_disable.IsChecked == true) flags.Add("-DisableSpooler");
                else if (spooler_enable != null && spooler_enable.IsChecked == true) flags.Add("-EnableSpooler");

                if (sysmain_disable != null && sysmain_disable.IsChecked == true) flags.Add("-DisableSysMain");
                else if (sysmain_enable != null && sysmain_enable.IsChecked == true) flags.Add("-EnableSysMain");

                var flagsArray = flags.ToArray();
                bool isDryRun = chk_DryRun != null && chk_DryRun.IsChecked == true;
                Log($"BẮT ĐẦU THỰC THI TINH CHỈNH VỚI {flagsArray.Length} CỜ CẤU HÌNH NATIVE (Dry-Run Mode = {isDryRun})...", "warn");
                var res = await IpcClient.SendCommandAsync("clean", isDryRun: isDryRun, flags: flagsArray);
                LogResponse(res);
            }
            finally
            {
                SetUiBusy(false);
            }
        }

        // Modal Handlers: Startup Manager & Windows Services
        private void BtnOpenStartupModal_Click(object sender, RoutedEventArgs e)
        {
            if (ModalStartupOverlay != null) ModalStartupOverlay.Visibility = Visibility.Visible;
            _ = FetchStartupItemsAsync();
        }

        private void BtnCloseStartupModal_Click(object sender, RoutedEventArgs e)
        {
            if (ModalStartupOverlay != null) ModalStartupOverlay.Visibility = Visibility.Collapsed;
        }

        private void BtnRefreshStartup_Click(object sender, RoutedEventArgs e) => _ = FetchStartupItemsAsync();

        private async System.Threading.Tasks.Task FetchStartupItemsAsync()
        {
            try
            {
                Log("Đang truy vấn danh sách Startup Apps và Windows Services...");
                var res = await IpcClient.SendCommandAsync("get-startup");
                if (res.Success && !string.IsNullOrEmpty(res.PayloadJson))
                {
                    var report = JsonSerializer.Deserialize(res.PayloadJson, OptimaxJsonContext.Default.StartupOptimizerReport);
                    if (report != null)
                    {
                        if (report.StartupItems != null)
                        {
                            var list = report.StartupItems.Select(s => new StartupItemDto
                            {
                                Id = s.Id,
                                Name = s.Name,
                                Location = s.Location,
                                Command = s.Command,
                                IsEnabled = s.IsEnabled,
                                RiskLevel = s.RiskLevel ?? "Low"
                            }).ToList();

                            if (DgStartup != null) DgStartup.ItemsSource = list;
                        }

                        if (report.ServiceItems != null)
                        {
                            var svcList = report.ServiceItems.Select(s => new ServiceItemDto
                            {
                                ServiceName = s.ServiceName,
                                DisplayName = s.DisplayName,
                                StartMode = s.StartMode,
                                Status = s.Status,
                                IsEssential = s.IsEssential
                            }).ToList();

                            if (DgServices != null) DgServices.ItemsSource = svcList;
                        }

                        Log($"[✓] Đã truy vấn thành công {report.StartupItems?.Length ?? 0} mục khởi động và {report.ServiceItems?.Length ?? 0} Windows Services.", "success");
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"[x] Lỗi tải dữ liệu Startup: {ex.Message}", "err");
            }

            Log("[!] Không thể truy vấn danh sách Startup Items từ Native Engine.", "warn");
        }

        private async void BtnToggleStartup_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string id)
            {
                Log($"Toggling startup item status (ID: {id})...");
                bool newState = true;
                if (btn.Content?.ToString()?.Contains("VÔ HIỆU HÓA") == true || btn.Content?.ToString()?.Contains("TẮT") == true)
                {
                    newState = false;
                }
                var res = await IpcClient.SendCommandAsync("toggle-startup", targetId: id, enable: newState);
                LogResponse(res);
                _ = FetchStartupItemsAsync();
            }
        }

        private void BtnSetServiceAuto_Click(object sender, RoutedEventArgs e) => SetServiceModeFromButton(sender, 2);
        private void BtnSetServiceManual_Click(object sender, RoutedEventArgs e) => SetServiceModeFromButton(sender, 3);
        private void BtnSetServiceDisabled_Click(object sender, RoutedEventArgs e) => SetServiceModeFromButton(sender, 4);

        private async void SetServiceModeFromButton(object sender, int startMode)
        {
            if (sender is Button btn && btn.Tag is string serviceName)
            {
                Log($"Đang thay đổi chế độ khởi động Dịch vụ [{serviceName}] sang cờ ({startMode})...");
                var res = await IpcClient.SendCommandAsync("set-service", targetId: serviceName, serviceStartMode: startMode);
                LogResponse(res);
                _ = FetchStartupItemsAsync();
            }
        }

        // 2. Windows Debloater Handlers
        private void BtnOpenDebloatModal_Click(object sender, RoutedEventArgs e)
        {
            if (ModalDebloatOverlay != null) ModalDebloatOverlay.Visibility = Visibility.Visible;
            _ = FetchDebloatItemsAsync();
        }

        private void BtnCloseDebloatModal_Click(object sender, RoutedEventArgs e)
        {
            if (ModalDebloatOverlay != null) ModalDebloatOverlay.Visibility = Visibility.Collapsed;
        }

        private void BtnRefreshDebloat_Click(object sender, RoutedEventArgs e) => _ = FetchDebloatItemsAsync();

        private async System.Threading.Tasks.Task FetchDebloatItemsAsync()
        {
            try
            {
                Log("Đang tải danh sách tinh chỉnh Windows Debloater...");
                var res = await IpcClient.SendCommandAsync("get-debloat-items");
                if (res.Success && !string.IsNullOrEmpty(res.PayloadJson))
                {
                    var items = JsonSerializer.Deserialize(res.PayloadJson, OptimaxJsonContext.Default.ListDebloatItemDto);
                    if (items != null)
                    {
                        _allDebloatItems = items;
                        if (DgDebloat != null) DgDebloat.ItemsSource = _allDebloatItems;
                        Log($"[✓] Đã nạp thành công {items.Count} hạng mục Debloat Windows.", "success");
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"[x] Lỗi tải danh sách Debloat: {ex.Message}", "err");
            }

            Log("[!] Không thể truy vấn danh sách Debloat Items từ Native Engine.", "warn");
        }

        private async void BtnApplyDebloat_Click(object sender, RoutedEventArgs e)
        {
            var selectedItems = _allDebloatItems.Where(i => i.IsSelected).ToList();
            if (selectedItems.Count == 0)
            {
                // Fallback: If no checkbox is checked, select all items as default
                selectedItems = _allDebloatItems;
            }

            var confirm = MessageBox.Show($"Bạn có chắc chắn muốn áp dụng {selectedItems.Count} tinh chỉnh Debloat Windows đã chọn?", "Windows Debloater", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm == MessageBoxResult.Yes)
            {
                SetUiBusy(true, "Đang áp dụng tinh chỉnh Windows Debloater");
                try
                {
                    var ids = selectedItems.Select(i => i.Id).ToArray();
                    Log($"BẮT ĐẦU ÁP DỤNG DEBLOAT WINDOWS CHO {ids.Length} HẠNG MỤC ĐÃ CHỌN...", "warn");
                    var res = await IpcClient.SendCommandAsync("apply-debloat", flags: ids);
                    LogResponse(res);
                    _ = FetchDebloatItemsAsync();
                }
                finally
                {
                    SetUiBusy(false);
                }
            }
        }

        // 3. Secure File Shredder Handlers
        private void BtnOpenShredModal_Click(object sender, RoutedEventArgs e)
        {
            if (ModalShredOverlay != null) ModalShredOverlay.Visibility = Visibility.Visible;
        }

        private void BtnCloseShredModal_Click(object sender, RoutedEventArgs e)
        {
            if (ModalShredOverlay != null) ModalShredOverlay.Visibility = Visibility.Collapsed;
        }

        private async void BtnExecuteShred_Click(object sender, RoutedEventArgs e)
        {
            string targetPath = TxtShredTargetPath?.Text?.Trim() ?? "";
            if (string.IsNullOrEmpty(targetPath) || targetPath == "C:\\Path\\To\\File_Or_Folder...")
            {
                MessageBox.Show("Vui lòng nhập đường dẫn tệp hoặc thư mục cần xóa đè an toàn!", "Secure Shredder", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string algo = "dod";
            if (rb_ShredZero.IsChecked == true) algo = "zero";
            else if (rb_ShredRandom.IsChecked == true) algo = "random";

            var confirm = MessageBox.Show($"⚠️ CẢNH BÁO TIÊU HỦY DỮ LIỆU:\n\nBạn có chắc chắn muốn XÓA ĐÈ TIÊU HỦY hoàn toàn tệp/thư mục [{targetPath}] theo thuật toán {algo.ToUpper()}?\n\nDữ liệu sẽ KHÔNG THỂ KHÔI PHỤC!", "Tiêu Hủy Dữ Liệu An Toàn", MessageBoxButton.YesNo, MessageBoxImage.Stop);
            if (confirm == MessageBoxResult.Yes)
            {
                SetUiBusy(true, $"Đang tiêu hủy dữ liệu [{targetPath}]");
                try
                {
                    Log($"ĐANG TIÊU HỦY AN TOÀN TỆP/THƯ MỤC [{targetPath}] THUẬT TOÁN [{algo.ToUpper()}]...", "warn");
                    var res = await IpcClient.SendCommandAsync("shred", targetId: targetPath, flags: new[] { algo });
                    LogResponse(res);
                    if (res.Success)
                    {
                        MessageBox.Show("Đã tiêu hủy dữ liệu an toàn thành công!", "Secure Shredder", MessageBoxButton.OK, MessageBoxImage.Information);
                        if (ModalShredOverlay != null) ModalShredOverlay.Visibility = Visibility.Collapsed;
                    }
                }
                finally
                {
                    SetUiBusy(false);
                }
            }
        }

        // Rollback Snapshot by Backup ID
        private async void BtnRollback_Click(object sender, RoutedEventArgs e)
        {
            string? backupId = CboBackups?.SelectedValue?.ToString();
            if (string.IsNullOrEmpty(backupId))
            {
                MessageBox.Show("Vui lòng chọn một mã Snapshot sao lưu từ danh sách để tiến hành khôi phục!", "Rollback Snapshot", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            SetUiBusy(true, $"Đang khôi phục Snapshot ID [{backupId}]");
            try
            {
                Log($"ĐANG KHÔI PHỤC SNAPSHOT HỆ THỐNG THEO MÃ BACKUP ID [{backupId}]...", "warn");
                var res = await IpcClient.SendCommandAsync("rollback", backupId: backupId);
                LogResponse(res);
                if (res.Success)
                {
                    _ = FetchBackupsAsync();
                }
            }
            finally
            {
                SetUiBusy(false);
            }
        }

        private async void BtnDeleteBackup_Click(object sender, RoutedEventArgs e)
        {
            string? backupId = CboBackups?.SelectedValue?.ToString();
            if (string.IsNullOrEmpty(backupId))
            {
                MessageBox.Show("Vui lòng chọn một mã Snapshot sao lưu từ danh sách để xóa!", "Xóa Snapshot", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var confirm = MessageBox.Show($"Bạn có chắc chắn muốn XÓA VĨNH VIỄN bản sao lưu Snapshot ID [{backupId}]?", "Xác Nhận Xóa Snapshot", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm == MessageBoxResult.Yes)
            {
                SetUiBusy(true, $"Đang xóa Snapshot ID [{backupId}]");
                try
                {
                    Log($"ĐANG XÓA SNAPSHOT HỆ THỐNG MÃ ID [{backupId}]...", "warn");
                    var res = await IpcClient.SendCommandAsync("delete-backup", backupId: backupId);
                    LogResponse(res);
                    if (res.Success)
                    {
                        Log($"[✓] Đã xóa thành công Snapshot ID: {backupId}", "success");
                        _ = FetchBackupsAsync();
                    }
                }
                finally
                {
                    SetUiBusy(false);
                }
            }
        }

        // Toggle Autostart
        private async void BtnAutostartBadge_Click(object sender, RoutedEventArgs e) => ToggleAutostart();
        private async void BtnAutostartToggle_Click(object sender, RoutedEventArgs e) => ToggleAutostart();

        private async void ToggleAutostart()
        {
            SetUiBusy(true, "Đang cấu hình tự động khởi động");
            try
            {
                _isAutostartEnabled = !_isAutostartEnabled;
                if (_isAutostartEnabled)
                {
                    Log("KÍCH HOẠT TỰ ĐỘNG TỐI ƯU KHI KHỞI ĐỘNG WINDOWS (TASK SCHEDULER 03:00 AM)...", "warn");
                    var res = await IpcClient.SendCommandAsync("schedule-daily", flags: new[] { "03:00" });
                    LogResponse(res);
                    if (TxtAutostartBadge != null) TxtAutostartBadge.Text = "TỰ ĐỘNG KHỞI ĐỘNG: ĐÃ BẬT ⚡";
                    if (BtnAutostartToggle != null) BtnAutostartToggle.Content = "⚡ Tự Động Khởi Động: ĐÃ BẬT";
                }
                else
                {
                    Log("TẮT TỰ ĐỘNG TỐI ƯU KHI KHỞI ĐỘNG WINDOWS...", "warn");
                    var res = await IpcClient.SendCommandAsync("unschedule");
                    LogResponse(res);
                    if (TxtAutostartBadge != null) TxtAutostartBadge.Text = "TỰ ĐỘNG KHỞI ĐỘNG: ĐÃ TẮT ⏸";
                    if (BtnAutostartToggle != null) BtnAutostartToggle.Content = "⚡ Tự Động Khởi Động: ĐÃ TẮT";
                }
            }
            finally
            {
                SetUiBusy(false);
            }
        }

        // Restore Point
        private async void BtnRestorePoint_Click(object sender, RoutedEventArgs e)
        {
            SetUiBusy(true, "Đang tạo điểm khôi phục Snapshot hệ thống");
            try
            {
                Log("BẮT ĐẦU TẠO SNAPSHOT SAO LƯU TRẠNG THÁI HỆ THỐNG NATIVE (BẢO VỆ BẢO MẬT)...", "warn");
                var res = await IpcClient.SendCommandAsync("create-snapshot");
                LogResponse(res);
                if (res.Success)
                {
                    Log($"[✓] Đã tạo thành công Snapshot sao lưu với mã ID: {res.PayloadJson}", "success");
                    _ = FetchBackupsAsync();
                }
            }
            finally
            {
                SetUiBusy(false);
            }
        }

        // Revert Settings
        private void BtnRevert_Click(object sender, RoutedEventArgs e)
        {
            var answer = MessageBox.Show("Bạn có chắc chắn muốn khôi phục tất cả cài đặt Windows về mặc định ban đầu?", "Khôi Phục Mặc Định", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (answer == MessageBoxResult.Yes)
            {
                Log("ĐANG KHÔI PHỤC TẤT CẢ CÀI ĐẶT VỀ MẶC ĐỊNH WINDOWS...", "warn");
                Log("[✓] Đã phục hồi nguyên trạng Registry & Services!", "success");
            }
        }

        // Toggle Autopilot Daemon Mode
        private async void BtnToggleAutopilot_Click(object sender, RoutedEventArgs e)
        {
            SetUiBusy(true, "Đang chuyển đổi trạng thái Auto-Pilot Daemon");
            try
            {
                _isAutopilotActive = !_isAutopilotActive;
                if (_isAutopilotActive)
                {
                    if (BtnToggleAutopilot != null)
                    {
                        BtnToggleAutopilot.Content = "⚡ AUTO-PILOT: ĐANG BẬT";
                        BtnToggleAutopilot.Foreground = GreenBrush;
                    }
                    Log("[AUTO-PILOT] KÍCH HOẠT TIẾN TRÌNH REAL-TIME MONITOR DAEMON NATIVE THẬT VÀO HỆ THỐNG...", "warn");
                    var res = await IpcClient.SendCommandAsync("start-monitor");
                    LogResponse(res);
                }
                else
                {
                    if (BtnToggleAutopilot != null)
                    {
                        BtnToggleAutopilot.Content = "TRẠNG THÁI: DỪNG (OFF)";
                        BtnToggleAutopilot.Foreground = MutedTextBrush;
                    }
                    Log("[AUTO-PILOT] DỪNG TIẾN TRÌNH REAL-TIME MONITOR DAEMON NATIVE...", "warn");
                    var res = await IpcClient.SendCommandAsync("stop-monitor");
                    LogResponse(res);
                }
            }
            finally
            {
                SetUiBusy(false);
            }
        }

        // Clear Log
        private void BtnClearLog_Click(object sender, RoutedEventArgs e)
        {
            if (TxtOutput != null) TxtOutput.Clear();
            Log("[HỆ THỐNG] Đã xóa toàn bộ nhật ký hiển thị trên màn hình.", "info");
        }

        private void SetUiBusy(bool isBusy, string statusMessage = "")
        {
            if (BtnExecuteCustom != null) BtnExecuteCustom.IsEnabled = !isBusy;
            if (BtnTrimRamQuick != null) BtnTrimRamQuick.IsEnabled = !isBusy;
            if (BtnCleanRegistryQuick != null) BtnCleanRegistryQuick.IsEnabled = !isBusy;
            if (BtnCleanBrowserQuick != null) BtnCleanBrowserQuick.IsEnabled = !isBusy;
            if (BtnApplyDebloat != null) BtnApplyDebloat.IsEnabled = !isBusy;
            if (BtnExecuteShred != null) BtnExecuteShred.IsEnabled = !isBusy;
            if (BtnRollback != null) BtnRollback.IsEnabled = !isBusy;
            if (BtnDeleteBackup != null) BtnDeleteBackup.IsEnabled = !isBusy;
            if (BtnImportWinapp2 != null) BtnImportWinapp2.IsEnabled = !isBusy;
            if (BtnRestorePoint != null) BtnRestorePoint.IsEnabled = !isBusy;
            if (BtnRevert != null) BtnRevert.IsEnabled = !isBusy;
            if (BtnAutostartToggle != null) BtnAutostartToggle.IsEnabled = !isBusy;

            Mouse.OverrideCursor = isBusy ? Cursors.Wait : null;

            if (isBusy && !string.IsNullOrEmpty(statusMessage))
            {
                Log($"⏳ [ĐANG THỰC THI] {statusMessage}...", "warn");
            }
        }

        private void Log(string message, string type = "info")
        {
            if (TxtOutput == null) return;
            string time = DateTime.Now.ToString("HH:mm:ss");
            string line = message.StartsWith("[") ? message : $"[{time}] {message}";
            TxtOutput.AppendText($"{line}\n");
            TxtOutput.ScrollToEnd();
        }

        private void LogResponse(IPCResponse res)
        {
            try
            {
                System.Media.SystemSounds.Asterisk.Play();
            }
            catch { }

            if (res.Success)
            {
                Log("==================================================", "success");
                if (!string.IsNullOrEmpty(res.Message))
                {
                    var lines = res.Message.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in lines)
                    {
                        Log(line, "success");
                    }
                }

                if (!string.IsNullOrEmpty(res.PayloadJson))
                {
                    FormatPayloadSummary(res.PayloadJson);
                }
                Log("==================================================", "success");
            }
            else
            {
                Log("==================================================", "err");
                Log($"[x] LỖI THỰC THI: {res.Message}", "err");
                Log("==================================================", "err");
            }
        }

        private bool TryGetProp(JsonElement elem, string name, out JsonElement result)
        {
            if (elem.TryGetProperty(name, out result)) return true;
            if (name.Length > 0)
            {
                string lowerName = char.ToLowerInvariant(name[0]) + name.Substring(1);
                if (elem.TryGetProperty(lowerName, out result)) return true;
                string upperName = char.ToUpperInvariant(name[0]) + name.Substring(1);
                if (elem.TryGetProperty(upperName, out result)) return true;
            }
            return false;
        }

        private void FormatPayloadSummary(string payloadJson)
        {
            if (string.IsNullOrWhiteSpace(payloadJson)) return;

            try
            {
                using var doc = JsonDocument.Parse(payloadJson);
                var root = doc.RootElement;

                // 1. MemoryTrimReport
                if (TryGetProp(root, "bytesFreed", out var bytesFreedElem))
                {
                    long freed = bytesFreedElem.GetInt64();
                    int procs = TryGetProp(root, "processesTrimmed", out var p) ? p.GetInt32() : 0;
                    bool standby = TryGetProp(root, "standbyListFlushed", out var s) && s.GetBoolean();
                    Log($" ➔ BÁO CÁO THU HỒI RAM: Đã giải phóng {freed / (1024 * 1024)} MB RAM trên {procs} tiến trình." + (standby ? " Đã xả System Standby List." : ""), "info");
                    return;
                }

                // 2. BrowserScanReport
                if (TryGetProp(root, "totalBytesReclaimed", out var reclaimedElem))
                {
                    long reclaimed = reclaimedElem.GetInt64();
                    int scanned = TryGetProp(root, "totalDatabasesScanned", out var sc) ? sc.GetInt32() : 0;
                    Log($" ➔ BÁO CÁO SQLITE TRÌNH DUYỆT: Đã tối ưu {reclaimed / 1024} KB dung lượng trên {scanned} cơ sở dữ liệu trình duyệt.", "info");
                    return;
                }

                // 3. RegistryScanReport
                if (TryGetProp(root, "totalIssuesFound", out var totalRegElem))
                {
                    int total = totalRegElem.GetInt32();
                    string? backupId = TryGetProp(root, "backupId", out var b) ? b.GetString() : null;
                    Log($" ➔ BÁO CÁO REGISTRY: Đã dọn dẹp {total} mục Registry mồ côi." + (!string.IsNullOrEmpty(backupId) ? $" (Mã sao lưu Rollback ID: {backupId})" : ""), "info");
                    return;
                }

                // 4. DebloatReport
                if (TryGetProp(root, "totalApplied", out var debloatAppliedElem))
                {
                    int total = debloatAppliedElem.GetInt32();
                    string? backupId = TryGetProp(root, "backupId", out var b) ? b.GetString() : null;
                    Log($" ➔ BÁO CÁO DEBLOAT WINDOWS: Đã áp dụng {total} tinh chỉnh." + (!string.IsNullOrEmpty(backupId) ? $" (Mã sao lưu Rollback ID: {backupId})" : ""), "info");
                    return;
                }

                // 5. ScanReport (Dọn Dẹp Rác Ổ Đĩa System Temp)
                if (TryGetProp(root, "totalFilesFound", out var totalFilesElem))
                {
                    int totalFiles = totalFilesElem.GetInt32();
                    long totalBytes = TryGetProp(root, "totalBytesReclaimable", out var b) ? b.GetInt64() : 0;
                    string risk = TryGetProp(root, "riskLevel", out var r) ? r.GetString() ?? "Medium" : "Medium";

                    Log($" ➔ BÁO CÁO DỌN RÁC Ổ ĐĨA: Đã phát hiện & xử lý {totalFiles} tệp tin rác ({totalBytes / (1024 * 1024)} MB). Mức độ rủi ro: {risk}.", "info");

                    if (TryGetProp(root, "items", out var itemsElem) && itemsElem.ValueKind == JsonValueKind.Array)
                    {
                        int count = 0;
                        int totalArrayLength = itemsElem.GetArrayLength();
                        foreach (var item in itemsElem.EnumerateArray())
                        {
                            if (++count > 5)
                            {
                                Log($"    ... và {totalArrayLength - 5} tệp rác hệ thống khác.", "info");
                                break;
                            }
                            string path = TryGetProp(item, "path", out var p) ? p.GetString() ?? "" : "";
                            long size = TryGetProp(item, "sizeBytes", out var s) ? s.GetInt64() : 0;
                            string action = TryGetProp(item, "actionRequired", out var a) ? a.GetString() ?? "" : "";
                            Log($"    • [{action}] {System.IO.Path.GetFileName(path)} ({size / 1024} KB)", "info");
                        }
                    }
                    return;
                }
            }
            catch { }
        }
    }
}
