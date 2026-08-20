using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using VoiceToTextPro.Models;

namespace VoiceToTextPro.Services
{
    /// <summary>
    /// V2V conversion engine mode selector.
    /// </summary>
    public enum V2VEngineMode
    {
        Auto,
        StudioClone,   // IndexTTS 2 — zero noise cascaded STT→TTS
        DirectNeural,  // RVC v2 — direct neural voice transfer
        Legacy         // Original DSP pitch-shift engine
    }

    public class VoiceProfileInfo
    {
        public string Name { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string Language { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }

    public class VoiceConverterService
    {
        private static readonly Lazy<VoiceConverterService> _instance = new(() => new VoiceConverterService());
        public static VoiceConverterService Instance => _instance.Value;

        public event Action<string>? OnStatusMessage;
        public event Action<int>? OnProgressChanged;

        private VoiceConverterService() { }

        /// <summary>
        /// Scans available target voice profiles in models/voice_profiles.
        /// </summary>
        public List<VoiceProfileInfo> GetAvailableVoiceProfiles()
        {
            var list = new List<VoiceProfileInfo>();
            try
            {
                string baseDir = AppSettings.Load().ModelsDirectory;
                string profilesDir = Path.Combine(baseDir, "voice_profiles");

                if (!Directory.Exists(profilesDir))
                {
                    Directory.CreateDirectory(profilesDir);
                }

                string[] files = Directory.GetFiles(profilesDir, "*.*", SearchOption.TopDirectoryOnly);
                foreach (var file in files)
                {
                    string ext = Path.GetExtension(file).ToLowerInvariant();
                    if (ext == ".pth" || ext == ".npz" || ext == ".onnx" || ext == ".wav" || ext == ".mp3")
                    {
                        string fileName = Path.GetFileNameWithoutExtension(file);
                        list.Add(new VoiceProfileInfo
                        {
                            Name = fileName,
                            FilePath = file,
                            Language = fileName.Contains("fa") ? "Persian (فارسی)" : "Universal",
                            Description = $"پروفایل صوتی هدف ({ext.ToUpper()})"
                        });
                    }
                }

                // Add standard preset profiles if directory is empty
                if (list.Count == 0)
                {
                    list.Add(new VoiceProfileInfo
                    {
                        Name = "گوینده گرم رادیو (پریست استاندارد)",
                        FilePath = "preset_radio_male",
                        Language = "Persian (فارسی)",
                        Description = "صدای عمیق و لحن گرم رادیویی"
                    });
                    list.Add(new VoiceProfileInfo
                    {
                        Name = "گوینده کتاب صوتی (زنانه)",
                        FilePath = "preset_audiobook_female",
                        Language = "Persian (فارسی)",
                        Description = "صدای روان و مناسب راوی داستان"
                    });
                    list.Add(new VoiceProfileInfo
                    {
                        Name = "English Studio Narrator",
                        FilePath = "preset_english_male",
                        Language = "English",
                        Description = "Crisp studio narration voice profile"
                    });
                }
            }
            catch (Exception ex)
            {
                LoggerService.ErrorLocalized("Log_V2V_SCAN_ERR", "خطا در اسکن پروفایل‌های صوتی V2V: {0}", "V2V_SERVICE", ex.Message);
            }

            return list;
        }

        /// <summary>
        /// Converts source audio file to target voice profile asynchronously.
        /// </summary>
        public async Task<string> ConvertVoiceAsync(string sourceWavPath, string targetProfilePath, string outputWavPath, int pitchShift = 0, bool denoise = true, float blendRatio = 1.0f, V2VEngineMode engineMode = V2VEngineMode.Auto)
        {
            if (!File.Exists(sourceWavPath))
            {
                throw new FileNotFoundException($"فایل صوتی منبع یافت نشد: {sourceWavPath}");
            }

            string pythonExe = GetPythonPath();
            string scriptPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "workers", "voice_converter.py");

            if (!File.Exists(scriptPath))
            {
                scriptPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "workers", "voice_converter.py");
                scriptPath = Path.GetFullPath(scriptPath);
            }

            string targetOutputFile = outputWavPath;
            if (string.IsNullOrWhiteSpace(targetOutputFile))
            {
                string cacheDir = Path.Combine(Path.GetTempPath(), "VoiceToTextPro_V2V");
                Directory.CreateDirectory(cacheDir);
                targetOutputFile = Path.Combine(cacheDir, $"v2v_output_{DateTime.Now:yyyyMMdd_HHmmss}.wav");
            }

            // Map engine mode to CLI argument
            string engineArg = engineMode switch
            {
                V2VEngineMode.StudioClone => "indextts",
                V2VEngineMode.DirectNeural => "rvc",
                V2VEngineMode.Legacy => "legacy",
                _ => "auto"
            };

            string denoiseArg = denoise ? "--denoise" : "";
            var psi = new ProcessStartInfo
            {
                FileName = pythonExe,
                Arguments = $"\"{scriptPath}\" --source_wav \"{sourceWavPath}\" --target_profile \"{targetProfilePath}\" --output_wav \"{targetOutputFile}\" --engine {engineArg} --precleaner auto --pitch_shift {pitchShift} {denoiseArg} --blend_ratio {blendRatio}",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding = System.Text.Encoding.UTF8
            };

            OnStatusMessage?.Invoke("در حال راه‌اندازی موتور تبدیل صدا (V2V)...");
            OnProgressChanged?.Invoke(5);
            LoggerService.InfoLocalized("Log_V2V_START", "شروع تبدیل صدا V2V: {0} با گام {1}", "V2V_SERVICE", Path.GetFileName(sourceWavPath), pitchShift);

            using var process = new Process { StartInfo = psi };
            var tcs = new TaskCompletionSource<bool>();

            process.EnableRaisingEvents = true;
            process.Exited += (s, e) => tcs.TrySetResult(true);

            process.OutputDataReceived += (s, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                {
                    try
                    {
                        var json = JObject.Parse(e.Data);
                        string status = json["status"]?.ToString() ?? "";
                        string msg = json["message"]?.ToString() ?? "";
                        int progress = json["progress"]?.Value<int>() ?? 0;

                        if (!string.IsNullOrEmpty(msg)) OnStatusMessage?.Invoke(msg);
                        if (progress > 0) OnProgressChanged?.Invoke(progress);
                    }
                    catch
                    {
                        // Plain text output fallback
                        OnStatusMessage?.Invoke(e.Data);
                    }
                }
            };

            process.Start();
            process.BeginOutputReadLine();
            string errorOutput = await process.StandardError.ReadToEndAsync();
            await tcs.Task;

            if (File.Exists(targetOutputFile) && new FileInfo(targetOutputFile).Length > 0)
            {
                OnStatusMessage?.Invoke("تبدیل صدا با موفقیت انجام شد.");
                OnProgressChanged?.Invoke(100);
                LoggerService.InfoLocalized("Log_V2V_SUCCESS", "فایل صوتی تبدیل‌شده در {0} ذخیره شد.", "V2V_SERVICE", targetOutputFile);
                return targetOutputFile;
            }

            throw new InvalidOperationException($"خطا در تبدیل صدا: {errorOutput}");
        }

        private string GetPythonPath()
        {
            string localEnv = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "env", "Scripts", "python.exe");
            if (File.Exists(localEnv)) return localEnv;
            return "python.exe";
        }
    }
}
