#nullable enable
using System;
using System.Globalization;
using System.IO;
using System.Windows.Data;

namespace Kor.Operations.Brochures
{
    public sealed class FileNameFromPathConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            var path = value as string;
            return string.IsNullOrWhiteSpace(path) ? string.Empty : Path.GetFileName(path);
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => Binding.DoNothing;
    }
}

