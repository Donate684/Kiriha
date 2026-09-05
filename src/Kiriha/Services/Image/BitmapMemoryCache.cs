using Kiriha.Services.Data.Image;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Kiriha.Services.Data.Image;

/// <summary>
/// Two-tier in-memory cache layered on top of the disk image cache used by
/// <see cref="ImageCacheService"/>.
///
///   L1 (decoded bitmap cache, ~64 MB)
///     Decoded Bitmap instances keyed by (path, decodeWidth).
///     Hit  => instant return of cached Bitmap reference without allocations.
///     Cost ~0 ms.
///
///   L2 (encoded bytes cache, ~32 MB)
///     Raw on-disk file bytes keyed by path.
///     Hit  => decode from <see cref="System.IO.MemoryStream"/> into a fresh
///             <see cref="Bitmap"/>, then promote to L1.
///     Cost ~3-10 ms (decode). No disk I/O.
/// </summary>
public sealed class BitmapMemoryCache
{
    private readonly BitmapEncodedCache _encoded;
    private readonly BitmapPixelCache _pixels;

    public BitmapMemoryCache(long encodedBudgetBytes = 32L * 1024 * 1024,
                              long pixelBudgetBytes = 64L * 1024 * 1024)
    {
        _encoded = new BitmapEncodedCache(encodedBudgetBytes);
        _pixels = new BitmapPixelCache(pixelBudgetBytes);
    }

    /// <summary>
    /// L1 hit path: returns cached Bitmap.
    /// Returns false on miss.
    /// </summary>
    public bool TryRentBitmap(string path, int decodeWidth, out Bitmap? bitmap)
        => _pixels.TryRentBitmap(path, decodeWidth, out bitmap);

    public bool TryGetEncoded(string path, out byte[]? bytes)
        => _encoded.TryGet(path, out bytes);

    public void StoreEncoded(string path, byte[] bytes)
        => _encoded.Store(path, bytes);

    public void Clear()
    {
        _encoded.Clear();
        _pixels.Clear();
    }

    /// <summary>
    /// Extracts pixels from a freshly-decoded Bitmap and promotes them to L1.
    /// Best-effort: silently no-ops if the source format is unknown or pixel
    /// extraction fails — the encoded-bytes layer still gives most of the win.
    /// </summary>
    public void StorePixelsFrom(string path, int decodeWidth, Bitmap bmp)
        => _pixels.StorePixelsFrom(path, decodeWidth, bmp);
}
