using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using VoiceToTextPro.Services;

namespace VoiceToTextPro.Tabs
{
    public partial class VoiceConverterTab : UserControl
    {
        private string? _sourceAudioPath;
        private string? _convertedAudioPath;

        private readonly MediaPlayer _sourcePlayer = new();
        private readonly MediaPlayer _convertedPlayer = new();
        private readonly DispatcherTimer _timer = new();

        private bool _isSourceSeeking = false;
        private bool _isConvertedSeeking = false;

        public VoiceConverterTab()
        {
            InitializeComponent();

            _timer.Interval = TimeSpan.FromMilliseconds(250);
            _timer.Tick += Timer_Tick;

            _sourcePlayer.MediaOpened += SourcePlayer_MediaOpened;
            _sourcePlayer.MediaEnded += SourcePlayer_MediaEnded;

            _convertedPlayer.MediaOpened += ConvertedPlayer_MediaOpened;
            _convertedPlayer.MediaEnded += ConvertedPlayer_MediaEnded;

            VoiceConverterService.Instance.OnStatusMessage += UpdateStatus;
            VoiceConverterService.Instance.OnProgressChanged += UpdateProgress;

            Loaded += VoiceConverterTab_Loaded;
            Unloaded += VoiceConverterTab_Unloaded;
        }

        private void VoiceConverterTab_Loaded(object sender, RoutedEventArgs e)
        {
            LoadVoiceProfiles();
            _timer.Start();
        }

        private void VoiceConverterTab_Unloaded(object sender, RoutedEventArgs e)
        {
            _timer.Stop();
            _sourcePlayer.Close();
            _convertedPlayer.Close();

            VoiceConverterService.Instance.OnStatusMessage -= UpdateStatus;
            VoiceConverterService.Instance.OnProgressChanged -= UpdateProgress;
        }

        private void LoadVoiceProfiles()
        {
            try
            {
                var profiles = VoiceConverterService.Instance.GetAvailableVoiceProfiles();
                VoiceProfileComboBox.ItemsSource = profiles;
                if (profiles.Count > 0) VoiceProfileComboBox.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                LoggerService.ErrorLocalized("Log_V2V_PROFILES_ERR", "خطا در دریافت پروفایل‌های گوینده: {0}", "V2V_TAB", ex.Message);
            }
        }

        private void VoiceProfileComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (VoiceProfileComboBox.SelectedItem is VoiceProfileInfo profile)
            {
                SelectedProfileInfoText.Text = $"صدای مرجع گوینده فعال: {profile.Name} ({profile.Language})";
            }
        }

        private void RefreshProfiles_Click(object sender, RoutedEventArgs e)
        {
            LoadVoiceProfiles();
            UpdateStatus("لیست گویندگان به‌روزرسانی شد.");
        }

