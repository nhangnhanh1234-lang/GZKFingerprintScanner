using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using GZKFingerprintScanner.Helpers;
using GZKFingerprintScanner.Logging;
using GZKFingerprintScanner.Models;
using GZKFingerprintScanner.Services;
using GZKFingerprintScanner.UI;

namespace GZKFingerprintScanner
{
    /// <summary>
    /// WinForms ApplicationContext - quản lý vòng đời của tray icon và service
    /// </summary>
    public sealed class TrayApplicationContext : ApplicationContext
    {
        private readonly NotifyIcon _tray;
        private readonly ContextMenuStrip _menu;
        private readonly System.Windows.Forms.Timer _refreshTimer;

        private readonly ToolStripMenuItem _miStatus;
        private readonly ToolStripMenuItem _miConn;
        private readonly ToolStripMenuItem _miUptime;
        private readonly ToolStripMenuItem _miDevice;
        private readonly ToolStripMenuItem _miRoom;
        private readonly ToolStripMenuItem _miAutostart;

        private SocketClientService _socket;
        private FingerprintService _service;
        private readonly DateTime _startedAt = DateTime.Now;
        private readonly string _logDir;

        private readonly ILogger _logger;
        private readonly ILogger _socketLogger;
        private readonly ILogger _zkLogger;
        private readonly FileLoggerProvider _loggerProvider;

        private SocketOptions _socketOpts;
        private DeviceOptions _deviceOpts;
        private string _currentRoom;
        private string _deviceId; // MAC Address used as device ID
        private string _pendingRoomFromDeepLink; // Room từ deep link khi khởi động, ưu tiên hơn MAC

        public TrayApplicationContext()
        {
            // Use LocalApplicationData to avoid requiring admin rights
            _logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "GZKFingerprintScanner",
                "logs");
            Directory.CreateDirectory(_logDir);

            // Setup logging
            _loggerProvider = new FileLoggerProvider(Path.Combine(_logDir, "app.log"));
            _logger = _loggerProvider.CreateLogger("TrayApp");
            _socketLogger = _loggerProvider.CreateLogger("Socket");
            _zkLogger = _loggerProvider.CreateLogger("ZkTeco");

            // Context menu - hiện đại, dark theme
            _menu = new ContextMenuStrip
            {
                Renderer = new ModernMenuRenderer(),
                BackColor = ModernColorTable.Background,
                ForeColor = ModernColorTable.Text,
                Font = new Font("Segoe UI", 9.25f, FontStyle.Regular),
                ShowImageMargin = false,
                ShowCheckMargin = false,
                Padding = new Padding(4, 6, 4, 6),
                DropShadowEnabled = true
            };

            // Header app title
            _menu.Items.Add(BuildTitleItem());
            _menu.Items.Add(BuildSeparator());

            // Status section
            _menu.Items.Add(BuildSectionHeader("STATUS"));
            _miStatus = AddInfoItem("●  Đang khởi động…");
            _miConn = AddInfoItem("Socket: —");
            _miDevice = AddInfoItem("Device: —");
            _miRoom = AddInfoItem("Room: —");
            _miRoom.Click += (s, e) => CopyRoomToClipboard();
            _miUptime = AddInfoItem("Uptime: 00:00:00");
            _menu.Items.Add(BuildSeparator());

            // Actions section
            _menu.Items.Add(BuildSectionHeader("ACTIONS"));
            _menu.Items.Add(BuildActionItem("\uD83C\uDF10  Mở Web Console", (s, e) => OpenWebConsole()));
            _menu.Items.Add(BuildActionItem("\uD83D\uDCCB  Copy Room", (s, e) => CopyRoomToClipboard()));
            _menu.Items.Add(BuildActionItem("\u21BB  Khởi động lại dịch vụ", (s, e) => Task.Run(RestartService)));
            _menu.Items.Add(BuildSeparator());

