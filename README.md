# OPTIMAX - Native Windows System Optimizer

OPTIMAX là giải pháp tối ưu hóa hiệu năng và dọn dẹp hệ thống Windows, được phát triển bằng .NET Native AOT (Win32 P/Invoke) kèm giao diện WPF (`Optimax.UI`). 

Ứng dụng có thời gian khởi động < 10ms, mức sử dụng RAM ~15–30 MB, tối ưu hóa thông qua các API Win32 Native cấp thấp (`NtSetSystemInformation`, `rstrtmgr.dll`, Win32 SCM) và cơ chế khôi phục trạng thái (Zero-Risk Architecture).

---

## Kiến trúc An toàn (Zero-Risk Architecture)

1. **Chế độ quét thử nghiệm (`--dry-run`):**
   - Quét bất đồng bộ đa luồng (IOCP ThreadPool) và xuất báo cáo chi tiết (danh sách tệp tin, dung lượng dự kiến giải phóng, mức độ rủi ro) trước khi thực hiện dọn dẹp thực sự.

2. **Cơ chế Rollback nguyên trạng (Transactional Rollback Engine):**
   - Tự động chụp Snapshot trạng thái Registry và dịch vụ Windows (Services StartMode/State) vào `%ProgramData%\Optimax\Backups\<backup-id>` trước khi can thiệp. Khôi phục hệ thống thông qua CLI (`--rollback <backup-id>`) hoặc UI.

3. **Kiểm tra khóa tệp tin qua Win32 Restart Manager API:**
   - Sử dụng `rstrtmgr.dll` để phát hiện chính xác ứng dụng đang khóa tệp thay vì dừng tiến trình cưỡng ép. Nếu tệp bị khóa bởi dịch vụ hệ thống critical, ứng dụng sẽ đăng ký xóa an toàn khi khởi động lại qua API `MoveFileExW` (`MOVEFILE_DELAY_UNTIL_REBOOT`).

4. **Dynamic Rule Engine & WinApp2.ini Importer:**
   - Hỗ trợ nạp bộ quy tắc dọn dẹp `WinApp2.ini` và quy tắc tùy biến từ `rules/custom_rules.json` kèm kiểm tra phiên bản OS Build.

---

## Các phân hệ Core Engine (`Optimax.Native`)

- **SafetyEngine.cs:** Kiểm tra khóa tệp qua Win32 Restart Manager API (`rstrtmgr.dll`) và đăng ký xóa sau khởi động (`MoveFileExW`).
- **ParallelScanner.cs:** Quét đĩa đa luồng dạng lazy enumeration (`IEnumerable<FileInfo>`) trên nền IOCP ThreadPool.
- **TransactionalRollback.cs:** Quản lý tạo Snapshot và Rollback nguyên trạng Registry & Windows Services.
- **KernelMemoryTrimmer.cs:** Giải phóng RAM kernel, thu hồi Working Set ứng dụng nền và xả System Standby List qua `NtSetSystemInformation`.
- **BrowserOptimizer.cs:** Tối ưu nén cơ sở dữ liệu SQLite (VACUUM / REINDEX) cho Chrome, Edge, Brave, Cốc Cốc, Vivaldi, Yandex, Opera, Firefox, Thunderbird.
- **DeepRegistryScanner.cs:** Quét 7 loại mục Registry mồ côi (SharedDLLs, App Paths, MUICache, TypeLib, CLSID) kèm danh sách loại trừ hệ thống.
- **SecureFileShredder.cs:** Tiêu hủy dữ liệu an toàn theo tiêu chuẩn DoD 5220.22-M (3-pass overwrites + rename MFT metadata).
- **WindowsDebloater.cs:** Vô hiệu hóa Telemetry, Windows Copilot, Bing Search, Widgets, Advertising ID và gỡ bỏ gói UWP bloatware.
- **SystemTweaksEngine.cs:** Tinh chỉnh CPU Priority Scheduling (`Win32PrioritySeparation`), MSI Mode (`MSISupported`), TRIM đĩa SSD/NVMe, Global Timer Resolution (0.5ms), MMCSS Games Priority, QoS Network Ack Frequency (`TCPNoDelay`).
- **NativeInterop.cs:** Đóng gói tập trung các định nghĩa P/Invoke Win32 (SCM API, MEMORYSTATUSEX) phục vụ dùng chung giữa các phân hệ.
- **OptimaxLogger.cs:** Hệ thống ghi log đa luồng hỗ trợ chẩn đoán production tại `%ProgramData%\Optimax\Logs`.
- **NamedPipeServer.cs:** Server IPC giao tiếp hai chiều bảo mật qua `\\.\pipe\OptimaxIPC` (NDJSON Streaming).

---

## Hướng dẫn Sử dụng

