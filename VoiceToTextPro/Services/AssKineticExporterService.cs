using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using VoiceToTextPro.Models;

namespace VoiceToTextPro.Services
{
    public enum KineticStylePreset
    {
        CapCutYellow,
        NeonGlow,
        MinimalDark
    }

    public class AssKineticExporterService
    {
        private static readonly Lazy<AssKineticExporterService> s_instance = new(() => new AssKineticExporterService());
        public static AssKineticExporterService Instance => s_instance.Value;

        public async Task<string> ExportKineticAssAsync(IEnumerable<SubtitleEntry> subtitleEntries, string outputPath, KineticStylePreset preset = KineticStylePreset.CapCutYellow)
        {
            var sb = new StringBuilder();

            // 1. Write ASS Headers & Style Definitions
            sb.AppendLine("[Script Info]");
            sb.AppendLine("Title: VoiceToText Pro Kinetic Subtitles");
            sb.AppendLine("ScriptType: v4.00+");
            sb.AppendLine("WrapStyle: 0");
            sb.AppendLine("PlayResX: 1920");
            sb.AppendLine("PlayResY: 1080");
            sb.AppendLine("ScaledBorderAndShadow: yes");
            sb.AppendLine();

            sb.AppendLine("[V4+ Styles]");
            sb.AppendLine("Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding");
            
            switch (preset)
            {
                case KineticStylePreset.NeonGlow:
                    sb.AppendLine("Style: Default,Outfit,48,&H00FFD700,&H0000FFFF,&H00FF007F,&H80000000,-1,0,0,0,100,100,0,0,1,3,4,2,20,20,50,1");
                    break;
                case KineticStylePreset.MinimalDark:
                    sb.AppendLine("Style: Default,Inter,42,&H00FFFFFF,&H00CCCCCC,&H00000000,&H80000000,-1,0,0,0,100,100,0,0,1,2,2,2,20,20,40,1");
                    break;
                case KineticStylePreset.CapCutYellow:
                default:
                    sb.AppendLine("Style: Default,Montserrat,46,&H0000FFFF,&H00FFFFFF,&H00000000,&H90000000,-1,0,0,0,100,100,0,0,1,3,2,2,20,20,45,1");
                    break;
            }
            sb.AppendLine();

            sb.AppendLine("[Events]");
            sb.AppendLine("Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text");

            // 2. Generate Karaoke Dialogue Events
            foreach (var item in subtitleEntries)
            {
                if (string.IsNullOrWhiteSpace(item.Text)) continue;

                TimeSpan startTs = TimeSpan.FromMilliseconds(item.StartMs);
                TimeSpan endTs = TimeSpan.FromMilliseconds(item.EndMs);

                string startAssTime = FormatAssTime(startTs);
                string endAssTime = FormatAssTime(endTs);

                string[] words = item.Text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (words.Length == 0) continue;

                double totalDurationCs = (item.EndMs - item.StartMs) / 10.0;
                int durationPerWordCs = Math.Max(10, (int)(totalDurationCs / words.Length));

                var lineSb = new StringBuilder();
                foreach (string word in words)
                {
                    lineSb.Append(@$"{{\k{durationPerWordCs}}}{word} ");
                }

                sb.AppendLine($"Dialogue: 0,{startAssTime},{endAssTime},Default,,0,0,0,,{lineSb.ToString().TrimEnd()}");
            }

            await File.WriteAllTextAsync(outputPath, sb.ToString(), Encoding.UTF8);
            LoggerService.InfoLocalized("Log_KINETIC_EXPORTER_bf7708", "خروجی زیرنویس متحرک ASS با موفقیت ذخیره شد: {0}", "KINETIC_EXPORTER", outputPath);

            return outputPath;
        }

        private string FormatAssTime(TimeSpan ts)
        {
            // Format: H:MM:SS.cs (e.g. 0:01:23.45)
            return $"{ts.Hours}:{ts.Minutes:D2}:{ts.Seconds:D2}.{ts.Milliseconds / 10:D2}";
        }
    }
}
