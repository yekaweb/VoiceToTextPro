using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using VoiceToTextPro.Models;
using VoiceToTextPro.Services;

namespace VoiceToTextPro.Tabs
{
    public partial class LiveTranscribeTab : UserControl
    {
        private bool _isStreamingActive = false;
        private readonly List<AiModel> _voskModels = new();

        public LiveTranscribeTab()
        {
            InitializeComponent();
            Loaded += LiveTranscribeTab_Loaded;
            Unloaded += LiveTranscribeTab_Unloaded;
        }

        private async void LiveTranscribeTab_Loaded(object sender, RoutedEventArgs e)
        {
            RefreshAudioDevices();
            await LoadInstalledModelsAsync();

            // Unsubscribe first to guarantee single event handling
            LiveAudioCaptureService.Instance.OnPeakVolumeChanged -= UpdateVolumeMeter;
            LiveStreamingService.Instance.OnPartialTextReceived -= HandlePartialText;
            LiveStreamingService.Instance.OnFinalTextReceived -= HandleFinalText;
            LiveStreamingService.Instance.OnStatusMessage -= UpdateStatusText;

            LiveAudioCaptureService.Instance.OnPeakVolumeChanged += UpdateVolumeMeter;
            LiveStreamingService.Instance.OnPartialTextReceived += HandlePartialText;
            LiveStreamingService.Instance.OnFinalTextReceived += HandleFinalText;
            LiveStreamingService.Instance.OnStatusMessage += UpdateStatusText;
        }

        private void LiveTranscribeTab_Unloaded(object sender, RoutedEventArgs e)
        {
            LiveAudioCaptureService.Instance.OnPeakVolumeChanged -= UpdateVolumeMeter;
            LiveStreamingService.Instance.OnPartialTextReceived -= HandlePartialText;
            LiveStreamingService.Instance.OnFinalTextReceived -= HandleFinalText;
            LiveStreamingService.Instance.OnStatusMessage -= UpdateStatusText;

            if (_isStreamingActive)
            {
                _ = LiveStreamingService.Instance.StopStreamingAsync();
                _isStreamingActive = false;
            }
        }

        private void SourceRadio_Click(object sender, RoutedEventArgs e)
        {
            try { RefreshAudioDevices(); }
            catch (Exception ex) { LoggerService.Error($"SourceRadio: {ex.Message}", "LIVE"); }
        }

        private void RefreshAudioDevices()
        {
            DeviceComboBox.ItemsSource = null;
            if (SystemAudioRadio.IsChecked == true)
            {
                var devices = LiveAudioCaptureService.Instance.GetSystemOutputDevices();
                DeviceComboBox.ItemsSource = devices;
                if (devices.Count > 0) DeviceComboBox.SelectedIndex = 0;
            }
            else
            {
                var devices = LiveAudioCaptureService.Instance.GetMicrophoneDevices();
                DeviceComboBox.ItemsSource = devices;
                if (devices.Count > 0) DeviceComboBox.SelectedIndex = 0;
            }
        }