        private void RecordProfile_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "Audio Sample (*.wav;*.mp3)|*.wav;*.mp3|All Files (*.*)|*.*",
                Title = "انتخاب عینه صوتی مرجع گوینده جهت شبیه‌سازی"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                try
                {
                    string baseDir = AppSettings.Load().ModelsDirectory;
                    string profilesDir = Path.Combine(baseDir, "voice_profiles");
                    Directory.CreateDirectory(profilesDir);

                    string profileName = $"Custom_Speaker_{DateTime.Now:yyyyMMdd_HHmmss}{Path.GetExtension(openFileDialog.FileName)}";
                    string targetPath = Path.Combine(profilesDir, profileName);
                    File.Copy(openFileDialog.FileName, targetPath, overwrite: true);

                    LoadVoiceProfiles();
                    UpdateStatus($"پروفایل صوتی جدید اضافه گردید: {profileName}");
                    ModernDialogService.ShowInfo($"پروفایل صوتی جدید با موفقیت ذخیره شد:\n{profileName}", "پروفایل گوینده");
                }
                catch (Exception ex)
                {
                    ModernDialogService.ShowError($"خطا در ذخیره پروفایل گوینده: {ex.Message}", "خطا");
                }
            }
        }

        private void UpdateStatus(string message)
        {
            Dispatcher.Invoke(() =>
            {
                StatusText.Text = message;
            });
        }

        private void UpdateProgress(int value)
        {
            Dispatcher.Invoke(() =>
            {
                ConvertProgressBar.Visibility = (value > 0 && value < 100) ? Visibility.Visible : Visibility.Collapsed;
                ConvertProgressBar.Value = value;
            });
        }

        #region Drag and Drop & File Browsing

        private void AudioDropZone_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files.Length > 0)
                {
                    SetSourceAudioFile(files[0]);
                }
            }
        }

        private void AudioDropZone_Click(object sender, MouseButtonEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "Audio Files (*.wav;*.mp3;*.m4a;*.flac;*.ogg)|*.wav;*.mp3;*.m4a;*.flac;*.ogg|All Files (*.*)|*.*",
                Title = "انتخاب فایل صوتی منبع"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                SetSourceAudioFile(openFileDialog.FileName);
            }
        }

        private void SetSourceAudioFile(string filePath)
        {
            if (!File.Exists(filePath)) return;

            _sourceAudioPath = filePath;
            DropZoneFileNameText.Text = Path.GetFileName(filePath);
            DropZoneSubText.Text = $"{new FileInfo(filePath).Length / 1024} KB • {Path.GetExtension(filePath).ToUpper()}";

            SourceAudioInfoText.Text = Path.GetFileName(filePath);
            _sourcePlayer.Open(new Uri(filePath));
            SourcePlayBtn.IsEnabled = true;
            SourceSeekSlider.IsEnabled = true;

            UpdateStatus($"فایل منبع بارگذاری شد: {Path.GetFileName(filePath)}");
        }

        #endregion

        #region V2V Conversion Execution

        private async void ConvertVoiceBtn_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_sourceAudioPath) || !File.Exists(_sourceAudioPath))
            {
                ModernDialogService.ShowWarning("لطفاً ابتدا یک فایل صوتی منبع انتخاب نمایید.", "فایل منبع انتخاب نشده");
                return;
            }

            string targetProfile = VoiceProfileComboBox.SelectedValue?.ToString() ?? "";
            int pitchShift = (int)PitchSlider.Value;
            bool denoise = DenoiseToggle.IsChecked ?? true;
            float blendRatio = (float)(AccentSlider.Value / 100.0);

            // Read selected engine mode
            V2VEngineMode engineMode = V2VEngineMode.Auto;
            if (EngineModeComboBox.SelectedItem is ComboBoxItem item && item.Tag is string tagStr)
            {
                Enum.TryParse(tagStr, out engineMode);
            }

            ConvertVoiceBtn.IsEnabled = false;
            ConvertProgressBar.Visibility = Visibility.Visible;
            ConvertProgressBar.Value = 10;

            try
            {
                string resultWav = await VoiceConverterService.Instance.ConvertVoiceAsync(
                    _sourceAudioPath,
                    targetProfile,
                    outputWavPath: "",
                    pitchShift: pitchShift,
                    denoise: denoise,
                    blendRatio: blendRatio,
                    engineMode: engineMode
                );

                _convertedAudioPath = resultWav;
                ConvertedAudioInfoText.Text = $"آماده پخش ({Path.GetFileName(resultWav)})";
                ConvertedStatusIcon.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00E676"));

                _convertedPlayer.Open(new Uri(resultWav));
                ConvertedPlayBtn.IsEnabled = true;
                ConvertedSeekSlider.IsEnabled = true;
                ExportWavBtn.IsEnabled = true;

                UpdateStatus("تبدیل هوشمند صدا با موفقیت پایان یافت!");
            }
            catch (Exception ex)
            {
                UpdateStatus($"خطا در تبدیل صدا: {ex.Message}");
                ModernDialogService.ShowError($"ارور در تبدیل صدا: {ex.Message}", "خطای موتور V2V");
            }
            finally
            {
                ConvertVoiceBtn.IsEnabled = true;
                ConvertProgressBar.Visibility = Visibility.Collapsed;
            }
        }

        private void ExportWavBtn_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_convertedAudioPath) || !File.Exists(_convertedAudioPath)) return;

            var saveFileDialog = new SaveFileDialog
            {
                Filter = "WAV Audio (*.wav)|*.wav",
                FileName = $"Converted_Voice_{DateTime.Now:yyyyMMdd_HHmmss}.wav",
                Title = "ذخیره فایل صوتی خروجی"
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                File.Copy(_convertedAudioPath, saveFileDialog.FileName, overwrite: true);
                ModernDialogService.ShowInfo($"فایل با موفقیت ذخیره گردید:\n{saveFileDialog.FileName}", "ذخیره فایل");
            }
        }

        #endregion

        #region Sliders & Fine-Tuning Events

        private void PitchSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (PitchValueText == null) return;
            int val = (int)e.NewValue;
            PitchValueText.Text = val == 0 ? "0 نیم‌گام (طبیعی)" : (val > 0 ? $"+{val} نیم‌گام (زیرتر)" : $"{val} نیم‌گام (بم‌تر)");
        }

        private void AccentSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (AccentValueText == null) return;
            AccentValueText.Text = $"{(int)e.NewValue}%";
        }

        #endregion

        #region Audio Players & Timers

        private void Timer_Tick(object? sender, EventArgs e)
        {
            if (_sourcePlayer.NaturalDuration.HasTimeSpan && !_isSourceSeeking)
            {
                SourceSeekSlider.Maximum = _sourcePlayer.NaturalDuration.TimeSpan.TotalSeconds;
                SourceSeekSlider.Value = _sourcePlayer.Position.TotalSeconds;
                SourceTimeText.Text = $"{_sourcePlayer.Position:mm\\:ss} / {_sourcePlayer.NaturalDuration.TimeSpan:mm\\:ss}";
            }

            if (_convertedPlayer.NaturalDuration.HasTimeSpan && !_isConvertedSeeking)
            {
                ConvertedSeekSlider.Maximum = _convertedPlayer.NaturalDuration.TimeSpan.TotalSeconds;
                ConvertedSeekSlider.Value = _convertedPlayer.Position.TotalSeconds;
                ConvertedTimeText.Text = $"{_convertedPlayer.Position:mm\\:ss} / {_convertedPlayer.NaturalDuration.TimeSpan:mm\\:ss}";
            }
        }

        private void SourcePlayer_MediaOpened(object? sender, EventArgs e) { }
        private void SourcePlayer_MediaEnded(object? sender, EventArgs e)
        {
            _sourcePlayer.Stop();
            SourcePlayIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.Play;
        }

        private void ConvertedPlayer_MediaOpened(object? sender, EventArgs e) { }
        private void ConvertedPlayer_MediaEnded(object? sender, EventArgs e)
        {
            _convertedPlayer.Stop();
            ConvertedPlayIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.Play;
        }

        private void SourcePlayBtn_Click(object sender, RoutedEventArgs e)
        {
            if (SourcePlayIcon.Kind == MaterialDesignThemes.Wpf.PackIconKind.Play)
            {
                _sourcePlayer.Play();
                SourcePlayIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.Pause;
            }
            else
            {
                _sourcePlayer.Pause();
                SourcePlayIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.Play;
            }
        }

        private void ConvertedPlayBtn_Click(object sender, RoutedEventArgs e)
        {
            if (ConvertedPlayIcon.Kind == MaterialDesignThemes.Wpf.PackIconKind.Play)
            {
                _convertedPlayer.Play();
                ConvertedPlayIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.Pause;
            }
            else
            {
                _convertedPlayer.Pause();
                ConvertedPlayIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.Play;
            }
        }

        private void SourceSeekSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (SourceSeekSlider.IsMouseCaptureWithin)
            {
                _sourcePlayer.Position = TimeSpan.FromSeconds(e.NewValue);
            }
        }

        private void ConvertedSeekSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (ConvertedSeekSlider.IsMouseCaptureWithin)
            {
                _convertedPlayer.Position = TimeSpan.FromSeconds(e.NewValue);
            }
        }

        #endregion
    }
}
