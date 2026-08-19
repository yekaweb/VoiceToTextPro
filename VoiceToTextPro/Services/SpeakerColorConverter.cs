using System;
using System.Collections.Generic;
using System.Windows.Media;

namespace VoiceToTextPro.Services
{
    public class SpeakerColorConverter
    {
        private static readonly Dictionary<string, SolidColorBrush> s_speakerBrushes = new(StringComparer.OrdinalIgnoreCase);

        private static readonly string[] s_paletteHex = new[]
        {
            "#3B82F6", // Blue
            "#10B981", // Emerald
            "#F59E0B", // Amber
            "#EC4899", // Pink
            "#8B5CF6", // Purple
            "#06B6D4"  // Cyan
        };

        public static SolidColorBrush GetBrushForSpeaker(string speakerId)
        {
            if (string.IsNullOrWhiteSpace(speakerId)) speakerId = "Speaker 1";

            if (!s_speakerBrushes.TryGetValue(speakerId, out var brush))
            {
                int index = Math.Abs(speakerId.GetHashCode()) % s_paletteHex.Length;
                Color color = (Color)ColorConverter.ConvertFromString(s_paletteHex[index]);
                brush = new SolidColorBrush(color);
                brush.Freeze();
                s_speakerBrushes[speakerId] = brush;
            }

            return brush;
        }
    }
}
