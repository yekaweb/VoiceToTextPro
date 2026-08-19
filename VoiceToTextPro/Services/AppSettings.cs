using System.IO;
using Newtonsoft.Json;

namespace VoiceToTextPro.Services
{
    public class AppSettings
    {
        private static readonly object _lock = new();
        private static AppSettings? _cachedInstance;
        private static readonly string SettingsPath =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");

        public string OutputDirectory { get; set; } =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "VoiceToTextPro", "output");
        public string DefaultLanguage { get; set; } = "en-US";
        public string PreferredEngine { get; set; } = "google";
        public string PythonPath { get; set; } = "";
        public string Theme { get; set; } = "Dark";
        public string DownloadDirectory { get; set; } =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "VoiceToTextPro", "downloads");
        public string ModelsDirectory { get; set; } =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Subtitle Edit", "Vosk");
        public string GeminiApiKey { get; set; } = "";
        public string GeminiModel { get; set; } = "gemini-2.0-flash";

        public static AppSettings Instance
        {
            get
            {
                lock (_lock)
                {
                    _cachedInstance ??= LoadFromDisk();
                    return _cachedInstance;
                }
            }
        }

        public static AppSettings Load() => Instance;

        private static AppSettings LoadFromDisk()
        {
            try
            {
                if (File.Exists(SettingsPath))
                    return JsonConvert.DeserializeObject<AppSettings>(File.ReadAllText(SettingsPath)) ?? new AppSettings();
            }
            catch (Exception ex)
            {
                LoggerService.WarnLocalized("Log_SETTINGS_32d22c", "خطا در بارگذاری تنظیمات: {0} (استفاده از تنظیمات پیش‌فرض)", "SETTINGS", ex.Message);
            }
            return new AppSettings();
        }

        public void Save()
        {
            try
            {
                lock (_lock)
                {
                    _cachedInstance = this;
                    Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
                    File.WriteAllText(SettingsPath, JsonConvert.SerializeObject(this, Formatting.Indented));
                }
            }
            catch (Exception ex)
            {
                LoggerService.ErrorLocalized("Log_SETTINGS_afeb75", "خطا در ذخیره‌سازی تنظیمات: {0}", "SETTINGS", ex.Message);
            }
        }
    }
}
