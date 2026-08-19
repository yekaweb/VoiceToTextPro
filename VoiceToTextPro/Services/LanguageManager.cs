using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Windows;

namespace VoiceToTextPro.Services
{
    public class LanguageManager
    {
        private static readonly object _lock = new();
        private static LanguageManager? _instance;

        public static LanguageManager Instance
        {
            get
            {
                lock (_lock)
                {
                    return _instance ??= new LanguageManager();
                }
            }
        }

        public string CurrentCulture { get; private set; } = "en-US";

        public event EventHandler<string>? LanguageChanged;

        private LanguageManager()
        {
        }

        public void ApplyLanguage(string cultureCode)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(cultureCode) || cultureCode.Equals("Auto", StringComparison.OrdinalIgnoreCase))
                {
                    cultureCode = CultureInfo.CurrentUICulture.Name;
                }

                // Supported cultures fallback map
                string targetCulture = cultureCode switch
                {
                    var c when c.StartsWith("fa", StringComparison.OrdinalIgnoreCase) => "fa-IR",
                    var c when c.StartsWith("ar", StringComparison.OrdinalIgnoreCase) => "ar-SA",
                    var c when c.StartsWith("zh", StringComparison.OrdinalIgnoreCase) => "zh-CN",
                    var c when c.StartsWith("ru", StringComparison.OrdinalIgnoreCase) => "ru-RU",
                    var c when c.StartsWith("de", StringComparison.OrdinalIgnoreCase) => "de-DE",
                    var c when c.StartsWith("tr", StringComparison.OrdinalIgnoreCase) => "tr-TR",
                    var c when c.StartsWith("es", StringComparison.OrdinalIgnoreCase) => "es-ES",
                    _ => "en-US"
                };

                // Load ResourceDictionary
                var dictUri = new Uri($"/Resources/Languages/Strings.{targetCulture}.xaml", UriKind.Relative);
                var newDict = new ResourceDictionary { Source = dictUri };

                // Find existing language dictionary if present
                var oldDict = Application.Current.Resources.MergedDictionaries
                    .FirstOrDefault(d => d.Source != null && d.Source.OriginalString.Contains("/Resources/Languages/Strings."));

                if (oldDict != null)
                {
                    int index = Application.Current.Resources.MergedDictionaries.IndexOf(oldDict);
                    Application.Current.Resources.MergedDictionaries[index] = newDict;
                }
                else
                {
                    Application.Current.Resources.MergedDictionaries.Add(newDict);
                }

                // Set CultureInfo
                var cultureInfo = new CultureInfo(targetCulture);
                Thread.CurrentThread.CurrentCulture = cultureInfo;
                Thread.CurrentThread.CurrentUICulture = cultureInfo;

                // Adjust FlowDirection dynamically across open windows
                FlowDirection flowDir = (targetCulture == "fa-IR" || targetCulture == "ar-SA") ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
                foreach (Window win in Application.Current.Windows)
                {
                    win.FlowDirection = flowDir;
                }

                CurrentCulture = targetCulture;
                AppSettings.Instance.DefaultLanguage = targetCulture;
                AppSettings.Instance.Save();

                LoggerService.InfoLocalized("Log_LangChanged", "زبان برنامه به {0} تغییر یافت. (FlowDirection: {1})", "i18n", targetCulture, flowDir);
                LanguageChanged?.Invoke(this, targetCulture);
            }
            catch (Exception ex)
            {
                LoggerService.ErrorLocalized("Log_I18N_04acf9", "خطا در اعمال زبان {0}: {1}", "i18n", cultureCode, ex.Message);
            }
        }

        public string GetString(string key, string fallback = "")
        {
            try
            {
                if (Application.Current != null && Application.Current.Resources.Contains(key))
                {
                    return Application.Current.Resources[key]?.ToString() ?? fallback;
                }
            }
            catch { }
            return fallback;
        }

        public string GetFormattedString(string key, string fallbackFormat, params object[] args)
        {
            string fmt = GetString(key, fallbackFormat);
            try
            {
                return string.Format(fmt, args);
            }
            catch
            {
                return fmt;
            }
        }
    }
}
