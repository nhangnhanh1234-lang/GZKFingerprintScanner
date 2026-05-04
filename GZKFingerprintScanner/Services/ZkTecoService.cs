using System;
using System.IO;
using System.Threading;
using libzkfpcsharp;
using GZKFingerprintScanner.Helpers;
using GZKFingerprintScanner.Models;
using GZKFingerprintScanner.Logging;

namespace GZKFingerprintScanner.Services
{
    public sealed class ZkTecoService : IDisposable
    {
        private readonly ILogger _logger;
        private readonly DeviceOptions _options;
        private readonly zkfp _fp = new zkfp();

        private Thread _captureThread;
        private volatile bool _running;
        private volatile bool _deviceOpen;
        private string _deviceSn = string.Empty;

        private int _width;
        private int _height;
        private byte[] _fpBuffer = new byte[0];
        private readonly byte[] _capTmp = new byte[2048];
        private int _capTmpLen = 2048;

        private readonly object _modeLock = new object();
        private bool _verifyMode;
        private byte[] _targetTemplate;
        private string _currentClientId;

        public event Func<FingerprintData, bool> OnFingerprintCaptured;
        public event Func<VerifyResult, bool> OnVerifyCompleted;
        public event Func<DeviceStatus, bool> OnDeviceStatusChanged;

        public bool IsDeviceOpen { get { return _deviceOpen; } }

        public ZkTecoService(ILogger logger, DeviceOptions options)
        {
            _logger = logger ?? throw new ArgumentNullException("logger");
            _options = options ?? new DeviceOptions();
        }

        public bool Connect()
        {
            if (_deviceOpen) return true;

            int ret = _fp.Initialize();
            if (ret != zkfp.ZKFP_ERR_OK)
            {
                _logger.LogError(string.Format("ZK Initialize failed: {0} - {1}", ret, ZkErrorCodes.GetMessage(ret)));
                RaiseStatus("error", string.Format("Init failed: {0}", ZkErrorCodes.GetMessage(ret)));
                return false;
            }

            int count = _fp.GetDeviceCount();
            if (count <= 0)
            {
                _logger.LogWarning("No ZK fingerprint device detected");
                _fp.Finalize();
                RaiseStatus("offline", "No device detected");
                return false;
            }

            ret = _fp.OpenDevice(_options.DeviceIndex);
            if (ret != zkfp.ZKFP_ERR_OK)
            {
                _logger.LogError(string.Format("ZK OpenDevice failed: {0} - {1}", ret, ZkErrorCodes.GetMessage(ret)));
                _fp.Finalize();
                RaiseStatus("error", string.Format("Open failed: {0}", ZkErrorCodes.GetMessage(ret)));
                return false;
            }

            byte[] param = new byte[4];
            int size = 4;
            _fp.GetParameters(1, param, ref size);
            zkfp2.ByteArray2Int(param, ref _width);

            size = 4;
            _fp.GetParameters(2, param, ref size);
            zkfp2.ByteArray2Int(param, ref _height);

            _fpBuffer = new byte[_width * _height];
            _deviceOpen = true;
            _deviceSn = _fp.devSn ?? string.Empty;

            _logger.LogInformation("===== ZK DEVICE CONNECTED =====");
            _logger.LogInformation(string.Format("  Serial Number : {0}", _deviceSn));
            _logger.LogInformation(string.Format("  Image Size    : {0} x {1}", _width, _height));
            _logger.LogInformation(string.Format("  Device Index  : {0}", _options.DeviceIndex));
            _logger.LogInformation("===============================");
            RaiseStatus("connected", string.Format("Device opened (SN={0})", _deviceSn), _deviceSn);

            StartCaptureLoop();
            return true;
        }

        public void Disconnect()
        {
            if (!_deviceOpen) return;

            StopCaptureLoop();

            try { _fp.CloseDevice(); } catch (Exception ex) { _logger.LogWarning(string.Format("CloseDevice threw: {0}", ex.Message)); }
            try { _fp.Finalize(); } catch (Exception ex) { _logger.LogWarning(string.Format("Finalize threw: {0}", ex.Message)); }

            _deviceOpen = false;
            _logger.LogInformation("ZK device disconnected");
            RaiseStatus("disconnected", "Device closed", _deviceSn);
            _deviceSn = string.Empty;
        }

