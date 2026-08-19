using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MaterialDesignThemes.Wpf;
using VoiceToTextPro.Services;

namespace VoiceToTextPro
{
    public partial class MainWindow : Window
    {
        private readonly ObservableCollection<LogEntry> _filteredLogs = new();
        private string _currentFilter = "ALL";
        private int _totalLogs = 0;

        public MainWindow()
        {
            InitializeComponent();

            LogListView.ItemsSource = _filteredLogs;

            // Wire events
            AppEventBus.FileReadyForTranscription += OnFileReadyForTranscription;
            AppEventBus.SrtReadyForSubtitleEditor += OnSrtReady;
            AppEventBus.MediaReadyForSubtitleEditor += OnMediaReadyForSubtitle;

            LoggerService.OnLogAdded += OnLogAdded;
            LanguageManager.Instance.LanguageChanged += (s, lang) => UpdateWorkspaceTitle();
            this.FlowDirection = (LanguageManager.Instance.CurrentCulture == "fa-IR" || LanguageManager.Instance.CurrentCulture == "ar-SA") ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

            // Initial log & Python check
            LoggerService.InfoLocalized("Log_SystemStartup", "راه‌اندازی سیستم VoiceToText Pro نسخه ۳.۰", "CORE");
            DetectPythonEnvironment();
            CleanupCache();
            CheckInstalledModelsAtStartup();

            Loaded += MainWindow_Loaded;
        }

