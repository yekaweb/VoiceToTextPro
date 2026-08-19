using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace VoiceToTextPro.Converters
{
    public class StringToBrushConverter : IValueConverter
    {
        private static readonly BrushConverter _converter = new();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string colorHex && !string.IsNullOrWhiteSpace(colorHex))
            {
                try
                {
                    var brush = _converter.ConvertFromString(colorHex) as Brush;
                    if (brush != null) return brush;
                }
                catch
                {
                    // Fallback to white/default brush
                }
            }
            return Brushes.Gray;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