        private void StartCaptureLoop()
        {
            if (_running) return;
            _running = true;

            _captureThread = new Thread(CaptureLoop)
            {
                IsBackground = true,
                Name = "ZkCaptureThread"
            };
            _captureThread.Start();
        }

        private void StopCaptureLoop()
        {
            _running = false;
            if (_captureThread != null && _captureThread.IsAlive)
            {
                _captureThread.Join(1500);
            }
            _captureThread = null;
        }

        private void CaptureLoop()
        {
            _logger.LogInformation("Capture loop started");
            while (_running)
            {
                try
                {
                    _capTmpLen = _capTmp.Length;
                    int ret = _fp.AcquireFingerprint(_fpBuffer, _capTmp, ref _capTmpLen);
                    if (ret == zkfp.ZKFP_ERR_OK)
                    {
                        HandleCaptured();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Capture loop error");
                }
                Thread.Sleep(_options.CaptureIntervalMs);
            }
            _logger.LogInformation("Capture loop stopped");
        }

        private void HandleCaptured()
        {
            string imageBase64;
            string templateBase64 = Convert.ToBase64String(_capTmp, 0, _capTmpLen);

            MemoryStream ms = new MemoryStream();
            try
            {
                BitmapFormat.GetBitmap(_fpBuffer, _width, _height, ref ms);
                imageBase64 = Convert.ToBase64String(ms.ToArray());
            }
            finally
            {
                ms.Dispose();
            }

            bool verifyMode;
            byte[] target;
            string clientId;
            lock (_modeLock)
            {
                verifyMode = _verifyMode;
                target = _targetTemplate;
                clientId = _currentClientId;
            }

            string tplPreview = templateBase64.Length <= 32
                ? templateBase64
                : templateBase64.Substring(0, 32) + "...";
            _logger.LogInformation(string.Format(
                "[SCAN] Fingerprint captured | mode={0} | tpl={1}b | img={2}b | preview={3} | client={4}",
                verifyMode ? "VERIFY" : "SCAN",
                _capTmpLen,
                imageBase64.Length,
                tplPreview,
                clientId ?? "(none)"));

            SafeInvoke(OnFingerprintCaptured, new FingerprintData
            {
                ImageBase64 = imageBase64,
                TemplateBase64 = templateBase64,
                ClientId = clientId
            });

            if (verifyMode && target != null)
            {
                int score = 0;
                bool isMatch = false;
                try
                {
                    score = _fp.Match(_capTmp, target);
                    isMatch = score > 0;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Match failed");
                }

                if (isMatch)
                    _logger.LogInformation(string.Format("[VERIFY] MATCH   score={0} client={1}", score, clientId));
                else
                    _logger.LogWarning(string.Format("[VERIFY] NO MATCH score={0} client={1}", score, clientId));

                SafeInvoke(OnVerifyCompleted, new VerifyResult
                {
                    IsMatch = isMatch,
                    Score = score,
                    ClientId = clientId
                });
            }
        }

        public void EnterVerifyMode(string clientId, string templateBase64)
        {
            byte[] target = Convert.FromBase64String(templateBase64);
            lock (_modeLock)
            {
                _currentClientId = clientId;
                _targetTemplate = target;
                _verifyMode = true;
            }
            _logger.LogInformation(string.Format("[MODE] >>> VERIFY MODE ON  | client={0} | template={1} bytes", clientId, target.Length));
            RaiseStatus("verify_mode", string.Format("Verify mode enabled for {0}", clientId));
        }

        public void ExitVerifyMode()
        {
            lock (_modeLock)
            {
                _verifyMode = false;
                _targetTemplate = null;
            }
            _logger.LogInformation("[MODE] >>> SCAN MODE");
            RaiseStatus("scan_mode", "Back to scan mode", _deviceSn);
        }

        public void SetClientId(string clientId)
        {
            lock (_modeLock) { _currentClientId = clientId; }
        }

        private void RaiseStatus(string status, string message, string deviceSn)
        {
            SafeInvoke(OnDeviceStatusChanged, new DeviceStatus { Status = status, Message = message, DeviceSn = deviceSn });
        }

        private void RaiseStatus(string status, string message)
        {
            RaiseStatus(status, message, null);
        }

        private void SafeInvoke<T>(Func<T, bool> handler, T arg)
        {
            if (handler == null) return;
            try { handler(arg); }
            catch (Exception ex) { _logger.LogError(ex, "Event handler threw"); }
        }

        public void Dispose()
        {
            Disconnect();
        }
    }
}
