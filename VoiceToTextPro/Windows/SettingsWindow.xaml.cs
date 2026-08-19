using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using VoiceToTextPro.Services;

namespace VoiceToTextPro.Windows
{
    public partial class SettingsWindow : Window
    {
        public SettingsWindow()
        {
            InitializeComponent();
            this.FlowDirection = (VoiceToTextPro.Services.LanguageManager.Instance.CurrentCulture == "fa-IR" || VoiceToTextPro.Services.LanguageManager.Instance.CurrentCulture == "ar-SA") ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
            LoadCurrentSettings();
        }

        private void LoadCurrentSettings()
        {
            try
            {
                var settings = AppSettings.Instance;
                OutputDirTxt.Text = settings.OutputDirectory;
                DownloadDirTxt.Text = settings.DownloadDirectory;
                ModelsDirTxt.Text = settings.ModelsDirectory;
                PythonPathTxt.Text = settings.PythonPath;

                // Select Language
                DefaultLangCombo.SelectedIndex = settings.DefaultLanguage switch
                {
                    "fa-IR" => 1,
                    "ar-SA" => 2,
                    "zh-CN" => 3,
                    "ru-RU" => 4,
                    "de-DE" => 5,
                    "tr-TR" => 6,
                    "es-ES" => 7,
                    "Auto" => 8,
                    _ => 0 // Default: en-US
                };

                // Select Preferred Engine
                if (settings.PreferredEngine == "faster-whisper") PreferredEngineCombo.SelectedIndex = 1;
                else if (settings.PreferredEngine == "cloud") PreferredEngineCombo.SelectedIndex = 2;
                else PreferredEngineCombo.SelectedIndex = 0;

                // Select Theme
                if (settings.Theme == "Light") ThemeCombo.SelectedIndex = 1;
                else ThemeCombo.SelectedIndex = 0;

                // Load Gemini Settings
                GeminiApiKeyTxt.Text = settings.GeminiApiKey;
                bool foundModel = false;
                for (int i = 0; i < GeminiModelCombo.Items.Count; i++)
                {
                    if (GeminiModelCombo.Items[i] is ComboBoxItem item && 
                        (item.Content?.ToString()?.Contains(settings.GeminiModel, StringComparison.OrdinalIgnoreCase) == true ||
                         item.Tag?.ToString()?.Equals(settings.GeminiModel, StringComparison.OrdinalIgnoreCase) == true))
                    {
                        GeminiModelCombo.SelectedIndex = i;
                        foundModel = true;
                        break;
                    }
                }
                if (!foundModel && !string.IsNullOrWhiteSpace(settings.GeminiModel))
                {
                    var customItem = new ComboBoxItem { Content = settings.GeminiModel, Tag = settings.GeminiModel, IsSelected = true };
                    GeminiModelCombo.Items.Add(customItem);
                    GeminiModelCombo.SelectedItem = customItem;
                }
            }
            catch (Exception ex)
            {
                LoggerService.ErrorLocalized("Log_SETTINGS_UI_e72221", "خطا در خواندن تنظیمات در پنجره SettingsWindow: {0}", "SETTINGS_UI", ex.Message);
            }
        }

        private void BrowseOutputDir_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog
            {
                Title = "انتخاب پوشه ذخیره‌سازی خروجی‌ها",
                InitialDirectory = OutputDirTxt.Text
            };
            if (dialog.ShowDialog() == true)
            {
                OutputDirTxt.Text = dialog.FolderName;
            }
        }

