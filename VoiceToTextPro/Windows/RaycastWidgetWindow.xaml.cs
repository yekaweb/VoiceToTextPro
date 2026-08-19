using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using VoiceToTextPro.Services;

namespace VoiceToTextPro.Windows
{
    public partial class RaycastWidgetWindow : Window
    {
        private bool _isRecording = false;
        public bool AutoInjectToActiveWindow { get; set; } = true;

        private readonly DispatcherTimer _recordingTimer = new();
        private int _elapsedSeconds = 0;

        public RaycastWidgetWindow()
        {
            InitializeComponent();
            this.FlowDirection = FlowDirection.LeftToRight;

            _recordingTimer.Interval = TimeSpan.FromSeconds(1);
            _recordingTimer.Tick += RecordingTimer_Tick;

            MouseDown += RaycastWidgetWindow_MouseDown;
            KeyDown += RaycastWidgetWindow_KeyDown;

            LiveStreamingService.Instance.OnPartialTextReceived += Instance_OnPartialTextReceived;
            LiveStreamingService.Instance.OnFinalTextReceived += Instance_OnFinalTextReceived;
            LiveStreamingService.Instance.OnStatusMessage += Instance_OnStatusMessage;
            LiveAudioCaptureService.Instance.OnPeakVolumeChanged += AudioCapture_OnPeakVolumeChanged;
        }

        private void RecordingTimer_Tick(object? sender, EventArgs e)
        {
            _elapsedSeconds++;
            TimerText.Text = TimeSpan.FromSeconds(_elapsedSeconds).ToString(@"mm\:ss");
        }

        private void AudioCapture_OnPeakVolumeChanged(float volume)
        {
            if (_isRecording)
            {
                Dispatcher.InvokeAsync(() =>
                {
                    // 1. Real audio-reactive waveform amplitude
                    WaveVisualizer.UpdateAudioLevel(volume);

                    // 2. Real audio-reactive microphone outer bloom (0.45 .. 1.0)
                    MicOuterBloom.Opacity = Math.Clamp(0.45 + volume * 0.55, 0.45, 1.0);

                    // 3. Real audio-reactive glass light bleed (0.35 .. 0.85)
                    MicGlassBleedGlow.Opacity = Math.Clamp(0.35 + volume * 0.50, 0.35, 0.85);
                });
            }
        }

