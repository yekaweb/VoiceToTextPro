using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NAudio.Wave;
using VoiceToTextPro.Models;

namespace VoiceToTextPro.Services
{
    public class SpeakerVoiceAssignment
    {
        public string SpeakerName { get; set; } = "Speaker 1";
        public string TtsModelPath { get; set; } = string.Empty;
        public string V2VProfilePath { get; set; } = string.Empty;
        public double PitchShift { get; set; } = 0;
        public double SpeechRate { get; set; } = 1.0;
    }

    public class ChainPipelineProgressEventArgs : EventArgs
    {
        public int CurrentLine { get; set; }
        public int TotalLines { get; set; }
        public int ProgressPercent { get; set; }
        public string StatusMessage { get; set; } = string.Empty;
    }

    public class ChainPipelineService
    {
        private static readonly Lazy<ChainPipelineService> _instance = new(() => new ChainPipelineService());
        public static ChainPipelineService Instance => _instance.Value;

        public event EventHandler<ChainPipelineProgressEventArgs>? OnProgress;
        public event Action<string>? OnStatusMessage;

        private ChainPipelineService() { }

        /// <summary>
        /// Orchestrates end-to-end subtitle dubbing chain: Subtitle Lines -> TTS -> V2V -> Audio Merging & BGM Ducking.
        /// </summary>
        public async Task<string> ProcessSubtitleDubbingChainAsync(
            List<SubtitleEntry> subtitleItems,
            Dictionary<string, SpeakerVoiceAssignment> speakerAssignments,
            string bgmAudioPath = "",
            double bgmVolumeDucked = 0.15,
            string outputAudioPath = "")
        {
            if (subtitleItems == null || subtitleItems.Count == 0)
            {
                throw new ArgumentException("فهرست زیرنویس‌ها خالی است.");
            }

            if (string.IsNullOrWhiteSpace(outputAudioPath))
            {
                string outputDir = AppSettings.Load().OutputDirectory;
                Directory.CreateDirectory(outputDir);
                outputAudioPath = Path.Combine(outputDir, $"Audiobook_Dubbed_{DateTime.Now:yyyyMMdd_HHmmss}.wav");
            }

            string tempDir = Path.Combine(Path.GetTempPath(), "VoiceToTextPro_ChainPipeline");
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
            Directory.CreateDirectory(tempDir);

            int total = subtitleItems.Count;
            ReportProgress(0, total, 5, "در حال مقداردهی اولیه زنجیره پردازش هوشمند...");
            LoggerService.InfoLocalized("Log_CHAIN_START", "شروع زنجیره دوبله خودکار برای {0} سطر زیرنویس", "CHAIN_PIPELINE", total);

            var lineAudioFiles = new List<(double StartSec, double EndSec, string FilePath)>();

            // Step 1: Synthesize each line using TTS and optional V2V
            for (int i = 0; i < total; i++)
            {
                var item = subtitleItems[i];
                int currentPercent = 10 + (int)((i / (double)total) * 70);
                ReportProgress(i + 1, total, currentPercent, $"در حال تولید صدای سطر {i + 1} از {total}: \"{TruncateText(item.Text, 25)}\"");

                string speakerTag = ExtractSpeakerTag(item.Text, out string cleanText);
                if (string.IsNullOrWhiteSpace(cleanText)) cleanText = item.Text;

                SpeakerVoiceAssignment assignment = GetAssignmentForSpeaker(speakerTag, speakerAssignments);

                string lineTtsWav = Path.Combine(tempDir, $"line_{i + 1:D4}_tts.wav");
                string lineFinalWav = lineTtsWav;

                try
                {
                    // Synthesize line with TTS
                    await TtsService.Instance.SynthesizeSpeechAsync(
                        cleanText,
                        assignment.TtsModelPath,
                        lineTtsWav,
                        speed: (float)assignment.SpeechRate
                    );

                    // Apply V2V voice conversion if profile specified
                    if (!string.IsNullOrEmpty(assignment.V2VProfilePath) && File.Exists(assignment.V2VProfilePath))
                    {
                        string lineV2vWav = Path.Combine(tempDir, $"line_{i + 1:D4}_v2v.wav");
                        await VoiceConverterService.Instance.ConvertVoiceAsync(
                            lineTtsWav,
                            assignment.V2VProfilePath,
                            lineV2vWav,
                            pitchShift: (int)assignment.PitchShift
                        );
                        lineFinalWav = lineV2vWav;
                    }

                    if (File.Exists(lineFinalWav))
                    {
                        lineAudioFiles.Add((item.StartMs / 1000.0, item.EndMs / 1000.0, lineFinalWav));
                    }
                }
                catch (Exception ex)
                {
                    LoggerService.WarnLocalized("Log_CHAIN_LINE_ERR", "خطا در پردازش سطر {0}: {1}", "CHAIN_PIPELINE", i + 1, ex.Message);
                }
            }

            // Step 2: Merge Line Audio Clips onto Master Timeline
            ReportProgress(total, total, 85, "در حال ترکیب قطعات صوتی روی خط زمان اصلی...");
            string masterSpeechWav = Path.Combine(tempDir, "master_speech.wav");
            MergeLineAudioClipsToMaster(lineAudioFiles, masterSpeechWav);

            // Step 3: Mix Background Music (BGM) with Ducking if BGM path exists
            if (!string.IsNullOrEmpty(bgmAudioPath) && File.Exists(bgmAudioPath))
            {
                ReportProgress(total, total, 92, "در حال اعمال افکت BGM Ducking و ترکیب موسیقی پس‌زمینه...");
                MixSpeechWithBgmDucking(masterSpeechWav, bgmAudioPath, outputAudioPath, bgmVolumeDucked);
            }
            else
            {
                File.Copy(masterSpeechWav, outputAudioPath, overwrite: true);
            }

            ReportProgress(total, total, 100, "زنجیره دوبله اتوماتیک با موفقیت به پایان رسید.");
            LoggerService.InfoLocalized("Log_CHAIN_SUCCESS", "فایل صوتی دوبله در {0} ذخیره گردید.", "CHAIN_PIPELINE", outputAudioPath);

            // Clean temp directory
            try { Directory.Delete(tempDir, recursive: true); } catch { }

            return outputAudioPath;
        }

