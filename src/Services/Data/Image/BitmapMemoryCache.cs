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
///   L1 (pixel cache, ~16 MB)
///     Decoded BGRA pixel buffers keyed by (path, decodeWidth).
///     Hit  => allocate a fresh <see cref="WriteableBitmap"/> and copy pixels in.
///     Cost ~1 ms (memcpy + GPU upload). No JPEG decode, no disk I/O.
///
///   L2 (encoded bytes cache, ~32 MB)
///     Raw on-disk file bytes keyed by path.
///     Hit  => decode from <see cref="System.IO.MemoryStream"/> into a fresh
///             <see cref="Bitmap"/>, then promote its pixels to L1.
///     Cost ~3-10 ms (decode). No disk I/O.
///
/// Every call returns an INDEPENDENT bitmap instance. This is intentional:
/// AsyncImageLoader's AdvancedImage disposes the "previous" Source on rebind
/// (recycling in ItemsRepeater), so any shared instance would die on the
/// neighbour cards and render blank. See the long-form note in
/// <see cref="ImageCacheService.LoadBitmapAsync"/>.
/// </summary>
public sealed class BitmapMemoryCache
{
    private readonly BitmapEncodedCache _encoded;
    private readonly BitmapPixelCache _pixels;

    public BitmapMemoryCache(long encodedBudgetBytes = 32L * 1024 * 1024,
                              long pixelBudgetBytes = 16L * 1024 * 1024)
    {
        _encoded = new BitmapEncodedCache(encodedBudgetBytes);
        _pixels = new BitmapPixelCache(pixelBudgetBytes);
    }

    /// <summary>
    /// L1 hit path: rent an independent WriteableBitmap built from cached pixels.
    /// Returns false on miss or on any failure during materialization (caller
    /// should then fall through to the encoded/disk path).
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
