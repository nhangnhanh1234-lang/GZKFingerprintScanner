using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;
using GZKFingerprintScanner.Logging;
using GZKFingerprintScanner.Models;
using GZKFingerprintScanner.Services;

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
        private readonly ToolStripMenuItem _miAutostart;

        private FingerprintService _service;
        private readonly DateTime _startedAt = DateTime.Now;
        private readonly string _logDir;

        private readonly ILogger _logger;
        private readonly ILogger _socketLogger;
        private readonly ILogger _zkLogger;
        private readonly FileLoggerProvider _loggerProvider;

        private SocketOptions _socketOpts;
        private DeviceOptions _deviceOpts;

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

            // Context menu
            _menu = new ContextMenuStrip();
            _menu.ShowImageMargin = false;

            _miStatus = AddInfoItem("Status: ● khởi động…");
            _miConn = AddInfoItem("Socket: —");
            _miDevice = AddInfoItem("Device: —");
            _miUptime = AddInfoItem("Uptime: 00:00:00");
            _menu.Items.Add(new ToolStripSeparator());

            _menu.Items.Add("Mở Web Console", null, (s, e) => OpenWebConsole());
            _menu.Items.Add("Restart service", null, (s, e) => Task.Run(RestartService));
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add("Open Install Folder", null, (s, e) => SafeOpen(AppDomain.CurrentDomain.BaseDirectory));
            _menu.Items.Add("Open Logs Folder", null, (s, e) => SafeOpen(_logDir));
            _menu.Items.Add(new ToolStripSeparator());

            _miAutostart = new ToolStripMenuItem("Khởi động cùng Windows")
            {
                CheckOnClick = true,
                Checked = StartupRegistry.IsEnabled()
            };
            _miAutostart.Click += (s, e) =>
            {
                try { StartupRegistry.SetEnabled(_miAutostart.Checked); }
                catch (Exception ex) { ShowError("Không thể cập nhật autostart: " + ex.Message); _miAutostart.Checked = !_miAutostart.Checked; }
            };
            _menu.Items.Add(_miAutostart);
            _menu.Items.Add(new ToolStripSeparator());

            _menu.Items.Add("Quit", null, (s, e) => Task.Run(QuitAsync));

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
            Task.Run(StartService);
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
                var socket = new SocketClientService(_socketLogger, _socketOpts, zk);
                _service = new FingerprintService(_logger, socket, zk, _deviceOpts);
                _service.Start();
                _logger.LogInformation("Service started successfully");
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
                    _miStatus.Text = "Status: ● Not started";
                    _miStatus.ForeColor = Color.Gray;
                    return;
                }

                bool sockOk = _service.IsSocketConnected;
                bool devOk = _service.IsDeviceOpen;

                if (sockOk && devOk)
                {
                    _miStatus.Text = "Status: ● Running";
                    _miStatus.ForeColor = Color.Green;
                }
                else if (sockOk && !devOk)
                {
                    _miStatus.Text = "Status: ◐ Socket only (no device)";
                    _miStatus.ForeColor = Color.DarkOrange;
                }
                else if (!sockOk && devOk)
                {
                    _miStatus.Text = "Status: ◐ Device only (no socket)";
                    _miStatus.ForeColor = Color.DarkOrange;
                }
                else
                {
                    _miStatus.Text = "Status: ● Offline";
                    _miStatus.ForeColor = Color.Red;
                }

                _miConn.Text = string.Format("Socket: {0} {1}", sockOk ? "✓" : "✗", _socketOpts.Url);
                string sn = _service.DeviceSerial;
                _miDevice.Text = devOk
                    ? string.Format("Device: ✓ {0}", string.IsNullOrEmpty(sn) ? "connected" : sn)
                    : "Device: ✗ offline";
                _miUptime.Text = string.Format("Uptime: {0:hh\\:mm\\:ss}", DateTime.Now - _startedAt);

                string trayText = _miStatus.Text;
                if (trayText.Length > 63)
                    trayText = trayText.Substring(0, 63);
                _tray.Text = trayText;
            }
            catch { }
        }

        private ToolStripMenuItem AddInfoItem(string text)
        {
            var it = new ToolStripMenuItem(text);
            it.Enabled = false;
            _menu.Items.Add(it);
            return it;
        }

        private void OpenWebConsole()
        {
            string file = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "WebClient", "index.html");
            file = Path.GetFullPath(file);
            if (File.Exists(file)) SafeOpen(file);
            else ShowBalloon("WebClient", "Không tìm thấy WebClient/index.html", ToolTipIcon.Warning);
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
