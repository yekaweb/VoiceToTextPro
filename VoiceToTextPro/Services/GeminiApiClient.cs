using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace VoiceToTextPro.Services
{
    public class GeminiApiClient
    {
        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        private static readonly Lazy<GeminiApiClient> _instance = new(() => new GeminiApiClient());
        public static GeminiApiClient Instance => _instance.Value;

        public bool IsConfigured
        {
            get
            {
                var settings = AppSettings.Load();
                return !string.IsNullOrWhiteSpace(settings.GeminiApiKey);
            }
        }

        public async Task<string> GenerateTextAsync(string prompt, string? customModel = null)
        {
            var settings = AppSettings.Load();
            if (string.IsNullOrWhiteSpace(settings.GeminiApiKey))
            {
                throw new InvalidOperationException("کلید Google Gemini API ثبت نشده است. لطفاً ابتدا در بخش تنظیمات کلید خود را وارد کنید.");
            }

            string model = !string.IsNullOrWhiteSpace(customModel) ? customModel : settings.GeminiModel;
            if (string.IsNullOrWhiteSpace(model)) model = "gemini-2.0-flash";

            string url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={settings.GeminiApiKey}";

            var requestPayload = new
            {
                contents = new[]
                {
                    new
                    {
                        parts = new[]
                        {
                            new { text = prompt }
                        }
                    }
                },
                generationConfig = new
                {
                    temperature = 0.2,
                    topK = 40,
                    topP = 0.95
                }
            };

            string jsonContent = JsonConvert.SerializeObject(requestPayload);
            using var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            LoggerService.Info($"در حال ارسال درخواست به Gemini API (Model: {model})...", "GEMINI_AI");
            var response = await _httpClient.PostAsync(url, content);
            string responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                LoggerService.Error($"خطای Gemini API [{response.StatusCode}]: {responseBody}", "GEMINI_AI");
                throw new HttpRequestException($"خطا از سرور جمینی: {response.StatusCode}\n{ParseErrorMessage(responseBody)}");
            }

            return ParseGeneratedText(responseBody);
        }

        public async Task<string> PolishSubtitleAsync(string rawText, string targetLanguage = "fa")
        {
            string prompt = $@"تو یک ویراستار و متفکر ارشد ادبی زبان هستی.
وظیفه تو ویراستاری، روان‌سازی و علائم‌گذاری دقیق این متن زیرنویس (رونویسی‌شده) به زبان {targetLanguage} است.
قوانین:
۱. غلط‌های املایی، واژگان نادقیق و اشتباهات شنیداری را بدون تغییر در معنی اصلی اصلاح کن.
۲. علائم نگارشی (نقطه، کاما، علامت سوال و تعجب) را با دقت بالا اضافه کن.
۳. متن نهایی باید کاملاً روان، خوانا و بدون هیچ متن توضیحی اضافه باشد (تنها متن اصلاح‌شده را خروجی بده).

متن ورودی:
{rawText}";

            return await GenerateTextAsync(prompt);
        }

        public async Task<string> TranslateSubtitleAsync(string text, string targetLangName)
        {
            string prompt = $@"You are a professional subtitle translator.
Translate the following subtitle text into {targetLangName} accurately, keeping natural idioms and correct context.
Return ONLY the translated text without commentary or preamble.

Source Text:
{text}";

            return await GenerateTextAsync(prompt);
        }

        public async Task<string> GenerateChaptersAsync(string transcriptText)
        {
            string prompt = $@"تو یک کارشناس تولید محتوا و یوتیوب هستی.
بر اساس متن رونویسی‌شده زیر، خلاصه‌سازی و فصل‌بندی زمانی (YouTube Chapters) تولید کن.
فرمت خروجی باید به این شکل باشد:
00:00 - عنوان فصل اول
01:30 - عنوان فصل دوم

متن ویدیوی رونویسی‌شده:
{transcriptText}";

            return await GenerateTextAsync(prompt);
        }

        public async Task<string> TranscribeAudioFileAsync(string audioFilePath)
        {
            var settings = AppSettings.Load();
            if (string.IsNullOrWhiteSpace(settings.GeminiApiKey))
            {
                throw new InvalidOperationException("کلید Google Gemini API ثبت نشده است.");
            }

            if (!File.Exists(audioFilePath))
            {
                throw new FileNotFoundException($"فایل صوتی یافت نشد: {audioFilePath}");
            }

            byte[] audioBytes = await File.ReadAllBytesAsync(audioFilePath);
            string base64Audio = Convert.ToBase64String(audioBytes);

            string extension = Path.GetExtension(audioFilePath).ToLowerInvariant();
            string mimeType = extension switch
            {
                ".mp3" => "audio/mp3",
                ".wav" => "audio/wav",
                ".ogg" => "audio/ogg",
                ".m4a" => "audio/mp4",
                ".flac" => "audio/flac",
                _ => "audio/mp3"
            };

            string model = "gemini-1.5-pro"; // Multimodal audio model
            string url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={settings.GeminiApiKey}";

            var requestPayload = new
            {
                contents = new[]
                {
                    new
                    {
                        parts = new object[]
                        {
                            new
                            {
                                inlineData = new
                                {
                                    mimeType = mimeType,
                                    data = base64Audio
                                }
                            },
                            new
                            {
                                text = "Listen carefully to this audio and write a complete, verbatim transcription in Persian (or the primary spoken language) with perfect punctuation. Do not add intro or outro."
                            }
                        }
                    }
                }
            };

            string jsonContent = JsonConvert.SerializeObject(requestPayload);
            using var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            LoggerService.Info($"در حال ارسال فایل صوتی ({audioBytes.Length / 1024} KB) به Gemini Multimodal Audio API...", "GEMINI_AI");
            var response = await _httpClient.PostAsync(url, content);
            string responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"خطای Gemini Audio API: {response.StatusCode}\n{ParseErrorMessage(responseBody)}");
            }

            return ParseGeneratedText(responseBody);
        }

        private string ParseGeneratedText(string jsonResponse)
        {
            try
            {
                var jo = JObject.Parse(jsonResponse);
                var textToken = jo["candidates"]?[0]?["content"]?["parts"]?[0]?["text"];
                return textToken?.ToString().Trim() ?? "";
            }
            catch (Exception ex)
            {
                LoggerService.Error($"خطا در پارس پاسخ Gemini: {ex.Message}", "GEMINI_AI");
                return jsonResponse;
            }
        }

        private string ParseErrorMessage(string jsonResponse)
        {
            try
            {
                var jo = JObject.Parse(jsonResponse);
                return jo["error"]?["message"]?.ToString() ?? jsonResponse;
            }
            catch
            {
                return jsonResponse;
            }
        }
    }
}
