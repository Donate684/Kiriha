using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Kiriha.Services.Data.Image;

public sealed class BitmapPixelCache
{
    private readonly ByteSizedLru<PixelKey, PixelEntry> _pixels;

    public BitmapPixelCache(long budgetBytes = 16L * 1024 * 1024)
    {
        _pixels = new ByteSizedLru<PixelKey, PixelEntry>(budgetBytes, p => p.Pixels.Length);
    }

    public bool TryRentBitmap(string path, int decodeWidth, out Bitmap? bitmap)
    {
        bitmap = null;
        if (!_pixels.TryGet(new PixelKey(path, decodeWidth), out var entry) || entry == null)
            return false;

        GCHandle handle = default;
        try
        {
            var wb = new WriteableBitmap(entry.Size, new Vector(96, 96), entry.Format, entry.AlphaFormat);
            using (var fb = wb.Lock())
            {
                Marshal.Copy(entry.Pixels, 0, fb.Address, entry.Pixels.Length);
            }
            bitmap = wb;
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (handle.IsAllocated) handle.Free();
        }
    }

    public void StorePixelsFrom(string path, int decodeWidth, Bitmap bmp)
    {
        if (bmp.Format is not { } fmt) return;
        var alpha = bmp.AlphaFormat ?? AlphaFormat.Premul;

        var size = bmp.PixelSize;
        if (size.Width <= 0 || size.Height <= 0) return;

        int bpp = (fmt.BitsPerPixel + 7) / 8;
        int stride = size.Width * bpp;
        int total = stride * size.Height;
        if (total <= 0) return;

        var buffer = new byte[total];
        var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try
        {
            bmp.CopyPixels(new PixelRect(0, 0, size.Width, size.Height),
                           handle.AddrOfPinnedObject(), total, stride);
        }
        catch
        {
            return;
        }
        finally
        {
            handle.Free();
        }

        _pixels.Set(new PixelKey(path, decodeWidth),
                    new PixelEntry(buffer, size, stride, fmt, alpha));
    }

    public void Clear() => _pixels.Clear();

    private readonly record struct PixelKey(string Path, int DecodeWidth);

    private sealed class PixelEntry
    {
        public PixelEntry(byte[] pixels, PixelSize size, int stride, PixelFormat fmt, AlphaFormat alpha)
        {
            Pixels = pixels; Size = size; Stride = stride; Format = fmt; AlphaFormat = alpha;
        }
        public byte[] Pixels { get; }
        public PixelSize Size { get; }
        public int Stride { get; }
        public PixelFormat Format { get; }
        public AlphaFormat AlphaFormat { get; }
    }
}
