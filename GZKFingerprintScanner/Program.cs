using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Windows.Forms;
using GZKFingerprintScanner.Helpers;

namespace GZKFingerprintScanner
{
    internal static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            // Lấy deep link từ command line arguments
            string deepLinkUrl = args != null && args.Length > 0 ? args[0] : null;

            // Kiểm tra xem có phải deep link không
            bool isDeepLink = !string.IsNullOrEmpty(deepLinkUrl) && deepLinkUrl.StartsWith("gemr://", StringComparison.OrdinalIgnoreCase);

            // Single Instance Manager với Named Pipes
            using (var singleInstance = new SingleInstanceManager())
            {
                bool isFirstInstance = singleInstance.Initialize();
                
                // Debug log
                System.Diagnostics.Debug.WriteLine($"[INSTANCE] isFirstInstance={isFirstInstance}, deepLink={deepLinkUrl}");

                if (!isFirstInstance)
                {
                    // Tiến trình thứ hai: gửi deep link (nếu có) và thoát NGAY
                    if (isDeepLink)
                    {
                        bool sent = SingleInstanceManager.SendToRunningInstance(deepLinkUrl);
                        System.Diagnostics.Debug.WriteLine($"[INSTANCE] Sent to running instance: {sent}");
                    }
                    else
                    {
                        MessageBox.Show(
                            "GZKFingerprintScanner đang chạy ở system tray.",
                            "GZKFingerprintScanner", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    // EXIT NGAY - không chạy gì thêm
                    return;
                }
                
                System.Diagnostics.Debug.WriteLine("[INSTANCE] This is FIRST instance, starting service...");

                // Tiến trình đầu tiên: đăng ký deep link protocol nếu chưa có
                // Lưu ý: ClickOnce không hỗ trợ requireAdministrator nên việc đăng ký
                // có thể thất bại nếu không có quyền Admin. Đăng ký thủ công qua .reg file.
                try
                {
                    if (!DeepLinkRegistry.IsRegistered())
                    {
                        DeepLinkRegistry.Register();
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    // ClickOnce chạy không có quyền Admin - bỏ qua
                    // Người dùng cần đăng ký thủ công qua RegisterDeepLink.reg
                }
                catch (Exception ex)
                {
                    // Ghi log nhưng không crash - app vẫn hoạt động
                    LogAndNotify(ex, "DeepLink registration");
                }

                // Bật TLS 1.2/1.3 cho .NET 4.7.2 (mặc định chỉ TLS 1.0/1.1).
                // Cần thiết cho HTTPS/WSS server hiện đại (vd: gemr-socket.emed.vn).
                try
                {
                    ServicePointManager.SecurityProtocol =
                        SecurityProtocolType.Tls12 |
                        (SecurityProtocolType)3072 | // Tls12 (compatibility)
                        (SecurityProtocolType)12288; // Tls13 (nếu OS hỗ trợ)
                }
                catch
                {
                    // Fallback nếu OS cũ không hỗ trợ Tls13
                    try { ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12; } catch { }
                }

                // Global exception handlers - đừng để app chết ngầm
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += (sender, e) => LogAndNotify(e.Exception, "UI thread");
                AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
                    LogAndNotify(e.ExceptionObject as Exception, "background");

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                // DOUBLE CHECK: Đảm bảo chỉ first instance mới tạo TrayApplicationContext
                // Nếu có cờ hiệu instance khác đang chạy, thoát ngay
                if (!isFirstInstance)
                {
                    System.Diagnostics.Debug.WriteLine("[INSTANCE] GUARD: Secondary instance detected, exiting!");
                    return;
                }

                // Use TrayApplicationContext instead of Form1 for tray-only app
                var context = new TrayApplicationContext();

                // Đăng ký event nhận deep link từ tiến trình khác
                singleInstance.DeepLinkReceived += url => context.ProcessDeepLink(url);

                // Xử lý deep link khởi động (nếu có)
                if (isDeepLink)
                {
                    context.ProcessDeepLink(deepLinkUrl);
                }

                Application.Run(context);
            }
        }

        private static void LogAndNotify(Exception ex, string source)
        {
            if (ex == null) return;
            try
            {
                // Use LocalApplicationData to avoid requiring admin rights
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "GZKFingerprintScanner",
                    "logs");
                Directory.CreateDirectory(dir);
                File.AppendAllText(
                    Path.Combine(dir, string.Format("crash-{0:yyyyMMdd}.log", DateTime.Now)),
                    string.Format("[{0:HH:mm:ss}] [{1}] {2}{3}{3}", DateTime.Now, source, ex, Environment.NewLine));
            }
            catch { }
        }
    }
}
