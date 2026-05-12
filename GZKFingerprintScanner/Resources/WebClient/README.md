# ZK Teco Web Test Console

Giao diện HTML đơn file dùng để test `ZK_TecoApp` Worker Service.

## Cách dùng

1. Khởi động **Socket.IO server** của bạn ở `http://127.0.0.1:8023` (phải bật
   broadcast cho tất cả client — web client và service phải "nhìn thấy" event
   của nhau).
2. Chạy `ZK_TecoApp` (Worker Service). Nó sẽ auto-connect socket và mở thiết bị.
3. Mở `WebClient/index.html` trực tiếp trong trình duyệt (double click).
4. Nhập URL + group + clientId (mặc định đã đúng) → bấm **Connect socket**.
5. Bấm **Kết nối máy quét** để gửi action `connect` (hoặc chỉ đợi service auto
   start).
6. Đặt ngón tay lên máy quét → ảnh + template hiển thị real-time.
7. Để test **Verify 1:1**:
   - Quét 1 lần, bấm **Dùng template vừa quét** (sao chép từ output sang input
     Target).
   - Bấm **Bật chế độ xác thực** → service chuyển sang Verify Mode.
   - Quét lại ngón tay → nhận `verify-result` với `isMatch` + `score`.
   - Bấm **Tắt xác thực** để quay lại Scan Mode.

## Lưu ý

- File HTML độc lập, không cần build, không cần web server. Mở bằng trình duyệt
  là chạy.
- Nếu trình duyệt chặn `file://` truy cập `http://` do mixed-content, serve nó
  qua: `python -m http.server 8080` rồi mở `http://localhost:8080/`.
- Socket.IO client dùng v4.7.5 từ CDN (tương thích với `SocketIOClient` 4.0.3
  của service).
