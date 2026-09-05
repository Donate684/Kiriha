using System;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Kiriha.Services.Data.Image;

public sealed class BitmapPixelCache
{
    private readonly ByteSizedLru<PixelKey, Bitmap> _bitmaps;

    public BitmapPixelCache(long budgetBytes = 64L * 1024 * 1024)
    {
        _bitmaps = new ByteSizedLru<PixelKey, Bitmap>(budgetBytes, bmp =>
        {
            var size = bmp.PixelSize;
            return (long)Math.Max(size.Width, 1) * Math.Max(size.Height, 1) * 4;
        });
    }

    public bool TryRentBitmap(string path, int decodeWidth, out Bitmap? bitmap)
    {
        return _bitmaps.TryGet(new PixelKey(path, decodeWidth), out bitmap);
    }

    public void StorePixelsFrom(string path, int decodeWidth, Bitmap bmp)
    {
        _bitmaps.Set(new PixelKey(path, decodeWidth), bmp);
    }

    public void Clear() => _bitmaps.Clear();

    private readonly record struct PixelKey(string Path, int DecodeWidth);
}
