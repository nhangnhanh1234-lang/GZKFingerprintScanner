using System;
using System.Threading;
using System.Threading.Tasks;
using GZKFingerprintScanner.Logging;
using GZKFingerprintScanner.Models;

namespace GZKFingerprintScanner.Services
{
    /// <summary>
    /// Service quản lý vòng đời của ứng dụng - thay thế cho BackgroundService của .NET Core
    /// </summary>
    public sealed class FingerprintService : IDisposable
    {
        private readonly ILogger _logger;
        private readonly SocketClientService _socket;
        private readonly ZkTecoService _zk;
        private readonly DeviceOptions _deviceOpts;

        private CancellationTokenSource _cts;
        private Task _mainTask;
        private volatile bool _isRunning;

        public bool IsRunning { get { return _isRunning; } }

        public FingerprintService(ILogger logger, SocketClientService socket, ZkTecoService zk, DeviceOptions deviceOpts)
        {
            _logger = logger ?? throw new ArgumentNullException("logger");
            _socket = socket ?? throw new ArgumentNullException("socket");
            _zk = zk ?? throw new ArgumentNullException("zk");
            _deviceOpts = deviceOpts ?? new DeviceOptions();
        }

        public void Start()
        {
            if (_isRunning) return;

            _logger.LogInformation("FingerprintService starting...");
            _cts = new CancellationTokenSource();
            _isRunning = true;

            _socket.Start(_cts.Token);

            if (_deviceOpts.AutoStart)
            {
                if (!_zk.Connect())
                    _logger.LogWarning("Auto-start: device could not be opened. Will retry every 30s.");
            }

            _mainTask = Task.Run(() => RunLoop(_cts.Token), _cts.Token);
        }

        public void Stop()
        {
            if (!_isRunning) return;

            _logger.LogInformation("FingerprintService stopping...");
            _isRunning = false;

            try
            {
                _cts?.Cancel();
            }
            catch { }

            try
            {
                _zk.Disconnect();
            }
            catch (Exception ex) { _logger.LogWarning(string.Format("zk.Disconnect: {0}", ex.Message)); }

            try
            {
                _socket.Stop();
            }
            catch (Exception ex) { _logger.LogWarning(string.Format("socket.Stop: {0}", ex.Message)); }

            try
            {
                if (_mainTask != null && !_mainTask.IsCompleted)
                {
                    _mainTask.Wait(TimeSpan.FromSeconds(5));
                }
            }
            catch { }

            _logger.LogInformation("FingerprintService stopped");
        }

        private void RunLoop(CancellationToken stoppingToken)
        {
            int tick = 0;
            while (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogDebug(string.Format("heartbeat socket={0} device={1}", _socket.IsConnected, _zk.IsDeviceOpen));

                if (_deviceOpts.AutoStart && !_zk.IsDeviceOpen && tick % 3 == 0 && tick > 0)
                {
                    _logger.LogInformation("[WORKER] Auto-retry connecting device...");
                    _zk.Connect();
                }

                tick++;
                try
                {
                    Task.Delay(TimeSpan.FromSeconds(10), stoppingToken).Wait();
                }
                catch (OperationCanceledException) { break; }
                catch (AggregateException) { break; }
            }
        }

        public void Dispose()
        {
            Stop();
            _cts?.Dispose();
            _socket?.Dispose();
            _zk?.Dispose();
        }
    }
}
