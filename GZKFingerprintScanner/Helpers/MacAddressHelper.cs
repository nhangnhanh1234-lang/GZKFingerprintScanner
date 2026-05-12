using System;
using System.Linq;
using System.Net.NetworkInformation;

namespace GZKFingerprintScanner.Helpers
{
    /// <summary>
    /// Helper lấy MAC Address của máy để dùng làm deviceId.
    /// </summary>
    public static class MacAddressHelper
    {
        /// <summary>
        /// Lấy MAC Address chính của máy (ưu tiên Ethernet, sau đó WiFi).
        /// Trả về chuỗi định dạng: AABBCCDDEEFF hoặc AA:BB:CC:DD:EE:FF
        /// </summary>
        public static string GetMacAddress(string separator = null)
        {
            try
            {
                var nics = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(n => n.OperationalStatus == OperationalStatus.Up &&
                                n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                    .ToList();

                // Ưu tiên Ethernet trước
                var ethernet = nics.FirstOrDefault(n =>
                    n.NetworkInterfaceType == NetworkInterfaceType.Ethernet);
                if (ethernet != null)
                    return FormatMacAddress(ethernet.GetPhysicalAddress(), separator);

                // Sau đó đến WiFi
                var wifi = nics.FirstOrDefault(n =>
                    n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211);
                if (wifi != null)
                    return FormatMacAddress(wifi.GetPhysicalAddress(), separator);

                // Nếu không có, lấy card đầu tiên khác loopback
                var first = nics.FirstOrDefault();
                if (first != null)
                    return FormatMacAddress(first.GetPhysicalAddress(), separator);

                return null;
            }
            catch
            {
                return null;
            }
        }

        private static string FormatMacAddress(PhysicalAddress address, string separator)
        {
            var mac = address.ToString();
            if (string.IsNullOrEmpty(mac)) return null;

            if (separator == null)
                return mac.ToUpperInvariant();

            // Chuyển AA:BB:CC:DD:EE:FF
            return string.Join(separator, Enumerable.Range(0, mac.Length / 2)
                .Select(i => mac.Substring(i * 2, 2)))
                .ToUpperInvariant();
        }
    }
}
