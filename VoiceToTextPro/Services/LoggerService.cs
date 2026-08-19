using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Windows;

namespace VoiceToTextPro.Services
{
    public enum LogLevel
    {
        Info,
        Warning,
        Error,
        Python,
        Success
    }

    public class LogEntry : System.ComponentModel.INotifyPropertyChanged
    {
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public LogLevel Level { get; set; }
        public string Source { get; set; } = "SYSTEM";

        public string? ResourceKey { get; set; }
        public string? FallbackMessage { get; set; }
        public object[]? Args { get; set; }

        private string _rawMessage = "";
        public string Message
        {
            get
            {
                if (!string.IsNullOrEmpty(ResourceKey))
                {
                    return LanguageManager.Instance.GetFormattedString(ResourceKey, FallbackMessage ?? "", Args ?? Array.Empty<object>());
                }
                return _rawMessage;
            }
            set
            {
                _rawMessage = value;
                OnPropertyChanged(nameof(Message));
            }
        }

        public string TimeFormatted => Timestamp.ToString("HH:mm:ss.fff");

        public string LevelBadge => Level switch
        {
            LogLevel.Info => "[INFO]",
            LogLevel.Warning => "[WARN]",
            LogLevel.Error => "[ERR!]",
            LogLevel.Python => "[PYTH]",
            LogLevel.Success => "[OK!!]",
            _ => "[LOG]"
        };

        public string ColorHex => Level switch
        {
            LogLevel.Info => "#38BDF8",      // Sky Blue
            LogLevel.Warning => "#FBBF24",   // Amber
            LogLevel.Error => "#F87171",     // Rose/Red
            LogLevel.Python => "#C084FC",    // Purple
            LogLevel.Success => "#34D399",   // Emerald
            _ => "#94A3B8"
        };

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        public void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
    }

    public static class LoggerService
    {
        private static readonly object _lock = new();
        private static readonly string LogFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
        private static readonly string LogFilePath = Path.Combine(LogFolder, $"app_{DateTime.Now:yyyyMMdd}.log");
        private static readonly System.Collections.Concurrent.BlockingCollection<string> _logQueue = new();

        public static ObservableCollection<LogEntry> Entries { get; } = new();
        public static event Action<LogEntry>? OnLogAdded;

        static LoggerService()
        {
            try
            {
                Directory.CreateDirectory(LogFolder);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"خطا در ساخت پوشه لاگ: {ex.Message}");
            }

            LanguageManager.Instance.LanguageChanged += (s, e) =>
            {
                Application.Current?.Dispatcher?.InvokeAsync(() =>
                {
                    foreach (var entry in Entries)
                    {
                        if (!string.IsNullOrEmpty(entry.ResourceKey))
                            entry.OnPropertyChanged(nameof(entry.Message));
                    }
                });
            };

            // Dedicated background logging thread
            var logThread = new System.Threading.Thread(ProcessLogQueue)
            {
                IsBackground = true,
                Name = "LoggerService_WriterThread"
            };
            logThread.Start();
        }

        private static void ProcessLogQueue()
        {
            foreach (var logLine in _logQueue.GetConsumingEnumerable())
            {
                try
                {
                    File.AppendAllText(LogFilePath, logLine + Environment.NewLine, Encoding.UTF8);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"خطا در نوشتن فایل لاگ: {ex.Message}");
                }
            }
        }

        public static void Log(string message, LogLevel level = LogLevel.Info, string source = "SYSTEM")
        {
            var entry = new LogEntry
            {
                Timestamp = DateTime.Now,
                Level = level,
                Source = source,
                Message = message
            };

            Application.Current?.Dispatcher?.InvokeAsync(() =>
            {
                lock (_lock)
                {
                    if (Entries.Count > 500)
                        Entries.RemoveAt(0);

                    Entries.Add(entry);
                }
                OnLogAdded?.Invoke(entry);
            });

            _logQueue.Add($"[{entry.TimeFormatted}] [{entry.Source}] {entry.LevelBadge} {entry.Message}");
        }

        public static void LogLocalized(string key, string fallback, LogLevel level = LogLevel.Info, string source = "SYSTEM", params object[] args)
        {
            var entry = new LogEntry
            {
                Timestamp = DateTime.Now,
                Level = level,
                Source = source,
                ResourceKey = key,
                FallbackMessage = fallback,
                Args = args
            };

            Application.Current?.Dispatcher?.InvokeAsync(() =>
            {
                lock (_lock)
                {
                    if (Entries.Count > 500)
                        Entries.RemoveAt(0);

                    Entries.Add(entry);
                }
                OnLogAdded?.Invoke(entry);
            });

            // For file output, evaluate once
            string evalMsg = LanguageManager.Instance.GetFormattedString(key, fallback, args);
            _logQueue.Add($"[{entry.TimeFormatted}] [{entry.Source}] {entry.LevelBadge} {evalMsg}");
        }

        public static void Info(string msg, string source = "SYSTEM") => Log(msg, LogLevel.Info, source);
        public static void Warn(string msg, string source = "SYSTEM") => Log(msg, LogLevel.Warning, source);
        public static void Error(string msg, string source = "SYSTEM") => Log(msg, LogLevel.Error, source);
        public static void Python(string msg) => Log(msg, LogLevel.Python, "PYTHON");
        public static void Success(string msg, string source = "SYSTEM") => Log(msg, LogLevel.Success, source);

        public static void InfoLocalized(string key, string fallback, string source = "SYSTEM", params object[] args) => LogLocalized(key, fallback, LogLevel.Info, source, args);
        public static void WarnLocalized(string key, string fallback, string source = "SYSTEM", params object[] args) => LogLocalized(key, fallback, LogLevel.Warning, source, args);
        public static void ErrorLocalized(string key, string fallback, string source = "SYSTEM", params object[] args) => LogLocalized(key, fallback, LogLevel.Error, source, args);
        public static void SuccessLocalized(string key, string fallback, string source = "SYSTEM", params object[] args) => LogLocalized(key, fallback, LogLevel.Success, source, args);

        public static void UpdateLastLog(string msg, string source = "SYSTEM", LogLevel level = LogLevel.Info)
        {
            Application.Current?.Dispatcher?.InvokeAsync(() =>
            {
                lock (_lock)
                {
                    if (Entries.Count > 0 && Entries[Entries.Count - 1].Source == source)
                    {
                        var last = Entries[Entries.Count - 1];
                        last.Message = msg;
                        last.Level = level;
                        // Replace to trigger UI update
                        Entries[Entries.Count - 1] = last;
                    }
                    else
                    {
                        Log(msg, level, source);
                    }
                }
            });
        }

        public static void Clear()
        {
            Application.Current?.Dispatcher?.Invoke(() => Entries.Clear());
        }

        public static string ExportAll()
        {
            var sb = new StringBuilder();
            foreach (var item in Entries)
            {
                sb.AppendLine($"[{item.TimeFormatted}] [{item.Source}] {item.LevelBadge} {item.Message}");
            }
            return sb.ToString();
        }
    }
}
