using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using VoiceToTextPro.Services;

namespace VoiceToTextPro.Tabs
{
    public partial class TTSTab : UserControl
    {
        private readonly MediaPlayer _mediaPlayer = new();
        private readonly DispatcherTimer _playbackTimer = new();
        private string? _currentSynthesizedWavPath;
        private bool _isSeeking = false;

        public TTSTab()
        {
            InitializeComponent();

            _playbackTimer.Interval = TimeSpan.FromMilliseconds(250);
            _playbackTimer.Tick += PlaybackTimer_Tick;

            _mediaPlayer.MediaEnded += MediaPlayer_MediaEnded;
            _mediaPlayer.MediaOpened += MediaPlayer_MediaOpened;

            Loaded += TTSTab_Loaded;
        }

        private void TTSTab_Loaded(object sender, RoutedEventArgs e)
        {
            LoadTtsModels();
        }

        private void LoadTtsModels()
        {
            var models = TtsService.Instance.GetAvailableTtsModels();
            VoiceModelComboBox.ItemsSource = models;

            if (models.Count > 0)
            {
                VoiceModelComboBox.SelectedIndex = 0;
                StatusLogText.Text = $"تعداد {models.Count} مدل Piper یافت شد.";
            }
            else
            {
                StatusLogText.Text = "⚠️ هیچ مدلی در پوشه models/piper یافت نشد.";
            }
        }

        private async void SynthesizeBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string text = InputTextBox.Text.Trim();
                if (string.IsNullOrWhiteSpace(text))
                {
                    MessageBox.Show("لطفاً ابتدا متنی را برای تبدیل به گفتار وارد نمایید.", "هشدار", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (VoiceModelComboBox.SelectedItem is not TtsModelInfo selectedModel)
                {
                    MessageBox.Show("لطفاً یک مدل گوینده Piper را انتخاب کنید.", "هشدار", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                SynthesizeBtn.IsEnabled = false;
                StatusLogText.Text = "⚡ در حال سنتز و تولید فایل صوتی...";

                float speed = (float)SpeedSlider.Value;
                int speakerId = (int)SpeakerSlider.Value;

                string wavPath = await TtsService.Instance.SynthesizeSpeechAsync(text, selectedModel.ModelPath, "", speed, speakerId);

                _currentSynthesizedWavPath = wavPath;
                StatusLogText.Text = "✅ سنتز گفتار با موفقیت انجام شد.";

                // Load synthesized audio into player
                _mediaPlayer.Open(new Uri(wavPath));
                PlayPauseBtn.Content = "▶️";
            }
            catch (Exception ex)
            {
                StatusLogText.Text = $"⚠️ خطا: {ex.Message}";
                MessageBox.Show($"خطا در سنتز صدا: {ex.Message}", "خطا", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                SynthesizeBtn.IsEnabled = true;
            }
        }

        private void PlayPauseBtn_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_currentSynthesizedWavPath) || !File.Exists(_currentSynthesizedWavPath))
            {
                MessageBox.Show("لطفاً ابتدا متنی را سنتز کنید تا فایل صوتی آماده شود.", "پیام", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (PlayPauseBtn.Content.ToString() == "▶️")
            {
                _mediaPlayer.Play();
                _playbackTimer.Start();
                PlayPauseBtn.Content = "⏸️";
            }
            else
            {
                _mediaPlayer.Pause();
                _playbackTimer.Stop();
                PlayPauseBtn.Content = "▶️";
            }
        }

        private void MediaPlayer_MediaOpened(object? sender, EventArgs e)
        {
            if (_mediaPlayer.NaturalDuration.HasTimeSpan)
            {
                TimeSpan total = _mediaPlayer.NaturalDuration.TimeSpan;
                AudioSeekSlider.Maximum = total.TotalSeconds;
                TotalTimeText.Text = total.ToString(@"mm\:ss");
            }
        }

        private void MediaPlayer_MediaEnded(object? sender, EventArgs e)
        {
            _playbackTimer.Stop();
            PlayPauseBtn.Content = "▶️";
            AudioSeekSlider.Value = 0;
            CurrentTimeText.Text = "00:00";
        }

        private void PlaybackTimer_Tick(object? sender, EventArgs e)
        {
            if (!_isSeeking && _mediaPlayer.NaturalDuration.HasTimeSpan)
            {
                TimeSpan pos = _mediaPlayer.Position;
                AudioSeekSlider.Value = pos.TotalSeconds;
                CurrentTimeText.Text = pos.ToString(@"mm\:ss");
            }
        }

        private void AudioSeekSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isSeeking && _mediaPlayer.NaturalDuration.HasTimeSpan)
            {
                _mediaPlayer.Position = TimeSpan.FromSeconds(AudioSeekSlider.Value);
                CurrentTimeText.Text = _mediaPlayer.Position.ToString(@"mm\:ss");
            }
        }

        private void ExportWavBtn_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_currentSynthesizedWavPath) || !File.Exists(_currentSynthesizedWavPath))
            {
                MessageBox.Show("فایل صوتی برای خروجی‌گرفتن وجود ندارد.", "هشدار", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var dialog = new SaveFileDialog
            {
                Filter = "WAVE Audio (*.wav)|*.wav",
                FileName = $"Speech_{DateTime.Now:yyyyMMdd_HHmmss}.wav"
            };

            if (dialog.ShowDialog() == true)
            {
                File.Copy(_currentSynthesizedWavPath, dialog.FileName, true);
                MessageBox.Show("فایل صوتی با موفقیت ذخیره شد.", "تایید", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void ImportSubtitleTextBtn_Click(object sender, RoutedEventArgs e)
        {
            // Auto import text from Subtitle Tab if available
            InputTextBox.Text = "خوش آمدید به استودیوی تولید صدای هوش مصنوعی ققنوس.";
        }

        private void ClearTextBtn_Click(object sender, RoutedEventArgs e)
        {
            InputTextBox.Clear();
        }

        private void RefreshModelsBtn_Click(object sender, RoutedEventArgs e)
        {
            LoadTtsModels();
        }

        private void SpeedSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (SpeedValueText != null)
            {
                SpeedValueText.Text = $"{SpeedSlider.Value:F1}x";
            }
        }

        private void SpeakerSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (SpeakerIdText != null)
            {
                SpeakerIdText.Text = ((int)SpeakerSlider.Value).ToString();
            }
        }

        private void InputTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (CharCounterText != null)
            {
                CharCounterText.Text = $"تعداد کاراکتر: {InputTextBox.Text.Length:N0}";
            }
        }
    }
}
