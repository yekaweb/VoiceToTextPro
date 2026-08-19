using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using VoiceToTextPro.Models;
using VoiceToTextPro.Services;
using VoiceToTextPro.Windows;

namespace VoiceToTextPro.Tabs
{
    public partial class SubtitleTab : UserControl
    {
        private readonly ObservableCollection<SubtitleEntry> _entries = new();
        private SubtitleEntry? _selected;
        private string? _selectedMediaFile;
        private DispatcherTimer _timer = new DispatcherTimer();
        private bool _isDraggingSlider = false;
        private CancellationTokenSource? _batchCts;

        public SubtitleTab()
        {
            InitializeComponent();
            SubGrid.ItemsSource = _entries;
            LoadVoskModels();
            _timer.Interval = TimeSpan.FromMilliseconds(100);
            _timer.Tick += Timer_Tick;
        }

        public void LoadSrtFile(string path)
        {
            if (!File.Exists(path)) return;
            LoadFromSrtText(File.ReadAllText(path, Encoding.UTF8));
            SubStatus.Text = $"فایل بارگذاری‌شده: {Path.GetFileName(path)}";
            LoggerService.InfoLocalized("Log_SUBTITLE_EDITOR_2f9ed0", "زیرنویس بارگذاری شد: {0}", "SUBTITLE_EDITOR", Path.GetFileName(path));
        }

        private void LoadFromSrtText(string content)
        {
            _entries.Clear();
            var blocks = content.Trim().Split(new[] { "\r\n\r\n", "\n\n" }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var block in blocks)
            {
                var lines = block.Trim().Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                if (lines.Length < 3) continue;
                try
                {
                    int idx = int.Parse(lines[0].Trim());
                    var times = lines[1].Split(new[] { " --> " }, StringSplitOptions.None);
                    int startMs = SubtitleEntry.SrtToMs(times[0].Trim());
                    int endMs = SubtitleEntry.SrtToMs(times[1].Trim());
                    string text = string.Join("\n", lines.Skip(2));
                    _entries.Add(new SubtitleEntry { Index = idx, StartMs = startMs, EndMs = endMs, Text = text });
                }
                catch (Exception ex)
                {
                    LoggerService.WarnLocalized("Log_SUBTITLE_PARSER_313937", "خطا در خواندن بلاک زیرنویس: {0}", "SUBTITLE_PARSER", ex.Message);
                }
            }
            UpdateCount();
        }

        private void LoadVoskModels()
        {
            try
            {
                VoskModelCombo.Items.Clear();
                var settings = AppSettings.Load();
                string voskDir = settings.ModelsDirectory;
                
                if (Directory.Exists(voskDir))
                {
                    var dirs = Directory.GetDirectories(voskDir);
                    foreach (var dir in dirs)
                    {
                        string modelName = Path.GetFileName(dir);
                        VoskModelCombo.Items.Add(new ComboBoxItem { Content = modelName, Tag = dir });
                    }
                }
                if (VoskModelCombo.Items.Count > 0) VoskModelCombo.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                LoggerService.ErrorLocalized("Log_SUBTITLE_EDITOR_1e8846", "خطا در بارگذاری لیست مدل‌های Vosk: {0}", "SUBTITLE_EDITOR", ex.Message);
            }
        }

        private void SetModelPath_Click(object s, RoutedEventArgs e)
        {
            try
            {
                var dlg = new OpenFolderDialog { Title = "انتخاب مسیر مدل‌های هوش مصنوعی" };
                if (dlg.ShowDialog() == true)
                {
                    var settings = AppSettings.Load();
                    settings.ModelsDirectory = dlg.FolderName;
                    settings.Save();
                    LoadVoskModels();
                    SubStatus.Text = $"مسیر مدل‌ها تغییر یافت: {dlg.FolderName}";
                }
            }
            catch (Exception ex) { LoggerService.Error($"SetModelPath: {ex.Message}", "SUBTITLE"); }
        }

        private void DownloadModels_Click(object s, RoutedEventArgs e)
        {
            try
            {
                var window = new Windows.ModelManagerWindow { Owner = Window.GetWindow(this) };
                window.ShowDialog();
                LoadVoskModels();
            }
            catch (Exception ex) { LoggerService.Error($"DownloadModels: {ex}", "SUBTITLE"); }
        }

        public void LoadMediaFile(string path)
        {
            if (!File.Exists(path)) return;
            _selectedMediaFile = path;
            SelectedMediaText.Text = Path.GetFileName(_selectedMediaFile);
            StartAiBtn.IsEnabled = true;
            
            Player.Source = new Uri(_selectedMediaFile);
            Player.Play();
            Player.Pause();
            SubStatus.Text = $"ویدیو/صوت بارگذاری شد: {Path.GetFileName(path)}";
            LoggerService.InfoLocalized("Log_SUBTITLE_EDITOR_f85379", "رسانه بارگذاری شد در استودیوی زیرنویس: {0}", "SUBTITLE_EDITOR", Path.GetFileName(path));
        }

        private void SelectMedia_Click(object s, RoutedEventArgs e)
        {
            try
            {
                var dlg = new OpenFileDialog { Filter = "Media Files|*.mp4;*.mp3;*.wav;*.mkv|All|*.*" };
                if (dlg.ShowDialog() == true)
                {
                    LoadMediaFile(dlg.FileName);
                }
            }
            catch (Exception ex) { LoggerService.Error($"SelectMedia: {ex.Message}", "SUBTITLE"); }
        }
        
        private bool _isUpdatingSelectionFromTimer = false;

