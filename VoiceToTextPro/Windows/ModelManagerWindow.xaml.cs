using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using VoiceToTextPro.Models;
using VoiceToTextPro.Services;

namespace VoiceToTextPro.Windows
{
    public partial class ModelManagerWindow : Window
    {
        public ObservableCollection<AiModel> VoskModels { get; set; } = new();
        public ObservableCollection<AiModel> WhisperModels { get; set; } = new();
        public ObservableCollection<AiModel> PiperModels { get; set; } = new();

        private ICollectionView _voskView = null!;
        private ICollectionView _whisperView = null!;
        private ICollectionView _piperView = null!;
        private bool _isLoaded = false;

        public ModelManagerWindow()
        {
            InitializeComponent();
            this.FlowDirection = (LanguageManager.Instance.CurrentCulture == "fa-IR" || LanguageManager.Instance.CurrentCulture == "ar-SA") ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

            _voskView = CollectionViewSource.GetDefaultView(VoskModels);
            _whisperView = CollectionViewSource.GetDefaultView(WhisperModels);
            _piperView = CollectionViewSource.GetDefaultView(PiperModels);

            // Sort installed & corrupted models to the TOP of the list
            _voskView.SortDescriptions.Add(new SortDescription(nameof(AiModel.SortOrder), ListSortDirection.Ascending));
            _voskView.SortDescriptions.Add(new SortDescription(nameof(AiModel.CleanName), ListSortDirection.Ascending));

            _whisperView.SortDescriptions.Add(new SortDescription(nameof(AiModel.SortOrder), ListSortDirection.Ascending));
            _whisperView.SortDescriptions.Add(new SortDescription(nameof(AiModel.CleanName), ListSortDirection.Ascending));

            _piperView.SortDescriptions.Add(new SortDescription(nameof(AiModel.SortOrder), ListSortDirection.Ascending));
            _piperView.SortDescriptions.Add(new SortDescription(nameof(AiModel.CleanName), ListSortDirection.Ascending));

            _voskView.Filter = FilterModel;
            _whisperView.Filter = FilterModel;
            _piperView.Filter = FilterModel;

            VoskModelsList.ItemsSource = _voskView;
            WhisperModelsList.ItemsSource = _whisperView;
            PiperModelsList.ItemsSource = _piperView;

            Loaded += ModelManagerWindow_Loaded;
            Unloaded += ModelManagerWindow_Unloaded;
        }

        private bool FilterModel(object obj)
        {
            if (obj is not AiModel model) return false;
            string query = SearchTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(query)) return true;

