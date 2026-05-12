# Hướng Dẫn Kết Nối Deep Link & Room Management

## Tổng Quan

App đã được nâng cấp để hỗ trợ:
- **Deep Link Protocol:** `gemr://`
- **Room Prefix:** `zk_` (để phân biệt máy quét vân tay)
- **Single Instance:** Dùng Named Pipes để truyền deep link giữa các tiến trình
- **Room mặc định:** MAC Address với prefix `zk_`

---

## 1. Deep Link Protocol

### Format
```
gemr://open?room=TEN_PHONG
```

### Ví Dụ
```
# Join room mặc định (MAC Address)
gemr://open

# Join room cụ thể
gemr://open?room=PhongKham1
gemr://open?room=PhongKham2
```

### Cách Sử Dụng

#### Từ Browser/HTML
```html
<a href="gemr://open?room=PhongKham1">Mở máy quét phòng khám 1</a>
```

#### Từ JavaScript (Web Console)
```javascript
// Chuyển máy quét sang room khác
window.location.href = 'gemr://open?room=PhongKham2';
```

#### Từ Command Line
```powershell
# Mở app với deep link
GZKFingerprintScanner.exe "gemr://open?room=PhongKham1"
```

---

## 2. Room Naming Convention

### Room Mặc Định (MAC Address)
Khi app khởi động, tự động join room theo MAC Address:
```
Format: zk_<MAC_ADDRESS>
Ví dụ: zk_AABBCCDDEEFF
```

### Room Từ Deep Link
Nếu room từ deep link không có prefix `zk_`, app tự động thêm vào:
```
Deep Link: gemr://open?room=PhongKham1
Actual Room: zk_PhongKham1
```

### Web Phân Biệt Room
```javascript
// Web client nhận diện room máy quét vân tay
if (room.startsWith('zk_')) {
    console.log('Đây là room máy quét vân tay:', room);
    // Hiển thị icon fingerprint, badge riêng...
}
```

---

## 3. Luồng Kết Nối

### Lần Đầu Khởi Động (Không Deep Link)
```
1. App lấy MAC Address → zk_AABBCCDDEEFF
2. Kết nối Socket.io server  
3. Emit "join" với room = zk_AABBCCDDEEFF (room mặc định)
4. Web nhận được và hiển thị máy quét online
```

### Khởi Động Từ Deep Link (App Chưa Chạy)
```
1. User click: gemr://open?room=PhongKham1
2. Windows khởi động instance đầu tiên
3. App lưu room từ deep link (PhongKham1) vào queue
4. Kết nối Socket.io server
5. Ưu tiên join room từ deep link (zk_PhongKham1) 
6. Web nhận được máy quét ở room PhongKham1
```
**Quan trọng:** Nếu khởi động từ deep link, app sẽ join room đó thay vì MAC Address.

### Nhận Deep Link Khi App Đang Chạy (Single Instance)
```
1. User click: gemr://open?room=PhongKham1
2. Windows khởi động instance thứ hai
3. Instance 2 phát hiện app đang chạy (Global Mutex)
4. Instance 2 gửi URL qua Named Pipe → EXIT NGAY (không chạy service)
5. Instance 1 nhận URL qua pipe và xử lý:
   a. Đợi socket connected (nếu đang connecting)
   b. Emit "leave" room hiện tại
   c. Emit "join" room mới (zk_PhongKham1)
   d. Hiển thị BalloonTip thông báo
```
**Lưu ý:** Instance thứ 2 không bao giờ khởi động service. Nó chỉ gửi URL và thoát.

### Restart Service
```
1. User chọn "Khởi động lại dịch vụ" từ menu
2. Leave room hiện tại
3. Disconnect socket
4. Reconnect socket
5. Join lại room mặc định (MAC Address)
```

---

## 4. Triển Khai ClickOnce

### Bước 1: Build & Publish
```powershell
# Build bình thường, không cần Admin manifest
msbuild GZKFingerprintScanner.sln /p:Configuration=Release
```