        private void RaycastWidgetWindow_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                Hide();
            }
        }

        private void RaycastWidgetWindow_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                DragMove();
            }
        }

        private async void RecordBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!_isRecording)
                {
                    await StartRecordingAsync();
                }
                else
                {
                    await StopRecordingAsync();
                }
            }
            catch (Exception ex) { LoggerService.Error($"Raycast RecordBtn: {ex.Message}", "RAYCAST"); }
        }

        private Models.AiModel? FindInstalledVoskModel()
        {
            try
            {
                string modelsDir = AppSettings.Load().ModelsDirectory;
                if (!System.IO.Directory.Exists(modelsDir)) return null;

                // Priority 1: Check for Persian models
                string[] preferredNames = new[] { "vosk-model-small-fa-0.5", "vosk-model-fa-0.5", "vosk-model-fa-0.42" };
                foreach (var name in preferredNames)
                {
                    string path = System.IO.Path.Combine(modelsDir, name);
                    if (System.IO.Directory.Exists(path))
                    {
                        return new Models.AiModel { Name = name, FolderName = name };
                    }
                }

                // Priority 2: Check any directory starting with vosk-model-
                var directories = System.IO.Directory.GetDirectories(modelsDir, "vosk-model-*");
                foreach (var dir in directories)
                {
                    string folderName = System.IO.Path.GetFileName(dir);
                    return new Models.AiModel { Name = folderName, FolderName = folderName };
                }

                // Priority 3: Fallback to any directory in modelsDir
                var allSubdirs = System.IO.Directory.GetDirectories(modelsDir);
                if (allSubdirs.Length > 0)
                {
                    string folderName = System.IO.Path.GetFileName(allSubdirs[0]);
                    return new Models.AiModel { Name = folderName, FolderName = folderName };
                }
            }
            catch (Exception ex)
            {
                LoggerService.ErrorLocalized("Log_RAYCAST_WIDGET_86e022", "خطا در یافتن مدل نصب‌شده: {0}", "RAYCAST_WIDGET", ex.Message);
            }

            return null;
        }

        private async System.Threading.Tasks.Task StartRecordingAsync()
        {
            try
            {
                var installedModel = FindInstalledVoskModel();
                if (installedModel == null)
                {
                    TranscriptBox.Text = "⚠️ هیچ مدلی یافت نشد. لطفاً از «مدیریت مدل‌ها» ابتدا یک مدل دانلود کنید.";
                    LoggerService.WarnLocalized("Log_RAYCAST_WIDGET_c5afa2", "ویجت شناور: هیچ مدل Vosk نصب‌شده‌ای روی دیسک یافت نشد.", "RAYCAST_WIDGET");
                    _isRecording = false;
                    return;
                }

                _isRecording = true;
                _elapsedSeconds = 0;
                TimerText.Text = "00:00";
                _recordingTimer.Start();

                TranscriptBox.Text = $"در حال راه‌اندازی مدل ({installedModel.Name})...";
                LoggerService.InfoLocalized("Log_RAYCAST_WIDGET_469417", "ویجت سریع در حال راه‌اندازی با مدل: {0}", "RAYCAST_WIDGET", installedModel.FolderName);

                await LiveStreamingService.Instance.StartStreamingAsync(installedModel, AudioSourceType.Microphone);
            }
            catch (Exception ex)
            {
                _recordingTimer.Stop();
                TranscriptBox.Text = $"⚠️ {ex.Message}";
                LoggerService.ErrorLocalized("Log_RAYCAST_WIDGET_04df2a", "خطا در شروع ویجت سریع: {0}", "RAYCAST_WIDGET", ex);
                _isRecording = false;
            }
        }

        private async System.Threading.Tasks.Task StopRecordingAsync()
        {
            try
            {
                _isRecording = false;
                _recordingTimer.Stop();
                WaveVisualizer.UpdateAudioLevel(0);
                MicOuterBloom.Opacity = 0.45;
                MicGlassBleedGlow.Opacity = 0.35;

                await LiveStreamingService.Instance.StopStreamingAsync();
                LoggerService.InfoLocalized("Log_RAYCAST_WIDGET_bc32d5", "ویجت سریع با موفقیت متوقف شد.", "RAYCAST_WIDGET");
            }
            catch (Exception ex)
            {
                LoggerService.ErrorLocalized("Log_RAYCAST_WIDGET_140346", "خطا در توقف ویجت: {0}", "RAYCAST_WIDGET", ex);
            }
        }

        private void Instance_OnPartialTextReceived(string text)
        {
            Dispatcher.Invoke(() =>
            {
                if (!string.IsNullOrWhiteSpace(text))
                {
                    TranscriptBox.Text = text;
                }
            });
        }

        private void Instance_OnFinalTextReceived(string text)
        {
            Dispatcher.Invoke(async () =>
            {
                if (!string.IsNullOrWhiteSpace(text))
                {
                    TranscriptBox.Text = text;

                    if (AutoInjectToActiveWindow)
                    {
                        LoggerService.InfoLocalized("Log_RAYCAST_WIDGET_7ef07b", "پردازش و تزریق ماکروی صوتی: '{0}'", "RAYCAST_WIDGET", text);
                        await VoiceMacroManager.Instance.ProcessVoiceTextAsync(text);
                    }
                }
            });
        }

        private void Instance_OnStatusMessage(string msg)
        {
            Dispatcher.Invoke(() =>
            {
                if (_isRecording && TranscriptBox.Text.StartsWith("در حال"))
                {
                    TranscriptBox.Text = msg;
                }
            });
        }

        private void CopyBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(TranscriptBox.Text))
                {
                    Clipboard.SetText(TranscriptBox.Text);
                    LoggerService.InfoLocalized("Log_RAYCAST_WIDGET_919e65", "متن ویجت سریع در حافظه کپی شد.", "RAYCAST_WIDGET");
                }
            }
            catch (Exception ex) { LoggerService.Error($"Raycast CopyBtn: {ex.Message}", "RAYCAST"); }
        }

        private void CloseWidgetBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _recordingTimer.Stop();
                Hide();
            }
            catch (Exception ex) { LoggerService.Error($"Raycast CloseWidget: {ex.Message}", "RAYCAST"); }
        }

        protected override void OnClosed(EventArgs e)
        {
            _recordingTimer.Stop();
            LiveStreamingService.Instance.OnPartialTextReceived -= Instance_OnPartialTextReceived;
            LiveStreamingService.Instance.OnFinalTextReceived -= Instance_OnFinalTextReceived;
            LiveStreamingService.Instance.OnStatusMessage -= Instance_OnStatusMessage;
            LiveAudioCaptureService.Instance.OnPeakVolumeChanged -= AudioCapture_OnPeakVolumeChanged;
            base.OnClosed(e);
        }
    }
}