        private readonly GlobalHotkeyService _hotkeyService = new();

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                _hotkeyService.Register(this, ToggleRaycastWidget);
            }
            catch (Exception ex)
            {
                LoggerService.WarnLocalized("Log_HOTKEY_06da6e", "خطا در تنظیم Hotkey: {0}", "HOTKEY", ex.Message);
            }
        }

        private void ToggleRaycastWidget()
        {
            Dispatcher.Invoke(() =>
            {
                if (_raycastWidget == null || !_raycastWidget.IsLoaded)
                {
                    _raycastWidget = new Windows.RaycastWidgetWindow();
                }

                if (_raycastWidget.IsVisible)
                {
                    _raycastWidget.Hide();
                }
                else
                {
                    _raycastWidget.Show();
                    _raycastWidget.Activate();
                }
            });
        }

        private void CheckInstalledModelsAtStartup()
        {
            Task.Run(() =>
            {
                try
                {
                    string targetDir = AppSettings.Load().ModelsDirectory;
                    if (!Directory.Exists(targetDir)) return;

                    var modelDirs = Directory.GetDirectories(targetDir);
                    bool alertShown = false;

                    foreach (var dir in modelDirs)
                    {
                        string folderName = Path.GetFileName(dir);
                        var dummyModel = new VoiceToTextPro.Models.AiModel
                        {
                            Name = folderName,
                            FolderName = folderName,
                            Url = folderName.StartsWith("faster-whisper") ? $"huggingface:Systran/{folderName}" : "https://alphacephei.com/vosk"
                        };

                        var status = ModelDownloadManager.Instance.VerifyModelIntegrity(dummyModel, targetDir);
                        if (status == VoiceToTextPro.Models.ModelStatus.Corrupted)
                        {
                            alertShown = true;
                            Dispatcher.Invoke(() =>
                            {
                                ShowLocalizedAlert("Msg_ModelCorruptedAlert", "⚠️ مدل هوش مصنوعی «{0}» به صورت ناقص دریافت شده است. لطفاً آن را ترمیم نمایید.", folderName);
                            });
                            break;
                        }
                    }

                    if (!alertShown)
                    {
                        var zipFiles = Directory.GetFiles(targetDir, "*.zip*");
                        if (zipFiles.Length > 0)
                        {
                            string zipName = Path.GetFileName(zipFiles[0]).Replace(".zip", "");
                            Dispatcher.Invoke(() =>
                            {
                                ShowLocalizedAlert("Msg_ModelCorruptedAlert", "⚠️ فایل دانلود مدل «{0}» به صورت ناقص باقی مانده است. لطفاً از مدیریت مدل‌ها آن را ترمیم کنید.", zipName);
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    LoggerService.WarnLocalized("Log_MODEL_MANAGER_643ebb", "خطا در بررسی اولیه سلامت مدل‌ها: {0}", "MODEL_MANAGER", ex.Message);
                }
            });
        }

        private void CleanupCache()
        {
            try
            {
                var settings = AppSettings.Load();
                if (!Directory.Exists(settings.OutputDirectory)) return;

                var files = Directory.GetFiles(settings.OutputDirectory, "*.*");
                int deletedCount = 0;
                foreach (var file in files)
                {
                    if (file.EndsWith(".srt", StringComparison.OrdinalIgnoreCase) || 
                        file.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) ||
                        file.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                    {
                        var info = new FileInfo(file);
                        if (DateTime.Now - info.CreationTime > TimeSpan.FromDays(7))
                        {
                            info.Delete();
                            deletedCount++;
                        }
                    }
                }
                if (deletedCount > 0)
                {
                    LoggerService.InfoLocalized("Log_CacheCleaned", "تعداد {0} فایل موقت و قدیمی از کش پاک شد.", "CACHE", deletedCount);
                }
            }
            catch (Exception ex)
            {
                LoggerService.ErrorLocalized("Log_CACHE_356cc4", "خطا در پاکسازی کش: {0}", "CACHE", ex.Message);
            }
        }

        private void DetectPythonEnvironment()
        {
            string pyCmd = PythonBridge.FindPythonExecutable();
            string workersDir = PythonBridge.GetWorkersDirectory();
            PythonVerText.Text = $"Python: {pyCmd}";
            LoggerService.InfoLocalized("Log_PythonEnv", "محیط پایتون: {0} | مسیر اسکریپت‌ها: {1}", "CORE", pyCmd, workersDir);
        }

        private static readonly string[] _tabTitleKeys =
        {
            "TabTitle_AudioTranscribe",
            "TabTitle_LiveTranscribe",
            "TabTitle_SubtitleStudio",
            "TabTitle_MediaDownloader",
            "TabTitle_TTS",
            "Tab_V2V_Title"
        };

        private void NavList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (NavList?.SelectedItem is ListBoxItem item && item.Tag != null && int.TryParse(item.Tag.ToString(), out int index))
                {
                    if (MainTabs != null)
                        MainTabs.SelectedIndex = index;
                    UpdateWorkspaceTitle();
                }
            }
            catch (Exception ex) { LoggerService.Error($"NavList: {ex.Message}", "UI"); }
        }

        private void UpdateWorkspaceTitle()
        {
            Dispatcher.Invoke(() =>
            {
                if (WorkspaceTitleText != null && MainTabs != null && MainTabs.SelectedIndex >= 0 && MainTabs.SelectedIndex < _tabTitleKeys.Length)
                {
                    WorkspaceTitleText.Text = LanguageManager.Instance.GetString(_tabTitleKeys[MainTabs.SelectedIndex]);
                }
                if (_lastAlertKey != null && AlertBanner != null && AlertBanner.Visibility == Visibility.Visible)
                {
                    AlertText.Text = LanguageManager.Instance.GetFormattedString(_lastAlertKey, _lastAlertFallback ?? "", _lastAlertArgs ?? Array.Empty<object>());
                }
            });
        }

        private void OnLogAdded(LogEntry entry)
        {
            Dispatcher.Invoke(() =>
            {
                _totalLogs++;
                LogBadge.Visibility = Visibility.Visible;
                LogCountText.Text = _totalLogs.ToString();

                if (entry.Level == LogLevel.Error)
                {
                    ShowAlert(entry.Message, entry.Source);
                    StatusDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444"));
                    EngineStatusText.Text = "موتور: خطا";
                }
                else if (entry.Level == LogLevel.Success)
                {
                    StatusDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981"));
                    EngineStatusText.Text = "موتور: آماده";
                }

                if (ShouldShowLog(entry))
                {
                    _filteredLogs.Add(entry);
                    if (_filteredLogs.Count > 500)
                        _filteredLogs.RemoveAt(0);

                    if (LogListView.Items.Count > 0)
                        LogListView.ScrollIntoView(LogListView.Items[LogListView.Items.Count - 1]);
                }
            });
        }

        private bool ShouldShowLog(LogEntry entry)
        {
            return _currentFilter switch
            {
                "PYTHON" => entry.Source == "PYTHON" || entry.Source == "PYTHON_BRIDGE",
                "ERROR" => entry.Level == LogLevel.Error,
                _ => true
            };
        }

        private void LogFilter_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && rb.Tag != null)
            {
                _currentFilter = rb.Tag.ToString()!;
                RebuildFilteredLogs();
            }
        }

        private void RebuildFilteredLogs()
        {
            _filteredLogs.Clear();
            foreach (var log in LoggerService.Entries)
            {
                if (ShouldShowLog(log))
                    _filteredLogs.Add(log);
            }
        }

        private string? _lastAlertKey;
        private string? _lastAlertFallback;
        private object[]? _lastAlertArgs;
        private bool _isCurrentAlertModelRelated = false;

        private void ShowAlert(string msg, string source = "GENERAL")
        {
            _lastAlertKey = null;
            AlertText.Text = msg;
            ConfigureAlertView(source);
            AlertBanner.Visibility = Visibility.Visible;
        }

        private void ShowLocalizedAlert(string key, string fallback, params object[] args)
        {
            _lastAlertKey = key;
            _lastAlertFallback = fallback;
            _lastAlertArgs = args;
            AlertText.Text = LanguageManager.Instance.GetFormattedString(key, fallback, args);

            string source = key.Contains("Model", StringComparison.OrdinalIgnoreCase) ? "MODEL" : "GENERAL";
            ConfigureAlertView(source);
            AlertBanner.Visibility = Visibility.Visible;
        }

        private void ConfigureAlertView(string source)
        {
            source = source?.ToUpperInvariant() ?? "GENERAL";
            switch (source)
            {
                case "MODEL":
                case "MODEL_MANAGER":
                    _isCurrentAlertModelRelated = true;
                    AlertCategoryText.Text = LanguageManager.Instance.GetString("Nav_AiModels");
                    AlertCategoryBadge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444"));
                    AlertActionButton.Content = LanguageManager.Instance.GetString("Nav_AiModels");
                    AlertActionButton.Visibility = Visibility.Visible;
                    break;

                case "DOWNLOADER":
                    _isCurrentAlertModelRelated = false;
                    AlertCategoryText.Text = LanguageManager.Instance.GetString("Nav_MediaDownloader");
                    AlertCategoryBadge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B"));
                    AlertActionButton.Content = LanguageManager.Instance.GetString("Btn_Logs");
                    AlertActionButton.Visibility = Visibility.Visible;
                    break;

                case "TRANSCRIBE":
                case "SUBTITLE":
                    _isCurrentAlertModelRelated = false;
                    AlertCategoryText.Text = LanguageManager.Instance.GetString("Nav_AudioTranscribe");
                    AlertCategoryBadge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444"));
                    AlertActionButton.Content = LanguageManager.Instance.GetString("Btn_Logs");
                    AlertActionButton.Visibility = Visibility.Visible;
                    break;

                case "TTS":
                case "V2V":
                case "AUDIO":
                    _isCurrentAlertModelRelated = false;
                    AlertCategoryText.Text = LanguageManager.Instance.GetString("Nav_TTS");
                    AlertCategoryBadge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444"));
                    AlertActionButton.Content = LanguageManager.Instance.GetString("Btn_Logs");
                    AlertActionButton.Visibility = Visibility.Visible;
                    break;

                default:
                    _isCurrentAlertModelRelated = false;
                    AlertCategoryText.Text = LanguageManager.Instance.GetString("Dialog_Error");
                    AlertCategoryBadge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DC2626"));
                    AlertActionButton.Content = LanguageManager.Instance.GetString("Btn_Logs");
                    AlertActionButton.Visibility = Visibility.Visible;
                    break;
            }
        }

        private void AlertAction_Click(object sender, RoutedEventArgs e)
        {
            if (_isCurrentAlertModelRelated)
            {
                OpenModelManagerFromAlert_Click(sender, e);
            }
            else
            {
                ToggleLogDrawer_Click(sender, e);
            }
        }

        private void DismissAlert_Click(object sender, RoutedEventArgs e)
        {
            try { AlertBanner.Visibility = Visibility.Collapsed; }
            catch (Exception ex) { LoggerService.Error($"DismissAlert: {ex.Message}", "UI"); }
        }

        private void OpenModelManagerFromAlert_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                AlertBanner.Visibility = Visibility.Collapsed;
                var window = new Windows.ModelManagerWindow { Owner = this };
                window.ShowDialog();
            }
            catch (Exception ex) { LoggerService.Error($"OpenModelManager: {ex}", "UI"); }
        }

        private void ToggleLogDrawer_Click(object sender, RoutedEventArgs e)
        {
            try { LogDrawer.Visibility = LogDrawer.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible; }
            catch (Exception ex) { LoggerService.Error($"ToggleLogDrawer: {ex.Message}", "UI"); }
        }

        private void ClearLogs_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                LoggerService.Clear();
                _filteredLogs.Clear();
                _totalLogs = 0;
                LogBadge.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex) { LoggerService.Error($"ClearLogs: {ex.Message}", "UI"); }
        }

        private void CopyLogs_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(LoggerService.ExportAll());
                LoggerService.InfoLocalized("Log_LogsCopied", "تمامی لاگ‌ها در حافظه کپی شدند.", "UI");
            }
            catch (Exception ex) { LoggerService.Error($"CopyLogs: {ex.Message}", "UI"); }
        }

        private void ExportLogs_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs", $"log_export_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
                File.WriteAllText(path, LoggerService.ExportAll());
                LoggerService.SuccessLocalized("Log_LogsSaved", "فایل لاگ ذخیره شد: {0}", "UI", path);
                ModernDialogService.ShowInfo(LanguageManager.Instance.GetFormattedString("Msg_LogSaved", "فایل لاگ در مسیر زیر ذخیره شد:\n{0}", path));
            }
            catch (Exception ex)
            {
                LoggerService.ErrorLocalized("Log_UI_67c6a1", "خطا در ذخیره لاگ: {0}", "UI", ex.Message);
            }
        }

        private void OnFileReadyForTranscription(string filePath)
        {
            Dispatcher.Invoke(() =>
            {
                NavList.SelectedIndex = 0;
                MainTabs.SelectedIndex = 0;
                TranscribeTabCtrl.LoadFile(filePath);
                GlobalStatus.Text = $"فایل آماده رونویسی: {Path.GetFileName(filePath)}";
                LoggerService.InfoLocalized("Log_EVENT_BUS_d6c78f", "فایل ارسال شد به بخش رونویسی: {0}", "EVENT_BUS", filePath);
            });
        }

        private void OnSrtReady(string srtPath)
        {
            Dispatcher.Invoke(() =>
            {
                NavList.SelectedIndex = 2;
                MainTabs.SelectedIndex = 2;
                SubtitleTabCtrl.LoadSrtFile(srtPath);
                GlobalStatus.Text = "فایل زیرنویس در محیط ویرایشگر بارگذاری شد.";
                LoggerService.InfoLocalized("Log_EVENT_BUS_f3c8be", "زیرنویس ارسال شد به ویرایشگر: {0}", "EVENT_BUS", srtPath);
            });
        }

        private void OnMediaReadyForSubtitle(string mediaPath)
        {
            Dispatcher.Invoke(() =>
            {
                NavList.SelectedIndex = 2;
                MainTabs.SelectedIndex = 2;
                SubtitleTabCtrl.LoadMediaFile(mediaPath);
                GlobalStatus.Text = $"رسانه در استودیوی زیرنویس بارگذاری شد: {Path.GetFileName(mediaPath)}";
                LoggerService.InfoLocalized("Log_EVENT_BUS_b1533a", "ویدیو/صوت ارسال شد به استودیوی زیرنویس: {0}", "EVENT_BUS", mediaPath);
            });
        }

        // ThemeCombo removed from header — theme switching via Settings
        private void ThemeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) { }

        private void Settings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var settingsWin = new Windows.SettingsWindow
                {
                    Owner = this
                };
                settingsWin.ShowDialog();
            }
            catch (Exception ex) { LoggerService.Error($"SettingsWindow open error: {ex.Message}", "UI"); }
        }

        private void MinimizeWindow_Click(object sender, RoutedEventArgs e)
        {
            try { WindowState = WindowState.Minimized; }
            catch (Exception ex) { LoggerService.Error($"Minimize: {ex.Message}", "UI"); }
        }

        private void MaximizeWindow_Click(object sender, RoutedEventArgs e)
        {
            try { WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized; }
            catch (Exception ex) { LoggerService.Error($"Maximize: {ex.Message}", "UI"); }
        }

        private Windows.RaycastWidgetWindow? _raycastWidget;

        private void RaycastWidgetBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_raycastWidget == null || !_raycastWidget.IsLoaded)
                    _raycastWidget = new Windows.RaycastWidgetWindow();
                _raycastWidget.Show();
                _raycastWidget.Activate();
            }
            catch (Exception ex) { LoggerService.Error($"RaycastWidget: {ex.Message}", "UI"); }
        }

        private void CloseWindow_Click(object sender, RoutedEventArgs e)
        {
            try { Close(); }
            catch (Exception ex) { LoggerService.Error($"CloseWindow: {ex.Message}", "UI"); }
        }

        protected override void OnClosed(EventArgs e)
        {
            AppEventBus.FileReadyForTranscription -= OnFileReadyForTranscription;
            AppEventBus.SrtReadyForSubtitleEditor -= OnSrtReady;
            AppEventBus.MediaReadyForSubtitleEditor -= OnMediaReadyForSubtitle;
            LoggerService.OnLogAdded -= OnLogAdded;
            _hotkeyService.Dispose();

            if (_raycastWidget != null)
            {
                try
                {
                    _raycastWidget.Close();
                }
                catch { }
                _raycastWidget = null;
            }

            base.OnClosed(e);
            Application.Current.Shutdown();
        }
    }
}