            string target = $"{model.Name} {model.CleanName} {model.Description} {model.FolderName} {model.DisplayLanguage}".ToLowerInvariant();
            return target.Contains(query.ToLowerInvariant());
        }

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ClearSearchBtn.Visibility = string.IsNullOrWhiteSpace(SearchTextBox.Text) ? Visibility.Collapsed : Visibility.Visible;
            _voskView.Refresh();
            _whisperView.Refresh();
            _piperView.Refresh();
        }

        private void ClearSearch_Click(object sender, RoutedEventArgs e)
        {
            SearchTextBox.Text = "";
            SearchTextBox.Focus();
        }

        private async void ModelManagerWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (_isLoaded) return;
            _isLoaded = true;

            ModelDownloadManager.Instance.OnProgressChanged += SyncUiWithManager;
            ModelDownloadManager.Instance.OnDownloadCompleted += Manager_OnDownloadCompleted;

            if (ModelDownloadManager.Instance.IsDownloading)
            {
                SyncUiWithManager();
            }

            await LoadAvailableModelsLiveAsync();
        }

        private void ModelManagerWindow_Unloaded(object sender, RoutedEventArgs e)
        {
            ModelDownloadManager.Instance.OnProgressChanged -= SyncUiWithManager;
            ModelDownloadManager.Instance.OnDownloadCompleted -= Manager_OnDownloadCompleted;
        }

        private void SyncUiWithManager()
        {
            Dispatcher.Invoke(() =>
            {
                if (ModelDownloadManager.Instance.IsDownloading)
                {
                    StatusText.Text = ModelDownloadManager.Instance.StatusText;
                    DownloadProgress.Value = ModelDownloadManager.Instance.ProgressValue;
                    DownloadProgress.IsIndeterminate = ModelDownloadManager.Instance.IsIndeterminate;
                }
            });
        }

        private void Manager_OnDownloadCompleted(bool success, string message)
        {
            Dispatcher.Invoke(() =>
            {
                SyncUiWithManager();
                if (success)
                {
                    StatusText.Text = LanguageManager.Instance.GetString("ModelManager_StatusInstalledSuccess", "مدل با موفقیت نصب شد!");
                    DownloadProgress.Value = 100;
                    DownloadProgress.IsIndeterminate = false;
                }
                else
                {
                    StatusText.Text = message;
                    DownloadProgress.Value = 0;
                    DownloadProgress.IsIndeterminate = false;
                }

                // Refresh installation states and sort views
                string targetDir = AppSettings.Load().ModelsDirectory;
                foreach (var m in VoskModels) m.Status = ModelDownloadManager.Instance.VerifyModelIntegrity(m, targetDir);
                foreach (var m in WhisperModels) m.Status = ModelDownloadManager.Instance.VerifyModelIntegrity(m, targetDir);
                foreach (var m in PiperModels) m.Status = ModelDownloadManager.Instance.VerifyModelIntegrity(m, targetDir);

                _voskView.Refresh();
                _whisperView.Refresh();
                _piperView.Refresh();

                CheckIncompleteModelsAlert();
            });
        }

        private static readonly List<AiModel> s_cachedVoskModels = new();
        private static readonly List<AiModel> s_cachedWhisperModels = new();
        private static readonly List<AiModel> s_cachedPiperModels = new();
        private static bool s_isCatalogCached = false;

        private async Task LoadAvailableModelsLiveAsync(bool forceRefresh = false)
        {
            VoskModels.Clear();
            WhisperModels.Clear();
            PiperModels.Clear();
            string targetDir = AppSettings.Load().ModelsDirectory;

            // ⚡ Instant load from in-memory cache if already fetched during this app session
            if (!forceRefresh && s_isCatalogCached && (s_cachedVoskModels.Count > 0 || s_cachedWhisperModels.Count > 0 || s_cachedPiperModels.Count > 0))
            {
                foreach (var model in s_cachedVoskModels)
                {
                    model.Status = ModelDownloadManager.Instance.VerifyModelIntegrity(model, targetDir);
                    VoskModels.Add(model);
                }

                foreach (var model in s_cachedWhisperModels)
                {
                    model.Status = ModelDownloadManager.Instance.VerifyModelIntegrity(model, targetDir);
                    WhisperModels.Add(model);
                }

                foreach (var model in s_cachedPiperModels)
                {
                    model.Status = ModelDownloadManager.Instance.VerifyModelIntegrity(model, targetDir);
                    PiperModels.Add(model);
                }

                _voskView.Refresh();
                _whisperView.Refresh();
                _piperView.Refresh();

                StatusText.Text = LanguageManager.Instance.GetFormattedString("ModelManager_StatusCountReady", "تعداد {0} مدل آماده استفاده است.", VoskModels.Count + WhisperModels.Count + PiperModels.Count);
                DownloadProgress.IsIndeterminate = false;
                DownloadProgress.Value = 0;
                CheckIncompleteModelsAlert();
                return;
            }

            StatusText.Text = LanguageManager.Instance.GetString("ModelManager_StatusFetching", "در حال دریافت لیست مدل‌ها از سرور...");
            DownloadProgress.IsIndeterminate = true;

            try
            {
                var newVoskList = new List<AiModel>();
                var newWhisperList = new List<AiModel>();

                var client = HttpService.Client;
                // 1. Fetch Vosk Models
                string html = await client.GetStringAsync("https://alphacephei.com/vosk/models");
                string rowPattern = @"<tr>(.*?)</tr>";
                var rows = Regex.Matches(html, rowPattern, RegexOptions.Singleline);
                string currentLang = "Vosk";

                foreach (Match row in rows)
                {
                    string rowHtml = row.Groups[1].Value;
                    var langMatch = Regex.Match(rowHtml, @"<td><strong>([^<]+)</strong></td>");
                    if (langMatch.Success)
                    {
                        currentLang = langMatch.Groups[1].Value.Trim();
                        continue;
                    }

                    var zipMatch = Regex.Match(rowHtml, @"<td><a href=""([^""]+\.zip)"">([^<]+)</a></td>", RegexOptions.Singleline);
                    if (zipMatch.Success)
                    {
                        string url = zipMatch.Groups[1].Value.Trim();
                        string folderName = zipMatch.Groups[2].Value.Trim();

                        var tdMatches = Regex.Matches(rowHtml, @"<td>(.*?)</td>", RegexOptions.Singleline);
                        string size = tdMatches.Count > 1 ? Regex.Replace(tdMatches[1].Groups[1].Value, @"<[^>]+>", "").Trim() : "";
                        string description = tdMatches.Count >= 4 ? Regex.Replace(tdMatches[tdMatches.Count - 2].Groups[1].Value, @"<[^>]+>", "").Trim() : folderName;

                        var model = new AiModel
                        {
                            Name = $"{currentLang} ({folderName})",
                            Description = string.IsNullOrWhiteSpace(description) ? folderName : description,
                            Size = LanguageManager.Instance.GetFormattedString("ModelManager_SizeLabel", "حجم: {0}", size),
                            Url = url,
                            FolderName = folderName
                        };
                        newVoskList.Add(model);
                    }
                }

                // 2. Fetch Whisper Models from HuggingFace (Standard & Distil)
                var whisperCatalog = new[]
                {
                    ("tiny", "Whisper Tiny", "Model_Whisper_Tiny_Desc", "مدل بسیار سبک Whisper نسخه Tiny | چند زبانه", "~75 MB"),
                    ("base", "Whisper Base", "Model_Whisper_Base_Desc", "مدل پایه Whisper نسخه Base | چند زبانه", "~145 MB"),
                    ("small", "Whisper Small", "Model_Whisper_Small_Desc", "مدل بهینه و دقیق Whisper نسخه Small | چند زبانه", "~480 MB"),
                    ("medium", "Whisper Medium", "Model_Whisper_Medium_Desc", "مدل پیشرفته و با دقت بالا Whisper نسخه Medium | چند زبانه", "~1.5 GB"),
                    ("large-v1", "Whisper Large v1", "Model_Whisper_LargeV1_Desc", "مدل بزرگ Whisper نسخه Large v1 | چند زبانه", "~3.1 GB"),
                    ("large-v2", "Whisper Large v2", "Model_Whisper_LargeV2_Desc", "مدل بزرگ Whisper نسخه Large v2 | چند زبانه", "~3.1 GB"),
                    ("large-v3", "Whisper Large v3", "Model_Whisper_LargeV3_Desc", "مدل پرچمدار و فوق دقیق Whisper نسخه Large v3 | چند زبانه", "~3.1 GB"),
                    ("large-v3-turbo", "Whisper Large v3 Turbo", "Model_Whisper_LargeV3Turbo_Desc", "مدل توربو پرسرعت Whisper نسخه Large v3 Turbo | چند زبانه", "~1.6 GB"),
                    ("distil-large-v3", "Distil Whisper Large v3", "Model_Distil_LargeV3_Desc", "مدل تقطیرشده و فوق‌العاده سریع Distil-Large-v3 | سرعت ۲ برابر", "~1.5 GB"),
                    ("distil-large-v2", "Distil Whisper Large v2", "Model_Distil_LargeV2_Desc", "مدل تقطیرشده و سریع Distil-Large-v2 | سرعت ۶ برابر", "~1.5 GB"),
                    ("distil-medium.en", "Distil Whisper Medium EN", "Model_Distil_MediumEn_Desc", "مدل تقطیرشده مخصوص زبان انگلیسی Distil-Medium", "~780 MB"),
                    ("distil-small.en", "Distil Whisper Small EN", "Model_Distil_SmallEn_Desc", "مدل تقطیرشده مخصوص زبان انگلیسی Distil-Small", "~400 MB")
                };

                foreach (var (key, title, descKey, fallbackDesc, sizeStr) in whisperCatalog)
                {
                    string folder = key.StartsWith("distil-") ? $"faster-{key}" : $"faster-whisper-{key}";
                    var model = new AiModel
                    {
                        Name = title,
                        DescriptionKey = descKey,
                        Description = fallbackDesc,
                        Size = LanguageManager.Instance.GetFormattedString("ModelManager_SizeLabel", "حجم: {0}", sizeStr),
                        Url = $"huggingface:Systran/{folder}",
                        FolderName = folder
                    };
                    newWhisperList.Add(model);
                }

                // 3. Piper Voice Models (TTS ONNX)
                var newPiperList = new List<AiModel>();
                var piperCatalog = new[]
                {
                    ("fa_IR-amir-medium", "Piper Persian (Amir - Medium)", "مدل صوتی باکیفیت زبان فارسی (امیر - Medium) | Piper ONNX", "~60 MB", "https://huggingface.co/rhasspy/piper-voices/resolve/main/fa/fa_IR/amir/medium/fa_IR-amir-medium.onnx"),
                    ("fa_IR-gyro-medium", "Piper Persian (Gyro - Medium)", "مدل صوتی روان زبان فارسی (ژیرو - Medium) | Piper ONNX", "~65 MB", "https://huggingface.co/rhasspy/piper-voices/resolve/main/fa/fa_IR/gyro/medium/fa_IR-gyro-medium.onnx"),
                    ("en_US-lessac-high", "Piper English (Lessac - High)", "مدل گفتاری باکیفیت و طبیعی زبان انگلیسی (Lessac - High) | Piper ONNX", "~110 MB", "https://huggingface.co/rhasspy/piper-voices/resolve/main/en/en_US/lessac/high/en_US-lessac-high.onnx"),
                    ("en_US-amy-medium", "Piper English (Amy - Medium)", "مدل گفتاری زنانه زبان انگلیسی (Amy - Medium) | Piper ONNX", "~60 MB", "https://huggingface.co/rhasspy/piper-voices/resolve/main/en/en_US/amy/medium/en_US-amy-medium.onnx"),
                    ("en_US-danny-low", "Piper English (Danny - Light)", "مدل سبک و فوق پرسرعت زبان انگلیسی (Danny - Low) | Piper ONNX", "~25 MB", "https://huggingface.co/rhasspy/piper-voices/resolve/main/en/en_US/danny/low/en_US-danny-low.onnx"),
                    ("de_DE-thorsten-high", "Piper German (Thorsten - High)", "مدل گفتاری عالی زبان آلمانی (Thorsten - High) | Piper ONNX", "~110 MB", "https://huggingface.co/rhasspy/piper-voices/resolve/main/de/de_DE/thorsten/high/de_DE-thorsten-high.onnx"),
                    ("fr_FR-gilles-high", "Piper French (Gilles - High)", "مدل گفتاری زبان فرانسوی (Gilles - High) | Piper ONNX", "~110 MB", "https://huggingface.co/rhasspy/piper-voices/resolve/main/fr/fr_FR/gilles/high/fr_FR-gilles-high.onnx"),
                    ("ar_JO-kareem-medium", "Piper Arabic (Kareem - Medium)", "مدل گفتاری زبان عربی (Kareem - Medium) | Piper ONNX", "~60 MB", "https://huggingface.co/rhasspy/piper-voices/resolve/main/ar/ar_JO/kareem/medium/ar_JO-kareem-medium.onnx")
                };

                foreach (var (key, title, fallbackDesc, sizeStr, urlStr) in piperCatalog)
                {
                    string folder = $"piper-{key}";
                    var model = new AiModel
                    {
                        Name = title,
                        Description = fallbackDesc,
                        Size = LanguageManager.Instance.GetFormattedString("ModelManager_SizeLabel", "حجم: {0}", sizeStr),
                        Url = urlStr,
                        FolderName = folder
                    };
                    newPiperList.Add(model);
                }

                // Save to static session cache
                s_cachedVoskModels.Clear();
                s_cachedVoskModels.AddRange(newVoskList);

                s_cachedWhisperModels.Clear();
                s_cachedWhisperModels.AddRange(newWhisperList);

                s_cachedPiperModels.Clear();
                s_cachedPiperModels.AddRange(newPiperList);

                s_isCatalogCached = true;

                // Populate UI collection and verify local integrity
                foreach (var model in s_cachedVoskModels)
                {
                    model.Status = ModelDownloadManager.Instance.VerifyModelIntegrity(model, targetDir);
                    VoskModels.Add(model);
                }

                foreach (var model in s_cachedWhisperModels)
                {
                    model.Status = ModelDownloadManager.Instance.VerifyModelIntegrity(model, targetDir);
                    WhisperModels.Add(model);
                }

                foreach (var model in s_cachedPiperModels)
                {
                    model.Status = ModelDownloadManager.Instance.VerifyModelIntegrity(model, targetDir);
                    PiperModels.Add(model);
                }

                _voskView.Refresh();
                _whisperView.Refresh();
                _piperView.Refresh();

                StatusText.Text = LanguageManager.Instance.GetFormattedString("ModelManager_StatusCountReady", "تعداد {0} مدل هوش مصنوعی با موفقیت دریافت شد.", VoskModels.Count + WhisperModels.Count + PiperModels.Count);
                CheckIncompleteModelsAlert();
            }
            catch (Exception ex)
            {
                StatusText.Text = $"خطا در دریافت لیست مدل‌ها: {ex.Message}";
                var defaultModel = new AiModel { Name = "فارسی (Vosk سریع)", Description = "vosk-model-small-fa-0.5", Size = "حجم: 48 MB", Url = "https://alphacephei.com/vosk/models/vosk-model-small-fa-0.5.zip", FolderName = "vosk-model-small-fa-0.5" };
                defaultModel.Status = ModelDownloadManager.Instance.VerifyModelIntegrity(defaultModel, targetDir);
                VoskModels.Add(defaultModel);
                _voskView.Refresh();
                CheckIncompleteModelsAlert();
            }
            finally
            {
                DownloadProgress.IsIndeterminate = false;
                DownloadProgress.Value = 0;
            }
        }

        private void CheckIncompleteModelsAlert()
        {
            var incompleteModel = VoskModels.FirstOrDefault(m => m.IsCorrupted) ?? WhisperModels.FirstOrDefault(m => m.IsCorrupted) ?? PiperModels.FirstOrDefault(m => m.IsCorrupted);
            if (incompleteModel != null)
            {
                IncompleteAlertText.Text = LanguageManager.Instance.GetFormattedString("Msg_ModelCorruptedAlert", "⚠️ مدل «{0}» به صورت ناقص دریافت شده است. لطفاً فایل را تکمیل و ترمیم کنید.", incompleteModel.CleanName);
                IncompleteAlertBanner.Visibility = Visibility.Visible;
            }
            else
            {
                IncompleteAlertBanner.Visibility = Visibility.Collapsed;
            }
        }

        private async void RepairIncompleteModel_Click(object sender, RoutedEventArgs e)
        {
            var incompleteModel = VoskModels.FirstOrDefault(m => m.IsCorrupted) ?? WhisperModels.FirstOrDefault(m => m.IsCorrupted) ?? PiperModels.FirstOrDefault(m => m.IsCorrupted);
            if (incompleteModel != null)
            {
                IncompleteAlertBanner.Visibility = Visibility.Collapsed;
                await ModelDownloadManager.Instance.StartDownloadAsync(incompleteModel);
            }
        }

        private void CloseWindow_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private async void Download_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is Button btn && btn.CommandParameter is AiModel model)
                {
                    if (ModelDownloadManager.Instance.IsDownloading)
                    {
                        ModernDialogService.ShowWarning(LanguageManager.Instance.GetString("Msg_DownloadInProgress", "یک دانلود در حال انجام است. لطفا تا پایان یا لغو آن صبر کنید."));
                        return;
                    }
                    await ModelDownloadManager.Instance.StartDownloadAsync(model);
                    _voskView.Refresh();
                    _whisperView.Refresh();
                }
            }
            catch (Exception ex) { LoggerService.Error($"Download_Click: {ex.Message}", "MODEL"); }
        }

        private void PauseResume_Click(object sender, RoutedEventArgs e)
        {
            try { ModelDownloadManager.Instance.TogglePause(); }
            catch (Exception ex) { LoggerService.Error($"PauseResume_Click: {ex.Message}", "MODEL"); }
        }

        private void CancelDownload_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (ModernDialogService.AskConfirmation(LanguageManager.Instance.GetString("Msg_ConfirmCancelDownload", "آیا از لغو دانلود این مدل اطمینان دارید؟")))
                {
                    ModelDownloadManager.Instance.CancelDownload();
                }
            }
            catch (Exception ex) { LoggerService.Error($"CancelDownload_Click: {ex.Message}", "MODEL"); }
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is Button btn && btn.CommandParameter is AiModel model)
                {
                    if (ModelDownloadManager.Instance.IsDownloading && ModelDownloadManager.Instance.CurrentModel == model)
                    {
                        ModernDialogService.ShowWarning(LanguageManager.Instance.GetString("Msg_ModelDownloadingWait", "این مدل در حال حاضر در حال دانلود است. ابتدا آن را لغو کنید."));
                        return;
                    }

                    if (ModernDialogService.AskConfirmation(LanguageManager.Instance.GetFormattedString("Msg_ConfirmDeleteModel", "آیا از حذف کامل مدل «{0}» از دیسک اطمینان دارید؟", model.Name)))
                    {
                        bool deleted = ModelDownloadManager.Instance.DeleteModel(model);
                        if (deleted)
                        {
                            StatusText.Text = $"مدل {model.Name} با موفقیت حذف شد.";
                            _voskView.Refresh();
                            _whisperView.Refresh();
                        }
                        else
                        {
                            ModernDialogService.ShowError(LanguageManager.Instance.GetString("Msg_DeleteModelError", "خطا در حذف فایل‌های مدل. ممکن است فایل‌ها توسط پروسه دیگری قفل شده باشند."));
                        }
                    }
                }
            }
            catch (Exception ex) { LoggerService.Error($"Delete_Click: {ex.Message}", "MODEL"); }
        }

        private async void DeepCheck_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (ModelDownloadManager.Instance.IsDownloading)
                {
                    ModernDialogService.ShowWarning(LanguageManager.Instance.GetString("Msg_DownloadInProgress", "یک دانلود در حال انجام است. لطفا تا پایان آن صبر کنید."));
                    return;
                }

                StatusText.Text = "در حال استعلام آنلاین حجم و تعداد فایل‌ها از سرور جهت بررسی دقیق سلامت...";
                DownloadProgress.IsIndeterminate = true;

                string targetDir = AppSettings.Load().ModelsDirectory;
                int installedCount = 0;
                int corruptedCount = 0;

                foreach (var m in VoskModels)
                {
                    m.Status = await ModelDownloadManager.Instance.DeepVerifyModelAsync(m, targetDir);
                    if (m.Status == ModelStatus.Installed) installedCount++;
                    else if (m.Status == ModelStatus.Corrupted) corruptedCount++;
                }

                foreach (var m in WhisperModels)
                {
                    m.Status = await ModelDownloadManager.Instance.DeepVerifyModelAsync(m, targetDir);
                    if (m.Status == ModelStatus.Installed) installedCount++;
                    else if (m.Status == ModelStatus.Corrupted) corruptedCount++;
                }

                _voskView.Refresh();
                _whisperView.Refresh();

                DownloadProgress.IsIndeterminate = false;
                StatusText.Text = $"بررسی دقیق آنلاین تکمیل شد. تعداد {installedCount} مدل سالم و {corruptedCount} مدل نیازمند ترمیم تشخیص داده شد.";
                ModernDialogService.ShowInfo(LanguageManager.Instance.GetFormattedString("Msg_ModelCheckResult", "بررسی دقیق آنلاین با موفقیت انجام شد:\n\nمدل‌های سالم و کامل: {0}\nمدل‌های ناقص یا نیازمند ترمیم: {1}", installedCount, corruptedCount));
            }
            catch (Exception ex) { LoggerService.Error($"DeepCheck_Click: {ex.Message}", "MODEL"); }
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            base.OnClosing(e);
        }
    }
}
