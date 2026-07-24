# 🚀 OPTIMAX - Enterprise-Grade Production Native Windows Optimizer

![Platform Windows](https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011-blue.svg?style=for-the-badge&logo=windows)
![Core Engine](https://img.shields.io/badge/Engine-.NET%20Native%20AOT-purple.svg?style=for-the-badge&logo=dotnet)
![Architecture](https://img.shields.io/badge/Architecture-Win32%20%7C%20RestartManager%20%7C%20IPC-brightgreen.svg?style=for-the-badge)
![License](https://img.shields.io/badge/License-MIT-green.svg?style=for-the-badge)

**OPTIMAX Native** là giải pháp tối ưu hóa hiệu năng và dọn dẹp hệ thống Windows thế hệ mới, được phát triển dưới dạng **Native Executable (.NET Native AOT / Win32)** và giao diện **Desktop WPF App (`Optimax.UI`)**. Công cụ có tốc độ khởi động siêu nhanh (< 10ms), tiêu thụ ít tài nguyên RAM (~15 - 30 MB), tuyệt đối không phụ thuộc vào PowerShell hay Node.js runtime, và tuân thủ 100% **Nguyên Tắc An Toàn Hệ Thống (Zero-Risk Architecture)** của Microsoft Windows.

---

## 🛡️ Kiến Trúc An Toàn Tuyệt Đối (Zero-Risk Architecture)

1. **Chế Độ Mô Phỏng Sau Quét (`--dry-run`):**
   - Quét bất đồng bộ đa luồng (IOCP / ThreadPool) và xuất báo cáo JSON chi tiết (danh sách tệp tin, dung lượng dự kiến giải phóng, đánh giá mức độ rủi ro `Low` / `Medium` / `High`) trước khi thực hiện dọn dẹp thực sự.
2. **Khôi Phục Trạng Thái 1-Click (Transactional Rollback Engine):**
   - Tự động chụp Snapshot trạng thái Registry & Dịch vụ Windows (Services StartMode) vào `%ProgramData%\Optimax\Backups\<backup-id>` trước khi can thiệp. Khôi phục nguyên trạng hệ thống trong vài giây bằng câu lệnh `Optimax.exe --rollback <backup-id>` hoặc qua danh sách Dropdown trên giao diện WPF.
3. **Kiểm Tra Khóa Tệp Chuẩn Windows Restart Manager API:**
   - Tuyệt đối **KHÔNG** ép dừng tiến trình nguy hiểm (`Stop-Process -Force`). Sử dụng Win32 Restart Manager API (`rstrtmgr.dll`) để phát hiện chính xác ứng dụng/tiến trình đang khóa tệp.
   - Nếu tệp bị khóa bởi dịch vụ hệ thống critical, tự động đăng ký xóa an toàn khi reboot bằng API `MoveFileExW` (`MOVEFILE_DELAY_UNTIL_REBOOT`).
4. **Dynamic Rule Engine & WinApp2.ini Importer:**
   - Hỗ trợ nạp trực tiếp bộ quy tắc dọn dẹp `WinApp2.ini` của cộng đồng (hàng ngàn ứng dụng) và quy tắc tùy biến từ `rules/custom_rules.json` với kiểm tra khoảng OS Build Number và phiên bản sản phẩm.

---

## ⚡ Các Phân Hệ Native Core (`Optimax.Native`)

- **Safety & Lock Inspection Engine ([`SafetyEngine.cs`](file:///d:/optimize/Optimax.Native/Core/SafetyEngine.cs)):** Tương tác trực tiếp Win32 Native API `rstrtmgr.dll` và `MoveFileExW`, kiểm tra tính khả dụng đĩa cục bộ.
- **Parallel Scanning Engine ([`ParallelScanner.cs`](file:///d:/optimize/Optimax.Native/Core/ParallelScanner.cs)):** Quét đĩa đa luồng bất đồng bộ dựa trên IOCP ThreadPool.
- **Transactional State Engine ([`TransactionalRollback.cs`](file:///d:/optimize/Optimax.Native/Core/TransactionalRollback.cs)):** Tạo Snapshot & Rollback nguyên trạng Registry & Windows Services.
- **Kernel Memory Trimmer ([`KernelMemoryTrimmer.cs`](file:///d:/optimize/Optimax.Native/Core/KernelMemoryTrimmer.cs)):** Giải phóng bộ nhớ RAM kernel, xả System Standby List bằng Win32 Native API `NtSetSystemInformation`.
- **SQLite Browser Optimizer ([`BrowserOptimizer.cs`](file:///d:/optimize/Optimax.Native/Core/BrowserOptimizer.cs)):** Tối ưu nén cơ sở dữ liệu SQLite (VACUUM) cho Chrome, Edge, Brave, Firefox, Opera.
- **Deep Safe Registry Cleaner ([`DeepRegistryScanner.cs`](file:///d:/optimize/Optimax.Native/Core/DeepRegistryScanner.cs)):** Quét & dọn dẹp an toàn SharedDLLs mồ côi, App Paths sai lệch, MUICache rác.
- **Secure File Shredder ([`SecureFileShredder.cs`](file:///d:/optimize/Optimax.Native/Core/SecureFileShredder.cs)):** Tiêu hủy dữ liệu an toàn theo tiêu chuẩn quân đội DoD 5220.22-M (3 lượt ghi đè), ZeroFill và RandomFill.
- **Windows Debloater Engine ([`WindowsDebloater.cs`](file:///d:/optimize/Optimax.Native/Core/WindowsDebloater.cs)):** Vô hiệu hóa Telemetry, Windows Copilot AI, Bing Search trong Start Menu, Taskbar Widgets, Advertising ID và gỡ bỏ ứng dụng UWP Bloatware rác.
- **System OS Tweaks Engine ([`SystemTweaksEngine.cs`](file:///d:/optimize/Optimax.Native/Core/SystemTweaksEngine.cs)):** Tinh chỉnh MSI Mode ngắt CPU, TRIM ổ SSD/NVMe, Global Timer Resolution (0.5ms), MMCSS Games Priority, QoS Network Ack Frequency, VSS Shadow Copies.
- **Named Pipe IPC Server ([`NamedPipeServer.cs`](file:///d:/optimize/Optimax.Native/IPC/NamedPipeServer.cs)):** Giao tiếp IPC bảo mật qua `\\.\pipe\OptimaxIPC` (NDJSON Streaming).

---

## 🛠️ Hướng Dẫn Cài Đặt & Sử Dụng

### 📋 Yêu cầu Hệ thống
- **Hệ điều hành:** Windows 10 / Windows 11 (64-bit).
- **Quyền hạn:** Quản trị viên (Administrator).
- **Yêu cầu Runtime:** KHÔNG CẦN (.NET Native AOT tự đóng gói thành 1 file `.exe` duy nhất).

---

### 🚀 Cách 1: Sử dụng Native App CLI (`Optimax.exe`)

Mở **Command Prompt / PowerShell (Run as Administrator)** tại thư mục `Optimax.Native`:

```powershell
# 1. Chạy Mô Phỏng Quét Hệ Thống (Dry-Run Mode):
.\Optimax.exe --dry-run

# 2. Chạy Quét & Dọn Dẹp Thực Sự:
.\Optimax.exe --scan

# 3. Tạo Snapshot Sao Lưu Hệ Thống Nguyên Trạng:
.\Optimax.exe --create-snapshot

# 4. Xem Danh Sách Các Mã Snapshot Đã Lưu:
.\Optimax.exe --get-backups

# 5. Phục Hồi Trạng Thái Hệ Thống (1-Click Rollback):
.\Optimax.exe --rollback <backup-id>

# 6. Xả RAM Kernel & System Standby List:
.\Optimax.exe --trim-ram

# 7. Tối Ưu SQLite Trình Duyệt:
.\Optimax.exe --clean-browser

# 8. Dọn Dẹp Registry An Toàn:
.\Optimax.exe --clean-registry

# 9. Tiêu Hủy Tệp An Toàn (DoD 5220.22-M):
.\Optimax.exe --shred "C:\Path\To\File" --shred-mode dod

# 10. Khởi Động Named Pipe IPC Service (Cho Giao diện UI kết nối):
.\Optimax.exe --ipc-service
```

---

### 💻 Cách 2: Biên Dịch Native AOT (AOT Compilation Guide)

Dự án hỗ trợ biên dịch Native AOT tạo tệp thực thi siêu nhỏ gọn:

```powershell
# Biên dịch Native AOT tệp Optimax.exe:
dotnet publish Optimax.Native/Optimax.csproj -c Release -r win-x64 /p:PublishAot=true
```

---

### 💻 Cách 3: Khởi chạy Giao diện Desktop WPF (`Optimax.UI`)

1. Biên dịch ứng dụng UI:
   ```powershell
   dotnet build Optimax.UI/Optimax.UI.csproj -c Release
   ```
2. Mở `Optimax.UI.exe` để điều khiển Native Engine qua kết nối bảo mật NamedPipe IPC (`\\.\pipe\OptimaxIPC`).

---

## 📦 Cấu Trúc Mã Nguồn Dự Án (Project Structure)

```
d:\optimize\
├── Optimax.Native/                 # Native C# .NET Native AOT Core Engine
│   ├── Core/
│   │   ├── SafetyEngine.cs         # Win32 Restart Manager API (rstrtmgr.dll) & MoveFileEx
│   │   ├── KernelMemoryTrimmer.cs  # NtSetSystemInformation Standby List & RAM Trimmer
│   │   ├── ParallelScanner.cs      # Async IOCP File & Directory Scanner
│   │   ├── TransactionalRollback.cs# Registry & Service Snapshot & Rollback Engine
│   │   ├── DeepRegistryScanner.cs  # Deep Safe Registry Orphan Cleaner
│   │   ├── BrowserOptimizer.cs     # Chromium & Firefox SQLite Database Vacuum
│   │   ├── SecureFileShredder.cs   # DoD 5220.22-M 3-Pass Secure File Shredder
│   │   ├── WindowsDebloater.cs     # Telemetry, Copilot, Bing, Widgets & UWP Debloater
│   │   ├── SystemTweaksEngine.cs   # OS Tweaks Engine (MSI, TRIM, MMCSS, TimerRes, QoS)
│   │   ├── StartupOptimizer.cs     # Startup & Service Risk Assessor & Manager
│   │   ├── RealtimeMonitorDaemon.cs# Temp Junk Realtime Monitoring Daemon
│   │   └── WinApp2IniParser.cs     # WinApp2.ini Ruleset Importer & Parser
│   ├── IPC/
│   │   ├── NamedPipeServer.cs      # Secure NDJSON Streaming IPC Server (\\.\pipe\OptimaxIPC)
│   │   ├── Protocol.cs             # IPC DTOs & Streaming Chunks
│   │   └── OptimaxJsonContext.cs   # Reflection-Free JSON Source Generator
│   └── Program.cs                  # CLI Parser & IPC Dispatcher
├── Optimax.UI/                     # Desktop WPF Native GUI App
│   ├── IPCClient.cs                # NDJSON IPC Streaming Client
│   ├── MainWindow.xaml             # WPF Modern Desktop UI Layout
│   └── MainWindow.xaml.cs          # Desktop UI Business Logic & Event Handlers
└── Winapp2.ini                     # Community App Cleaning Ruleset
```

---

## 📄 Giấy phép (License)
Dự án được phát hành dưới giấy phép [MIT License](LICENSE).