### Yêu cầu Hệ thống
- Hệ điều hành: Windows 10 / Windows 11 (64-bit).
- Quyền hạn: Administrator.
- Runtime: Không yêu cầu (.NET Native AOT tự đóng gói thành file binary độc lập). PowerShell chỉ được dùng khi thực hiện tác vụ quản lý gói UWP Appx do hạn chế của API Windows.

---

### Sử dụng qua Command Line (`Optimax.exe`)

```powershell
# Chạy mô phỏng quét (Dry-Run Mode):
.\Optimax.exe --dry-run

# Chạy quét và dọn dẹp thực tế:
.\Optimax.exe --scan

# Tạo Snapshot sao lưu trạng thái hệ thống:
.\Optimax.exe --create-snapshot

# Xem danh sách các bản sao lưu đã lưu:
.\Optimax.exe --get-backups

# Phục hồi trạng thái hệ thống theo Backup ID:
.\Optimax.exe --rollback <backup-id>

# Giải phóng bộ nhớ RAM Kernel & Standby List:
.\Optimax.exe --trim-ram

# Tối ưu hóa CSDL SQLite trình duyệt:
.\Optimax.exe --clean-browser

# Dọn dẹp Registry mồ côi:
.\Optimax.exe --clean-registry

# Tiêu hủy tệp tin an toàn (DoD 5220.22-M):
.\Optimax.exe --shred "C:\Path\To\File" --shred-mode dod

# Khởi động IPC Service kết nối với giao diện WPF:
.\Optimax.exe --ipc-service
```

---

### Biên dịch Native AOT

```powershell
dotnet publish Optimax.Native/Optimax.csproj -c Release
```

File thực thi sau khi publish sẽ nằm tại: `Optimax.Native/bin/Release/net10.0-windows/publish/Optimax.exe`

---

### Giao diện WPF Desktop (`Optimax.UI`)

1. Biên dịch ứng dụng UI:
   ```powershell
   dotnet build Optimax.UI/Optimax.UI.csproj -c Release
   ```
2. Mở `Optimax.UI.exe` để điều khiển Native Engine qua kết nối NamedPipe IPC (`\\.\pipe\OptimaxIPC`).

---

## Cấu trúc Dự án

```
.
├── Optimax.Native/                 # Native C# .NET Native AOT Core Engine
│   ├── Core/
│   │   ├── SafetyEngine.cs         # Win32 Restart Manager API (rstrtmgr.dll) & MoveFileEx
│   │   ├── KernelMemoryTrimmer.cs  # NtSetSystemInformation Standby List & RAM Trimmer
│   │   ├── ParallelScanner.cs      # Async IOCP File Scanner (Lazy Enumeration)
│   │   ├── TransactionalRollback.cs# Snapshot & Rollback Engine (Registry & Services)
│   │   ├── DeepRegistryScanner.cs  # Deep Safe Registry Orphan Cleaner
│   │   ├── BrowserOptimizer.cs     # Chromium & Gecko SQLite Database Vacuum Engine
│   │   ├── SecureFileShredder.cs   # DoD 5220.22-M 3-Pass Secure File Shredder
│   │   ├── WindowsDebloater.cs     # Telemetry, Copilot, Bing, Widgets & UWP Debloater
│   │   ├── SystemTweaksEngine.cs   # OS Tweaks (CPU Priority, MSI Mode, TRIM, MMCSS, TimerRes, QoS)
│   │   ├── NativeInterop.cs        # Consolidated Win32 Native P/Invoke Definitions
│   │   ├── OptimaxLogger.cs        # Production Diagnostic Logger
│   │   ├── StartupOptimizer.cs     # Startup & Service Risk Assessor
│   │   ├── RealtimeMonitorDaemon.cs# Temp Junk Realtime Monitoring Daemon
│   │   └── WinApp2IniParser.cs     # WinApp2.ini Ruleset Importer & Parser
│   ├── IPC/
│   │   ├── NamedPipeServer.cs      # Secure NDJSON Streaming IPC Server (\\.\pipe\OptimaxIPC)
│   │   ├── Protocol.cs             # IPC DTOs & Streaming Chunks
│   │   └── OptimaxJsonContext.cs   # Reflection-Free JSON Source Generator
│   └── Program.cs                  # CLI Parser & IPC Dispatcher
├── Optimax.UI/                     # WPF Modern GUI Application
│   ├── IPCClient.cs                # NDJSON IPC Streaming Client
│   ├── MainWindow.xaml             # Desktop UI Layout
│   └── MainWindow.xaml.cs          # UI Logic & Event Handlers
└── Winapp2.ini                     # Community App Cleaning Ruleset
```

---

## Giấy phép

Dự án được phát hành theo giấy phép [MIT License](LICENSE).
