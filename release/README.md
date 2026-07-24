# 🚀 OPTIMAX - Enterprise-Grade Production Native Windows Optimizer

![Platform Windows](https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011-blue.svg?style=for-the-badge&logo=windows)
![Core Engine](https://img.shields.io/badge/Engine-.NET%20Native%20AOT-purple.svg?style=for-the-badge&logo=dotnet)
![Architecture](https://img.shields.io/badge/Architecture-Win32%20%7C%20RestartManager%20%7C%20IPC-brightgreen.svg?style=for-the-badge)
![License](https://img.shields.io/badge/License-MIT-green.svg?style=for-the-badge)

**OPTIMAX Native** là giải pháp tối ưu hóa hiệu năng và dọn dẹp hệ thống Windows thế hệ mới, được phát triển lại hoàn toàn dưới dạng **Native Executable (.NET Native AOT / Win32)**. Công cụ có tốc độ khởi động cực nhanh (< 10ms), không phụ thuộc vào PowerShell hay Node.js runtime, và tuân thủ 100% **Nguyên Tắc An Toàn Hệ Thống (Zero-Risk Architecture)** của Microsoft Windows.

---

## 🛡️ Kiến Trúc An Toàn Tuyệt Đối (Zero-Risk Architecture)

1. **Chế Độ Mô Phỏng Sau Quét (`--dry-run`):**
   - Quét bất đồng bộ đa luồng (IOCP / ThreadPool) và xuất báo cáo JSON chi tiết (danh sách tệp tin, dung lượng dự kiến giải phóng, đánh giá mức độ rủi ro `Low` / `Medium`) trước khi thực hiện dọn dẹp.
2. **Khôi Phục Trạng Thái 1-Click (Transactional Rollback Engine):**
   - Tự động chụp Snapshot trạng thái Registry & Windows Services vào `%ProgramData%\Optimax\Backups\<backup-id>` trước khi can thiệp. Khôi phục nguyên trạng hệ thống trong vài giây bằng câu lệnh `Optimax.exe --rollback <backup-id>`.
3. **Kiểm Tra Khóa Tệp Chuẩn Windows Restart Manager API:**
   - Tuyệt đối **KHÔNG** sử dụng ép dừng tiến trình (`Stop-Process -Force`). Sử dụng Win32 Restart Manager API (`rstrtmgr.dll`) để phát hiện chính xác ứng dụng/tiến trình đang khóa tệp.
   - Nếu tệp bị khóa bởi dịch vụ hệ thống critical, tự động đăng ký xóa an toàn khi reboot bằng API `MoveFileExW` (`MOVEFILE_DELAY_UNTIL_REBOOT`).
4. **Dynamic Rule Engine (Globbing & Regex):**
   - Đọc quy tắc dọn dẹp từ `rules/custom_rules.json`, kiểm tra khoảng Build Number OS và phiên bản phần mềm (Product Version) trước khi thực thi quy tắc.

---

## ⚡ Các Phân Hệ Native Core (`Optimax.Native`)

- **Safety & Lock Inspection Engine ([`SafetyEngine.cs`](file:///d:/optimize/Optimax.Native/Core/SafetyEngine.cs)):** Tương tác trực tiếp Win32 Native API `rstrtmgr.dll` và `MoveFileExW`, kiểm tra tính khả dụng của ổ đĩa cục bộ (`DriveInfo.IsReady`).
- **Parallel Scanning Engine ([`ParallelScanner.cs`](file:///d:/optimize/Optimax.Native/Core/ParallelScanner.cs)):** Quét đĩa đa luồng bất đồng bộ dựa trên IOCP ThreadPool.
- **Transactional State Engine ([`TransactionalRollback.cs`](file:///d:/optimize/Optimax.Native/Core/TransactionalRollback.cs)):** Lưu trữ và phục hồi trạng thái cài đặt Registry và dịch vụ.
- **Dynamic Rule Parser ([`DynamicRuleParser.cs`](file:///d:/optimize/Optimax.Native/Core/DynamicRuleParser.cs)):** Hỗ trợ Deep Globbing, biểu thức chính quy (Regex) loại trừ và đánh giá phiên bản.
- **Named Pipe IPC Server ([`NamedPipeServer.cs`](file:///d:/optimize/Optimax.Native/IPC/NamedPipeServer.cs)):** Giao tiếp IPC bảo mật qua `\\.\pipe\OptimaxIPC` (Chỉ cấp quyền cho System & Administrator).

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
# 1. Chạy Mô Phỏng Quét Hệ Thống (Dry-Run Mode - KHÔNG xóa tệp, xuất JSON báo cáo):
.\Optimax.exe --dry-run

# 2. Chạy Quét & Dọn Dẹp Thực Sự:
.\Optimax.exe --scan

# 3. Phục Hồi Trạng Thái Hệ Thống Nguyên Trạng (1-Click Rollback):
.\Optimax.exe --rollback <backup-id>

# 4. Khởi Động Named Pipe IPC Service (Cho Giao diện UI/Dashboard kết nối):
.\Optimax.exe --ipc-service
```

---

### 💻 Cách 2: Biên Dịch Native AOT (AOT Compilation Guide)

Dự án hỗ trợ biên dịch Native AOT tạo tệp thực thi siêu nhỏ gọn, khởi động < 10ms:

```powershell
# Biên dịch Native AOT tệp Optimax.exe tại bin\Release:
dotnet publish d:\optimize\Optimax.Native\Optimax.csproj -c Release -r win-x64 /p:PublishAot=true
```

---

### 💻 Cách 3: Khởi chạy Giao diện Desktop WPF (`Optimax.UI`)

1. Biên dịch ứng dụng UI: `dotnet build Optimax.UI/Optimax.UI.csproj -c Release`
2. Mở `Optimax.UI.exe` để điều khiển Native Engine qua kết nối bảo mật NamedPipe IPC (`\\.\pipe\OptimaxIPC`).

---

## 📦 Cấu Trúc Mã Nguồn Dự Án (Project Structure)

```
d:\optimize\
├── Optimax.Native/                 # Native C# .NET Native AOT Core Engine
│   ├── Core/
│   │   ├── SafetyEngine.cs         # Win32 Restart Manager API (rstrtmgr.dll) & MoveFileEx
│   │   ├── KernelMemoryTrimmer.cs  # Adaptive System Standby Purge & Idle RAM Trimmer
│   │   ├── ParallelScanner.cs      # Async IOCP File & Directory Scanner
│   │   ├── TransactionalRollback.cs# Registry & Service Snapshot & Rollback Engine
│   │   └── DynamicRuleParser.cs    # Globbing & Regex Rules Engine
│   ├── IPC/
│   │   ├── NamedPipeServer.cs      # Secure NDJSON Streaming IPC Server (\\.\pipe\OptimaxIPC)
│   │   ├── Protocol.cs             # IPC DTOs & Streaming Chunks
│   │   └── OptimaxJsonContext.cs   # Reflection-Free JSON Source Generator
│   └── Program.cs                  # CLI Parser & IPC Dispatcher
├── Optimax.UI/                     # Desktop WPF Native GUI App
│   ├── IPCClient.cs                # NDJSON IPC Streaming Client
│   └── MainWindow.xaml             # WPF Modern Desktop UI
└── Optimax.ps1                     # Script PowerShell CLI Engine
```

---

## 📄 Giấy phép (License)
Dự án được phát hành dưới giấy phép [MIT License](LICENSE).
