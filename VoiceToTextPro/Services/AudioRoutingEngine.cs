using System;
using System.IO;
using VoiceToTextPro.Models;

namespace VoiceToTextPro.Services
{
    public enum EngineType
    {
        VoskFastLocal,
        FasterWhisperGpu,
        CloudFallback
    }

    public class AudioRoutingEngine
    {
        private static readonly Lazy<AudioRoutingEngine> s_instance = new(() => new AudioRoutingEngine());
        public static AudioRoutingEngine Instance => s_instance.Value;

        public EngineType DetermineOptimalEngine(string? mediaFilePath, double durationSeconds = 0)
        {
            try
            {
                // 1. Live microphone stream -> Vosk fast local
                if (string.IsNullOrEmpty(mediaFilePath))
                {
                    return EngineType.VoskFastLocal;
                }

                // 2. Short audio clips (< 30 seconds) -> Vosk fast local
                if (durationSeconds > 0 && durationSeconds < 30)
                {
                    return EngineType.VoskFastLocal;
                }

                // 3. Heavy audio/video files (> 30 seconds) -> Faster-Whisper GPU
                if (File.Exists(mediaFilePath))
                {
                    var fileInfo = new FileInfo(mediaFilePath);
                    // Files larger than 5MB or longer than 30s
                    if (fileInfo.Length > 5 * 1024 * 1024 || durationSeconds >= 30)
                    {
                        return EngineType.FasterWhisperGpu;
                    }
                }
            }
            catch (Exception ex)
            {
                LoggerService.ErrorLocalized("Log_ROUTING_ENGINE_0d861f", "خطا در تعیین موتور بهینه: {0}", "ROUTING_ENGINE", ex.Message);
            }

            return EngineType.VoskFastLocal;
        }
    }
}
