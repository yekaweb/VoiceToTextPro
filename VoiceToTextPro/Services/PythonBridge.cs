using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace VoiceToTextPro.Services
{
    public class PythonBridge
    {
        [DllImport("ntdll.dll", PreserveSig = false)]
        private static extern void NtSuspendProcess(IntPtr handle);
        [DllImport("ntdll.dll", PreserveSig = false)]
        private static extern void NtResumeProcess(IntPtr handle);

        private Process? _process;
        private bool _isPaused;

        // Events
        public event Action<double, string>? OnProgress;
        public event Action<string>? OnText;
        public event Action<string>? OnResult;
        public event Action<string>? OnError;
        public event Action<string>? OnSrtPath;
        public event Action<string>? OnPolishedText;
        public event Action<string>? OnCorrectedText;

        public bool IsRunning => _process != null && !_process.HasExited;

        /// <summary>
        /// Robust search for workers directory across output folder and source tree.
        /// </summary>
        public static string GetWorkersDirectory()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            
            // 1. Direct subfolder in output directory
            var path = Path.Combine(baseDir, "workers");
            if (File.Exists(Path.Combine(path, "main_worker.py"))) 
                return path;

            // 2. Walk up parent directories until workers/main_worker.py is found (Dev mode)
            var dir = new DirectoryInfo(baseDir);
            while (dir != null)
            {
                var testPath = Path.Combine(dir.FullName, "workers");
                if (File.Exists(Path.Combine(testPath, "main_worker.py")))
                {
                    return testPath;
                }
                dir = dir.Parent;
            }

            return path;
        }

        public string CurrentMode { get; private set; } = "IDLE";

        public void KillProcess() => Stop();

        public async Task<bool> PingAsync()
        {
            if (IsRunning) return true; // If actively running another worker command, bridge is alive

            bool pongReceived = false;
            void Handler(string res)
            {
                if (res == "PONG") pongReceived = true;
            }

            OnResult += Handler;
            try
            {
                bool success = await RunAsync("ping");
                return success && pongReceived;
            }
            finally
            {
                OnResult -= Handler;
            }
        }

        public Task<bool> RunAsync(string mode, params string[] args)
        {
            if (IsRunning)
            {
                string warnMsg = $"عملیات پایتون دیگری در حال اجراست ({CurrentMode}). لطفا تا پایان آن شکیبا باشید.";
                LoggerService.Warn(warnMsg, "PYTHON_BRIDGE");
                OnError?.Invoke(warnMsg);
                return Task.FromResult(false);
            }
            CurrentMode = mode;

            var tcs = new TaskCompletionSource<bool>();
            Task.Run(() =>
            {
                try
                {
                    string pythonExe = FindPythonExecutable();
                    string workersDir = GetWorkersDirectory();
                    string scriptPath = Path.Combine(workersDir, "main_worker.py");

                    if (!File.Exists(scriptPath))
                    {
                        string msg = $"فایل اسکریپت یافت نشد: {scriptPath}";
                        LoggerService.Error(msg, "PYTHON_BRIDGE");
                        OnError?.Invoke(msg);
                        tcs.SetResult(false);
                        return;
                    }

                    LoggerService.Info($"در حال اجرای پایتون: {pythonExe} {scriptPath} {mode} {string.Join(" ", args)}", "PYTHON_BRIDGE");

                    var psi = new ProcessStartInfo
                    {
                        FileName = pythonExe,
                        WorkingDirectory = workersDir,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        StandardOutputEncoding = Encoding.UTF8,
                        StandardErrorEncoding = Encoding.UTF8
                    };

                    psi.ArgumentList.Add(scriptPath);
                    psi.ArgumentList.Add(mode);
                    foreach (var a in args)
                    {
                        psi.ArgumentList.Add(a);
                    }

                    _process = new Process { StartInfo = psi };

                    _process.OutputDataReceived += (s, e) =>
                    {
                        if (string.IsNullOrEmpty(e.Data)) return;
                        bool isInternal = ParseLine(e.Data);
                        if (!isInternal)
                        {
                            LoggerService.Python(e.Data);
                        }
                    };

                    _process.ErrorDataReceived += (s, e) =>
                    {
                        if (string.IsNullOrEmpty(e.Data)) return;
                        LoggerService.Error(e.Data, "PYTHON_STDERR");
                        OnError?.Invoke(e.Data);
                    };

                    _process.Start();
                    _process.BeginOutputReadLine();
                    _process.BeginErrorReadLine();
                    _process.WaitForExit();

                    LoggerService.SuccessLocalized("Log_PYTHON_BRIDGE_37fc17", "فرآیند پایتون با کد {0} به پایان رسید.", "PYTHON_BRIDGE",  _process.ExitCode );
                    tcs.SetResult(_process.ExitCode == 0);
                }
                catch (Exception ex)
                {
                    string err = $"خطا در شروع فرآیند پایتون: {ex.Message}";
                    LoggerService.Error(err, "PYTHON_BRIDGE");
                    OnError?.Invoke(err);
                    tcs.SetResult(false);
                }
                finally
                {
                    _process = null;
                    _isPaused = false;
                }
            });
            return tcs.Task;
        }

        private bool ParseLine(string line)
        {
            if (line.StartsWith("PROG:"))
            {
                var parts = line[5..].Split('|', 2);
                if (parts.Length == 2 && double.TryParse(parts[0], out double val))
                    OnProgress?.Invoke(val, parts[1]);
                return true;
            }
            if (line.StartsWith("TEXT:"))
            {
                OnText?.Invoke(line[5..]);
                return true;
            }
            if (line.StartsWith("POLISHED_TEXT:"))
            {
                OnPolishedText?.Invoke(line[14..]);
                return true;
            }
            if (line.StartsWith("CORRECTED_TEXT:"))
            {
                OnCorrectedText?.Invoke(line[15..]);
                return true;
            }
            if (line.StartsWith("RESULT:"))
            {
                OnResult?.Invoke(line[7..]);
                return true;
            }
            if (line.StartsWith("ERROR:"))
            {
                OnError?.Invoke(line[6..]);
                return true;
            }
            if (line.StartsWith("SRT_PATH:"))
            {
                OnSrtPath?.Invoke(line[9..]);
                return true;
            }
            return false;
        }

        public void Pause()
        {
            if (_process == null || _process.HasExited || _isPaused) return;
            try
            {
                NtSuspendProcess(_process.Handle);
                _isPaused = true;
                LoggerService.WarnLocalized("Log_PYTHON_BRIDGE_b7c803", "پردازش پایتون متوقف (Pause) شد.", "PYTHON_BRIDGE");
            }
            catch (Exception ex)
            {
                LoggerService.ErrorLocalized("Log_PYTHON_BRIDGE_0f6888", "خطا در مکث فرآیند: {0}", "PYTHON_BRIDGE", ex.Message);
            }
        }

        public void Resume()
        {
            if (_process == null || _process.HasExited || !_isPaused) return;
            try
            {
                NtResumeProcess(_process.Handle);
                _isPaused = false;
                LoggerService.InfoLocalized("Log_PYTHON_BRIDGE_580907", "پردازش پایتون ادامه یافت (Resume).", "PYTHON_BRIDGE");
            }
            catch (Exception ex)
            {
                LoggerService.ErrorLocalized("Log_PYTHON_BRIDGE_e919eb", "خطا در ادامه فرآیند: {0}", "PYTHON_BRIDGE", ex.Message);
            }
        }

        public void Stop()
        {
            if (_process == null || _process.HasExited) return;
            try
            {
                if (_isPaused) Resume();
                _process.Kill(entireProcessTree: true);
                LoggerService.WarnLocalized("Log_PYTHON_BRIDGE_fcee1f", "پردازش پایتون متوقف (Kill) شد.", "PYTHON_BRIDGE");
            }
            catch (Exception ex)
            {
                LoggerService.ErrorLocalized("Log_PYTHON_BRIDGE_e1340e", "خطا در لغو فرآیند: {0}", "PYTHON_BRIDGE", ex.Message);
            }
        }

        public static string FindPythonExecutable()
        {
            var settings = AppSettings.Load();
            if (!string.IsNullOrEmpty(settings.PythonPath) && (File.Exists(settings.PythonPath) || settings.PythonPath.Equals("python", StringComparison.OrdinalIgnoreCase) || settings.PythonPath.Equals("py", StringComparison.OrdinalIgnoreCase)))
            {
                return settings.PythonPath;
            }

            string[] candidates = { "python", "py", "python3" };

            foreach (var cmd in candidates)
            {
                try
                {
                    var p = Process.Start(new ProcessStartInfo
                    {
                        FileName = cmd,
                        Arguments = "--version",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    });
                    p?.WaitForExit(1500);
                    if (p != null && p.ExitCode == 0)
                    {
                        settings.PythonPath = cmd;
                        settings.Save();
                        return cmd;
                    }
                }
                catch (Exception ex)
                {
                    LoggerService.WarnLocalized("Log_PYTHON_BRIDGE_31d98f", "تست مسیر پایتون ({0}) متوقف شد: {1}", "PYTHON_BRIDGE", cmd, ex.Message);
                }
            }

            return "python";
        }
    }
}
