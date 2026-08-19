using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VoiceToTextPro.Models;

namespace VoiceToTextPro.Services
{
    public class YouTubeChapterGeneratorService
    {
        private static readonly Lazy<YouTubeChapterGeneratorService> s_instance = new(() => new YouTubeChapterGeneratorService());
        public static YouTubeChapterGeneratorService Instance => s_instance.Value;

        public Task<string> GenerateYouTubeChaptersAsync(IEnumerable<SubtitleEntry> subtitleEntries)
        {
            var itemsList = subtitleEntries.OrderBy(x => x.StartMs).ToList();
            if (itemsList.Count == 0) return Task.FromResult(string.Empty);

            var sb = new StringBuilder();
            sb.AppendLine("📌 فصل‌بندی و زمان‌بندی خودکار ویدیو (YouTube Chapters):\n");

            // Always start at 00:00
            sb.AppendLine($"00:00 {TruncateText(itemsList[0].Text, 40)}");

            // Group into logical time blocks (~2 to 3 minute intervals)
            TimeSpan lastTimestamp = TimeSpan.Zero;
            TimeSpan interval = TimeSpan.FromMinutes(2);

            foreach (var item in itemsList)
            {
                TimeSpan itemStartTime = TimeSpan.FromMilliseconds(item.StartMs);

                if (itemStartTime - lastTimestamp >= interval)
                {
                    string timeFormatted = $"{itemStartTime.Minutes:D2}:{itemStartTime.Seconds:D2}";
                    if (itemStartTime.TotalHours >= 1)
                    {
                        timeFormatted = $"{(int)itemStartTime.TotalHours:D2}:{itemStartTime.Minutes:D2}:{itemStartTime.Seconds:D2}";
                    }

                    sb.AppendLine($"{timeFormatted} {TruncateText(item.Text, 45)}");
                    lastTimestamp = itemStartTime;
                }
            }

            LoggerService.InfoLocalized("Log_CHAPTER_GENERATOR_e6ffe2", "فصل‌بندی خودکار یوتیوب تولید شد.", "CHAPTER_GENERATOR");
            return Task.FromResult(sb.ToString());
        }

        private string TruncateText(string text, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(text)) return "فصل جدید";
            string clean = text.Trim().Replace('\n', ' ');
            return clean.Length <= maxLength ? clean : clean.Substring(0, maxLength) + "...";
        }
    }
}