        private async Task LoadInstalledModelsAsync()
        {
            _voskModels.Clear();
            ModelComboBox.ItemsSource = null;

            string targetDir = AppSettings.Load().ModelsDirectory;

            // Pre-defined Vosk and Whisper models list
            var catalog = new List<AiModel>
            {
                new AiModel { Name = "Vosk Persian (فارسی)", FolderName = "vosk-model-fa-0.5", Url = "https://alphacephei.com/vosk/models/vosk-model-fa-0.5.zip" },
                new AiModel { Name = "Vosk Small FA (فارسی سبک)", FolderName = "vosk-model-small-fa-0.5", Url = "https://alphacephei.com/vosk/models/vosk-model-small-fa-0.5.zip" },
                new AiModel { Name = "Vosk Small EN (انگلیسی)", FolderName = "vosk-model-small-en-us-0.15", Url = "https://alphacephei.com/vosk/models/vosk-model-small-en-us-0.15.zip" },
                
                // Whisper Multilingual Live Models
                new AiModel { Name = "✨ Whisper TINY (چندزبانه)", FolderName = "faster-whisper-tiny", Url = "huggingface:Systran/faster-whisper-tiny" },
                new AiModel { Name = "✨ Whisper BASE (چندزبانه)", FolderName = "faster-whisper-base", Url = "huggingface:Systran/faster-whisper-base" },
                new AiModel { Name = "✨ Whisper SMALL (چندزبانه)", FolderName = "faster-whisper-small", Url = "huggingface:Systran/faster-whisper-small" },
                new AiModel { Name = "✨ Whisper MEDIUM (چندزبانه)", FolderName = "faster-whisper-medium", Url = "huggingface:Systran/faster-whisper-medium" },
                new AiModel { Name = "✨ Whisper LARGE-V3 (چندزبانه دوقدرته)", FolderName = "faster-whisper-large-v3", Url = "huggingface:Systran/faster-whisper-large-v3" },
                new AiModel { Name = "✨ Whisper LARGE-V3-TURBO (چندزبانه توربو)", FolderName = "faster-whisper-large-v3-turbo", Url = "huggingface:Systran/faster-whisper-large-v3-turbo" }
            };

            // Dynamically scan model directory for any additional installed folders
            if (Directory.Exists(targetDir))
            {
                var subDirs = Directory.GetDirectories(targetDir);
                foreach (var dir in subDirs)
                {
                    string folderName = Path.GetFileName(dir);
                    if (!catalog.Any(m => m.FolderName.Equals(folderName, StringComparison.OrdinalIgnoreCase)))
                    {
                        catalog.Add(new AiModel
                        {
                            Name = folderName.StartsWith("faster-whisper") ? $"✨ Whisper ({folderName})" : $"Vosk ({folderName})",
                            FolderName = folderName,
                            Url = ""
                        });
                    }
                }
            }

            foreach (var model in catalog)
            {
                var status = ModelDownloadManager.Instance.VerifyModelIntegrity(model, targetDir);
                model.Status = status;
                if (status == ModelStatus.Installed)
                {
                    _voskModels.Add(model);
                }
            }

            ModelComboBox.ItemsSource = _voskModels;
            if (_voskModels.Count > 0)
            {
                ModelComboBox.SelectedIndex = 0;
            }
            else
            {
                UpdateStatusText("هیچ مدل زبانی بر روی سیستم نصب نیست. لطفا ابتدا از تب مدیریت مدل‌ها، یک مدل را دریافت کنید.");
            }
            await Task.CompletedTask;
        }

