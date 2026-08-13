using System;

namespace Kiriha.Services.Data.Image;

public sealed class BitmapEncodedCache
{
    private readonly ByteSizedLru<string, byte[]> _encoded;

    public BitmapEncodedCache(long budgetBytes = 32L * 1024 * 1024)
    {
        _encoded = new ByteSizedLru<string, byte[]>(budgetBytes, b => b.Length);
    }

    public bool TryGet(string path, out byte[]? bytes) => _encoded.TryGet(path, out bytes);

    public void Store(string path, byte[] bytes) => _encoded.Set(path, bytes);

    public void Clear() => _encoded.Clear();
}
