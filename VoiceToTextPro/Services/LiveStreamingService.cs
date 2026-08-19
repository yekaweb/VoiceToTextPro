using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using VoiceToTextPro.Models;

namespace VoiceToTextPro.Services
{
    public class LiveStreamingService : IDisposable
    {
        private static readonly Lazy<LiveStreamingService> _instance = new(() => new LiveStreamingService());
        public static LiveStreamingService Instance => _instance.Value;

        private Process? _pythonProcess;
        private TcpClient? _tcpClient;
        private NetworkStream? _netStream;
        private CancellationTokenSource? _cts;
        private bool _isStreaming;
        private const int SocketPort = 9876;

        public bool IsStreaming => _isStreaming;

        /// <summary>
        /// Raised when real-time interim (draft) text is received from Vosk.
        /// </summary>
        public event Action<string>? OnPartialTextReceived;

        /// <summary>
        /// Raised when a finalized complete sentence is recognized.
        /// </summary>
        public event Action<string>? OnFinalTextReceived;

        /// <summary>
        /// Raised when status message or error occurs.
        /// </summary>
        public event Action<string>? OnStatusMessage;

        private LiveStreamingService() { }

        /// <summary>
        /// Starts live transcription using the active Vosk model and selected audio source.
        /// </summary>
        public async Task StartStreamingAsync(AiModel model, AudioSourceType sourceType, string? deviceId = null)
        {
            if (_isStreaming) await StopStreamingAsync();

            string modelsDir = AppSettings.Load().ModelsDirectory;
            string modelFolderPath = Path.Combine(modelsDir, model.FolderName);

            if (!Directory.Exists(modelFolderPath))
            {
                throw new DirectoryNotFoundException($"مدل یافت نشد: {modelFolderPath}. لطفا ابتدا آن را دانلود کنید.");
            }

            OnStatusMessage?.Invoke($"در حال راه‌اندازی کارگر زنده پایتون برای {model.Name}...");
            _cts = new CancellationTokenSource();

            try
            {
                // 1. Launch Python live worker process
                string pythonExe = GetPythonPath();
                string scriptPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "workers", "live_worker.py");

                if (!File.Exists(scriptPath))
                {
                    // Fallback to project root directory in dev mode
                    scriptPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "workers", "live_worker.py");
                    scriptPath = Path.GetFullPath(scriptPath);
                }

                var psi = new ProcessStartInfo
                {
                    FileName = pythonExe,
                    Arguments = $"\"{scriptPath}\" --model_path \"{modelFolderPath}\" --port {SocketPort}",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                _pythonProcess = new Process { StartInfo = psi };
                _pythonProcess.ErrorDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        LoggerService.Info($"[PYTHON_LIVE] {e.Data}", "LIVE_SERVICE");
                    }
                };
                _pythonProcess.Start();
                _pythonProcess.BeginErrorReadLine();

                // 2. Retry connecting C# TCP socket client to Python worker (up to 15 retries x 300ms)
                _tcpClient = new TcpClient();
                bool connected = false;

                for (int i = 0; i < 15; i++)
                {
                    if (_cts.IsCancellationRequested) break;
                    try
                    {
                        await Task.Delay(300, _cts.Token);
                        await _tcpClient.ConnectAsync("127.0.0.1", SocketPort, _cts.Token);
                        if (_tcpClient.Connected)
                        {
                            connected = true;
                            break;
                        }
                    }
                    catch (Exception)
                    {
                        if (i == 14) throw;
                    }
                }

                if (!connected || !_tcpClient.Connected)
                {
                    throw new SocketException((int)SocketError.NotConnected);
                }

                _netStream = _tcpClient.GetStream();
                _isStreaming = true;
                OnStatusMessage?.Invoke($"استریم زنده با موفقیت فعال شد ({sourceType}).");

                // 3. Start receiving JSON responses from Python in background
                _ = Task.Run(() => ReadResponsesAsync(_cts.Token));

