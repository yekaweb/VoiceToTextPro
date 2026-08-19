using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using VoiceToTextPro.Models;

namespace VoiceToTextPro.Services
{
    public class TtsModelInfo
    {
        public string Name { get; set; } = string.Empty;
        public string ModelPath { get; set; } = string.Empty;
        public string Language { get; set; } = string.Empty;
    }

    public class TtsService
    {
        private static readonly Lazy<TtsService> _instance = new(() => new TtsService());
        public static TtsService Instance => _instance.Value;

        public event Action<string>? OnStatusMessage;

        private TtsService() { }

        /// <summary>
        /// Scans the models directory for available Piper ONNX TTS models.
        /// </summary>
        public List<TtsModelInfo> GetAvailableTtsModels()
        {
            var list = new List<TtsModelInfo>();
            try
            {
                string baseDir = AppSettings.Load().ModelsDirectory;
                string ttsDir = Path.Combine(baseDir, "piper");
                
                if (!Directory.Exists(ttsDir))
                {
                    Directory.CreateDirectory(ttsDir);
                }

                // Scan .onnx files in piper directory
                string[] files = Directory.GetFiles(ttsDir, "*.onnx", SearchOption.AllDirectories);
                foreach (var file in files)
                {
                    string fileName = Path.GetFileNameWithoutExtension(file);
                    list.Add(new TtsModelInfo
                    {
                        Name = fileName,
                        ModelPath = file,
                        Language = fileName.Contains("fa") ? "Persian (فارسی)" : fileName.Contains("en") ? "English" : "Universal"
                    });
                }
            }
            catch (Exception ex)
            {
                LoggerService.ErrorLocalized("Log_TTS_SERVICE_SCAN_ERR", "خطا در اسکن مدل‌های TTS: {0}", "TTS_SERVICE", ex.Message);
            }

            return list;
        }

        /// <summary>
        /// Synthesizes text into a WAV audio file asynchronously.
        /// </summary>
        public async Task<string> SynthesizeSpeechAsync(string text, string modelPath, string outputWavPath, float speed = 1.0f, int speakerId = 0)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                throw new ArgumentException("متن ورودی برای تبدیل به گفتار نمی‌تواند خالی باشد.");
            }

            string pythonExe = GetPythonPath();
            string scriptPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "workers", "tts_worker.py");

            if (!File.Exists(scriptPath))
            {
                scriptPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "workers", "tts_worker.py");
                scriptPath = Path.GetFullPath(scriptPath);
            }

            string tempOutputFile = outputWavPath;
            if (string.IsNullOrWhiteSpace(tempOutputFile))
            {
                string cacheDir = Path.Combine(Path.GetTempPath(), "VoiceToTextPro_TTS");
                Directory.CreateDirectory(cacheDir);
                tempOutputFile = Path.Combine(cacheDir, $"tts_{DateTime.Now:yyyyMMdd_HHmmss}.wav");
            }

            var psi = new ProcessStartInfo
            {
                FileName = pythonExe,
                Arguments = $"\"{scriptPath}\" --text \"{EscapeCommandLineArg(text)}\" --model_path \"{modelPath}\" --output_wav \"{tempOutputFile}\" --speed {speed} --speaker_id {speakerId}",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding = System.Text.Encoding.UTF8
            };

            OnStatusMessage?.Invoke("در حال سنتز گفتار با استفاده از مدل Piper...");
            LoggerService.InfoLocalized("Log_TTS_START", "شروع سنتز گفتار: {0} کلمه‌/کاراکتر با مدل {1}", "TTS_SERVICE", text.Length, Path.GetFileName(modelPath));

            using var process = new Process { StartInfo = psi };
            var tcs = new TaskCompletionSource<bool>();

            process.EnableRaisingEvents = true;
            process.Exited += (s, e) => tcs.TrySetResult(true);

            process.Start();
            string outputJson = await process.StandardOutput.ReadToEndAsync();
            string errorOutput = await process.StandardError.ReadToEndAsync();
            await tcs.Task;

            if (File.Exists(tempOutputFile) && new FileInfo(tempOutputFile).Length > 0)
            {
                OnStatusMessage?.Invoke("سنتز گفتار با موفقیت انجام شد.");
                LoggerService.InfoLocalized("Log_TTS_SUCCESS", "فایل صوتی خروجی در {0} ایجاد شد.", "TTS_SERVICE", tempOutputFile);
                return tempOutputFile;
            }

            throw new InvalidOperationException($"خطا در سنتز صدا: {errorOutput} | {outputJson}");
        }

        private static string EscapeCommandLineArg(string arg)
        {
            return arg.Replace("\"", "\\\"").Replace("\n", " ").Replace("\r", "");
        }

        private string GetPythonPath()
        {
            string localEnv = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "env", "Scripts", "python.exe");
            if (File.Exists(localEnv)) return localEnv;
            return "python.exe";
        }
    }
}