        private async void StartStopBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_isStreamingActive)
                {
                    // Stop Streaming
                    await LiveStreamingService.Instance.StopStreamingAsync();
                    SetStreamingUiState(false);
                }
                else
                {
                    // Start Streaming
                    if (ModelComboBox.SelectedItem is not AiModel selectedModel)
                    {
                        ModernDialogService.ShowWarning(LanguageManager.Instance.GetString("Msg_SelectModelFirst", "لطفا ابتدا یک مدل زبانی (Vosk یا Whisper) نصب‌شده را انتخاب کنید."));
                        return;
                    }

                    var sourceType = (SystemAudioRadio.IsChecked == true) ? AudioSourceType.SystemAudio : AudioSourceType.Microphone;
                    string? deviceId = (DeviceComboBox.SelectedItem is AudioDeviceInfo dev) ? dev.Id : null;

                    try
                    {
                        await LiveStreamingService.Instance.StartStreamingAsync(selectedModel, sourceType, deviceId);
                        SetStreamingUiState(true);
                    }
                    catch (Exception ex)
                    {
                        ModernDialogService.ShowError(LanguageManager.Instance.GetFormattedString("Msg_LiveStreamError", "راه‌اندازی استریم زنده با خطا مواجه شد:\n{0}", ex.Message));
                        SetStreamingUiState(false);
                    }
                }
            }
            catch (Exception ex) { LoggerService.Error($"StartStopBtn: {ex.Message}", "LIVE"); }
        }

        private void SetStreamingUiState(bool active)
        {
            _isStreamingActive = active;
            Dispatcher.Invoke(() =>
            {
                if (active)
                {
                    StartStopText.Text = "توقف استریم زنده";
                    StartStopIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.StopCircle;
                    StatusDotIcon.Foreground = new SolidColorBrush(Color.FromRgb(16, 185, 129)); // Emerald Green
                    DeviceComboBox.IsEnabled = false;
                    ModelComboBox.IsEnabled = false;
                    SystemAudioRadio.IsEnabled = false;
                    MicrophoneRadio.IsEnabled = false;
                }
                else
                {
                    StartStopText.Text = "شروع استریم زنده";
                    StartStopIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.RecordRec;
                    StatusDotIcon.Foreground = new SolidColorBrush(Color.FromRgb(107, 114, 128)); // Gray
                    DeviceComboBox.IsEnabled = true;
                    ModelComboBox.IsEnabled = true;
                    SystemAudioRadio.IsEnabled = true;
                    MicrophoneRadio.IsEnabled = true;
                    GlowVisualizer.UpdateAudioLevel(0);
                    InterimDraftTextBlock.Text = "منتظر دریافت صدا...";
                }
            });
        }

        private void UpdateVolumeMeter(float peakVolume)
        {
            Dispatcher.BeginInvoke(() =>
            {
                GlowVisualizer.UpdateAudioLevel(peakVolume);
            });
        }

        private void HandlePartialText(string partialText)
        {
            Dispatcher.BeginInvoke(() =>
            {
                InterimDraftTextBlock.Text = $"... {partialText}";
            });
        }

        private void HandleFinalText(string finalText)
        {
            Dispatcher.BeginInvoke(() =>
            {
                InterimDraftTextBlock.Text = "...";

                string sourceBadge = (SystemAudioRadio.IsChecked == true) ? "[🔊 سیستم]" : "[🎙️ میکروفن]";
                string timestamp = DateTime.Now.ToString("HH:mm:ss");
                string formattedLine = $"[{timestamp}] {sourceBadge} {finalText}\n";

                LiveCanvasTextBox.AppendText(formattedLine);

                if (AutoScrollCheckBox.IsChecked == true)
                {
                    LiveCanvasTextBox.ScrollToEnd();
                }
            });
        }

        private void UpdateStatusText(string statusMsg)
        {
            Dispatcher.BeginInvoke(() =>
            {
                StatusTextBlock.Text = statusMsg;
            });
        }

        private void CopyText_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(LiveCanvasTextBox.Text))
                {
                    ModernDialogService.ShowInfo(LanguageManager.Instance.GetString("Msg_CanvasEmpty", "بوم متنی خالی است."));
                    return;
                }
                Clipboard.SetText(LiveCanvasTextBox.Text);
                ModernDialogService.ShowInfo(LanguageManager.Instance.GetString("Msg_TextCopied", "متن بوم زنده با موفقیت در حافظه کپی شد."));
            }
            catch (Exception ex) { LoggerService.Error($"CopyText: {ex.Message}", "LIVE"); }
        }

        private void ExportText_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(LiveCanvasTextBox.Text))
                {
                    ModernDialogService.ShowInfo(LanguageManager.Instance.GetString("Msg_CanvasEmpty", "بوم متنی خالی است."));
                    return;
                }

                var dlg = new SaveFileDialog
                {
                    Filter = "فایل متنی (*.txt)|*.txt|فایل زیرنویس (*.srt)|*.srt",
                    FileName = $"LiveTranscribe_{DateTime.Now:yyyyMMdd_HHmmss}.txt"
                };

                if (dlg.ShowDialog() == true)
                {
                    File.WriteAllText(dlg.FileName, LiveCanvasTextBox.Text, System.Text.Encoding.UTF8);
                    ModernDialogService.ShowInfo(LanguageManager.Instance.GetFormattedString("Msg_OutputSaved", "خروجی با موفقیت در فایل ذخیره شد:\n{0}", dlg.FileName));
                }
            }
            catch (Exception ex) { LoggerService.Error($"ExportText: {ex.Message}", "LIVE"); }
        }

        private void ClearCanvas_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (ModernDialogService.AskConfirmation(LanguageManager.Instance.GetString("Msg_ConfirmClearCanvas", "آیا از پاکسازی متن بوم مطمئن هستید؟")))
                {
                    LiveCanvasTextBox.Clear();
                    InterimDraftTextBlock.Text = "منتظر دریافت صدا...";
                }
            }
            catch (Exception ex) { LoggerService.Error($"ClearCanvas: {ex.Message}", "LIVE"); }
        }
    }
}