        private void Timer_Tick(object? sender, EventArgs e)
        {
            if (!_isDraggingSlider && Player.NaturalDuration.HasTimeSpan)
            {
                PlayerSlider.Maximum = Player.NaturalDuration.TimeSpan.TotalSeconds;
                PlayerSlider.Value = Player.Position.TotalSeconds;
                TimeLabel.Text = $"{Player.Position:mm\\:ss} / {Player.NaturalDuration.TimeSpan:mm\\:ss}";

                int currentMs = (int)Player.Position.TotalMilliseconds;
                var activeEntry = _entries.FirstOrDefault(x => currentMs >= x.StartMs && currentMs <= x.EndMs);
                
                if (activeEntry != null)
                {
                    SubtitleOverlay.Text = activeEntry.Text;
                    if (SubGrid.SelectedItem != activeEntry)
                    {
                        try
                        {
                            _isUpdatingSelectionFromTimer = true;
                            SubGrid.SelectedItem = activeEntry;
                            SubGrid.ScrollIntoView(activeEntry);
                        }
                        finally
                        {
                            _isUpdatingSelectionFromTimer = false;
                        }
                    }
                }
                else
                {
                    SubtitleOverlay.Text = "";
                }
            }
        }

        private void Player_MediaOpened(object sender, RoutedEventArgs e)
        {
            if (Player.NaturalDuration.HasTimeSpan)
            {
                PlayerSlider.Maximum = Player.NaturalDuration.TimeSpan.TotalSeconds;
                TimeLabel.Text = $"00:00 / {Player.NaturalDuration.TimeSpan:mm\\:ss}";
            }
            _timer.Start();
        }

        private void Player_MediaEnded(object sender, RoutedEventArgs e)
        {
            Player.Position = TimeSpan.Zero;
            Player.Stop();
        }

        private void PlayBtn_Click(object sender, RoutedEventArgs e) { try { Player.Play(); } catch (Exception ex) { LoggerService.Error($"PlayBtn: {ex.Message}", "SUBTITLE"); } }
        private void PauseBtn_Click(object sender, RoutedEventArgs e) { try { Player.Pause(); } catch (Exception ex) { LoggerService.Error($"PauseBtn: {ex.Message}", "SUBTITLE"); } }
        
        private void PlayerSlider_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            _isDraggingSlider = true;
        }

