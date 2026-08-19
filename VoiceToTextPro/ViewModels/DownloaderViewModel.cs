using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows.Input;
using VoiceToTextPro.Models;
using VoiceToTextPro.Services;

namespace VoiceToTextPro.ViewModels
{
    public class DownloaderViewModel : ViewModelBase
    {
        private PythonBridge? _bridge;
        private string _mediaUrl = "";
        private string _mediaTitle = "عنوان رسانه در اینجا نمایش داده می‌شود";
        private string _mediaInfo = "پلتفرم و توضیحات کانال / کاربر";
        private string _selectedQuality = "Best Quality";
        private string _downloadLogText = "منتظر دریافت اطلاعات لینک...";
        private double _downloadProgress;
        private bool _isDownloading;
        private bool _hasMediaInfo;
        private string? _downloadedFilePath;

        public ObservableCollection<DownloadedMediaItem> GalleryItems { get; } = new();
        public ObservableCollection<string> Formats { get; } = new();

        public string MediaUrl
        {
            get => _mediaUrl;
            set => SetProperty(ref _mediaUrl, value);
        }

        public string MediaTitle
        {
            get => _mediaTitle;
            set => SetProperty(ref _mediaTitle, value);
        }

        public string MediaInfo
        {
            get => _mediaInfo;
            set => SetProperty(ref _mediaInfo, value);
        }

        public string SelectedQuality
        {
            get => _selectedQuality;
            set => SetProperty(ref _selectedQuality, value);
        }

        public string DownloadLogText
        {
            get => _downloadLogText;
            set => SetProperty(ref _downloadLogText, value);
        }

        public double DownloadProgress
        {
            get => _downloadProgress;
            set => SetProperty(ref _downloadProgress, value);
        }

        public bool IsDownloading
        {
            get => _isDownloading;
            set
            {
                if (SetProperty(ref _isDownloading, value))
                {
                    OnPropertyChanged(nameof(CanStartDownload));
                }
            }
        }

        public bool HasMediaInfo
        {
            get => _hasMediaInfo;
            set
            {
                if (SetProperty(ref _hasMediaInfo, value))
                {
                    OnPropertyChanged(nameof(CanStartDownload));
                }
            }
        }

        public bool CanStartDownload => HasMediaInfo && !IsDownloading;

        public ICommand FetchInfoCommand { get; }
        public ICommand StartDownloadCommand { get; }
        public ICommand RefreshGalleryCommand { get; }

        public DownloaderViewModel()
        {
            FetchInfoCommand = new AsyncRelayCommand(FetchInfoAsync, () => !IsDownloading && !string.IsNullOrWhiteSpace(MediaUrl));
            StartDownloadCommand = new AsyncRelayCommand(StartDownloadAsync, () => CanStartDownload);
            RefreshGalleryCommand = new RelayCommand(LoadGallery);

            LoadGallery();
        }

        public async System.Threading.Tasks.Task FetchInfoAsync()
        {
            if (string.IsNullOrWhiteSpace(MediaUrl)) return;

            IsDownloading = true;
            DownloadLogText = "در حال تحلیل لینک و دریافت اطلاعات کیفیت‌ها...";
            DownloadProgress = 0;

            _bridge = new PythonBridge();
            _bridge.OnResult += (res) =>
            {
                try
                {
                    var json = Newtonsoft.Json.Linq.JObject.Parse(res);
                    MediaTitle = json["title"]?.ToString() ?? "رسانه انتخابی";
                    string platform = json["platform"]?.ToString() ?? "پلتفرم";
                    string uploader = json["uploader"]?.ToString() ?? "کانال";
                    string icon = json["icon"]?.ToString() ?? "🎥";
                    MediaInfo = $"{icon} پلتفرم: {platform} | کانال / کاربر: {uploader}";

                    Formats.Clear();
                    var fmts = json["formats"]?.ToObject<string[]>();
                    if (fmts != null && fmts.Length > 0)
                    {
                        foreach (var f in fmts) Formats.Add(f);
                        SelectedQuality = Formats.First();
                    }
                    else
                    {
                        Formats.Add("Best Quality");
                        Formats.Add("فقط صوت (Audio MP3)");
                        SelectedQuality = "Best Quality";
                    }

                    HasMediaInfo = true;
                    DownloadLogText = "اطلاعات لینک با موفقیت دریافت شد. آماده دانلود.";
                }
                catch (Exception ex)
                {
                    LoggerService.WarnLocalized("Log_DOWNLOADER_95d6b0", "خطا در پارس اطلاعات لینک: {0}", "DOWNLOADER", ex.Message);
                }
            };

            _bridge.OnError += (err) =>
            {
                DownloadLogText = $"❌ خطا: {err}";
                LoggerService.Error(err, "DOWNLOADER");
            };

            await _bridge.RunAsync("info", MediaUrl);
            IsDownloading = false;
        }

        public async System.Threading.Tasks.Task StartDownloadAsync()
        {
            if (string.IsNullOrWhiteSpace(MediaUrl)) return;

            IsDownloading = true;
            DownloadLogText = "در حال شروع فرایند دانلود...";
            DownloadProgress = 0;

            string outDir = AppSettings.Instance.DownloadDirectory;
            Directory.CreateDirectory(outDir);

            _bridge = new PythonBridge();
            _bridge.OnProgress += (pct, msg) =>
            {
                DownloadProgress = pct;
                DownloadLogText = msg;
            };

            _bridge.OnResult += (res) =>
            {
                _downloadedFilePath = res;
                try
                {
                    var json = Newtonsoft.Json.Linq.JObject.Parse(res);
                    _downloadedFilePath = json["file"]?.ToString() ?? res;
                }
                catch (Exception ex)
                {
                    LoggerService.WarnLocalized("Log_DOWNLOADER_061483", "خطا در پارس پاسخ JSON دانلود: {0}", "DOWNLOADER", ex.Message);
                }

                if (!string.IsNullOrEmpty(_downloadedFilePath) && File.Exists(_downloadedFilePath))
                {
                    LoggerService.SuccessLocalized("Log_DOWNLOADER_4b2356", "دانلود کامل شد: {0}", "DOWNLOADER", _downloadedFilePath);
                    LoadGallery();
                }
            };

            _bridge.OnError += (err) =>
            {
                DownloadLogText = $"❌ خطا: {err}";
                LoggerService.Error(err, "DOWNLOADER");
            };

            await _bridge.RunAsync("download", MediaUrl, outDir, SelectedQuality);
            IsDownloading = false;
        }

        public void LoadGallery()
        {
            try
            {
                GalleryItems.Clear();
                string outDir = AppSettings.Instance.DownloadDirectory;
                if (!Directory.Exists(outDir)) return;

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

                    GalleryItems.Add(new DownloadedMediaItem
                    {
                        Title = f.Name,
                        FilePath = f.FullName,
                        PlatformIcon = icon,
                        DownloadedAt = f.LastWriteTime
                    });
                }
            }
            catch (Exception ex)
            {
                LoggerService.WarnLocalized("Log_GALLERY_47b617", "خطا در بارگذاری گالری رسانه‌ها: {0}", "GALLERY", ex.Message);
            }
        }
    }
}
