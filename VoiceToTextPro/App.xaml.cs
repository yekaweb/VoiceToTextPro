using System;
using System.Windows;
using System.Windows.Threading;
using MaterialDesignThemes.Wpf;
using VoiceToTextPro.Services;
using VoiceToTextPro.ViewModels;

namespace VoiceToTextPro
{
    public partial class App : Application
    {
        public static AppSettings Settings => AppSettings.Instance;
        public static ModelDownloadManager Downloader => ModelDownloadManager.Instance;

        protected override void OnStartup(StartupEventArgs e)
        {
            ShutdownMode = ShutdownMode.OnMainWindowClose;

            // ═══ Global Exception Handlers — prevent silent crashes ═══
            DispatcherUnhandledException += App_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

            base.OnStartup(e);

            var settings = AppSettings.Instance;
            var helper = new PaletteHelper();
            var theme = helper.GetTheme();
            theme.SetBaseTheme(settings.Theme == "Light" ? BaseTheme.Light : BaseTheme.Dark);
            helper.SetTheme(theme);

            LanguageManager.Instance.ApplyLanguage(settings.DefaultLanguage);

            UXTelemetryService.Start();
        }

        private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            try
            {
                LoggerService.Error($"[UNHANDLED UI ERROR] {e.Exception.GetType().Name}: {e.Exception.Message}\n{e.Exception.StackTrace}", "APP_EXCEPTION");
                string title = VoiceToTextPro.Services.LanguageManager.Instance.GetString("Msg_UnexpectedErrorTitle", "خطای غیرمنتظره");
                string msg = VoiceToTextPro.Services.LanguageManager.Instance.GetFormattedString("Msg_UnexpectedError", "خطایی رخ داد اما برنامه ادامه می‌دهد:\n\n{0}", e.Exception.Message);
                ModernDialogService.ShowWarning(msg, title);
            }
            catch
            {
                string title = VoiceToTextPro.Services.LanguageManager.Instance.GetString("Msg_UnexpectedErrorTitle", "خطای غیرمنتظره");
                string msg = VoiceToTextPro.Services.LanguageManager.Instance.GetFormattedString("Msg_UnexpectedError", "خطایی رخ داد اما برنامه ادامه می‌دهد:\n\n{0}", e.Exception.Message);
                MessageBox.Show(msg, title, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                // Mark as handled so the app does NOT crash
                e.Handled = true;
            }
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            try
            {
                var ex = e.ExceptionObject as Exception;
                LoggerService.Error($"[FATAL] {ex?.GetType().Name}: {ex?.Message}\n{ex?.StackTrace}", "APP_EXCEPTION");
                string title = VoiceToTextPro.Services.LanguageManager.Instance.GetString("Msg_CriticalErrorTitle", "خطای بحرانی");
                string msg = VoiceToTextPro.Services.LanguageManager.Instance.GetFormattedString("Msg_CriticalError", "خطای بحرانی:\n\n{0}", ex?.Message ?? "");
                ModernDialogService.ShowError(msg, title);
            }
            catch
            {
                var ex = e.ExceptionObject as Exception;
                string title = VoiceToTextPro.Services.LanguageManager.Instance.GetString("Msg_CriticalErrorTitle", "خطای بحرانی");
                string msg = VoiceToTextPro.Services.LanguageManager.Instance.GetFormattedString("Msg_CriticalError", "خطای بحرانی:\n\n{0}", ex?.Message ?? "");
                MessageBox.Show(msg, title, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            UXTelemetryService.Stop();
            base.OnExit(e);
        }
    }
}