        private void PlayerSlider_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            _isDraggingSlider = false;
            Player.Position = TimeSpan.FromSeconds(PlayerSlider.Value);
        }

        private PythonBridge? _bridge;

        private async void TranslateAi_Click(object s, RoutedEventArgs e)
        {
            if (_entries.Count == 0)
            {
                ModernDialogService.ShowWarning(LanguageManager.Instance.GetString("Msg_NoSubtitleLoaded", "هیچ ردیف زیرنویسی بارگذاری نشده است."));
                return;
            }

            SubStatus.Text = "در حال ترجمه هوشمند بافت‌محور زیرنویس (AI Dual Studio)...";
            try
            {
                _bridge = new PythonBridge();
                string jsonResult = "";

                _bridge.OnResult += (res) => { jsonResult = res; };

                var rawList = _entries.Select(x => new { start_ms = x.StartMs, end_ms = x.EndMs, text = x.Text }).ToList();
                string jsonInput = System.Text.Json.JsonSerializer.Serialize(rawList);

                bool success = await _bridge.RunAsync("translate", jsonInput, "fa", "ollama");

                if (success && !string.IsNullOrEmpty(jsonResult))
                {
                    var options = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var translatedList = System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.List<TranslationResult>>(jsonResult, options);

                    if (translatedList != null && translatedList.Count == _entries.Count)
                    {
                        for (int i = 0; i < _entries.Count; i++)
                        {
                            _entries[i].TranslatedText = translatedList[i].Translated_Text;
                        }
                        SubStatus.Text = "ترجمه هوشمند زیرنویس با موفقیت انجام شد!";
                        LoggerService.SuccessLocalized("Log_SUBTITLE_EDITOR_d1d762", "استودیوی دوزبانه: ترجمه هوشمند زیرنویس با موفقیت اعمال شد.", "SUBTITLE_EDITOR");
                    }
                }
            }
            catch (Exception ex)
            {
                LoggerService.ErrorLocalized("Log_SUBTITLE_EDITOR_4a0517", "خطا در ترجمه هوشمند زیرنویس: {0}", "SUBTITLE_EDITOR", ex.Message);
                SubStatus.Text = "خطا در پردازش ترجمه زیرنویس.";
            }
            finally
            {
                _bridge = null;
            }
        }

        private class TranslationResult
        {
            public int Start_Ms { get; set; }
            public int End_Ms { get; set; }
            public string Text { get; set; } = "";
            public string Translated_Text { get; set; } = "";
        }

        private async void DiarizeAudio_Click(object s, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_selectedMediaFile) || !File.Exists(_selectedMediaFile))
            {
                ModernDialogService.ShowWarning(LanguageManager.Instance.GetString("Msg_SelectMediaFirst", "لطفاً ابتدا یک فایل صوتی یا ویدیو انتخاب کنید."));
                return;
            }

            if (_entries.Count == 0)
            {
                ModernDialogService.ShowWarning(LanguageManager.Instance.GetString("Msg_NoSubtitleLoaded", "هیچ ردیف زیرنویسی بارگذاری نشده است."));
                return;
            }

            SubStatus.Text = "در حال تفکیک خودکار گویندگان (Diarization)...";
            try
            {
                _bridge = new PythonBridge();
                string jsonResult = "";

                _bridge.OnResult += (res) => { jsonResult = res; };

                var rawList = _entries.Select(x => new { start_ms = x.StartMs, end_ms = x.EndMs, text = x.Text }).ToList();
                string jsonInput = System.Text.Json.JsonSerializer.Serialize(rawList);

                bool success = await _bridge.RunAsync("diarize", _selectedMediaFile, jsonInput);

                if (success && !string.IsNullOrEmpty(jsonResult))
                {
                    var options = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var diarizedList = System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.List<DiarizationResult>>(jsonResult, options);

                    if (diarizedList != null && diarizedList.Count == _entries.Count)
                    {
                        for (int i = 0; i < _entries.Count; i++)
                        {
                            _entries[i].SpeakerTag = diarizedList[i].Speaker;
                        }
                        SubStatus.Text = "تفکیک خودکار گویندگان با موفقیت انجام شد!";
                        LoggerService.SuccessLocalized("Log_SUBTITLE_EDITOR_018330", "تفکیک خودکار گویندگان اعمال گردید.", "SUBTITLE_EDITOR");
                    }
                }
            }
            catch (Exception ex)
            {
                LoggerService.ErrorLocalized("Log_SUBTITLE_EDITOR_d1fa28", "خطا در تفکیک گویندگان: {0}", "SUBTITLE_EDITOR", ex.Message);
                SubStatus.Text = "خطا در پردازش تفکیک گویندگان.";
            }
            finally
            {
                _bridge = null;
            }
        }

        private class DiarizationResult
        {
            public int StartMs { get; set; }
            public int EndMs { get; set; }
            public string Speaker { get; set; } = "";
        }

        private async void StartVosk_Click(object s, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_selectedMediaFile) || VoskModelCombo.SelectedItem is not ComboBoxItem selectedModel) return;
            string modelPath = selectedModel.Tag?.ToString() ?? "";
            
            string outDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "output");
            Directory.CreateDirectory(outDir);
            
            StartAiBtn.IsEnabled = false;
            SubStatus.Text = "در حال تولید زیرنویس هوشمند با Vosk...";
            
            try
            {
                _bridge = new PythonBridge();
                
                _bridge.OnProgress += (percent, msg) => 
                {
                    Dispatcher.Invoke(() => { SubStatus.Text = msg; });
                };
                
                string generatedSrt = "";
                _bridge.OnSrtPath += (path) => { generatedSrt = path; };

                bool success = await _bridge.RunAsync("subtitle", _selectedMediaFile, modelPath, outDir);
                
                if (success && !string.IsNullOrEmpty(generatedSrt) && File.Exists(generatedSrt))
                {
                    LoadSrtFile(generatedSrt);
                    SubStatus.Text = "زیرنویس هوشمند با موفقیت تولید و بارگذاری شد!";
                }
                else
                {
                    SubStatus.Text = "خطا در تولید زیرنویس.";
                }
            }
            finally
            {
                StartAiBtn.IsEnabled = true;
                _bridge = null;
            }
        }

        private void OpenSrt_Click(object s, RoutedEventArgs e)
        {
            try
            {
                var dlg = new OpenFileDialog { Filter = "Subtitle Files|*.srt;*.vtt|All|*.*" };
                if (dlg.ShowDialog() == true) LoadSrtFile(dlg.FileName);
            }
            catch (Exception ex) { LoggerService.Error($"OpenSrt: {ex.Message}", "SUBTITLE"); }
        }

        private void SaveSrt_Click(object s, RoutedEventArgs e) { try { SaveAs(".srt", BuildSrt()); } catch (Exception ex) { LoggerService.Error($"SaveSrt: {ex.Message}", "SUBTITLE"); } }
        private void SaveVtt_Click(object s, RoutedEventArgs e) { try { SaveAs(".vtt", BuildVtt()); } catch (Exception ex) { LoggerService.Error($"SaveVtt: {ex.Message}", "SUBTITLE"); } }
        private void SaveAss_Click(object s, RoutedEventArgs e) { try { SaveAs(".ass", BuildAss()); } catch (Exception ex) { LoggerService.Error($"SaveAss: {ex.Message}", "SUBTITLE"); } }

        private void SaveAs(string ext, string content)
        {
            var dlg = new SaveFileDialog { DefaultExt = ext, Filter = $"*{ext}|*{ext}" };
            if (dlg.ShowDialog() == true)
            {
                File.WriteAllText(dlg.FileName, content, Encoding.UTF8);
                LoggerService.SuccessLocalized("Log_SUBTITLE_EDITOR_690d34", "زیرنویس ذخیره شد: {0}", "SUBTITLE_EDITOR", dlg.FileName);
            }
        }

        private string BuildSrt()
        {
            var sb = new StringBuilder();
            foreach (var e in _entries)
                sb.AppendLine($"{e.Index}\r\n{e.StartStr} --> {e.EndStr}\r\n{e.Text}\r\n");
            return sb.ToString();
        }

        private string BuildVtt()
        {
            var sb = new StringBuilder("WEBVTT\r\n\r\n");
            foreach (var e in _entries)
                sb.AppendLine($"{e.Index}\r\n{e.StartStr.Replace(',', '.')} --> {e.EndStr.Replace(',', '.')}\r\n{e.Text}\r\n");
            return sb.ToString();
        }

        private string BuildAss()
        {
            var sb = new StringBuilder("[Script Info]\r\nTitle: VoiceToText Pro\r\nScriptType: v4.00+\r\n\r\n");
            sb.AppendLine("[V4+ Styles]\r\nFormat: Name, Fontname, Fontsize, PrimaryColour, Bold, Alignment\r\nStyle: Default,Arial,20,&H00FFFFFF,-1,2\r\n\r\n[Events]\r\nFormat: Layer, Start, End, Style, Text");
            foreach (var e in _entries)
                sb.AppendLine($"Dialogue: 0,{MsToAss(e.StartMs)},{MsToAss(e.EndMs)},Default,{e.Text}");
            return sb.ToString();
        }

        private static string MsToAss(int ms)
        {
            int h = ms / 3_600_000, m = (ms % 3_600_000) / 60_000, s = (ms % 60_000) / 1000, cs = (ms % 1000) / 10;
            return $"{h}:{m:00}:{s:00}.{cs:00}";
        }

        private void AddRow_Click(object s, RoutedEventArgs e)
        {
            try
            {
                int nextIdx = _entries.Count + 1;
                int lastEnd = _entries.LastOrDefault()?.EndMs ?? 0;
                _entries.Add(new SubtitleEntry { Index = nextIdx, StartMs = lastEnd, EndMs = lastEnd + 3000, Text = "متن جدید زیرنویس" });
                UpdateCount();
                LoggerService.InfoLocalized("Log_SUBTITLE_EDITOR_5c3016", "ردیف جدید زیرنویس اضافه شد.", "SUBTITLE_EDITOR");
            }
            catch (Exception ex) { LoggerService.Error($"AddRow: {ex.Message}", "SUBTITLE"); }
        }

        private void DeleteRow_Click(object s, RoutedEventArgs e)
        {
            try
            {
                if (_selected != null)
                {
                    _entries.Remove(_selected);
                    ReIndex();
                    UpdateCount();
                    LoggerService.InfoLocalized("Log_SUBTITLE_EDITOR_a1155e", "ردیف زیرنویس حذف گردید.", "SUBTITLE_EDITOR");
                }
            }
            catch (Exception ex) { LoggerService.Error($"DeleteRow: {ex.Message}", "SUBTITLE"); }
        }

        private void ReIndex()
        {
            for (int i = 0; i < _entries.Count; i++) _entries[i].Index = i + 1;
        }

        private void UpdateCount() => CountLabel.Text = $"{_entries.Count} ردیف";

        private void SubGrid_SelectionChanged(object s, SelectionChangedEventArgs e)
        {
            _selected = SubGrid.SelectedItem as SubtitleEntry;
            if (_selected == null) return;
            StartEdit.Text = _selected.StartStr;
            EndEdit.Text = _selected.EndStr;
            TextEdit.Text = _selected.Text;
            InfoLabel.Text = $"ردیف {_selected.Index} | مدت: {_selected.DurationStr}";

            // Only seek player position if selection was made directly by user click, NOT by auto-sync timer playback
            if (!_isUpdatingSelectionFromTimer && Player.Source != null && _selected.StartMs >= 0)
            {
                Player.Position = TimeSpan.FromMilliseconds(_selected.StartMs);
            }
        }

        private void ApplyEdit_Click(object s, RoutedEventArgs e)
        {
            try
            {
                if (_selected == null) return;
                _selected.StartMs = SubtitleEntry.SrtToMs(StartEdit.Text);
                _selected.EndMs = SubtitleEntry.SrtToMs(EndEdit.Text);
                _selected.Text = TextEdit.Text;
                InfoLabel.Text = $"ردیف {_selected.Index} | مدت: {_selected.DurationStr}";
                LoggerService.InfoLocalized("Log_SUBTITLE_EDITOR_8f31fa", "تغییرات ردیف {0} ثبت گردید.", "SUBTITLE_EDITOR", _selected.Index);
            }
            catch (Exception ex) { LoggerService.Error($"ApplyEdit: {ex.Message}", "SUBTITLE"); }
        }

        private async void AdvancedCorrectionBtn_Click(object s, RoutedEventArgs e)
        {
            if (_selected == null)
            {
                ModernDialogService.ShowWarning(LanguageManager.Instance.GetString("Msg_SelectSubtitleRow", "لطفاً یک ردیف زیرنویس را انتخاب کنید."));
                return;
            }
            if (string.IsNullOrEmpty(_selectedMediaFile) || !File.Exists(_selectedMediaFile))
            {
                ModernDialogService.ShowWarning(LanguageManager.Instance.GetString("Msg_SelectMediaFirst", "هیچ فایل صوتی یا ویدیویی بارگذاری نشده است. لطفاً ابتدا ویدیو را در برنامه باز کنید."));
                return;
            }

            try
            {
                AdvancedCorrectionBtn.IsEnabled = false;
                AdvancedCorrectionBtn.Content = "⏳ در حال استخراج و ارتباط با گوگل...";

                int startMs = SubtitleEntry.SrtToMs(StartEdit.Text);
                int endMs = SubtitleEntry.SrtToMs(EndEdit.Text);
                string lang = "fa-IR";

                _bridge = new PythonBridge();
                string correctedText = "";
                
                _bridge.OnCorrectedText += (output) =>
                {
                    correctedText = output.Trim();
                };

                bool success = await _bridge.RunAsync("transcribe_chunk", _selectedMediaFile, startMs.ToString(), endMs.ToString(), lang);

                if (success && !string.IsNullOrEmpty(correctedText) && !correctedText.StartsWith("خطا"))
                {
                    bool? userAgreed = null;
                    var dlg = new Window
                    {
                        Title = "✨ اصلاح پیشرفته متن (هوش مصنوعی گوگل)",
                        Width = 550, Height = 350,
                        WindowStartupLocation = WindowStartupLocation.CenterOwner,
                        Owner = Window.GetWindow(this),
                        Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#0F172A")),
                        Foreground = System.Windows.Media.Brushes.White,
                        FlowDirection = FlowDirection.RightToLeft,
                        WindowStyle = WindowStyle.ToolWindow
                    };
                    
                    var sp = new StackPanel { Margin = new Thickness(20) };
                    sp.Children.Add(new TextBlock { Text = "متن فعلی زیرنویس:", Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#94A3B8")), Margin = new Thickness(0,0,0,8) });
                    
                    var oldTxtBox = new TextBox { Text = TextEdit.Text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, Height = 60, Style = (Style)FindResource("MaterialDesignOutlinedTextBox"), Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#1E293B")), Foreground = System.Windows.Media.Brushes.White };
                    sp.Children.Add(oldTxtBox);
                    
                    sp.Children.Add(new TextBlock { Text = "متن پیشنهادی گوگل (دقت بالا):", Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#38BDF8")), Margin = new Thickness(0,16,0,8), FontWeight = FontWeights.Bold });
                    
                    var newTxtBox = new TextBox { Text = correctedText, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, Height = 60, Style = (Style)FindResource("MaterialDesignOutlinedTextBox"), Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#1E293B")), Foreground = System.Windows.Media.Brushes.White, BorderBrush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#8B5CF6")) };
                    sp.Children.Add(newTxtBox);
                    
                    var btnSp = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0,20,0,0) };
                    var cancelBtn = new Button { Content = "لغو", Margin = new Thickness(0,0,12,0), Style = (Style)FindResource("MaterialDesignOutlinedButton"), Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#94A3B8")), BorderBrush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#334155")) };
                    cancelBtn.Click += (sender, args) => { userAgreed = false; dlg.Close(); };
                    
                    var okBtn = new Button { Content = "تایید و جایگزینی", Style = (Style)FindResource("MaterialDesignRaisedButton"), Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#10B981")), Foreground = System.Windows.Media.Brushes.White };
                    okBtn.Click += (sender, args) => { userAgreed = true; dlg.Close(); };
                    
                    btnSp.Children.Add(cancelBtn);
                    btnSp.Children.Add(okBtn);
                    sp.Children.Add(btnSp);
                    
                    dlg.Content = sp;
                    dlg.ShowDialog();
                    
                    if (userAgreed == true)
                    {
                        int index = _entries.IndexOf(_selected);
                        if (index > 0)
                        {
                            correctedText = DeduplicateOverlapWords(_entries[index - 1].Text, correctedText);
                        }

                        TextEdit.Text = correctedText;
                        ApplyEdit_Click(this, new RoutedEventArgs());

                        if (index >= 0 && index < _entries.Count - 1)
                        {
                            _entries[index + 1].Text = DeduplicateOverlapWords(correctedText, _entries[index + 1].Text);
                        }

                        LoggerService.SuccessLocalized("Log_SUBTITLE_EDITOR_fae0a8", "متن ردیف {0} با موفقیت توسط گوگل جایگزین شد.", "SUBTITLE_EDITOR", _selected.Index);
                    }
                }
                else
                {
                    ShowErrorDialog("خطا در استخراج", "گوگل نتوانست متن صحیحی را استخراج کند، صدای این بخش نامفهوم است یا ارتباط اینترنت قطع می‌باشد.");
                }
            }
            catch (Exception ex)
            {
                ShowErrorDialog("خطا", $"خطای سیستمی: {ex.Message}");
            }
            finally
            {
                AdvancedCorrectionBtn.IsEnabled = true;
                AdvancedCorrectionBtn.Content = "✨ اصلاح با هوش مصنوعی گوگل";
            }
        }

        private void RowAdvancedCorrection_Click(object s, RoutedEventArgs e)
        {
            try
            {
                if (s is Button btn && btn.DataContext is SubtitleEntry entry)
                {
                    // Select the row in DataGrid to update right panel
                    SubGrid.SelectedItem = entry;
                    // Trigger the main correction logic
                    AdvancedCorrectionBtn_Click(this, new RoutedEventArgs());
                }
            }
            catch (Exception ex) { LoggerService.Error($"RowAdvancedCorrection: {ex.Message}", "SUBTITLE"); }
        }

        private async void BatchAiPolish_Click(object sender, RoutedEventArgs e)
        {
            if (_entries.Count == 0)
            {
                ShowErrorDialog("لیست خالی است", "هیچ زیرنویسی برای اصلاح وجود ندارد. ابتدا یک فایل زیرنویس بارگذاری یا استخراج کنید.");
                return;
            }

            if (string.IsNullOrEmpty(_selectedMediaFile) || !File.Exists(_selectedMediaFile))
            {
                var openDlg = new Microsoft.Win32.OpenFileDialog
                {
                    Filter = "فایل‌های ویدیو و صدا|*.mp4;*.mkv;*.avi;*.mov;*.mp3;*.wav;*.m4a;*.flac;*.webm|تمام فایل‌ها|*.*",
                    Title = "انتخاب فایل صوتی/ویدیویی مرجع جهت برش و استعلام از گوگل"
                };
                if (openDlg.ShowDialog() == true)
                {
                    LoadMediaFile(openDlg.FileName);
                }
                else
                {
                    return;
                }
            }

            // Confirm batch start
            bool confirmed = ModernDialogService.AskConfirmation(
                $"آیا مایلید تمام {_entries.Count} ردیف زیرنویس به ترتیب به هوش مصنوعی گوگل ارسال و اصلاح شوند؟\n\nاین پروسه به صورت خودکار انجام خواهد شد و در هر لحظه امکان لغو وجود دارد.",
                "⚡ اصلاح سراسری کل زیرنویس با گوگل");

            if (!confirmed) return;

            _batchCts = new CancellationTokenSource();
            var token = _batchCts.Token;

            BatchAiPolishBtn.IsEnabled = false;
            BatchProgressBanner.Visibility = Visibility.Visible;
            BatchProgressBar.Value = 0;

            int total = _entries.Count;
            int updatedCount = 0;
            string lang = "fa-IR";

            try
            {
                for (int i = 0; i < total; i++)
                {
                    if (token.IsCancellationRequested) break;

                    var entry = _entries[i];

                    // 1. Live UI update (select & scroll)
                    Dispatcher.Invoke(() =>
                    {
                        SubGrid.SelectedItem = entry;
                        SubGrid.ScrollIntoView(entry);

                        int percent = (int)(((double)(i + 1) / total) * 100);
                        BatchCurrentRowText.Text = $"ردیف {i + 1} از {total} ({percent}%)";
                        BatchProgressBar.Value = percent;
                        BatchStepDetail.Text = $"🎙️ برش صوتی ردیف {entry.Index} ({entry.StartTimeFormatted}) ➔ 🌐 ارسال به گوگل...";
                    });

                    // 2. Transcribe chunk via Python bridge
                    _bridge = new PythonBridge();
                    string correctedText = "";
                    _bridge.OnCorrectedText += (output) => { correctedText = output.Trim(); };

                    bool success = await _bridge.RunAsync("transcribe_chunk", _selectedMediaFile!, entry.StartMs.ToString(), entry.EndMs.ToString(), lang);

                    if (token.IsCancellationRequested) break;

                    // 3. Update entry live
                    if (success && !string.IsNullOrEmpty(correctedText) && !correctedText.StartsWith("خطا"))
                    {
                        if (i > 0)
                        {
                            correctedText = DeduplicateOverlapWords(_entries[i - 1].Text, correctedText);
                        }

                        Dispatcher.Invoke(() =>
                        {
                            entry.Text = correctedText;
                            if (_selected == entry)
                            {
                                TextEdit.Text = correctedText;
                            }
                            BatchStepDetail.Text = $"✅ ردیف {entry.Index} با موفقیت توسط گوگل اصلاح گردید.";
                        });
                        updatedCount++;
                    }
                    else
                    {
                        Dispatcher.Invoke(() =>
                        {
                            BatchStepDetail.Text = $"⚠️ صدای ردیف {entry.Index} نامفهوم بود یا پاسخ نیاورد (بدون تغییر ماند).";
                        });
                    }

                    // 4. Short friendly delay between API calls
                    await Task.Delay(250, token);
                }

                // Final pass to clean any remaining word overlaps between adjacent rows
                SanitizeAllSubtitleOverlaps();

                if (token.IsCancellationRequested)
                {
                    LoggerService.WarnLocalized("Log_SUBTITLE_EDITOR_56d5f9", "عملیات اصلاح سراسری توسط کاربر لغو شد. {0} ردیف اصلاح شده بودند.", "SUBTITLE_EDITOR", updatedCount);
                }
                else
                {
                    LoggerService.SuccessLocalized("Log_SUBTITLE_EDITOR_7059d6", "اصلاح سراسری زیرنویس به پایان رسید. {0} از {1} ردیف با موفقیت توسط گوگل بازنویسی شدند.", "SUBTITLE_EDITOR", updatedCount, total);
                    ModernDialogService.ShowInfo(
                        $"🎉 پروسه اصلاح سراسری با موفقیت پایان یافت!\n\nتعداد {updatedCount} ردیف از مجموع {total} ردیف با هوش مصنوعی گوگل دقیق‌تر بازنویسی شدند.\nهمچنین کلمات تکراری در مرز ردیف‌ها به صورت خودکار پاکسازی شدند.",
                        "اتمام موفقیت‌آمیز اصلاح سراسری");
                }
            }
            catch (TaskCanceledException)
            {
                // Handled gracefully
            }
            catch (Exception ex)
            {
                ShowErrorDialog("خطای پروسه اصلاح", $"خطا در پروسه اصلاح سراسری: {ex.Message}");
            }
            finally
            {
                BatchProgressBanner.Visibility = Visibility.Collapsed;
                BatchAiPolishBtn.IsEnabled = true;
                _batchCts = null;
            }
        }

        private async void AutoDubbingChain_Click(object sender, RoutedEventArgs e)
        {
            if (_entries.Count == 0)
            {
                ModernDialogService.ShowWarning("هیچ ردیف زیرنویسی جهت دوبله و تبدیل به کتاب صوتی وجود ندارد. لطفاً ابتدا زیرنویس بارگذاری کنید.", "زیرنویس خالی است");
                return;
            }

            bool confirm = ModernDialogService.AskConfirmation(
                $"آیا مایلید پروسه دوبله خودکار و تولید کتاب صوتی برای {_entries.Count} ردیف زیرنویس آغاز گردد؟\n\nاین پروسه به صورت زنجیره‌ای (STT ➔ TTS ➔ V2V ➔ BGM Ducking) خروجی نهایی صوتی را با زمان‌بندی دقیق تولید خواهد نمود.",
                "🎙️ شروع زنجیره دوبله خودکار و استودیوی کتاب صوتی");

            if (!confirm) return;

            var items = _entries.ToList();

            var assignments = new Dictionary<string, SpeakerVoiceAssignment>
            {
                { "Speaker 1", new SpeakerVoiceAssignment { SpeakerName = "Speaker 1", V2VProfilePath = "preset_radio_male" } },
                { "Speaker 2", new SpeakerVoiceAssignment { SpeakerName = "Speaker 2", V2VProfilePath = "preset_audiobook_female" } }
            };

            try
            {
                SubStatus.Text = "در حال اجرای زنجیره دوبله خودکار و ترکیب صوتی...";
                string finalAudio = await ChainPipelineService.Instance.ProcessSubtitleDubbingChainAsync(
                    items,
                    assignments,
                    bgmAudioPath: "",
                    bgmVolumeDucked: 0.15
                );

                SubStatus.Text = $"دوبله اتوماتیک تکمیل شد: {Path.GetFileName(finalAudio)}";
                ModernDialogService.ShowInfo($"زنجیره دوبله اتوماتیک و کتاب صوتی با موفقیت پایان یافت!\n\nفایل صوتی خروجی در مسیر زیر ذخیره گردید:\n{finalAudio}", "اتمام موفقیت‌آمیز دوبله");
            }
            catch (Exception ex)
            {
                SubStatus.Text = "خطا در اجرای زنجیره دوبله اتوماتیک.";
                ModernDialogService.ShowError($"خطا در زنجیره دوبله اتوماتیک: {ex.Message}", "خطای پروسه");
            }
        }

        private void CancelBatchPolish_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_batchCts != null && !_batchCts.IsCancellationRequested)
                {
                    _batchCts.Cancel();
                    BatchStepDetail.Text = "🛑 در حال لغو پروسه اصلاح...";
                }
            }
            catch (Exception ex) { LoggerService.Error($"CancelBatchPolish: {ex.Message}", "SUBTITLE"); }
        }

        private void ShowErrorDialog(string title, string message)
        {
            ModernDialogService.ShowError(message, title);
        }

        private void TimeEdit_Changed(object s, TextChangedEventArgs e) { }
        private void TextEdit_Changed(object s, TextChangedEventArgs e) { }

        private async void GeminiPolish_Click(object sender, RoutedEventArgs e)
        {
            if (_entries.Count == 0)
            {
                ModernDialogService.ShowWarning("هیچ ردیف زیرنویسی جهت ویراستاری وجود ندارد.", "زیرنویس خالی است");
                return;
            }

            if (!GeminiApiClient.Instance.IsConfigured)
            {
                ModernDialogService.ShowWarning("کلید Google Gemini API ثبت نشده است. لطفاً ابتدا کلید خود را در پنجره تنظیمات وارد کنید.", "کلید API یافت نشد");
                new SettingsWindow { Owner = Window.GetWindow(this) }.ShowDialog();
                if (!GeminiApiClient.Instance.IsConfigured) return;
            }

            try
            {
                SubStatus.Text = "در حال ویراستاری کل زیرنویس توسط Google Gemini Pro...";
                var fullText = string.Join("\n", _entries.Select(x => x.Text));
                string polished = await GeminiApiClient.Instance.PolishSubtitleAsync(fullText);

                var lines = polished.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < Math.Min(lines.Length, _entries.Count); i++)
                {
                    _entries[i].Text = lines[i].Trim();
                }

                SanitizeAllSubtitleOverlaps();

                SubStatus.Text = "ویراستاری هوشمند با Gemini Pro تکمیل گردید.";
                ModernDialogService.ShowInfo("🎉 ویراستاری و علائم‌گذاری سراسری زیرنویس توسط Google Gemini Pro با موفقیت انجام شد!", "پایان موفقیت‌آمیز ویراستاری");
            }
            catch (Exception ex)
            {
                SubStatus.Text = "خطا در ویراستاری Gemini.";
                ModernDialogService.ShowError($"خطا در ارتباط با Gemini API: {ex.Message}", "خطای هوش مصنوعی");
            }
        }

        private async void GeminiTranslate_Click(object sender, RoutedEventArgs e)
        {
            if (_entries.Count == 0)
            {
                ModernDialogService.ShowWarning("هیچ ردیف زیرنویسی جهت ترجمه وجود ندارد.", "زیرنویس خالی است");
                return;
            }

            if (!GeminiApiClient.Instance.IsConfigured)
            {
                ModernDialogService.ShowWarning("کلید Google Gemini API ثبت نشده است. لطفاً ابتدا کلید خود را در پنجره تنظیمات وارد کنید.", "کلید API یافت نشد");
                new SettingsWindow { Owner = Window.GetWindow(this) }.ShowDialog();
                if (!GeminiApiClient.Instance.IsConfigured) return;
            }

            try
            {
                SubStatus.Text = "در حال ترجمه هوشمند توسط Google Gemini Pro...";
                var fullText = string.Join("\n", _entries.Select(x => x.Text));
                string translated = await GeminiApiClient.Instance.TranslateSubtitleAsync(fullText, "English");

                var lines = translated.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < Math.Min(lines.Length, _entries.Count); i++)
                {
                    _entries[i].TranslatedText = lines[i].Trim();
                }

                SubStatus.Text = "ترجمه هوشمند Gemini Pro تکمیل گردید.";
                ModernDialogService.ShowInfo("🌐 ترجمه سراسری با موفقیت انجام و در ستون 'ترجمه' قرار گرفت!", "پایان ترجمه هوشمند");
            }
            catch (Exception ex)
            {
                SubStatus.Text = "خطا در ترجمه Gemini.";
                ModernDialogService.ShowError($"خطا در ترجمه با Gemini API: {ex.Message}", "خطای هوش مصنوعی");
            }
        }

        private async void GeminiChapters_Click(object sender, RoutedEventArgs e)
        {
            if (_entries.Count == 0)
            {
                ModernDialogService.ShowWarning("هیچ متن رونویسی‌شده‌ای برای فصل‌بندی وجود ندارد.", "زیرنویس خالی است");
                return;
            }

            if (!GeminiApiClient.Instance.IsConfigured)
            {
                ModernDialogService.ShowWarning("کلید Google Gemini API ثبت نشده است. لطفاً ابتدا کلید خود را در پنجره تنظیمات وارد کنید.", "کلید API یافت نشد");
                new SettingsWindow { Owner = Window.GetWindow(this) }.ShowDialog();
                if (!GeminiApiClient.Instance.IsConfigured) return;
            }

            try
            {
                SubStatus.Text = "در حال ساخت فصل‌های ویدیویی یوتیوب با Gemini Pro...";
                var fullText = string.Join("\n", _entries.Select(x => $"{x.StartTimeFormatted} {x.Text}"));
                string chapters = await GeminiApiClient.Instance.GenerateChaptersAsync(fullText);

                SubStatus.Text = "فصل‌بندی یوتیوب آماده گردید.";
                ModernDialogService.ShowInfo($"📌 فصل‌های ویدیویی تولیدشده توسط Gemini Pro:\n\n{chapters}", "فصل‌بندی خودکار یوتیوب");
            }
            catch (Exception ex)
            {
                SubStatus.Text = "خطا در تولید فصل‌ها.";
                ModernDialogService.ShowError($"خطا در ارتباط با Gemini API: {ex.Message}", "خطای هوش مصنوعی");
            }
        }

        private void FindReplace_Click(object s, RoutedEventArgs e)
        {
            try
            {
                var dlg = new Window
                {
                    Title = "جستجو و جایگزینی", Width = 400, Height = 220,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Owner = Window.GetWindow(this),
                    Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#0F172A"))
                };
                var sp = new StackPanel { Margin = new Thickness(16) };
                var findBox = new TextBox { Width = 350, Style = (Style)FindResource("MaterialDesignOutlinedTextBox"), Foreground = System.Windows.Media.Brushes.White, Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#1E293B")) };
                var replaceBox = new TextBox { Width = 350, Style = (Style)FindResource("MaterialDesignOutlinedTextBox"), Foreground = System.Windows.Media.Brushes.White, Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#1E293B")) };
                var btn = new Button { Content = "جایگزینی در تمامی ردیف‌ها", Margin = new Thickness(0, 12, 0, 0), Style = (Style)FindResource("MaterialDesignRaisedButton"), Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#6366F1")) };
                btn.Click += (_, __) =>
                {
                    int replaced = 0;
                    foreach (var entry in _entries)
                    {
                        if (entry.Text.Contains(findBox.Text))
                        {
                            entry.Text = entry.Text.Replace(findBox.Text, replaceBox.Text);
                            replaced++;
                        }
                    }
                    LoggerService.SuccessLocalized("Log_SUBTITLE_EDITOR_297749", "عملیات جستجو و جایگزینی در {0} ردیف انجام شد.", "SUBTITLE_EDITOR", replaced);
                    dlg.Close();
                };
                sp.Children.Add(new TextBlock { Text = "عبارت مورد نظر برای جستجو:", Foreground = System.Windows.Media.Brushes.White });
                sp.Children.Add(findBox);
                sp.Children.Add(new TextBlock { Text = "عبارت جایگزین:", Foreground = System.Windows.Media.Brushes.White, Margin = new Thickness(0, 8, 0, 0) });
                sp.Children.Add(replaceBox);
                sp.Children.Add(btn);
                dlg.Content = sp;
                dlg.ShowDialog();
            }
            catch (Exception ex) { LoggerService.Error($"FindReplace: {ex.Message}", "SUBTITLE"); }
        }

        private string DeduplicateOverlapWords(string prevText, string currentText)
        {
            if (string.IsNullOrWhiteSpace(prevText) || string.IsNullOrWhiteSpace(currentText))
                return currentText;

            char[] separators = new[] { ' ', '\t', '\r', '\n' };
            var prevWords = prevText.Split(separators, StringSplitOptions.RemoveEmptyEntries);
            var currWords = currentText.Split(separators, StringSplitOptions.RemoveEmptyEntries);

            if (prevWords.Length == 0 || currWords.Length == 0)
                return currentText;

            static string CleanWord(string w)
            {
                if (string.IsNullOrWhiteSpace(w)) return "";
                var sb = new StringBuilder();
                foreach (char c in w)
                {
                    if (char.IsLetterOrDigit(c))
                        sb.Append(c);
                }
                return sb.ToString().ToLowerInvariant();
            }

            int maxCheck = Math.Min(4, Math.Min(prevWords.Length, currWords.Length));

            for (int overlapLen = maxCheck; overlapLen >= 1; overlapLen--)
            {
                bool match = true;
                for (int k = 0; k < overlapLen; k++)
                {
                    string prevW = CleanWord(prevWords[prevWords.Length - overlapLen + k]);
                    string currW = CleanWord(currWords[k]);

                    if (string.IsNullOrEmpty(prevW) || string.IsNullOrEmpty(currW) || prevW != currW)
                    {
                        match = false;
                        break;
                    }
                }

                if (match)
                {
                    var remainingWords = currWords.Skip(overlapLen);
                    string cleaned = string.Join(" ", remainingWords).Trim();
                    LoggerService.Info($"حذف کلمه/کلمات همپوشانی تکراری بین ردیف‌ها: '{string.Join(" ", currWords.Take(overlapLen))}'", "SUBTITLE_DEDUP");
                    return cleaned;
                }
            }

            return currentText;
        }

        private void SanitizeAllSubtitleOverlaps()
        {
            if (_entries == null || _entries.Count < 2) return;

            int cleanedCount = 0;
            for (int i = 1; i < _entries.Count; i++)
            {
                string prev = _entries[i - 1].Text;
                string curr = _entries[i].Text;
                string deduped = DeduplicateOverlapWords(prev, curr);
                if (deduped != curr)
                {
                    _entries[i].Text = deduped;
                    cleanedCount++;
                }
            }

            if (cleanedCount > 0)
            {
                LoggerService.Info($"تعداد {cleanedCount} کلمه/کلمات همپوشانی تکراری در مرز ردیف‌ها پاکسازی شد.", "SUBTITLE_DEDUP");
            }
        }
    }
}