                // 4. Wire up Audio Capture -> Send PCM bytes to Socket
                LiveAudioCaptureService.Instance.OnPcmDataAvailable -= SendAudioDataToSocket;
                LiveAudioCaptureService.Instance.OnPcmDataAvailable += SendAudioDataToSocket;
                LiveAudioCaptureService.Instance.StartCapture(sourceType, deviceId);
            }
            catch (Exception ex)
            {
                await StopStreamingAsync();
                LoggerService.ErrorLocalized("Log_LIVE_SERVICE_14f229", "خطا در شروع استریم زنده: {0}", "LIVE_SERVICE", ex.Message);
                OnStatusMessage?.Invoke($"خطا در راه‌اندازی استریم زنده: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Sends PCM bytes over TCP socket to Python Vosk process.
        /// </summary>
        private void SendAudioDataToSocket(byte[] buffer, int length)
        {
            if (!_isStreaming || _netStream == null || length <= 0) return;

            try
            {
                _netStream.Write(buffer, 0, length);
            }
            catch (Exception ex)
            {
                LoggerService.WarnLocalized("Log_LIVE_SERVICE_64925a", "خطا در ارسال داده صوتی به سوکت: {0}", "LIVE_SERVICE", ex.Message);
            }
        }

        /// <summary>
        /// Background loop reading JSON lines from Python socket.
        /// </summary>
        private async Task ReadResponsesAsync(CancellationToken ct)
        {
            if (_netStream == null) return;
            using var reader = new StreamReader(_netStream, Encoding.UTF8);

            try
            {
                while (!ct.IsCancellationRequested && _isStreaming)
                {
                    string? line = await reader.ReadLineAsync(ct);
                    if (string.IsNullOrEmpty(line)) continue;

                    var json = JObject.Parse(line);
                    string? type = json["type"]?.ToString();
                    string? text = json["text"]?.ToString();

                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        if (type == "partial")
                        {
                            OnPartialTextReceived?.Invoke(text);
                        }
                        else if (type == "final")
                        {
                            OnFinalTextReceived?.Invoke(text);
                        }
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (_isStreaming)
                {
                    LoggerService.WarnLocalized("Log_LIVE_SERVICE_11aeb5", "خطا در دریافت پاسخ متنی سوکت: {0}", "LIVE_SERVICE", ex.Message);
                }
            }
        }

        /// <summary>
        /// Stops live audio capture and disposes Python process & sockets cleanly.
        /// </summary>
        public async Task StopStreamingAsync()
        {
            _isStreaming = false;

            LiveAudioCaptureService.Instance.OnPcmDataAvailable -= SendAudioDataToSocket;
            LiveAudioCaptureService.Instance.StopCapture();

            _cts?.Cancel();

            try
            {
                _netStream?.Close();
                _netStream?.Dispose();
                _netStream = null;

                _tcpClient?.Close();
                _tcpClient?.Dispose();
                _tcpClient = null;

                if (_pythonProcess != null && !_pythonProcess.HasExited)
                {
                    _pythonProcess.Kill(true);
                    await _pythonProcess.WaitForExitAsync();
                    _pythonProcess.Dispose();
                    _pythonProcess = null;
                }
            }
            catch (Exception ex)
            {
                LoggerService.WarnLocalized("Log_LIVE_SERVICE_b7ff97", "اشکال جزئی در بستن پروسه پایتون: {0}", "LIVE_SERVICE", ex.Message);
            }
            finally
            {
                _cts?.Dispose();
                _cts = null;
                OnStatusMessage?.Invoke("استریم زنده متوقف شد.");
            }
        }

        private string GetPythonPath()
        {
            string localEnv = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "env", "Scripts", "python.exe");
            if (File.Exists(localEnv)) return localEnv;

            // System python fallback
            return "python.exe";
        }

        public void Dispose()
        {
            _ = StopStreamingAsync();
        }
    }
}
