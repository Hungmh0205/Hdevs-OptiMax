# 🚀 OPTIMAX - Extreme Windows System Optimizer & Performance Tuning

![Windows 10/11 Support](https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011-blue.svg?style=for-the-badge&logo=windows)
![PowerShell](https://img.shields.io/badge/Engine-PowerShell%205.1%2B-blueviolet.svg?style=for-the-badge&logo=powershell)
![Node.js Backend](https://img.shields.io/badge/Dashboard-Node.js%20Server-brightgreen.svg?style=for-the-badge&logo=nodedotjs)
![License](https://img.shields.io/badge/License-MIT-green.svg?style=for-the-badge)

**OPTIMAX** là bộ công cụ tối ưu hóa hiệu năng hệ thống Windows toàn diện, giải phóng bộ nhớ RAM, tăng tốc CPU Kernel, giảm latency mạng/game, dọn dẹp dung lượng SSD khổng lồ và điều khiển trực quan qua **Giao diện Web Real-Time Dashboard** cho Máy trạm (Workstation) & Gaming PC.

---

## 📸 Giao diện Dashboard Web

- **Real-Time Monitoring:** Theo dõi % tải CPU, RAM rảnh/dùng, Tốc độ mạng Tải về/Tải lên và dung lượng ổ C: theo thời gian thực.
- **Live Stream Logs:** Hiển thị trực tiếp tiến trình chạy của Optimax PowerShell Engine thông qua Server-Sent Events (SSE).
- **1-Click Execution:** Kích hoạt từng phân hệ tối ưu lẻ hoặc 1-Click tối ưu triệt để 100%.

---

## ⚡ Các Phân Hệ Tối Ưu Chính (Feature Modules)

### 🧠 1. Tối ưu Bộ nhớ RAM & Latency
* **Win32 API EmptyWorkingSet:** Sử dụng DLL `psapi.dll` cưỡng ép thu hồi WorkingSet RAM thừa từ tất cả tiến trình.
* **Standby Memory & Garbage Collector:** Dọn dẹp RAM chờ (Standby List) và gọi thu gom rác bộ nhớ `[GC]::Collect()`.
* **Disable Executive Paging:** Giữ Kernel & Drivers cố định trên RAM vật lý, loại bỏ độ trễ Paging trên ổ đĩa.
* **Memory Compression Optimization:** Tinh chỉnh tính năng nén RAM cho game và đồ họa nặng.

### ⚡ 2. Tối ưu CPU, Kernel Priority & Điện năng
* **Win32PrioritySeparation (0x26 / 38):** Cấp ưu tiên chu kỳ CPU cao nhất cho ứng dụng/game mặt trước (Foreground App), giảm micro-stuttering và giật khung hình.
* **CPU Core Unparking:** Mở khóa toàn bộ các nhân/luồng CPU vật lý, ngăn Windows đưa nhân phụ vào chế độ ngủ.
* **PowerThrottling OFF:** Vô hiệu hóa chính sách thắt chặt điện năng CPU.
* **Ultimate Performance Power Plan:** Kích hoạt gói điện năng ẩn hiệu năng cao nhất của Windows.

### 🎮 3. Giảm Độ Trễ Gaming & Xử Lý Đồ Họa (Low-Latency)
* **PCIe Device MSI Mode:** Chuyển ngắt IRQ của GPU và Card Mạng sang *Message Signaled Interrupts*, giảm DPC Latency spikes.
* **Tắt Multi-Plane Overlay (MPO):** Tắt MPO trong DWM Registry, sửa triệt để lỗi chớp màn hình, xé hình khi vừa chơi game vừa xem video/trình duyệt.
* **Tắt thuật toán Nagle (`TcpAckFrequency = 1`, `TCPNoDelay = 1`):** Gửi gói tin mạng lập tức không chờ gom bus, giảm Ping trong game online.
* **Socket Recycling Fast Turnaround:** Đặt `MaxUserPort = 65534` và `TcpTimedWaitDelay = 30` tăng tốc độ tái sử dụng cổng kết nối gấp 4 lần.

### 💾 4. Thu hồi Dung lượng Ổ cứng SSD/NVMe (15GB - 60GB+)
* **Tắt Hibernation (`powercfg /h off`):** Xóa `hiberfil.sys`, thu hồi thực tế dung lượng SSD bằng 100% dung lượng RAM (16GB - 32GB).
* **Dọn dẹp WinSxS (`DISM`):** Xóa các bản cập nhật Windows cũ tích tụ, giải phóng 5GB - 15GB.
* **Xóa Shadow Copies cũ (`vssadmin`):** Thu hồi 10GB - 50GB dung lượng System Restore đã tích tụ.
* **TRIM Đa ổ SSD:** Tự động nhận diện và ReTrim toàn bộ các ổ đĩa SSD có trong máy (`C:`, `D:`, `E:`...).
* **NTFS Disk I/O Acceleration:** Tắt tạo tên 8.3 (`disable8dot3`) và tắt cập nhật thời gian truy cập cuối (`disablelastaccess`).
* **Deep Clean Caches:** Xóa GPU Shader Cache (NVIDIA, AMD, DirectX), Browser Cache (Chrome, Edge, Firefox, Brave), Event Logs, Temp, WER Logs.

### 🛡️ 5. Bảo mật, Quyền riêng tư & Debloat
* **Vô hiệu hóa Vĩnh viễn Dịch vụ Nặng (`Service Hardening`):** Đưa `SysMain` (Superfetch), `DiagTrack` (Telemetry), `WSearch` về trạng thái `Disabled` vĩnh viễn.
* **Gỡ UWP Bloatware & Telemetry Tasks:** Gỡ ứng dụng rác tích hợp (CandyCrush, News, Weather...) và vô hiệu hóa các Scheduled Tasks theo dõi ngầm.
* **Tắt Bing Search trong Start Menu & Telemetry Policy.**

### 🔄 6. Safety Backup & Khôi phục Mặc định
* **Tự tạo System Restore Point:** Tự động tạo điểm khôi phục `Optimax_PreOptimization` trước khi tối ưu.
* **1-Click Revert (Undo):** Phục hồi toàn bộ cài đặt Registry, Services, TCP Stack về mặc định ban đầu của Windows.

---

## 🛠️ Hướng dẫn Cài đặt & Sử dụng

### 📋 Yêu cầu Hệ thống
- **Hệ điều hành:** Windows 10 / Windows 11 (64-bit).
- **Node.js:** Phiên bản 16.x trở lên (Dùng cho Giao diện Web Dashboard).
- **Quyền hạn:** Quản trị viên (Administrator).

### 🚀 Cách 1: Sử dụng Giao diện Web Dashboard (Khuyên dùng)
1. Tải bản mã nguồn hoặc bản Release Zip về máy.
2. Nhấp chuột phải vào tệp **`Start-ServerAsAdmin.bat`** -> Chọn **Run as administrator**.
3. Mở trình duyệt web và truy cập địa chỉ: `http://localhost:3000`
4. Bấm nút trên Dashboard để thực thi tối ưu.

### 💻 Cách 2: Sử dụng dòng lệnh PowerShell (CLI Mode)
Mở **PowerShell (Run as Administrator)** tại thư mục dự án và chạy các lệnh:

```powershell
# Kiểm tra chỉ số hệ thống (Audit Check)
powershell -ExecutionPolicy Bypass -File .\Optimax.ps1 -Check

# Chạy Tối ưu Triệt để EXTREME 100% (1-Click All-In-One)
powershell -ExecutionPolicy Bypass -File .\Optimax.ps1 -Extreme

# Khôi phục cài đặt mặc định ban đầu của Windows (Revert / Undo)
powershell -ExecutionPolicy Bypass -File .\Optimax.ps1 -Revert
```

---

## 📦 Cách Đóng gói & Tạo Bản Release (GitHub Release Guide)

Dự án có sẵn script tự động đóng gói release tệp nén Zip để tải lên GitHub Releases:

1. Chạy script đóng gói trong PowerShell:
   ```powershell
   powershell -ExecutionPolicy Bypass -File .\Build-Release.ps1
   ```
2. Tệp release sẽ được tạo tự động tại thư mục dự án: `Optimax-v2.0.0.zip`.
3. **Đăng lên GitHub:**
   - Đẩy mã nguồn lên repository GitHub của bạn (`git push origin main`).
   - Tạo mới một Tag (Ví dụ: `v2.0.0`).
   - Vào mục **Releases** trên GitHub -> Bấm **Draft a new release**.
   - Tải tệp `Optimax-v2.0.0.zip` lên mục **Attach binaries / Assets** và bấm **Publish release**.

---

## 📄 Giấy phép (License)
Dự án được phát hành dưới giấy phép [MIT License](LICENSE).
