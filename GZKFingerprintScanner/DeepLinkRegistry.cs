using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace GZKFingerprintScanner
{
    /// <summary>
    /// Đăng ký/huỷ đăng ký Deep Link protocol gemr:// vào Windows Registry.
    /// Yêu cầu quyền Administrator để ghi vào HKLM.
    /// </summary>
    internal static class DeepLinkRegistry
    {
        private const string ProtocolName = "gemr";
        private const string RegistryPath = @"SOFTWARE\Classes\" + ProtocolName;

        private static string ExePath
        {
            get
            {
                var module = Process.GetCurrentProcess().MainModule;
                return module != null ? module.FileName : string.Empty;
            }
        }

        /// <summary>
        /// Kiểm tra protocol đã được đăng ký chưa.
        /// </summary>
        public static bool IsRegistered()
        {
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(RegistryPath, writable: false))
                {
                    if (key == null) return false;
                    var command = key.OpenSubKey("shell\\open\\command");
                    if (command == null) return false;
                    var value = command.GetValue(null) as string;
                    command.Dispose();
                    return !string.IsNullOrEmpty(value) && value.Contains(ExePath);
                }
            }
            catch { return false; }
        }

        /// <summary>
        /// Đăng ký protocol gemr:// vào Registry.
        /// Cần chạy với quyền Administrator.
        /// </summary>
        public static void Register()
        {
            try
            {
                using (var key = Registry.LocalMachine.CreateSubKey(RegistryPath))
                {
                    key.SetValue(null, "URL:gemr Protocol", RegistryValueKind.String);
                    key.SetValue("URL Protocol", "", RegistryValueKind.String);

                    using (var iconKey = key.CreateSubKey("DefaultIcon"))
                    {
                        iconKey.SetValue(null, string.Format("\"{0}\",0", ExePath));
                    }

                    using (var commandKey = key.CreateSubKey("shell\\open\\command"))
                    {
                        commandKey.SetValue(null, string.Format("\"{0}\" \"%1\"", ExePath), RegistryValueKind.String);
                    }
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new InvalidOperationException("Không thể đăng ký Deep Link. Vui lòng chạy ứng dụng với quyền Administrator.", ex);
            }
        }

        /// <summary>
        /// Huỷ đăng ký protocol.
        /// Cần chạy với quyền Administrator.
        /// </summary>
        public static void Unregister()
        {
            try
            {
                Registry.LocalMachine.DeleteSubKeyTree(RegistryPath, false);
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new InvalidOperationException("Không thể huỷ đăng ký Deep Link. Vui lòng chạy ứng dụng với quyền Administrator.", ex);
            }
        }
    }
}
