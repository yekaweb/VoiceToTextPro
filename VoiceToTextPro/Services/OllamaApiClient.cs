using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace VoiceToTextPro.Services
{
    public interface IOllamaApiClient
    {
        Task<bool> IsOllamaAvailableAsync();
        Task<string> FixGrammarAsync(string transcript, string modelName = "llama3");
        Task<string> SummarizeTranscriptAsync(string transcript, string modelName = "llama3");
        Task<string> TranslateTranscriptAsync(string transcript, string targetLang = "Persian", string modelName = "llama3");
    }

    public class OllamaApiClient : IOllamaApiClient
    {
        private static readonly Lazy<OllamaApiClient> s_instance = new(() => new OllamaApiClient());
        public static OllamaApiClient Instance => s_instance.Value;

        private readonly HttpClient _httpClient;
        private const string DefaultOllamaUrl = "http://localhost:11434";

        public OllamaApiClient()
        {
            _httpClient = new HttpClient
            {
                BaseAddress = new Uri(DefaultOllamaUrl),
                Timeout = TimeSpan.FromSeconds(60)
            };
        }

        public async Task<bool> IsOllamaAvailableAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync("/api/tags");
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        public async Task<string> FixGrammarAsync(string transcript, string modelName = "llama3")
        {
            if (string.IsNullOrWhiteSpace(transcript)) return transcript;

            string prompt = $"تو یک ویراستار زبان فارسی و انگلیسی هستی. متن پیاده‌شده از صدای زیر را اصلاح نگارشی، علائم‌گذاری (نقطه، ویرگول، علامت سوال) و روان‌سازی کن. پرش‌های کلامی (مثل اممم، آاا) را حذف کن و فقط متن بازنویسی‌شده نهایی را برگردان بدون توضیحات اضافی:\n\n\"{transcript}\"";

            return await GenerateResponseAsync(prompt, modelName);
        }

        public async Task<string> SummarizeTranscriptAsync(string transcript, string modelName = "llama3")
        {
            if (string.IsNullOrWhiteSpace(transcript)) return string.Empty;

            string prompt = $"لطفاً متن پیاده‌سازی شده زیر را آنالیز کرده و آن را به صورت خلاصه شامل ۵ نقطه کلیدی و موضوعات اصلی در قالب بالت‌پوینت‌های مرتب فارسی خلاصه‌سازی کن:\n\n\"{transcript}\"";

            return await GenerateResponseAsync(prompt, modelName);
        }

        public async Task<string> TranslateTranscriptAsync(string transcript, string targetLang = "Persian", string modelName = "llama3")
        {
            if (string.IsNullOrWhiteSpace(transcript)) return string.Empty;

            string prompt = $"Translate the following audio transcript into fluent {targetLang}. Provide ONLY the final translated text without any explanation:\n\n\"{transcript}\"";

            return await GenerateResponseAsync(prompt, modelName);
        }

        private async Task<string> GenerateResponseAsync(string prompt, string modelName)
        {
            try
            {
                var requestBody = new
                {
                    model = modelName,
                    prompt = prompt,
                    stream = false
                };

                string json = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                LoggerService.InfoLocalized("Log_OLLAMA_671d3f", "فراخوانی هوش مصنوعی محلی Ollama ({0})...", "OLLAMA", modelName);
                var response = await _httpClient.PostAsync("/api/generate", content);

                if (!response.IsSuccessStatusCode)
                {
                    LoggerService.WarnLocalized("Log_OLLAMA_a4d88f", "پاسخ ناپایدار از سرویس Ollama: Status {0}", "OLLAMA", response.StatusCode);
                    return prompt;
                }

                string jsonResult = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(jsonResult);
                if (doc.RootElement.TryGetProperty("response", out var respProp))
                {
                    string result = respProp.GetString()?.Trim() ?? string.Empty;
                    LoggerService.InfoLocalized("Log_OLLAMA_4ac4d1", "پردازش متن با هوش محلی Ollama با موفقیت انجام شد.", "OLLAMA");
                    return result;
                }
            }
            catch (Exception ex)
            {
                LoggerService.ErrorLocalized("Log_OLLAMA_06e681", "خطا در ارتباط با سرویس محلی Ollama: {0}", "OLLAMA", ex.Message);
            }

            return prompt;
        }
    }
}
