using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Windows.Forms;

namespace GZKFingerprintScanner
{
    internal static class Program
    {
        /// <summary>Single-instance mutex key.</summary>
        private const string MutexKey = @"Global\GZKFingerprintScanner_SingleInstance_v1";

        [STAThread]
        static void Main()
        {
            // Single-instance lock: tránh chạy 2 lần (double-click icon, autostart trùng...)
            using (var mutex = new Mutex(true, MutexKey, out bool createdNew))
            {
                if (!createdNew)
                {
                    MessageBox.Show(
                        "GZKFingerprintScanner đang chạy ở system tray.",
                        "GZKFingerprintScanner", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
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

                // Use TrayApplicationContext instead of Form1 for tray-only app
                Application.Run(new TrayApplicationContext());
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
