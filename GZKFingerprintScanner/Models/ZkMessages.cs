using System;
using Newtonsoft.Json;

namespace GZKFingerprintScanner.Models
{
    public class SocketCommand
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("group")]
        public string Group { get; set; }

        [JsonProperty("action")]
        public string Action { get; set; }

        [JsonProperty("clientId")]
        public string ClientId { get; set; }

        [JsonProperty("templateData")]
        public string TemplateData { get; set; }
    }

    public class FingerprintData
    {
        [JsonProperty("type")]
        public string Type { get; set; } = "fingerprint";

        [JsonProperty("imageBase64")]
        public string ImageBase64 { get; set; } = string.Empty;

        [JsonProperty("templateBase64")]
        public string TemplateBase64 { get; set; } = string.Empty;

        [JsonProperty("clientId")]
        public string ClientId { get; set; }

        [JsonProperty("timestamp")]
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    public class VerifyResult
    {
        [JsonProperty("type")]
        public string Type { get; set; } = "verify_result";

        [JsonProperty("isMatch")]
        public bool IsMatch { get; set; }

        [JsonProperty("score")]
        public int Score { get; set; }

        [JsonProperty("clientId")]
        public string ClientId { get; set; }

        [JsonProperty("timestamp")]
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    public class DeviceStatus
    {
        [JsonProperty("type")]
        public string Type { get; set; } = "device_status";

        [JsonProperty("status")]
        public string Status { get; set; } = string.Empty;

        [JsonProperty("message")]
        public string Message { get; set; }

        [JsonProperty("deviceSn")]
        public string DeviceSn { get; set; }

        [JsonProperty("service")]
        public string Service { get; set; } = "GZKFingerprintScanner";

        [JsonProperty("timestamp")]
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    public class SocketOptions
    {
        public string Url { get; set; } = "http://203.171.28.178:8023";
        public string ClientName { get; set; } = "GZKFingerprintScanner";
        public string Group { get; set; } = "kyvantay";
        public int ReconnectionDelayMs { get; set; } = 2000;
        public int ConnectionTimeoutSeconds { get; set; } = 20;
    }

    public class DeviceOptions
    {
        public int DeviceIndex { get; set; } = 0;
        public int CaptureIntervalMs { get; set; } = 100;
        public bool AutoStart { get; set; } = true;
    }
}
