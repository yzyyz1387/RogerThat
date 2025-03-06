using System;
using System.Windows.Data;
using MaterialDesignThemes.Wpf;

namespace RogerThat.Converters
{
    public class BoolToIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value is bool isPlaying && parameter is string icons)
            {
                var iconNames = icons.Split('|');
                return (PackIconKind)Enum.Parse(typeof(PackIconKind), isPlaying ? iconNames[1] : iconNames[0]);
            }
            return PackIconKind.Play;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
} 