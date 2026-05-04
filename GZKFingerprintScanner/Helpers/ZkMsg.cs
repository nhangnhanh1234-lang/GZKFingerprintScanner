namespace GZKFingerprintScanner.Helpers
{
    public static class ZkMsg
    {
        public const string InitComplete = "Khởi tạo hoàn tất.";
        public const string NotConnected = "Chưa kết nối thiết bị!";
        public const string DeviceStarted = "Máy vân tay đã sẵn sàng!";
        public const string PressFinger = "Vui lòng đặt ngón tay lên máy quét...";

        public const string MatchSuccess = "Xác thực thành công. Điểm: ";
        public const string MatchFailed = "Xác thực thất bại. Điểm: ";

        public const string ErrorExtract = "Lỗi trích xuất mẫu vân tay.";
        public const string Disconnected = "Đã ngắt kết nối với thiết bị.";
    }
}
