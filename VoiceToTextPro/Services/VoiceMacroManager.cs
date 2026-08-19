using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace VoiceToTextPro.Services
{
    public class VoiceMacroManager
    {
        private static readonly Lazy<VoiceMacroManager> s_instance = new(() => new VoiceMacroManager());
        public static VoiceMacroManager Instance => s_instance.Value;

        private readonly Dictionary<string, Func<Task>> _macroActions = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _macroReplacements = new(StringComparer.OrdinalIgnoreCase);

        public VoiceMacroManager()
        {
            RegisterDefaultMacros();
        }

        private void RegisterDefaultMacros()
        {
            // Punctuation & Symbols
            _macroReplacements["نقطه"] = ".";
            _macroReplacements["علامت سوال"] = "؟";
            _macroReplacements["علامت تعجب"] = "!";
            _macroReplacements["ویرگول"] = "،";
            _macroReplacements["دو نقطه"] = ":";
            _macroReplacements["پرانتز باز"] = "(";
            _macroReplacements["پرانتز بسته"] = ")";

            // Key Actions (Virtual Key Codes)
            // VK_RETURN = 0x0D (Enter)
            // VK_BACK = 0x08 (Backspace)
            // VK_TAB = 0x09 (Tab)
            _macroActions["خط جدید"] = async () => await KeystrokeInjectorService.Instance.InjectKeyAsync(0x0D);
            _macroActions["اینتر"] = async () => await KeystrokeInjectorService.Instance.InjectKeyAsync(0x0D);
            _macroActions["پاراگراف جدید"] = async () =>
            {
                await KeystrokeInjectorService.Instance.InjectKeyAsync(0x0D);
                await KeystrokeInjectorService.Instance.InjectKeyAsync(0x0D);
            };
            _macroActions["پاک کن"] = async () => await KeystrokeInjectorService.Instance.InjectKeyAsync(0x08);
            _macroActions["حذف"] = async () => await KeystrokeInjectorService.Instance.InjectKeyAsync(0x08);
            _macroActions["تب"] = async () => await KeystrokeInjectorService.Instance.InjectKeyAsync(0x09);
        }

        public async Task<bool> ProcessVoiceTextAsync(string rawText)
        {
            if (string.IsNullOrWhiteSpace(rawText)) return false;

            string trimmed = rawText.Trim();

            // 1. Check for Key Macro Actions (e.g. "خط جدید")
            if (_macroActions.TryGetValue(trimmed, out var action))
            {
                LoggerService.InfoLocalized("Log_VOICE_MACRO_1396fa", "اجرای ماکروی کلیدی صوتی: '{0}'", "VOICE_MACRO", trimmed);
                await action();
                return true;
            }

            // 2. Check for Text Symbol Replacements (e.g. "نقطه" -> ".")
            string textToInject = rawText;
            if (_macroReplacements.TryGetValue(trimmed, out var replacement))
            {
                textToInject = replacement;
                LoggerService.InfoLocalized("Log_VOICE_MACRO_7478cb", "جایگزینی نماد صوتی: '{0}' ➔ '{1}'", "VOICE_MACRO", trimmed, replacement);
            }

            await KeystrokeInjectorService.Instance.InjectTextAsync(textToInject + " ");
            return true;
        }
    }
}
