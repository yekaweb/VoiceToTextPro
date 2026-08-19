using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media.Imaging;
using VoiceToTextPro.Models;
using VoiceToTextPro.Services;

namespace VoiceToTextPro.Tabs
{
    public partial class DownloaderTab : UserControl
    {
        private PythonBridge? _bridge;
        private string? _lastInfoUrl;
        private string? _downloadedFilePath;
        public ObservableCollection<DownloadedMediaItem> GalleryItems { get; } = new();

        public DownloaderTab()
        {
            InitializeComponent();
            GalleryListView.ItemsSource = GalleryItems;
            LoadGalleryFromDirectory();
        }

        private void UrlInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            string url = UrlInput.Text.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(url))
            {
                DetectedPlatformBadge.Visibility = Visibility.Collapsed;
                return;
            }

            DetectedPlatformBadge.Visibility = Visibility.Visible;

            if (url.Contains("youtube.com") || url.Contains("youtu.be"))
            {
                DetectedPlatformText.Text = "▶️ YouTube";
            }
            else if (url.Contains("instagram.com") || url.Contains("instagr.am"))
            {
                DetectedPlatformText.Text = "📸 Instagram";
            }
            else if (url.Contains("tiktok.com"))
            {
                DetectedPlatformText.Text = "🎵 TikTok";
            }
            else
            {
                DetectedPlatformText.Text = "🌐 لینک عمومی رسانه";
            }
        }

        private async void FetchInfo_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string url = UrlInput.Text.Trim();
                if (string.IsNullOrEmpty(url))
                {
                    ModernDialogService.ShowWarning(LanguageManager.Instance.GetString("Msg_EnterUrl", "لطفاً آدرس لینک رسانه را وارد کنید."));
                    return;
                }

            DownloadLogText.Text = "در حال بررسی لینک و کشف کیفیت‌های واقعی ویدیو...";
            LoggerService.InfoLocalized("Log_DOWNLOADER_b3eb32", "کشف اطلاعات و کیفیت‌های واقعی لینک: {0}", "DOWNLOADER", url);

            _bridge = new PythonBridge();
            _bridge.OnResult += (res) =>
            {
                Dispatcher.Invoke(() =>
                {
                    try
                    {
                        var json = Newtonsoft.Json.Linq.JObject.Parse(res);
                        string title = json["title"]?.ToString() ?? "رسانه شناسایی گردید";
                        string uploader = json["uploader"]?.ToString() ?? "کاربر";
                        string platform = json["platform"]?.ToString() ?? "پلتفرم";
                        string icon = json["icon"]?.ToString() ?? "🎥";
                        string thumb = json["thumbnail_url"]?.ToString() ?? "";

                        MediaTitleText.Text = title;
                        MediaUploaderText.Text = $"{icon} پلتفرم: {platform} | کانال / کاربر: {uploader}";

                        if (!string.IsNullOrEmpty(thumb))
                        {
                            try
                            {
                                ThumbnailImage.Source = new BitmapImage(new Uri(thumb));
                            }
                            catch (Exception ex)
                            {
                                LoggerService.WarnLocalized("Log_DOWNLOADER_dccbba", "خطا در بارگذاری تصویر کاور: {0}", "DOWNLOADER", ex.Message);
                            }
                        }

                        // Populate formats
                        QualityCombo.Items.Clear();
                        var formats = json["formats"]?.ToObject<string[]>();
                        if (formats != null && formats.Length > 0)
                        {
                            foreach (var fmt in formats)
                            {
                                QualityCombo.Items.Add(new ComboBoxItem { Content = fmt });
                            }
                            QualityCombo.SelectedIndex = 0;
                        }
                        else
                        {
                            QualityCombo.Items.Add(new ComboBoxItem { Content = "Best Quality (بهترین کیفیت)", IsSelected = true });
                            QualityCombo.Items.Add(new ComboBoxItem { Content = "فقط صوت (Audio MP3)" });
                        }

                        MediaInfoCard.Visibility = Visibility.Visible;
                        DownloadLogText.Text = $"✅ اطلاعات رسانه و کیفیت‌ها کشف گردید. کیفیت مورد نظر را انتخاب و دکمه دانلود را بزنید.";
                        LoggerService.SuccessLocalized("Log_DOWNLOADER_10e920", "کیفیت‌های کشف‌شده برای {0}: {1} حالت", "DOWNLOADER", title, QualityCombo.Items.Count);
                    }
                    catch (Exception ex)
                    {
                        DownloadLogText.Text = $"پاسخ دریافت شد اما خطا در تحلیل جزییات: {ex.Message}";
                        MediaInfoCard.Visibility = Visibility.Visible;
                    }
                });
            };

            _bridge.OnError += (err) =>
            {
                Dispatcher.Invoke(() => DownloadLogText.Text = err);
            };

            _lastInfoUrl = url;
            await _bridge.RunAsync("info", url);
            }
            catch (Exception ex) { LoggerService.Error($"FetchInfo: {ex.Message}", "DOWNLOADER"); }
        }

        private async void DirectDownload_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string url = UrlInput.Text.Trim();
                if (string.IsNullOrEmpty(url))
                {
                    ModernDialogService.ShowWarning(LanguageManager.Instance.GetString("Msg_EnterUrl", "لطفاً آدرس لینک رسانه را وارد کنید."));
                    return;
                }

                if (QualityCombo.Items.Count == 0)
                {
                    QualityCombo.Items.Add(new ComboBoxItem { Content = "Best Quality (اصلی)", IsSelected = true });
                    QualityCombo.SelectedIndex = 0;
                }

                Download_Click(sender, e);
            }
            catch (Exception ex) { LoggerService.Error($"DirectDownload: {ex.Message}", "DOWNLOADER"); }
        }

        private async void Download_Click(object sender, RoutedEventArgs e)
        {
            string url = UrlInput.Text.Trim();
            if (string.IsNullOrEmpty(url))
            {
                ModernDialogService.ShowWarning(LanguageManager.Instance.GetString("Msg_EnterValidUrl", "لطفاً آدرس لینک معتبر را وارد کنید."));
                return;
            }

            _downloadedFilePath = null;
            DownloadBtn.IsEnabled = false;
            StopDownloadBtn.IsEnabled = true;
            DownloadProgress.Value = 0;
            DownloadPercentText.Text = "0%";
            DownloadLogText.Text = "در حال دریافت فایل رسانه با کیفیت انتخابی...";

            bool audioOnly = AudioOnlyCheck.IsChecked == true;
            string selectedQuality = (QualityCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Best Quality";
            
            var settings = AppSettings.Load();
            string outDir = settings.DownloadDirectory;
            Directory.CreateDirectory(outDir);

            _bridge = new PythonBridge();

            _bridge.OnProgress += (pct, msg) =>
            {
                Dispatcher.Invoke(() =>
                {
                    DownloadProgress.Value = pct;
                    DownloadPercentText.Text = $"{pct:0}%";
                    DownloadLogText.Text = msg;
                    
                    int filled = (int)(pct / 5.0);
                    if (filled < 0) filled = 0;
                    if (filled > 20) filled = 20;
                    int empty = 20 - filled;
                    string bar = new string('█', filled) + new string('░', empty);
                    string terminalMsg = $"[{bar}] {msg}";
                    
                    LoggerService.UpdateLastLog(terminalMsg, "DOWNLOADER", LogLevel.Info);
                });
            };

            _bridge.OnResult += (res) =>
            {
                Dispatcher.Invoke(() =>
                {
                    string downloadedFile = res;
                    try
                    {
                        var json = Newtonsoft.Json.Linq.JObject.Parse(res);
                        downloadedFile = json["file"]?.ToString() ?? res;
                    }
                    catch (Exception ex)
                    {
                        LoggerService.WarnLocalized("Log_DOWNLOADER_061483", "خطا در پارس پاسخ JSON دانلود: {0}", "DOWNLOADER", ex.Message);
                    }

                    if (!string.IsNullOrEmpty(downloadedFile) && File.Exists(downloadedFile))
                    {
                        _downloadedFilePath = downloadedFile;
                        try
                        {
                            var historyPath = Path.Combine(outDir, "history.json");
                            var history = new System.Collections.Generic.Dictionary<string, string>();
                            if (File.Exists(historyPath))
                            {
                                history = Newtonsoft.Json.JsonConvert.DeserializeObject<System.Collections.Generic.Dictionary<string, string>>(File.ReadAllText(historyPath)) ?? new System.Collections.Generic.Dictionary<string, string>();
                            }
                            history[downloadedFile] = url;
                            File.WriteAllText(historyPath, Newtonsoft.Json.JsonConvert.SerializeObject(history));
                        }
                        catch (Exception ex)
                        {
                            LoggerService.WarnLocalized("Log_DOWNLOADER_3dc77b", "خطا در ذخیره تاریخچه دانلود: {0}", "DOWNLOADER", ex.Message);
                        }

                        LoggerService.SuccessLocalized("Log_DOWNLOADER_4b2356", "دانلود کامل شد: {0}", "DOWNLOADER", downloadedFile);
                        LoadGalleryFromDirectory();
                    }
                    else
                    {
                        _downloadedFilePath = null;
                        LoggerService.ErrorLocalized("Log_DOWNLOADER_2867f8", "فایل دانلودشده روی دیسک یافت نشد یا عملیات با خطا مواجه شد.", "DOWNLOADER");
                    }
                });
            };

            _bridge.OnError += (err) =>
            {
                Dispatcher.Invoke(() =>
                {
                    _downloadedFilePath = null;
                    DownloadProgress.Value = 0;
                    DownloadPercentText.Text = "0%";
                    DownloadLogText.Text = $"❌ خطا: {err}";
                    LoggerService.Error(err, "DOWNLOADER");
                });
            };

            LoggerService.InfoLocalized("Log_DOWNLOADER_8cc2c4", "شروع دانلود رسانه با کیفیت '{0}': {1}", "DOWNLOADER", selectedQuality, url);

            bool ok = await _bridge.RunAsync("download", url, outDir, selectedQuality, audioOnly ? "true" : "false");

            Dispatcher.Invoke(() =>
            {
                DownloadBtn.IsEnabled = true;
                StopDownloadBtn.IsEnabled = false;
                if (ok && !string.IsNullOrEmpty(_downloadedFilePath) && File.Exists(_downloadedFilePath))
                {
                    DownloadProgress.Value = 100;
                    DownloadPercentText.Text = "100%";
                    DownloadLogText.Text = $"✅ دانلود با موفقیت انجام شد: {Path.GetFileName(_downloadedFilePath)}";
                    LoadGalleryFromDirectory();
                }
                else
                {
                    if (string.IsNullOrEmpty(DownloadLogText.Text) || !DownloadLogText.Text.StartsWith("❌"))
                    {
                        DownloadProgress.Value = 0;
                        DownloadPercentText.Text = "0%";
                        DownloadLogText.Text = "❌ خطا: دریافت لینک یا دانلود رسانه با ناامنی همراه بود و فایلی ذخیره نشد.";
                    }
                }
            });
        }

        private void StopDownload_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _bridge?.Stop();
                DownloadBtn.IsEnabled = true;
                StopDownloadBtn.IsEnabled = false;
                DownloadLogText.Text = "دانلود توسط کاربر متوقف شد...";
                LoggerService.WarnLocalized("Log_DOWNLOADER_16d70d", "عملیات دانلود متوقف شد.", "DOWNLOADER");
            }
            catch (Exception ex) { LoggerService.Error($"StopDownload: {ex.Message}", "DOWNLOADER"); }
        }

        private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            try
            {
                if (e.Uri != null && (e.Uri.AbsoluteUri.StartsWith("http://") || e.Uri.AbsoluteUri.StartsWith("https://")))
                {
                    Process.Start(new ProcessStartInfo { FileName = e.Uri.AbsoluteUri, UseShellExecute = true });
                }
                else
                {
                    ModernDialogService.ShowInfo(LanguageManager.Instance.GetString("Msg_SourceUrlNotFound", "لینک منبع برای این فایل در تاریخچه یافت نشد."));
                }
            }
            catch (Exception ex)
            {
                LoggerService.WarnLocalized("Log_DOWNLOADER_681556", "باز کردن لینک با خطا مواجه شد: {0}", "DOWNLOADER", ex.Message);
            }
            finally
            {
                e.Handled = true;
            }
        }

        // ══ Gallery Management Methods ══
        private void LoadGalleryFromDirectory()
        {
            try
            {
                GalleryItems.Clear();
                var settings = AppSettings.Load();
                string outDir = settings.DownloadDirectory;

                if (!Directory.Exists(outDir)) return;

                var historyPath = Path.Combine(outDir, "history.json");
                var history = new System.Collections.Generic.Dictionary<string, string>();
                if (File.Exists(historyPath))
                {
                    try
                    {
                        history = Newtonsoft.Json.JsonConvert.DeserializeObject<System.Collections.Generic.Dictionary<string, string>>(File.ReadAllText(historyPath)) ?? new System.Collections.Generic.Dictionary<string, string>();
                    }
                    catch (Exception ex)
                    {
                        LoggerService.WarnLocalized("Log_GALLERY_9cc472", "خطا در خواندن فایل تاریخچه دانلود: {0}", "GALLERY", ex.Message);
                    }
                }

                var files = new DirectoryInfo(outDir).GetFiles("*.*")
                    .Where(f => !f.Name.EndsWith(".part") && !f.Name.EndsWith(".ytdl") && !f.Name.EndsWith(".json"))
                    .OrderByDescending(f => f.LastWriteTime)
                    .Take(50);

                foreach (var f in files)
                {
                    string ext = f.Extension.ToLower();
                    string icon = ext switch
                    {
                        ".mp3" or ".wav" or ".m4a" => "🎵",
                        ".mp4" or ".mkv" or ".avi" or ".mov" => "🎬",
                        _ => "📁"
                    };

                    string sourceUrl = history.ContainsKey(f.FullName) ? history[f.FullName] : "نامشخص";

                    GalleryItems.Add(new DownloadedMediaItem
                    {
                        Title = f.Name,
                        FilePath = f.FullName,
                        PlatformIcon = icon,
                        SourceUrl = sourceUrl,
                        DownloadedAt = f.LastWriteTime
                    });
                }

                GalleryCountText.Text = $"{GalleryItems.Count} فایل";
            }
            catch (Exception ex)
            {
                LoggerService.WarnLocalized("Log_GALLERY_47b617", "خطا در بارگذاری گالری رسانه‌ها: {0}", "GALLERY", ex.Message);
            }
        }

        private void RefreshGallery_Click(object sender, RoutedEventArgs e) => LoadGalleryFromDirectory();

        private void SendItemToTranscribe_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is Button btn && btn.Tag is string path && File.Exists(path))
                {
                    AppEventBus.RaiseFileReady(path);
                    LoggerService.InfoLocalized("Log_GALLERY_157f7a", "رسانه انتخابی به تب رونویسی ارسال شد: {0}", "GALLERY", path);
                }
            }
            catch (Exception ex) { LoggerService.Error($"SendItemToTranscribe: {ex.Message}", "GALLERY"); }
        }

        private void SendItemToSubtitle_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is Button btn && btn.Tag is string path && File.Exists(path))
                {
                    AppEventBus.RaiseMediaReadyForSubtitle(path);
                    LoggerService.InfoLocalized("Log_GALLERY_0d08c9", "رسانه انتخابی به استودیوی زیرنویس ارسال شد: {0}", "GALLERY", path);
                }
            }
            catch (Exception ex) { LoggerService.Error($"SendItemToSubtitle: {ex.Message}", "GALLERY"); }
        }

        private void PlayMedia_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string path && File.Exists(path))
            {
                try
                {
                    Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
                    LoggerService.InfoLocalized("Log_GALLERY_d66431", "پخش فایل رسانه: {0}", "GALLERY", path);
                }
                catch (Exception ex)
                {
                    LoggerService.ErrorLocalized("Log_GALLERY_ef66dd", "عدم توانایی در پخش فایل: {0}", "GALLERY", ex.Message);
                }
            }
        }

        private void ShowInExplorer_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string path && File.Exists(path))
            {
                try
                {
                    Process.Start("explorer.exe", $"/select,\"{path}\"");
                }
                catch (Exception ex)
                {
                    LoggerService.WarnLocalized("Log_GALLERY_c41348", "خطا در نمایش فایل در Explorer: {0}", "GALLERY", ex.Message);
                }
            }
        }

        private void DeleteFile_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string path && File.Exists(path))
            {
                if (ModernDialogService.AskConfirmation(LanguageManager.Instance.GetFormattedString("Msg_ConfirmDeleteFile", "آیا از حذف این فایل مطمئن هستید؟\n{0}", Path.GetFileName(path))))
                {
                    try
                    {
                        File.Delete(path);
                        LoggerService.SuccessLocalized("Log_GALLERY_68325a", "فایل حذف گردید: {0}", "GALLERY", path);
                        LoadGalleryFromDirectory();
                    }
                    catch (Exception ex)
                    {
                        LoggerService.ErrorLocalized("Log_GALLERY_f6b4f2", "خطا در حذف فایل: {0}", "GALLERY", ex.Message);
                    }
                }
            }
        }

        private void OpenSourceUrl_Click(object sender, RoutedEventArgs e)
        {
            string? url = (sender as Button)?.Tag as string;
            if (string.IsNullOrEmpty(url) && sender is Hyperlink link && link.NavigateUri != null)
            {
                url = link.NavigateUri.AbsoluteUri;
            }

            if (!string.IsNullOrEmpty(url) && (url.StartsWith("http://") || url.StartsWith("https://")))
            {
                try
                {
                    Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
                    LoggerService.InfoLocalized("Log_GALLERY_22e548", "باز کردن لینک منبع رسانه در مرورگر: {0}", "GALLERY", url);
                }
                catch (Exception ex)
                {
                    LoggerService.WarnLocalized("Log_GALLERY_50fc73", "باز کردن لینک منبع با خطا مواجه شد: {0}", "GALLERY", ex.Message);
                }
            }
            else
            {
                ModernDialogService.ShowInfo(LanguageManager.Instance.GetString("Msg_SourceUrlNotFound", "لینک منبع معتبری برای این فایل در تاریخچه یافت نشد."));
            }
        }

        // Context Menu Handlers
        private void CtxOpenSourceUrl_Click(object sender, RoutedEventArgs e)
        {
            if (GalleryListView.SelectedItem is DownloadedMediaItem item && !string.IsNullOrEmpty(item.SourceUrl) && (item.SourceUrl.StartsWith("http://") || item.SourceUrl.StartsWith("https://")))
            {
                try
                {
                    Process.Start(new ProcessStartInfo { FileName = item.SourceUrl, UseShellExecute = true });
                    LoggerService.InfoLocalized("Log_GALLERY_febb87", "باز کردن لینک منبع از منوی راست‌کلیک: {0}", "GALLERY", item.SourceUrl);
                }
                catch (Exception ex)
                {
                    LoggerService.WarnLocalized("Log_GALLERY_ec2680", "خطا در باز کردن لینک منبع: {0}", "GALLERY", ex.Message);
                }
            }
            else
            {
                ModernDialogService.ShowInfo(LanguageManager.Instance.GetString("Msg_SourceUrlNotFound", "لینک منبع معتبری برای این فایل یافت نشد."));
            }
        }

        private void CtxSendToTranscribe_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (GalleryListView.SelectedItem is DownloadedMediaItem item && File.Exists(item.FilePath))
                {
                    AppEventBus.RaiseFileReady(item.FilePath);
                }
            }
            catch (Exception ex) { LoggerService.Error($"CtxSendToTranscribe: {ex.Message}", "GALLERY"); }
        }

        private void CtxSendToSubtitle_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (GalleryListView.SelectedItem is DownloadedMediaItem item && File.Exists(item.FilePath))
                {
                    AppEventBus.RaiseMediaReadyForSubtitle(item.FilePath);
                }
            }
            catch (Exception ex) { LoggerService.Error($"CtxSendToSubtitle: {ex.Message}", "GALLERY"); }
        }

        private void CtxPlayMedia_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (GalleryListView.SelectedItem is DownloadedMediaItem item && File.Exists(item.FilePath))
                {
                    Process.Start(new ProcessStartInfo { FileName = item.FilePath, UseShellExecute = true });
                }
            }
            catch (Exception ex) { LoggerService.Error($"CtxPlayMedia: {ex.Message}", "GALLERY"); }
        }

        private void CtxShowInExplorer_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (GalleryListView.SelectedItem is DownloadedMediaItem item && File.Exists(item.FilePath))
                {
                    Process.Start("explorer.exe", $"/select,\"{item.FilePath}\"");
                }
            }
            catch (Exception ex) { LoggerService.Error($"CtxShowInExplorer: {ex.Message}", "GALLERY"); }
        }

        private void CtxDeleteFile_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (GalleryListView.SelectedItem is DownloadedMediaItem item && File.Exists(item.FilePath))
                {
                    if (ModernDialogService.AskConfirmation(LanguageManager.Instance.GetFormattedString("Msg_ConfirmDeleteFile", "آیا از حذف این فایل مطمئن هستید؟\n{0}", item.FileName)))
                    {
                        File.Delete(item.FilePath);
                        LoadGalleryFromDirectory();
                    }
                }
            }
            catch (Exception ex) { LoggerService.Error($"CtxDeleteFile: {ex.Message}", "GALLERY"); }
        }
    }
}
