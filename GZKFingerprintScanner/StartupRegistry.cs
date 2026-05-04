using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace GZKFingerprintScanner
{
    /// <summary>
    /// Đăng ký/huỷ đăng ký ứng dụng tự khởi động cùng Windows
    /// thông qua HKCU\Software\Microsoft\Windows\CurrentVersion\Run.
    /// Dùng HKCU nên KHÔNG cần quyền admin.
    /// </summary>
    internal static class StartupRegistry
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValName = "GZKFingerprintScanner";

        private static string ExePath
        {
            get
            {
                var module = Process.GetCurrentProcess().MainModule;
                return module != null ? module.FileName : string.Empty;
            }
        }

        public static bool IsEnabled()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false))
                {
                    var existing = key.GetValue(ValName) as string;
                    return !string.IsNullOrEmpty(existing)
                        && string.Equals(Path.GetFullPath(existing.Trim('"')),
                                         Path.GetFullPath(ExePath),
                                         StringComparison.OrdinalIgnoreCase);
                }
            }
            catch { return false; }
        }

        public static void SetEnabled(bool enabled)
        {
            using (var key = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (enabled)
                    key.SetValue(ValName, string.Format("\"{0}\"", ExePath), RegistryValueKind.String);
                else if (key.GetValue(ValName) != null)
                    key.DeleteValue(ValName, false);
            }
        }
    }
}
