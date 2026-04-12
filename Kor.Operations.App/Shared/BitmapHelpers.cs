#nullable enable
using System.IO;
using System.Windows.Media.Imaging;

namespace Kor.Operations.Shared;

public static class BitmapHelpers
{
    public static BitmapSource FromBytes(byte[] bytes, int? decodePixelWidth = null)
    {
        using var stream = new MemoryStream(bytes);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.StreamSource = stream;
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        if (decodePixelWidth.HasValue)
            bitmap.DecodePixelWidth = decodePixelWidth.Value;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }
}