        private void BrowseDownloadDir_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog
            {
                Title = "انتخاب پوشه ذخیره‌سازی دانلودها",
                InitialDirectory = DownloadDirTxt.Text
            };
            if (dialog.ShowDialog() == true)
            {
                DownloadDirTxt.Text = dialog.FolderName;
            }
        }

        private void BrowseModelsDir_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog
            {
                Title = "انتخاب پوشه مدل‌های AI",
                InitialDirectory = ModelsDirTxt.Text
            };
            if (dialog.ShowDialog() == true)
            {
                ModelsDirTxt.Text = dialog.FolderName;
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var settings = AppSettings.Instance;

                // Ensure directories exist
                if (!string.IsNullOrWhiteSpace(OutputDirTxt.Text))
                {
                    Directory.CreateDirectory(OutputDirTxt.Text);
                    settings.OutputDirectory = OutputDirTxt.Text.Trim();
                }

                if (!string.IsNullOrWhiteSpace(DownloadDirTxt.Text))
                {
                    Directory.CreateDirectory(DownloadDirTxt.Text);
                    settings.DownloadDirectory = DownloadDirTxt.Text.Trim();
                }

                if (!string.IsNullOrWhiteSpace(ModelsDirTxt.Text))
                {
                    Directory.CreateDirectory(ModelsDirTxt.Text);
                    settings.ModelsDirectory = ModelsDirTxt.Text.Trim();
                }

                settings.PythonPath = PythonPathTxt.Text.Trim();

                // Save & Apply Default Language
                string targetLangCode = DefaultLangCombo.SelectedIndex switch
                {
                    1 => "fa-IR",
                    2 => "ar-SA",
                    3 => "zh-CN",
                    4 => "ru-RU",
                    5 => "de-DE",
                    6 => "tr-TR",
                    7 => "es-ES",
                    8 => "Auto",
                    _ => "en-US"
                };

                LanguageManager.Instance.ApplyLanguage(targetLangCode);

                // Save Preferred Engine
                settings.PreferredEngine = PreferredEngineCombo.SelectedIndex switch
                {
                    1 => "faster-whisper",
                    2 => "cloud",
                    _ => "vosk"
                };

                // Save Theme
                settings.Theme = ThemeCombo.SelectedIndex == 1 ? "Light" : "Dark";

                // Save Gemini Settings
                settings.GeminiApiKey = GeminiApiKeyTxt.Text.Trim();
                if (GeminiModelCombo.SelectedItem is ComboBoxItem selectedComboItem)
                {
                    string modelName = selectedComboItem.Tag?.ToString() ?? selectedComboItem.Content?.ToString() ?? "gemini-2.0-flash";
                    // Clean up display names if needed
                    if (modelName.Contains(" (")) modelName = modelName.Substring(0, modelName.IndexOf(" ("));
                    settings.GeminiModel = modelName.Trim();
                }
                else if (GeminiModelCombo.SelectedItem != null)
                {
                    settings.GeminiModel = GeminiModelCombo.SelectedItem.ToString() ?? "gemini-2.0-flash";
                }

                // Save to appsettings.json
                settings.Save();

                LoggerService.InfoLocalized("Log_SettingsSaved", "تنظیمات عمومی جدید با موفقیت ذخیره شدند.", "SETTINGS_UI");
                ModernDialogService.ShowInfo(LanguageManager.Instance.GetString("Msg_SettingsSaved", "تنظیمات عمومی سیستم با موفقیت ذخیره شد."));
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                LoggerService.ErrorLocalized("Log_SETTINGS_afeb75", "خطا در ذخیره‌سازی تنظیمات: {0}", "SETTINGS_UI", ex.Message);
                ModernDialogService.ShowError(LanguageManager.Instance.GetFormattedString("Msg_SettingsSaveError", "خطا در ذخیره‌سازی تنظیمات: {0}", ex.Message));
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void GetGeminiApiKey_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://aistudio.google.com/app/apikey",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                LoggerService.ErrorLocalized("Log_SETTINGS_UI_GeminiLink", "خطا در باز کردن لینک کلید API: {0}", "SETTINGS_UI", ex.Message);
            }
        }

        private async void TestGemini_Click(object sender, RoutedEventArgs e)
        {
            string key = GeminiApiKeyTxt.Text.Trim();
            if (string.IsNullOrWhiteSpace(key))
            {
                ModernDialogService.ShowWarning("لطفاً ابتدا کلید Gemini API Key خود را وارد کنید.", "کلید خالی است");
                return;
            }

            try
            {
                TestGeminiBtn.IsEnabled = false;
                GeminiTestStatusTxt.Visibility = Visibility.Visible;
                GeminiTestStatusTxt.Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FBBF24"));
                GeminiTestStatusTxt.Text = "⏳ در حال برقراری اتصال و استعلام مدل‌های فعال از Google AI Studio...";

                var models = await GeminiApiClient.Instance.FetchAvailableModelsAsync(key);

                GeminiModelCombo.Items.Clear();
                int selectedIndex = 0;
                string currentSavedModel = AppSettings.Instance.GeminiModel;

                for (int i = 0; i < models.Count; i++)
                {
                    string m = models[i];
                    var item = new ComboBoxItem { Content = m, Tag = m };
                    GeminiModelCombo.Items.Add(item);

                    if (m.Equals(currentSavedModel, StringComparison.OrdinalIgnoreCase))
                    {
                        selectedIndex = i;
                    }
                }

                if (GeminiModelCombo.Items.Count > 0)
                {
                    GeminiModelCombo.SelectedIndex = selectedIndex;
                }

                GeminiTestStatusTxt.Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#34D399"));
                GeminiTestStatusTxt.Text = $"✅ اتصال با موفقیت برقرار گردید! {models.Count} مدل فعال دریافت شد.";
                ModernDialogService.ShowInfo($"🎉 اتصال به Google Gemini API برقرار گردید!\n\nتعداد {models.Count} مدل هوش مصنوعی فعال و آماده استفاده در اکانت شما بارگذاری شد.", "تست اتصال موفق");
            }
            catch (Exception ex)
            {
                GeminiTestStatusTxt.Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#F87171"));
                GeminiTestStatusTxt.Text = $"❌ خطا در اتصال: {ex.Message}";
                ModernDialogService.ShowError($"خطا در تست اتصال با کلید API ارائه شده:\n\n{ex.Message}", "خطای اتصال Gemini");
            }
            finally
            {
                TestGeminiBtn.IsEnabled = true;
            }
        }
    }
}
