using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace VoiceToTextPro.Converters
{
    public class SpeakerBrushConverter : IValueConverter
    {
        private static readonly SolidColorBrush[] SpeakerBrushes = new[]
        {
            new SolidColorBrush((Color)ColorConverter.ConvertFromString("#38BDF8")), // Sky Blue (Speaker 1)
            new SolidColorBrush((Color)ColorConverter.ConvertFromString("#34D399")), // Emerald (Speaker 2)
            new SolidColorBrush((Color)ColorConverter.ConvertFromString("#C084FC")), // Purple (Speaker 3)
            new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FBBF24")), // Amber (Speaker 4)
            new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F87171")), // Rose (Speaker 5)
            new SolidColorBrush((Color)ColorConverter.ConvertFromString("#818CF8"))  // Indigo (Default/Other)
        };

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string speakerTag && !string.IsNullOrWhiteSpace(speakerTag))
            {
                int hash = Math.Abs(speakerTag.GetHashCode());
                int index = hash % SpeakerBrushes.Length;
                return SpeakerBrushes[index];
            }
            return SpeakerBrushes[0];
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