        private void MergeLineAudioClipsToMaster(List<(double StartSec, double EndSec, string FilePath)> lineClips, string masterWavPath)
        {
            const int sampleRate = 22050;
            const int channels = 1;

            if (lineClips.Count == 0) return;

            double maxEndTime = lineClips.Max(c => c.EndSec) + 2.0;
            int totalSamples = (int)(maxEndTime * sampleRate);
            float[] masterBuffer = new float[totalSamples];

            foreach (var clip in lineClips)
            {
                if (!File.Exists(clip.FilePath)) continue;

                using var reader = new AudioFileReader(clip.FilePath);
                var resampler = new MediaFoundationResampler(reader, WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels));
                var floatReader = resampler.ToSampleProvider();

                int startSampleIndex = (int)(clip.StartSec * sampleRate);
                float[] buffer = new float[4096];
                int read;
                int currentPos = startSampleIndex;

                while ((read = floatReader.Read(buffer, 0, buffer.Length)) > 0)
                {
                    for (int n = 0; n < read; n++)
                    {
                        if (currentPos < totalSamples)
                        {
                            masterBuffer[currentPos] += buffer[n];
                            currentPos++;
                        }
                    }
                }
            }

            // Write master buffer to WAV
            using var writer = new WaveFileWriter(masterWavPath, new WaveFormat(sampleRate, 16, channels));
            short[] int16Buffer = new short[totalSamples];
            for (int i = 0; i < totalSamples; i++)
            {
                float clamped = Math.Clamp(masterBuffer[i], -1.0f, 1.0f);
                int16Buffer[i] = (short)(clamped * 32767.0f);
            }
            writer.WriteSamples(int16Buffer, 0, int16Buffer.Length);
        }

        private void MixSpeechWithBgmDucking(string speechWavPath, string bgmAudioPath, string outputWavPath, double duckedVolume)
        {
            const int sampleRate = 22050;
            using var speechReader = new AudioFileReader(speechWavPath);
            using var bgmReader = new AudioFileReader(bgmAudioPath);

            var speechResampled = new MediaFoundationResampler(speechReader, WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 1)).ToSampleProvider();
            var bgmResampled = new MediaFoundationResampler(bgmReader, WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 1)).ToSampleProvider();

            int totalSamples = (int)(speechReader.TotalTime.TotalSeconds * sampleRate);
            float[] outputBuffer = new float[totalSamples];

            float[] speechBuf = new float[4096];
            float[] bgmBuf = new float[4096];

            int pos = 0;
            while (pos < totalSamples)
            {
                int toRead = Math.Min(4096, totalSamples - pos);
                int sRead = speechResampled.Read(speechBuf, 0, toRead);
                int bRead = bgmResampled.Read(bgmBuf, 0, toRead);

                for (int i = 0; i < sRead; i++)
                {
                    float speechVal = speechBuf[i];
                    float bgmVal = (i < bRead) ? bgmBuf[i] : 0.0f;

                    // Ducking logic: attenuate BGM when speech energy is present
                    float speechEnergy = Math.Abs(speechVal);
                    float currentBgmGain = speechEnergy > 0.02f ? (float)duckedVolume : 0.65f;

                    outputBuffer[pos + i] = Math.Clamp(speechVal + (bgmVal * currentBgmGain), -1.0f, 1.0f);
                }
                pos += sRead;
                if (sRead == 0) break;
            }

            using var writer = new WaveFileWriter(outputWavPath, new WaveFormat(sampleRate, 16, 1));
            short[] pcm = new short[pos];
            for (int i = 0; i < pos; i++)
            {
                pcm[i] = (short)(outputBuffer[i] * 32767.0f);
            }
            writer.WriteSamples(pcm, 0, pcm.Length);
        }

        private string ExtractSpeakerTag(string rawText, out string cleanText)
        {
            cleanText = rawText;
            if (rawText.Contains(':'))
            {
                var parts = rawText.Split(':', 2);
                if (parts[0].Trim().Length <= 15)
                {
                    cleanText = parts[1].Trim();
                    return parts[0].Trim();
                }
            }
            return "Speaker 1";
        }

        private SpeakerVoiceAssignment GetAssignmentForSpeaker(string speakerTag, Dictionary<string, SpeakerVoiceAssignment> assignments)
        {
            if (assignments != null && assignments.TryGetValue(speakerTag, out var val))
            {
                return val;
            }
            return new SpeakerVoiceAssignment { SpeakerName = speakerTag };
        }

        private string TruncateText(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            return text.Length <= maxLength ? text : text.Substring(0, maxLength) + "...";
        }

        private void ReportProgress(int currentLine, int totalLines, int percent, string message)
        {
            OnStatusMessage?.Invoke(message);
            OnProgress?.Invoke(this, new ChainPipelineProgressEventArgs
            {
                CurrentLine = currentLine,
                TotalLines = totalLines,
                ProgressPercent = percent,
                StatusMessage = message
            });
        }
    }
}
