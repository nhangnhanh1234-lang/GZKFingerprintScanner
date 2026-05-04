namespace GZKFingerprintScanner.Helpers
{
    public static class ZkErrorCodes
    {
        public static string GetMessage(int errCode)
        {
            switch (errCode)
            {
                case 0: return "Thành công";
                case 1: return "Đã khởi tạo";
                case -1: return "Lỗi khởi tạo thư viện thuật toán";
                case -2: return "Lỗi khởi tạo thư viện capture";
                case -3: return "Chưa kết nối thiết bị";
                case -4: return "Giao diện không hỗ trợ";
                case -5: return "Tham số không hợp lệ";
                case -6: return "Không thể khởi động thiết bị";
                case -7: return "Handle không hợp lệ";
                case -8: return "Lỗi chụp hình ảnh vân tay";
                case -9: return "Lỗi trích xuất template";
                case -10: return "Đã hủy bỏ (Aborted)";
                case -11: return "Bộ nhớ không đủ";
                case -12: return "Đang trong quá trình quét vân tay";
                case -13: return "Lỗi thêm template";
                case -14: return "Lỗi xóa template";
                case -17: return "Thao tác thất bại";
                case -18: return "Đã hủy chụp";
                case -20: return "So khớp vân tay thất bại";
                case -22: return "Lỗi hợp nhất template";
                case -23: return "Thiết bị chưa được khởi động";
                case -24: return "Thiết bị chưa được khởi tạo";
                case -25: return "Thiết bị đã được kết nối rồi";
                default: return string.Format("Lỗi không xác định ({0})", errCode);
            }
        }
    }
}