            // Folders section
            _menu.Items.Add(BuildSectionHeader("FOLDERS"));
            _menu.Items.Add(BuildActionItem("\uD83D\uDCC1  Mở thư mục cài đặt", (s, e) => SafeOpen(AppDomain.CurrentDomain.BaseDirectory)));
            _menu.Items.Add(BuildActionItem("\uD83D\uDCDC  Mở thư mục logs", (s, e) => SafeOpen(_logDir)));
            _menu.Items.Add(BuildSeparator());

            // Settings
            _miAutostart = new ToolStripMenuItem("\u2699  Khởi động cùng Windows")
            {
                CheckOnClick = true,
                Checked = StartupRegistry.IsEnabled(),
                Padding = new Padding(8, 6, 8, 6),
                Font = _menu.Font
            };
            _miAutostart.Click += (s, e) =>
            {
                try { StartupRegistry.SetEnabled(_miAutostart.Checked); }
                catch (Exception ex) { ShowError("Không thể cập nhật autostart: " + ex.Message); _miAutostart.Checked = !_miAutostart.Checked; }
            };
            _menu.Items.Add(_miAutostart);
            _menu.Items.Add(BuildSeparator());

            // Quit - màu đỏ nhạt để nổi bật
            var quit = BuildActionItem("\u2715  Thoát", (s, e) => Task.Run(QuitAsync));
            quit.ForeColor = Color.FromArgb(239, 100, 100);
            _menu.Items.Add(quit);

            // Tray icon
            _tray = new NotifyIcon
            {
                Icon = LoadIcon(),
                ContextMenuStrip = _menu,
                Visible = true,
                Text = "ZK Fingerprint Service"
            };
            _tray.DoubleClick += (s, e) => OpenWebConsole();

            // Timer refresh menu
            _refreshTimer = new System.Windows.Forms.Timer();
            _refreshTimer.Interval = 1000;
            _refreshTimer.Tick += (s, e) => RefreshMenu();
            _refreshTimer.Start();

            // Load config and start service
            LoadConfig();
            GenerateDeviceId();
            Task.Run(StartService);
        }

        /// <summary>
        /// Sinh deviceId từ MAC Address của máy.
        /// Thêm prefix "zk_" để web phân biệt room máy quét vân tay.
        /// </summary>
        private void GenerateDeviceId()
        {
            string mac = MacAddressHelper.GetMacAddress();
            if (string.IsNullOrEmpty(mac))
            {
                // Fallback: dùng tên máy + timestamp nếu không lấy được MAC
                mac = string.Format("{0}_{1:yyyyMMddHHmmss}", Environment.MachineName, DateTime.Now);
            }
            // Thêm prefix để web phân biệt
            _deviceId = "zk_" + mac;
            _logger.LogInformation(string.Format("Device ID (MAC): {0}", _deviceId));
        }

