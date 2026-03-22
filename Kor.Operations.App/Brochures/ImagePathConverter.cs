#nullable enable
using System;
using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using Kor.Operations.Core.Models.Brochure;
using Kor.Operations.Brochures.SubVms;

namespace Kor.Operations.Brochures
{
    public class ImagePathConverter : IValueConverter
    {
        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                return value switch
                {
                    byte[] bytes when bytes.Length > 0 => CreateBitmap(bytes),
                    string path when !string.IsNullOrWhiteSpace(path) => CreateBitmap(path),
                    BrochurePhoto photo when photo.ImageBytes.Length > 0 => CreateBitmap(photo.ImageBytes),
                    BrochurePhoto photo when !string.IsNullOrWhiteSpace(photo.FilePath) => CreateBitmap(photo.FilePath),
                    BrochurePerson person when person.PhotoBytes.Length > 0 => CreateBitmap(person.PhotoBytes),
                    BrochurePerson person when !string.IsNullOrWhiteSpace(person.PhotoPath) => CreateBitmap(person.PhotoPath),
                    BrochureCoverVm cover when cover.CoverPhotoBytes.Length > 0 => CreateBitmap(cover.CoverPhotoBytes),
                    BrochureCoverVm cover when !string.IsNullOrWhiteSpace(cover.CoverPhotoPath) => CreateBitmap(cover.CoverPhotoPath),
                    _ => null
                };
            }
            catch
            {
                return null;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;

        private static BitmapImage? CreateBitmap(string path)
        {
            var resolvedPath = Path.IsPathRooted(path)
                ? path
                : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, path);

            if (!File.Exists(resolvedPath))
                return null;

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(resolvedPath);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = 180;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }

        private static BitmapImage? CreateBitmap(byte[] bytes)
        {
            using var stream = new MemoryStream(bytes);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.StreamSource = stream;
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = 180;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
    }
}