### Bước 2: Đăng Ký Deep Link (1 lần duy nhất)
**Yêu cầu:** Administrator

**Cách 1:** Chạy app 1 lần với quyền Admin
```powershell
# Nếu có quyền admin, app tự đăng ký
Run as Administrator → GZKFingerprintScanner.exe
```

**Cách 2:** Import Registry file
```powershell
# Sửa đường dẫn EXE trong file RegisterDeepLink.reg cho đúng với ClickOnce
# Sau đó import:
reg import RegisterDeepLink.reg
```

**Nội dung Registry:**
```reg
Windows Registry Editor Version 5.00

[HKEY_LOCAL_MACHINE\SOFTWARE\Classes\gemr]
@="URL:gemr Protocol"
"URL Protocol"=""

[HKEY_LOCAL_MACHINE\SOFTWARE\Classes\gemr\shell\open\command]
@="\"C:\\Path\\To\\GZKFingerprintScanner.exe\" \"%1\""
```

### Bước 3: Sử Dụng
```powershell
# Test deep link
gemr://open?room=PhongKham1
```

---

## 5. Menu Tray Icon

### Copy Room
```
1. Click tray icon
2. Chọn "📋 Copy Room" hoặc click vào dòng Room trong STATUS
3. Room name (có prefix zk_) được copy vào clipboard
```

### Xem Room Hiện Tại
```
Tray Menu → STATUS
  ✓  Room  ·  zk_AABBCCDDEEFF  [click để copy]
```

---

## 6. Xử Lý Lỗi

### App Không Mở Từ Deep Link
| Nguyên Nhân | Giải Pháp |
|-------------|-----------|
| Protocol chưa đăng ký | Chạy RegisterDeepLink.reg với Admin |
| ClickOnce path thay đổi | Cập nhật Registry với đường dẫn mới |
| App đang chạy lỗi | Kill process, restart app |

### Web Không Nhận Được Room
| Nguyên Nhân | Giải Pháp |
|-------------|-----------|
| Room không có prefix zk_ | Kiểm tra app đã join đúng chưa |
| Socket disconnect | Kiểm tra network, restart service |
| Server không emit joined | Kiểm tra server-side event handling |

---

## 7. API Reference (Client → Server)

### Emit Events
```javascript
// Join room
socket.emit('join', 'zk_AABBCCDDEEFF');

// Join room với metadata (nếu dùng cách 2)
socket.emit('join', {
    room: 'zk_AABBCCDDEEFF',
    type: 'fingerprint_scanner',
    device: 'ZKTeco'
});

// Leave room
socket.emit('leave', 'zk_AABBCCDDEEFF');

// Send message
socket.emit('message', {
    type: 'fingerprint',
    data: templateData
});
```

### Listen Events
```javascript
// Nhận khi có client join room
socket.on('joined', (room) => {
    console.log('Joined room:', room);
});

// Nhận message từ máy quét
socket.on('message', (data) => {
    if (data.type === 'fingerprint') {
        // Xử lý vân tay
    }
});
```

---

## 8. Thay Đổi Từ Phiên Bản Cũ

| Cũ | Mới |
|----|-----|
| `leocare://` | `gemr://` |
| Room = `kyvantay` | Room = `zk_<MAC>` |
| Không phân biệt loại room | Prefix `zk_` phân biệt máy quét |
| Có thể chạy nhiều instance | Single instance với Named Pipes |

---

## 9. Kiểm Tra Nhanh

```powershell
# 1. Kiểm tra protocol đã đăng ký chưa
reg query "HKLM\SOFTWARE\Classes\gemr"

# 2. Test deep link từ command line
start gemr://open?room=TestRoom

# 3. Kiểm tra app đang chạy
tasklist | findstr GZKFingerprintScanner

# 4. Xem log
cat "%LOCALAPPDATA%\GZKFingerprintScanner\logs\app.log"
```

---

**Cập nhật:** 2024
**Phiên bản:** 2.0 (Deep Link + Single Instance)
