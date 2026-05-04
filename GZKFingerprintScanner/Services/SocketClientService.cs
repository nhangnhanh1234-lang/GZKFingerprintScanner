using System;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using SocketIOClient;
using GZKFingerprintScanner.Models;
using GZKFingerprintScanner.Logging;

namespace GZKFingerprintScanner.Services
{
    public sealed class SocketClientService : IDisposable
    {
        private readonly ILogger _logger;
        private readonly SocketOptions _opts;
        private readonly ZkTecoService _zk;

        private SocketIOClient.SocketIO _client;
        private static readonly JsonSerializerSettings JsonOpts = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            DateFormatString = "yyyy-MM-ddTHH:mm:ss.fffZ"
        };

        public bool IsConnected
        {
            get { return _client != null && _client.Connected; }
        }

        public SocketClientService(ILogger logger, SocketOptions opts, ZkTecoService zk)
        {
            _logger = logger ?? throw new ArgumentNullException("logger");
            _opts = opts ?? new SocketOptions();
            _zk = zk ?? throw new ArgumentNullException("zk");

            _zk.OnFingerprintCaptured += HandleFingerprintCaptured;
            _zk.OnVerifyCompleted += HandleVerifyCompleted;
            _zk.OnDeviceStatusChanged += HandleDeviceStatusChanged;
        }

        public void Start(CancellationToken ct)
        {
            var uri = new Uri(_opts.Url);
            _client = new SocketIOClient.SocketIO(uri, new SocketIOOptions
            {
                Reconnection = true,
                ReconnectionDelay = _opts.ReconnectionDelayMs,
                ReconnectionAttempts = 10,
                ConnectionTimeout = TimeSpan.FromSeconds(_opts.ConnectionTimeoutSeconds),
                Transport = SocketIOClient.Transport.TransportProtocol.WebSocket
            });

            _client.OnConnected += async (sender, e) =>
            {
                _logger.LogInformation(string.Format("[SOCKET] ====== CONNECTED ====== url={0} id={1}", _opts.Url, _client.Id));

                await _client.EmitAsync("join", _opts.Group);
                _logger.LogInformation(string.Format("[SOCKET] Joined room '{0}'", _opts.Group));

                await EmitMessageAsync(new
                {
                    type = "serviceReady",
                    service = _opts.ClientName,
                    group = _opts.Group,
                    status = "online",
                    deviceOpen = _zk.IsDeviceOpen,
                    timestamp = DateTime.UtcNow
                });
            };

            _client.OnDisconnected += (sender, e) =>
                _logger.LogWarning(string.Format("[SOCKET] disconnected ({0})", e));

            _client.OnError += (sender, e) =>
                _logger.LogError(string.Format("[SOCKET] error: {0}", e));

            _client.OnReconnectAttempt += (sender, e) =>
                _logger.LogInformation(string.Format("[SOCKET] reconnect attempt {0}", e));

            _client.On("message", response =>
            {
                try
                {
                    var raw = response.GetValue<string>(0);
                    OnMessageReceived(raw);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error handling message");
                }
            });

            _client.On("joined", response =>
            {
                try
                {
                    var info = response.GetValue<string>(0);
                    _logger.LogInformation(string.Format("[SOCKET] Room joined confirmed: {0}", info));
                }
                catch { }
            });

            Task.Run(async () =>
            {
                try
                {
                    await _client.ConnectAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(string.Format("Initial Socket.IO connect failed (will retry in background): {0}", ex.Message));
                }
            }, ct);
        }

        public void Stop()
        {
            if (_client == null) return;
            try
            {
                _client.DisconnectAsync().Wait(TimeSpan.FromSeconds(2));
            }
            catch { }
        }

        private void OnMessageReceived(string raw)
        {
            try
            {
                SocketCommand cmd = null;
                try
                {
                    if (!string.IsNullOrWhiteSpace(raw))
                        cmd = JsonConvert.DeserializeObject<SocketCommand>(raw, JsonOpts);
                }
                catch { }

                if (cmd == null) return;

                string msgType = (cmd.Type ?? string.Empty).Trim().ToLowerInvariant();
                if (msgType != "command") return;

                string action = (cmd.Action ?? string.Empty).Trim().ToLowerInvariant();
                _logger.LogInformation(string.Format(
                    "[CMD <<] action={0} client={1} hasTemplate={2} deviceOpen={3}",
                    action, cmd.ClientId,
                    !string.IsNullOrWhiteSpace(cmd.TemplateData),
                    _zk.IsDeviceOpen));

                switch (action)
                {
                    case "connect":
                        _zk.SetClientId(cmd.ClientId);
                        if (_zk.IsDeviceOpen)
                        {
                            _logger.LogInformation("[CMD] device already open — re-announcing");
                            _ = EmitMessageAsync(new DeviceStatus { Status = "connected", Message = "Device already open" });
                        }
                        else
                        {
                            _logger.LogInformation("[CMD] opening device...");
                            _zk.Connect();
                        }
                        break;

                    case "disconnect":
                        _logger.LogInformation("[CMD] closing device");
                        _zk.Disconnect();
                        break;

                    case "scan":
                        _zk.SetClientId(cmd.ClientId);
                        _zk.ExitVerifyMode();
                        break;

                    case "verify":
                        if (string.IsNullOrWhiteSpace(cmd.TemplateData))
                        {
                            _logger.LogWarning("[CMD] 'verify' missing templateData");
                            return;
                        }
                        _zk.EnterVerifyMode(cmd.ClientId, cmd.TemplateData);
                        break;

                    case "stopverify":
                    case "stop_verify":
                        _zk.ExitVerifyMode();
                        break;

                    default:
                        _logger.LogWarning(string.Format("[CMD] Unknown action: '{0}'", action));
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while handling message");
            }
        }

        private bool HandleFingerprintCaptured(FingerprintData data)
        {
            _ = EmitMessageAsync(data);
            return true;
        }

        private bool HandleVerifyCompleted(VerifyResult result)
        {
            _ = EmitMessageAsync(result);
            return true;
        }

        private bool HandleDeviceStatusChanged(DeviceStatus status)
        {
            _ = EmitMessageAsync(status);
            return true;
        }

        private async Task EmitMessageAsync(object payload)
        {
            if (_client == null || !_client.Connected)
            {
                _logger.LogDebug("[EMIT skipped] message (socket not connected)");
                return;
            }
            try
            {
                var json = JsonConvert.SerializeObject(payload, JsonOpts);
                await _client.EmitAsync("message", json);

                string typeStr = "?";
                if (payload is DeviceStatus ds)
                    typeStr = ds.Status;
                else if (payload is FingerprintData)
                    typeStr = "fingerprint";
                else if (payload is VerifyResult)
                    typeStr = "verify_result";

                _logger.LogDebug(string.Format("[>> EMIT] message type={0}", typeStr));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Emit message failed");
            }
        }

        public void Dispose()
        {
            _zk.OnFingerprintCaptured -= HandleFingerprintCaptured;
            _zk.OnVerifyCompleted -= HandleVerifyCompleted;
            _zk.OnDeviceStatusChanged -= HandleDeviceStatusChanged;

            if (_client != null)
            {
                try
                {
                    _client.DisconnectAsync().Wait(TimeSpan.FromSeconds(2));
                }
                catch { }
                _client.Dispose();
                _client = null;
            }
        }
    }
}
