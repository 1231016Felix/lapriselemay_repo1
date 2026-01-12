using System.Globalization;
using System.Windows.Data;
using TempCleaner.Helpers;

namespace TempCleaner.Converters;

public class FileSizeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is long bytes ? FileSizeHelper.Format(bytes) : "0 B";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
