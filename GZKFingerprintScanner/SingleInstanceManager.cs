using System;
using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace GZKFingerprintScanner
{
    /// <summary>
    /// Quản lý Single Instance và Named Pipe giao tiếp giữa các tiến trình.
    /// </summary>
    public sealed class SingleInstanceManager : IDisposable
    {
        private const string MutexKey = @"Global\GZKFingerprintScanner_SingleInstance_v1";
        private const string PipeName = @"GZKFingerprintScanner_Pipe_v1";
        private const int PipeTimeoutMs = 5000;

        private Mutex _mutex;
        private NamedPipeServerStream _pipeServer;
        private CancellationTokenSource _cts;
        private bool _isFirstInstance;
        private bool _disposed;

        public bool IsFirstInstance => _isFirstInstance;

        /// <summary>
        /// Event khi nhận được deep link URL từ tiến trình khác.
        /// </summary>
        public event Action<string> DeepLinkReceived;

        /// <summary>
        /// Khởi tạo và kiểm tra single instance.
        /// </summary>
        public bool Initialize()
        {
            _mutex = new Mutex(true, MutexKey, out _isFirstInstance);

            if (_isFirstInstance)
            {
                // Tiến trình đầu tiên: bắt đầu pipe server
                StartPipeServer();
            }

            return _isFirstInstance;
        }

        /// <summary>
        /// Gửi deep link URL đến tiến trình đang chạy.
        /// </summary>
        public static bool SendToRunningInstance(string deepLinkUrl)
        {
            try
            {
                using (var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out))
                {
                    client.Connect(PipeTimeoutMs);

                    var buffer = Encoding.UTF8.GetBytes(deepLinkUrl);
                    client.Write(buffer, 0, buffer.Length);
                    client.Flush();
                }
                return true;
            }
            catch (Exception ex)
            {
                // Ghi log lỗi nếu cần
                System.Diagnostics.Debug.WriteLine($"Failed to send to running instance: {ex.Message}");
                return false;
            }
        }

        private void StartPipeServer()
        {
            _cts = new CancellationTokenSource();

            Task.Run(async () =>
            {
                while (!_cts.IsCancellationRequested)
                {
                    try
                    {
                        _pipeServer = new NamedPipeServerStream(
                            PipeName,
                            PipeDirection.In,
                            1,
                            PipeTransmissionMode.Message,
                            PipeOptions.Asynchronous);

                        await _pipeServer.WaitForConnectionAsync(_cts.Token);

                        // Đọc message
                        var sb = new StringBuilder();
                        var buffer = new byte[4096];

                        do
                        {
                            int bytesRead = await _pipeServer.ReadAsync(buffer, 0, buffer.Length, _cts.Token);
                            if (bytesRead > 0)
                            {
                                sb.Append(Encoding.UTF8.GetString(buffer, 0, bytesRead));
                            }
                        }
                        while (!_pipeServer.IsMessageComplete);

                        var message = sb.ToString();

                        // Xử lý deep link trên UI thread (tray app dùng SynchronizationContext)
                        if (!string.IsNullOrEmpty(message))
                        {
                            try
                            {
                                var context = SynchronizationContext.Current;
                                if (context != null)
                                {
                                    context.Post(_ => DeepLinkReceived?.Invoke(message), null);
                                }
                                else
                                {
                                    DeepLinkReceived?.Invoke(message);
                                }
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"Error invoking deep link: {ex.Message}");
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (IOException)
                    {
                        // Pipe đã đóng, tiếp tục vòng lặp
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Pipe server error: {ex.Message}");
                        await Task.Delay(1000, _cts.Token);
                    }
                    finally
                    {
                        try
                        {
                            _pipeServer?.Disconnect();
                        }
                        catch { }
                        try
                        {
                            _pipeServer?.Dispose();
                        }
                        catch { }
                        _pipeServer = null;
                    }
                }
            }, _cts.Token);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                _cts?.Cancel();
            }
            catch { }

            try
            {
                _pipeServer?.Dispose();
            }
            catch { }

            try
            {
                _mutex?.ReleaseMutex();
                _mutex?.Dispose();
            }
            catch { }

            try
            {
                _cts?.Dispose();
            }
            catch { }
        }
    }
}
