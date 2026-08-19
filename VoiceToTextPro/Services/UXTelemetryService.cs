using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace VoiceToTextPro.Services
{
    public class TelemetryMetrics
    {
        public double CpuUsagePercent { get; set; }
        public long MemoryWorkingSetMb { get; set; }
        public double UiDispatchLagMs { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }

    public static class UXTelemetryService
    {
        private static CancellationTokenSource? _cts;
        private static bool _isRunning;
        private static readonly object _lock = new();

        public static event Action<TelemetryMetrics>? OnTelemetryUpdated;
        public static event Action<string>? OnPerformanceAlert;

        public static TelemetryMetrics CurrentMetrics { get; private set; } = new();

        public static void Start(int pollingIntervalMs = 2000)
        {
            lock (_lock)
            {
                if (_isRunning) return;
                _isRunning = true;
                _cts = new CancellationTokenSource();
            }

            LoggerService.InfoLocalized("Log_UXTelemetryStarted", "سرویس تلمتری و پایش سلامت UX فعال شد.", "UX_TELEMETRY");

            Task.Run(async () =>
            {
                var process = Process.GetCurrentProcess();
                var lastCpuTime = process.TotalProcessorTime;
                var lastTime = DateTime.UtcNow;

                while (_cts != null && !_cts.Token.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay(pollingIntervalMs, _cts.Token);

                        // 1. Measure Memory
                        process.Refresh();
                        long memoryMb = process.WorkingSet64 / (1024 * 1024);

                        // 2. Measure CPU
                        var curTime = DateTime.UtcNow;
                        var curCpuTime = process.TotalProcessorTime;
                        var timeDiff = (curTime - lastTime).TotalMilliseconds;
                        var cpuDiff = (curCpuTime - lastCpuTime).TotalMilliseconds;

                        double cpuPercent = 0.0;
                        if (timeDiff > 0)
                        {
                            cpuPercent = (cpuDiff / (timeDiff * Environment.ProcessorCount)) * 100.0;
                        }

                        lastCpuTime = curCpuTime;
                        lastTime = curTime;

                        // 3. Measure UI Thread Lag
                        double uiLagMs = await MeasureUiLagAsync();

                        var metrics = new TelemetryMetrics
                        {
                            CpuUsagePercent = Math.Round(cpuPercent, 1),
                            MemoryWorkingSetMb = memoryMb,
                            UiDispatchLagMs = Math.Round(uiLagMs, 1),
                            Timestamp = DateTime.Now
                        };

                        CurrentMetrics = metrics;
                        OnTelemetryUpdated?.Invoke(metrics);

                        // Check threshold alerts
                        if (uiLagMs > 250)
                        {
                            string alert = $"کندی در لایه رابط کاربری (UI Lag): {uiLagMs:F0}ms";
                            LoggerService.Warn(alert, "UX_TELEMETRY");
                            OnPerformanceAlert?.Invoke(alert);
                        }

                        if (memoryMb > 1500)
                        {
                            string alert = $"مصرف حافظه بالا (RAM Usage): {memoryMb}MB";
                            LoggerService.Warn(alert, "UX_TELEMETRY");
                            OnPerformanceAlert?.Invoke(alert);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        LoggerService.ErrorLocalized("Log_UX_TELEMETRY_d0a659", "خطا در سرویس تلمتری: {0}", "UX_TELEMETRY", ex.Message);
                    }
                }
            });
        }

        private static async Task<double> MeasureUiLagAsync()
        {
            var app = Application.Current;
            if (app?.Dispatcher == null) return 0.0;

            var sw = Stopwatch.StartNew();
            var tcs = new TaskCompletionSource<bool>();

            await app.Dispatcher.InvokeAsync(() =>
            {
                sw.Stop();
                tcs.SetResult(true);
            });

            await tcs.Task;
            return sw.Elapsed.TotalMilliseconds;
        }

        public static void Stop()
        {
            lock (_lock)
            {
                if (!_isRunning) return;
                _isRunning = false;
                _cts?.Cancel();
                _cts = null;
            }
            LoggerService.InfoLocalized("Log_UXTelemetryStopped", "سرویس تلمتری غیرفعال شد.", "UX_TELEMETRY");
        }
    }
}
