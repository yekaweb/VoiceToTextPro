using System;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using NAudio.Wave;
using VoiceToTextPro.Services;

namespace VoiceToTextPro.Tabs
{
    public partial class TranscribeTab : UserControl, IDisposable
    {
        public void Dispose()
        {
            DisposeAudioPlayer();
        }
        private PythonBridge? _bridge;
        private string? _currentSrtPath;
        private string? _audioFilePath;

        // NAudio Player
        private AudioFileReader? _audioReader;
        private WaveOutEvent? _waveOut;
        private DispatcherTimer? _playTimer;
        private bool _isUpdatingSlider = false;

        public TranscribeTab()
        {
            InitializeComponent();

            _playTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            _playTimer.Tick += PlayTimer_Tick;
            Unloaded += (s, e) => DisposeAudioPlayer();
        }

        public void LoadFile(string path)
        {
            if (!File.Exists(path)) return;
            _audioFilePath = path;
            FilePathBox.Text = path;
            InitAudioPlayer(path);
            LoggerService.InfoLocalized("Log_TRANSCRIBE_d1b6be", "فایل جهت رونویسی بارگذاری شد: {0}", "TRANSCRIBE", System.IO.Path.GetFileName(path));
        }

        private void Browse_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dlg = new OpenFileDialog
                {
                    Filter = "فایل‌های صوتی و تصویری|*.mp3;*.wav;*.m4a;*.flac;*.ogg;*.mp4;*.mkv;*.avi;*.mov|همه فایل‌ها|*.*"
                };
                if (dlg.ShowDialog() == true)
                    LoadFile(dlg.FileName);
            }
            catch (Exception ex) { LoggerService.Error($"Browse: {ex.Message}", "TRANSCRIBE"); }
        }

        private async void Start_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string file = FilePathBox.Text.Trim();
                if (string.IsNullOrEmpty(file) || !File.Exists(file))
                {
                    ModernDialogService.ShowWarning(LanguageManager.Instance.GetString("Msg_SelectValidAudioFile", "لطفاً یک فایل صوتی یا تصویری معتبر انتخاب کنید."));
                    return;
                }

                StartBtn.IsEnabled = false;
                StopBtn.IsEnabled = true;
                SendToSubBtn.IsEnabled = false;
                ResultTextBox.Clear();
                ProgressBarCtrl.Value = 0;
                ProgressPercentText.Text = "0%";
                ProgressStatusText.Text = "در حال شروع موتور پایتون...";

                string lang = (LangCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "fa-IR";
                var settings = AppSettings.Load();
                string outDir = settings.OutputDirectory;
                Directory.CreateDirectory(outDir);

                _bridge = new PythonBridge();

                _bridge.OnProgress += (percent, msg) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        ProgressBarCtrl.Value = percent;
                        ProgressPercentText.Text = $"{percent:0}%";
                        ProgressStatusText.Text = msg;
                    });
                };

                _bridge.OnText += (chunkText) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        ResultTextBox.AppendText(chunkText + "\n");
                        ResultTextBox.ScrollToEnd();
                    });
                };

                _bridge.OnPolishedText += (finalText) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        ResultTextBox.Text = finalText;
                        LoggerService.SuccessLocalized("Log_TRANSCRIBE_2adfad", "پالایش هوشمند متن با موفقیت انجام شد.", "TRANSCRIBE");
                    });
                };

                _bridge.OnSrtPath += (srtPath) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        _currentSrtPath = srtPath;
                        SendToSubBtn.IsEnabled = true;
                        LoggerService.SuccessLocalized("Log_TRANSCRIBE_478d47", "فایل زیرنویس ساخته شد: {0}", "TRANSCRIBE", srtPath);
                    });
                };

                _bridge.OnError += (err) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        LoggerService.Error(err, "TRANSCRIBE");
                    });
                };

                LoggerService.InfoLocalized("Log_TRANSCRIBE_0a42c0", "شروع عملیات رونویسی: {0} ({1})", "TRANSCRIBE", System.IO.Path.GetFileName(file), lang);

                bool success = await _bridge.RunAsync("transcribe", file, lang, outDir);

                Dispatcher.Invoke(() =>
                {
                    StartBtn.IsEnabled = true;
                    StopBtn.IsEnabled = false;
                    if (success)
                    {
                        ProgressStatusText.Text = "عملیات رونویسی با موفقیت تکمیل شد.";
                        ProgressBarCtrl.Value = 100;
                        ProgressPercentText.Text = "100%";
                        LoggerService.SuccessLocalized("Log_TRANSCRIBE_d63f31", "عملیات رونویسی تمام شد.", "TRANSCRIBE");
                    }
                    else
                    {
                        ProgressStatusText.Text = "عملیات متوقف شد یا با خطا مواجه گردید.";
                    }
                });
            }
            catch (Exception ex) { LoggerService.Error($"Start_Click: {ex.Message}", "TRANSCRIBE"); }
        }

        private void Stop_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _bridge?.Stop();
                StartBtn.IsEnabled = true;
                StopBtn.IsEnabled = false;
                ProgressStatusText.Text = "عملیات توسط کاربر متوقف شد.";
                LoggerService.WarnLocalized("Log_TRANSCRIBE_bf4b36", "عملیات رونویسی متوقف شد.", "TRANSCRIBE");
            }
            catch (Exception ex) { LoggerService.Error($"Stop: {ex.Message}", "TRANSCRIBE"); }
        }

        private void SendToSub_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!string.IsNullOrEmpty(_currentSrtPath) && File.Exists(_currentSrtPath))
                    AppEventBus.RaiseSrtReady(_currentSrtPath);
            }
            catch (Exception ex) { LoggerService.Error($"SendToSub: {ex.Message}", "TRANSCRIBE"); }
        }

        private void SaveTxt_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(ResultTextBox.Text)) return;
                var dlg = new SaveFileDialog { Filter = "Text File|*.txt", DefaultExt = ".txt" };
                if (dlg.ShowDialog() == true)
                {
                    File.WriteAllText(dlg.FileName, ResultTextBox.Text, System.Text.Encoding.UTF8);
                    LoggerService.SuccessLocalized("Log_TRANSCRIBE_e6b40d", "متن رونویسی ذخیره شد: {0}", "TRANSCRIBE", dlg.FileName);
                }
            }
            catch (Exception ex) { LoggerService.Error($"SaveTxt: {ex.Message}", "TRANSCRIBE"); }
        }

        private void SaveSrt_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (string.IsNullOrEmpty(_currentSrtPath) || !File.Exists(_currentSrtPath)) return;
                var dlg = new SaveFileDialog { Filter = "SRT Subtitle|*.srt", DefaultExt = ".srt" };
                if (dlg.ShowDialog() == true)
                {
                    File.Copy(_currentSrtPath, dlg.FileName, overwrite: true);
                    LoggerService.SuccessLocalized("Log_TRANSCRIBE_910b49", "فایل SRT ذخیره شد: {0}", "TRANSCRIBE", dlg.FileName);
                }
            }
            catch (Exception ex) { LoggerService.Error($"SaveSrt: {ex.Message}", "TRANSCRIBE"); }
        }

        private void CopyText_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!string.IsNullOrEmpty(ResultTextBox.Text))
                {
                    Clipboard.SetText(ResultTextBox.Text);
                    LoggerService.InfoLocalized("Log_TRANSCRIBE_9c95ec", "متن رونویسی کپی شد.", "TRANSCRIBE");
                }
            }
            catch (Exception ex) { LoggerService.Error($"CopyText: {ex.Message}", "TRANSCRIBE"); }
        }

        // ══ NAudio Player Logic ══
        private void InitAudioPlayer(string path)
        {
            try
            {
                DisposeAudioPlayer();
                _audioReader = new AudioFileReader(path);
                _waveOut = new WaveOutEvent();
                _waveOut.Init(_audioReader);
                _waveOut.Volume = (float)VolumeSlider.Value;
                DrawWaveform();
            }
            catch (Exception ex)
            {
                LoggerService.WarnLocalized("Log_AUDIO_PLAYER_97a6cc", "عدم توانایی در ایجاد نمایشگر صوتی: {0}", "AUDIO_PLAYER", ex.Message);
            }
        }

        private void DrawWaveform()
        {
            WaveCanvas.Children.Clear();
            double width = WaveCanvas.ActualWidth;
            double height = WaveCanvas.ActualHeight;

            if (width <= 0 || height <= 0) return;

            // Simple decorative waveform visualization
            var rand = new Random(42);
            int barCount = (int)(width / 4);
            double centerY = height / 2;

            for (int i = 0; i < barCount; i++)
            {
                double h = rand.NextDouble() * (height * 0.7);
                var line = new Line
                {
                    X1 = i * 4,
                    Y1 = centerY - (h / 2),
                    X2 = i * 4,
                    Y2 = centerY + (h / 2),
                    Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981")),
                    StrokeThickness = 2
                };
                WaveCanvas.Children.Add(line);
            }
        }

        private void WaveCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => DrawWaveform();

        private void PlayPause_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_waveOut == null) return;
                if (_waveOut.PlaybackState == PlaybackState.Playing)
                {
                    _waveOut.Pause();
                    _playTimer?.Stop();
                    PlayIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.Play;
                }
                else
                {
                    _waveOut.Play();
                    _playTimer?.Start();
                    PlayIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.Pause;
                }
            }
            catch (Exception ex) { LoggerService.Error($"PlayPause: {ex.Message}", "AUDIO"); }
        }

        private void StopAudio_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_waveOut == null) return;
                _waveOut.Stop();
                if (_audioReader != null) _audioReader.Position = 0;
                _playTimer?.Stop();
                PlayIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.Play;
                UpdateTimeLabel();
            }
            catch (Exception ex) { LoggerService.Error($"StopAudio: {ex.Message}", "AUDIO"); }
        }

        private void PlayTimer_Tick(object? sender, EventArgs e)
        {
            if (_audioReader == null) return;
            double pct = (_audioReader.CurrentTime.TotalSeconds / _audioReader.TotalTime.TotalSeconds) * 100;
            
            _isUpdatingSlider = true;
            SeekSlider.Value = pct;
            _isUpdatingSlider = false;
            
            UpdateTimeLabel();
        }

        private void UpdateTimeLabel()
        {
            if (_audioReader == null) return;
            TimeLabel.Text = $"{_audioReader.CurrentTime:mm\\:ss} / {_audioReader.TotalTime:mm\\:ss}";
        }

        private void SeekSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_audioReader == null || _isUpdatingSlider) return;
            
            double targetSec = (_audioReader.TotalTime.TotalSeconds * e.NewValue) / 100;
            
            // Only seek if the difference is more than 0.5 seconds to avoid micro-stutters during normal playback
            if (Math.Abs(_audioReader.CurrentTime.TotalSeconds - targetSec) > 0.5)
            {
                _audioReader.CurrentTime = TimeSpan.FromSeconds(targetSec);
            }
        }

        private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_waveOut != null)
                _waveOut.Volume = (float)e.NewValue;
        }

        private void DisposeAudioPlayer()
        {
            _playTimer?.Stop();
            _waveOut?.Stop();
            _waveOut?.Dispose();
            _audioReader?.Dispose();
            _waveOut = null;
            _audioReader = null;
        }
    }
}