        /// <summary>
        /// Xử lý deep link URL và chuyển room nếu cần.
        /// Format: gemr://open?room=TEN_PHONG
        /// </summary>
        public void ProcessDeepLink(string deepLinkUrl)
        {
            if (string.IsNullOrWhiteSpace(deepLinkUrl)) return;

            _logger.LogInformation(string.Format("[DEEPLINK] Received: {0}", deepLinkUrl));

            try
            {
                // Parse URL để lấy tham số room
                string roomName = ParseRoomFromDeepLink(deepLinkUrl);

                if (string.IsNullOrWhiteSpace(roomName))
                {
                    _logger.LogWarning("[DEEPLINK] No room parameter found");
                    return;
                }

                // Nếu service chưa start (khởi động từ deep link), lưu lại để ưu tiên
                if (_service == null)
                {
                    _logger.LogInformation(string.Format("[DEEPLINK] Service not started yet, queuing room: {0}", roomName));
                    _pendingRoomFromDeepLink = roomName.StartsWith("zk_", StringComparison.OrdinalIgnoreCase) 
                        ? roomName 
                        : "zk_" + roomName;
                    return;
                }

                // Chuyển room - TrayApplicationContext chạy trên UI thread nên gọi trực tiếp
                // hoặc dùng InvokeRequired nếu cần
                if (SynchronizationContext.Current != null)
                {
                    SynchronizationContext.Current.Post(_ => SwitchRoom(roomName), null);
                }
                else
                {
                    SwitchRoom(roomName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[DEEPLINK] Error processing deep link");
            }
        }

        /// <summary>
        /// Parse tham số room từ deep link URL.
        /// </summary>
        private string ParseRoomFromDeepLink(string url)
        {
            try
            {
                // Remove protocol prefix
                if (url.StartsWith("gemr://", StringComparison.OrdinalIgnoreCase))
                {
                    url = url.Substring("gemr://".Length);
                }

                // Parse query string
                var queryIndex = url.IndexOf('?');
                if (queryIndex < 0) return null;

                var query = url.Substring(queryIndex + 1);
                var pairs = query.Split('&');

                foreach (var pair in pairs)
                {
                    var eqIndex = pair.IndexOf('=');
                    if (eqIndex > 0)
                    {
                        var key = pair.Substring(0, eqIndex).Trim().ToLowerInvariant();
                        var value = pair.Substring(eqIndex + 1).Trim();

                        if (key == "room")
                        {
                            return Uri.UnescapeDataString(value);
                        }
                    }
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// Chuyển sang room mới: leave room hiện tại, join room mới, hiển thị thông báo.
        /// </summary>
        private async void SwitchRoom(string newRoom)
        {
            if (string.IsNullOrWhiteSpace(newRoom)) return;
            if (newRoom == _currentRoom)
            {
                _logger.LogInformation(string.Format("[ROOM] Already in room '{0}'", newRoom));
                return;
            }

            _logger.LogInformation(string.Format("[ROOM] Switching from '{0}' to '{1}'", _currentRoom ?? "(none)", newRoom));

            if (_service == null)
            {
                _logger.LogWarning("[ROOM] Service not started, cannot switch room");
                return;
            }

            // Đợi socket connected trước khi chuyển room
            int waitCount = 0;
            while (!_socket.IsConnected && waitCount < 50) // Tối đa 5 giây
            {
                await Task.Delay(100);
                waitCount++;
            }

            if (!_socket.IsConnected)
            {
                _logger.LogError("[ROOM] Socket not connected after waiting, cannot switch room");
                ShowBalloon("Lỗi", "Không thể kết nối đến server.", ToolTipIcon.Error);
                return;
            }

            try
            {
                // 1. Gọi leave_room cho phòng hiện tại
                if (!string.IsNullOrEmpty(_currentRoom))
                {
                    await _socket.LeaveRoomAsync();
                }

                // 2. Gọi join_room cho phòng mới
                // Nếu room từ deep link không có prefix zk_, thêm vào để web nhận diện
                string targetRoom = newRoom.StartsWith("zk_", StringComparison.OrdinalIgnoreCase) 
                    ? newRoom 
                    : "zk_" + newRoom;
                bool success = await _socket.JoinRoomAsync(targetRoom);

                if (success)
                {
                    _currentRoom = targetRoom; // Lưu room có prefix zk_

                    // 3. Hiển thị BalloonTip thông báo (hiển thị không prefix cho gọn)
                    string displayRoom = newRoom.StartsWith("zk_", StringComparison.OrdinalIgnoreCase) 
                        ? newRoom.Substring(3) 
                        : newRoom;
                    ShowBalloon("Phòng mới", string.Format("Đã chuyển sang phòng: {0}", displayRoom), ToolTipIcon.Info);
                    _logger.LogInformation(string.Format("[ROOM] Successfully switched to room '{0}'", newRoom));
                }
                else
                {
                    ShowBalloon("Lỗi chuyển phòng", "Không thể kết nối đến phòng mới.", ToolTipIcon.Error);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ROOM] Error switching room");
                ShowBalloon("Lỗi", "Có lỗi khi chuyển phòng: " + ex.Message, ToolTipIcon.Error);
            }
        }

        private void LoadConfig()
        {
            try
            {
                // Try to load from remote config first
                var remoteConfig = TryFetchRemoteConfig();
                if (remoteConfig != null)
                {
                    _socketOpts = new SocketOptions
                    {
                        Url = remoteConfig.ContainsKey("Socket:Url") ? remoteConfig["Socket:Url"] : "https://gemr-socket.emed.vn",
                        ClientName = remoteConfig.ContainsKey("Socket:ClientName") ? remoteConfig["Socket:ClientName"] : "GZKFingerprintScanner",
                        Group = remoteConfig.ContainsKey("Socket:Group") ? remoteConfig["Socket:Group"] : "kyvantay",
                        ReconnectionDelayMs = remoteConfig.ContainsKey("Socket:ReconnectionDelayMs") ? int.Parse(remoteConfig["Socket:ReconnectionDelayMs"]) : 2000,
                        ConnectionTimeoutSeconds = remoteConfig.ContainsKey("Socket:ConnectionTimeoutSeconds") ? int.Parse(remoteConfig["Socket:ConnectionTimeoutSeconds"]) : 20
                    };
                    _deviceOpts = new DeviceOptions
                    {
                        DeviceIndex = remoteConfig.ContainsKey("Device:DeviceIndex") ? int.Parse(remoteConfig["Device:DeviceIndex"]) : 0,
                        CaptureIntervalMs = remoteConfig.ContainsKey("Device:CaptureIntervalMs") ? int.Parse(remoteConfig["Device:CaptureIntervalMs"]) : 100,
                        AutoStart = remoteConfig.ContainsKey("Device:AutoStart") ? bool.Parse(remoteConfig["Device:AutoStart"]) : true
                    };
                    return;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(string.Format("Failed to load remote config: {0}", ex.Message));
            }

            // Default config
            _socketOpts = new SocketOptions();
            _deviceOpts = new DeviceOptions();
        }

        private System.Collections.Generic.Dictionary<string, string> TryFetchRemoteConfig()
        {
            const string RemoteConfigUrl = "https://nhangnhanh1234-lang.github.io/zk_fingerprint_config/settings/appsettings.json";
            try
            {
                using (var http = new System.Net.Http.HttpClient())
                {
                    http.Timeout = TimeSpan.FromSeconds(8);
                    var json = http.GetStringAsync(RemoteConfigUrl).Result;
                    var dict = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                    dynamic doc = Newtonsoft.Json.JsonConvert.DeserializeObject(json);
                    if (doc.Socket != null)
                    {
                        if (doc.Socket.Url != null) dict["Socket:Url"] = doc.Socket.Url.ToString();
                        if (doc.Socket.ClientName != null) dict["Socket:ClientName"] = doc.Socket.ClientName.ToString();
                        if (doc.Socket.Group != null) dict["Socket:Group"] = doc.Socket.Group.ToString();
                        if (doc.Socket.ReconnectionDelayMs != null) dict["Socket:ReconnectionDelayMs"] = doc.Socket.ReconnectionDelayMs.ToString();
                        if (doc.Socket.ConnectionTimeoutSeconds != null) dict["Socket:ConnectionTimeoutSeconds"] = doc.Socket.ConnectionTimeoutSeconds.ToString();
                    }
                    if (doc.Device != null)
                    {
                        if (doc.Device.DeviceIndex != null) dict["Device:DeviceIndex"] = doc.Device.DeviceIndex.ToString();
                        if (doc.Device.CaptureIntervalMs != null) dict["Device:CaptureIntervalMs"] = doc.Device.CaptureIntervalMs.ToString();
                        if (doc.Device.AutoStart != null) dict["Device:AutoStart"] = doc.Device.AutoStart.ToString();
                    }
                    return dict;
                }
            }
            catch { return null; }
        }

        private void LogStartupDiagnostics()
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                _logger.LogInformation("===== STARTUP DIAGNOSTICS =====");
                _logger.LogInformation(string.Format("  Install dir : {0}", baseDir));
                _logger.LogInformation(string.Format("  Process bits: {0}-bit", IntPtr.Size * 8));
                _logger.LogInformation(string.Format("  OS         : {0}", Environment.OSVersion));
                _logger.LogInformation(string.Format("  CLR        : {0}", Environment.Version));

                string[] requiredDlls = {
                    "libzkfp.dll", "libzkfpcsharp.dll",
                    "Newtonsoft.Json.dll", "SocketIOClient.dll",
                    "Microsoft.Bcl.AsyncInterfaces.dll",
                    "System.Buffers.dll", "System.Memory.dll",
                    "System.Numerics.Vectors.dll",
                    "System.Runtime.CompilerServices.Unsafe.dll",
                    "System.Threading.Channels.dll",
                    "System.Threading.Tasks.Extensions.dll",
                    "System.ValueTuple.dll",
                    "System.Text.Encodings.Web.dll",
                    "System.Text.Json.dll"
                };
                foreach (string dll in requiredDlls)
                {
                    string path = Path.Combine(baseDir, dll);
                    if (File.Exists(path))
                        _logger.LogInformation(string.Format("  [OK] {0} ({1:N0} bytes)", dll, new FileInfo(path).Length));
                    else
                        _logger.LogError(string.Format("  [MISSING] {0} - installer did not deploy this file!", dll));
                }
                _logger.LogInformation("===============================");
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Diagnostics failed: " + ex.Message);
            }
        }

        private void StartService()
        {
            try
            {
                LogStartupDiagnostics();
                var zk = new ZkTecoService(_zkLogger, _deviceOpts);
                _socket = new SocketClientService(_socketLogger, _socketOpts, zk);
                _service = new FingerprintService(_logger, _socket, zk, _deviceOpts);
                _service.Start();
                _logger.LogInformation("Service started successfully");

                // Join room mặc định là deviceId (MAC Address) khi khởi động
                Task.Run(async () =>
                {
                    // Đợi socket kết nối
                    int retries = 0;
                    while (!_socket.IsConnected && retries < 50) // Tối đa 50 giây
                    {
                        await Task.Delay(1000);
                        retries++;
                    }

                    if (_socket.IsConnected && !string.IsNullOrEmpty(_deviceId))
                    {
                        // Ưu tiên room từ deep link nếu có, nếu không thì dùng MAC
                        string targetRoom = _pendingRoomFromDeepLink ?? _deviceId;
                        
                        await _socket.JoinRoomAsync(targetRoom);
                        _currentRoom = targetRoom;
                        
                        if (_pendingRoomFromDeepLink != null)
                        {
                            _logger.LogInformation(string.Format("[ROOM] Auto-joined room from deep link: {0}", targetRoom));
                            _pendingRoomFromDeepLink = null; // Clear pending
                        }
                        else
                        {
                            _logger.LogInformation(string.Format("[ROOM] Auto-joined room (deviceId): {0}", targetRoom));
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                LogCrash(ex);
                ShowBalloon("Lỗi khởi động", ex.Message, ToolTipIcon.Error);
            }
        }

        private void RestartService()
        {
            try
            {
                ShowBalloon("Restart", "Đang khởi động lại service...", ToolTipIcon.Info);

                if (_service != null)
                {
                    _service.Stop();
                    _service.Dispose();
                    _service = null;
                }
                _socket = null;
                _currentRoom = null;
                _pendingRoomFromDeepLink = null; // Clear pending room khi restart

                LoadConfig();
                StartService();

                ShowBalloon("Restart", "Service đã khởi động lại.", ToolTipIcon.Info);
            }
            catch (Exception ex)
            {
                LogCrash(ex);
                ShowError("Restart thất bại: " + ex.Message);
            }
        }

        /// <summary>
        /// Copy room name hiện tại vào clipboard.
        /// </summary>
        private void CopyRoomToClipboard()
        {
            try
            {
                string roomName = _currentRoom ?? (_socket?.CurrentRoom);
                if (!string.IsNullOrEmpty(roomName))
                {
                    Clipboard.SetText(roomName);
                    ShowBalloon("Đã copy", string.Format("Room '{0}' đã được copy vào clipboard.", roomName), ToolTipIcon.Info);
                    _logger.LogInformation(string.Format("[ROOM] Copied to clipboard: {0}", roomName));
                }
                else
                {
                    ShowBalloon("Thông báo", "Chưa có room nào để copy.", ToolTipIcon.Warning);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ROOM] Failed to copy room to clipboard");
                ShowBalloon("Lỗi", "Không thể copy room: " + ex.Message, ToolTipIcon.Error);
            }
        }

        private async Task QuitAsync()
        {
            _refreshTimer.Stop();
            _tray.Visible = false;
            try
            {
                if (_service != null)
                {
                    _service.Stop();
                    _service.Dispose();
                }
                _loggerProvider.Dispose();
            }
            catch (Exception ex) { LogCrash(ex); }
            finally
            {
                _tray.Dispose();
                Application.Exit();
            }
        }

        private void RefreshMenu()
        {
            try
            {
                if (_service == null)
                {
                    _miStatus.Text = "○  Chưa khởi động";
                    _miStatus.ForeColor = Color.FromArgb(160, 160, 160);
                    return;
                }

                bool sockOk = _service.IsSocketConnected;
                bool devOk = _service.IsDeviceOpen;

                Color colorOk = Color.FromArgb(34, 197, 94);     // green-500
                Color colorWarn = Color.FromArgb(245, 158, 11);  // amber-500
                Color colorErr = Color.FromArgb(239, 68, 68);    // red-500
                Color colorMuted = Color.FromArgb(180, 180, 180);

                if (sockOk && devOk)
                {
                    _miStatus.Text = "●  Đang hoạt động";
                    _miStatus.ForeColor = colorOk;
                }
                else if (sockOk && !devOk)
                {
                    _miStatus.Text = "◐  Chỉ có Socket (thiếu device)";
                    _miStatus.ForeColor = colorWarn;
                }
                else if (!sockOk && devOk)
                {
                    _miStatus.Text = "◐  Chỉ có Device (thiếu socket)";
                    _miStatus.ForeColor = colorWarn;
                }
                else
                {
                    _miStatus.Text = "●  Offline";
                    _miStatus.ForeColor = colorErr;
                }

                _miConn.Text = string.Format("{0}  Socket  ·  {1}", sockOk ? "✓" : "✗", _socketOpts.Url);
                _miConn.ForeColor = sockOk ? colorMuted : colorErr;

                string sn = _service.DeviceSerial;
                _miDevice.Text = devOk
                    ? string.Format("✓  Device  ·  {0}", string.IsNullOrEmpty(sn) ? "đã kết nối" : sn)
                    : "✗  Device  ·  chưa kết nối";
                _miDevice.ForeColor = devOk ? colorMuted : colorErr;

                _miUptime.ForeColor = colorMuted;
                _miUptime.Text = string.Format("Uptime: {0:hh\\:mm\\:ss}", DateTime.Now - _startedAt);

                // Cập nhật Room status - cho phép click để copy
                string roomName = _currentRoom ?? (_socket?.CurrentRoom);
                if (!string.IsNullOrEmpty(roomName))
                {
                    _miRoom.Text = string.Format("✓  Room  ·  {0}", roomName);
                    _miRoom.ForeColor = colorMuted;
                    _miRoom.Enabled = true;
                    _miRoom.ToolTipText = "Click để copy room name";
                }
                else
                {
                    _miRoom.Text = "○  Room  ·  —";
                    _miRoom.ForeColor = colorMuted;
                    _miRoom.Enabled = false;
                    _miRoom.ToolTipText = null;
                }

                string trayText = _miStatus.Text;
                if (trayText.Length > 63)
                    trayText = trayText.Substring(0, 63);
                _tray.Text = trayText;
            }
            catch { }
        }

        private ToolStripMenuItem AddInfoItem(string text)
        {
            var it = new ToolStripMenuItem(text)
            {
                Enabled = false,
                Padding = new Padding(8, 4, 8, 4),
                Font = new Font("Segoe UI", 9f, FontStyle.Regular)
            };
            _menu.Items.Add(it);
            return it;
        }

        private ToolStripLabel BuildTitleItem()
        {
            return new ToolStripLabel("ZK Fingerprint Service")
            {
                Enabled = false,
                Padding = new Padding(10, 8, 10, 4),
                Font = new Font("Segoe UI Semibold", 10.5f, FontStyle.Bold),
                ForeColor = Color.White
            };
        }

        private ToolStripLabel BuildSectionHeader(string text)
        {
            return new ToolStripLabel(text)
            {
                Enabled = false,
                Padding = new Padding(10, 4, 10, 2),
                Font = new Font("Segoe UI", 7.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(140, 140, 140)
            };
        }

        private ToolStripMenuItem BuildActionItem(string text, EventHandler onClick)
        {
            var it = new ToolStripMenuItem(text)
            {
                Padding = new Padding(8, 6, 8, 6),
                Font = new Font("Segoe UI", 9.25f, FontStyle.Regular)
            };
            it.Click += onClick;
            return it;
        }

        private ToolStripSeparator BuildSeparator()
        {
            return new ToolStripSeparator { Margin = new Padding(0, 4, 0, 4) };
        }

        private void OpenWebConsole()
        {
            string file = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "WebClient", "index.html");
            if (!File.Exists(file))
            {
                ShowBalloon("WebClient", "Không tìm thấy Resources\\WebClient\\index.html", ToolTipIcon.Warning);
                return;
            }

            // Truyền room và server URL qua query params để web tự điền
            string room = _currentRoom ?? _deviceId ?? string.Empty;
            string serverUrl = _socketOpts?.Url ?? string.Empty;

            _logger.LogInformation(string.Format("[WEBCONSOLE] room={0} url={1}", room, serverUrl));

            var sb = new System.Text.StringBuilder("file:///");
            sb.Append(file.Replace('\\', '/'));
            sb.Append("?");
            if (!string.IsNullOrEmpty(room))
                sb.Append("room=").Append(Uri.EscapeDataString(room)).Append("&");
            if (!string.IsNullOrEmpty(serverUrl))
                sb.Append("url=").Append(Uri.EscapeDataString(serverUrl));

            string finalUrl = sb.ToString().TrimEnd('&', '?');
            _logger.LogInformation(string.Format("[WEBCONSOLE] Opening: {0}", finalUrl));
            SafeOpen(finalUrl);
        }

        private void SafeOpen(string path)
        {
            try
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch (Exception ex) { ShowError("Không mở được: " + ex.Message); }
        }

        private Icon LoadIcon()
        {
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "app.ico");
                if (File.Exists(path)) return new Icon(path);
                string exe = Assembly.GetExecutingAssembly().Location;
                if (!string.IsNullOrEmpty(exe))
                {
                    Icon ico = Icon.ExtractAssociatedIcon(exe);
                    if (ico != null) return ico;
                }
            }
            catch { }
            return SystemIcons.Application;
        }

        private void ShowBalloon(string title, string msg, ToolTipIcon icon)
        {
            try { _tray.ShowBalloonTip(3000, title, msg, icon); } catch { }
        }

        private void ShowError(string msg)
        {
            ShowBalloon("Lỗi", msg, ToolTipIcon.Error);
        }

        private void LogCrash(Exception ex)
        {
            try
            {
                string path = Path.Combine(_logDir, string.Format("crash-{0:yyyyMMdd}.log", DateTime.Now));
                File.AppendAllText(path,
                    string.Format("[{0:HH:mm:ss}] {1}{2}{2}", DateTime.Now, ex, Environment.NewLine));
            }
            catch { }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _refreshTimer?.Dispose();
                _tray?.Dispose();
                _menu?.Dispose();
                _service?.Dispose();
                _loggerProvider?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
