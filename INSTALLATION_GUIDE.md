# Hướng Dẫn Cài Đặt & Sử Dụng GZKFingerprintScanner

## 📋 Mục Lục

1. [Yêu Cầu Hệ Thống](#yêu-cầu-hệ-thống)
2. [Bước 1: Cài Driver Thiết Bị](#bước-1-cài-driver-thiết-bị)
3. [Bước 2: Cài Phần Mềm](#bước-2-cài-phần-mềm)
4. [Bước 3: Sử Dụng](#bước-3-sử-dụng)
5. [Xử Lý Sự Cố](#xử-lý-s-cố)

---

## Yêu Cầu Hệ Thống

| Thành phần | Yêu cầu tối thiểu |
|------------|-------------------|
| **Hệ điều hành** | Windows 7 SP1 / 8.1 / 10 / 11 (32-bit hoặc 64-bit) |
| **.NET Framework** | 4.7.2 hoặc cao hơn |
| **RAM** | 512 MB |
| **Ổ đĩa** | 50 MB dung lượng trống |
| **USB** | 1 cổng USB 2.0 trở lên |
| **Thiết bị** | Máy quét vân tay ZKTeco (SLK20R, ZK4500, ZK9500, v.v.) |

---

## Bước 1: Cài Driver Thiết Bị

> ⚠️ **QUAN TRỌNG**: Phải cài driver TRƯỚC khi cài phần mềm. Không cài driver sẽ không nhận được máy quét.

### 1.1 Tìm Driver Phù Hợp

Tùy theo model máy quét của bạn:

| Model | File Driver | Vị trí |
|-------|-------------|--------|
| SLK20R | `SLK20R_Driver.exe` | Trong thư mục `Drivers/SLK20R/` |
| ZK4500 | `ZK4500_Driver.exe` | Trong thư mục `Drivers/ZK4500/` |
| ZK9500 | `ZK9500_Driver.exe` | Trong thư mục `Drivers/ZK9500/` |

### 1.2 Cài Đặt Driver

1. **Không cắm máy quét vào máy tính** (nếu đã cắm, rút ra)
2. Chạy file `Driver.exe` tương ứng với model của bạn
3. Nhấn **Next** → **Install** → đợi cài đặt hoàn tất
4. Nhấn **Finish** khi hoàn thành
5. **Khởi động lại máy tính** (khuyến nghị)

### 1.3 Kiểm Tra Driver

Sau khi khởi động lại:

1. Cắm máy quét vân tay vào cổng USB
2. Mở **Device Manager** (Win + X → Device Manager)
3. Tìm mục **"Biometric devices"** hoặc **"Universal Serial Bus devices"**
4. Kiểm tra thiết bị có hiện lên không có dấu `!` màu vàng

> ✅ **Nếu thấy thiết bị không có lỗi** → Driver đã cài thành công
> 
> ❌ **Nếu thấy dấu `!` màu vàng** → Driver chưa cài đúng, cần cài lại

---

## Bước 2: Cài Phần Mềm

### 2.1 Cài Visual C++ Redistributable (Nếu chưa có)

Phần mềm cần Visual C++ 2010 Redistributable để chạy:

1. Tải từ Microsoft: [vcredist_x86.exe](https://www.microsoft.com/en-us/download/details.aspx?id=26999)
2. Chạy file → **Install**
3. Khởi động lại máy (nếu yêu cầu)

### 2.2 Cài GZKFingerprintScanner

1. Chạy file `GZKFingerprintScanner-Setup.msi` (hoặc `setup.exe`)
2. Nhấn **Next** để tiếp tục
3. Chọn thư mục cài đặt (mặc định: `C:\Program Files (x86)\Gtel\GZKFingerprintScannerApp\`)
4. Nhấn **Install**
5. Đợi cài đặt hoàn tất → nhấn **Finish**

### 2.3 Tùy Chọn Khởi Động Cùng Windows

Trong quá trình cài đặt hoặc sau khi cài:

1. Click chuột phải vào icon GZKFingerprintScanner ở **System Tray** (góc dưới phải màn hình)
2. Chọn **⚙ Khởi động cùng Windows** để tự động chạy khi mở máy

---

## Bước 3: Sử Dụng

### 3.1 Khởi Động Phần Mềm

Sau khi cài đặt, phần mềm tự động chạy và hiện icon ở **System Tray**:

![Tray Icon](docs/tray-icon.png)

> Nếu không thấy icon, tìm trong mục ẩn của System Tray (mũi tên lên)

### 3.2 Kiểm Tra Trạng Thái

Click chuột phải vào icon để xem menu:

| Màu/Icon | Ý nghĩa |
|----------|---------|
| 🟢 **● Đang hoạt động** | Mọi thứ hoạt động bình thường (Socket + Device OK) |
| 🟠 **◐ Chỉ có Socket/Device** | Một phần hoạt động, một phần lỗi |
| 🔴 **● Offline** | Cả Socket và Device đều lỗi |
| ⚪ **○ Chưa khởi động** | Service đang khởi động |

### 3.3 Các Chức Năng Chính

#### 🌐 Mở Web Console
- Xem log và quản lý thiết bị qua giao diện web
- Truy cập tại: http://localhost hoặc mở qua menu

#### ↻ Khởi Động Lại Dịch Vụ
- Khi gặp lỗi kết nối, thử restart service
- Giữ nguyên kết nối thiết bị

#### 📁 Mở Thư Mục Cài Đặt
- Truy cập nhanh thư mục chứa phần mềm
- Hữu ích khi cần kiểm tra file cấu hình

#### 📜 Mở Thư Mục Logs
- Xem file log để debug lỗi
- File log được lưu tại: `%LOCALAPPDATA%\GZKFingerprintScanner\logs\`

---

## Xử Lý Sự Cố

### ❌ Không Nhận Thiết Bị (Device: ✗)

| Nguyên nhân | Cách khắc phục |
|-------------|----------------|
| Chưa cài driver | Cài driver theo [Bước 1](#bước-1-cài-driver-thiết-bị) |
| Thiết bị chưa cắm | Kiểm tra kết nối USB |
| Driver lỗi | Gỡ driver cũ → cài lại |
| Cổng USB lỗi | Thử cổng USB khác |

### ❌ Không Kết Nối Socket (Socket: ✗)

| Nguyên nhân | Cách khắc phục |
|-------------|----------------|
| Không có internet | Kiểm tra kết nối mạng |
| Firewall chặn | Thêm exception cho GZKFingerprintScanner |
| URL server sai | Kiểm tra cấu hình server |
| TLS lỗi (Win 7/8) | Cài VC++ Redistributable |

### ❌ Cả Device và Socket đều lỗi

1. **Kiểm tra file log**: Mở `%LOCALAPPDATA%\GZKFingerprintScanner\logs\`
2. **Restart service**: Click chuột phải → ↻ Khởi động lại dịch vụ
3. **Uninstall & Cài lại**: Gỡ phần mềm → cài lại từ đầu

### 📋 Thông Tin Debug

Khi báo lỗi cho admin, cung cấp:

1. File log gần nhất: `%LOCALAPPDATA%\GZKFingerprintScanner\logs\app-YYYYMMdd.log`
2. Model máy quét
3. Phiên bản Windows
4. Ảnh chụp menu tray (click chuột phải vào icon)

---

## 📞 Hỗ Trợ

- **Email**: support@gtel.vn
- **Hotline**: 1900-xxxx
- **Docs**: https://github.com/nhangnhanh1234-lang/zk_fingerprint_config

---

**Phiên bản**: 1.0.0  
**Cập nhật**: 05/2026
