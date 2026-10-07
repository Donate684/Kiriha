using Avalonia.Media.Imaging;

namespace Kiriha.Services.Data.Image;

public interface IImageCacheService
{
    Task<Bitmap?> LoadBitmapAsync(string url, int decodeWidth = 300, CancellationToken ct = default);

    Task PerformSmartCleanupAsync(IEnumerable<string> activePaths);

    void ClearMemoryCache();

    Task<string> GetLocalPathOrDownload(string url, CancellationToken ct = default);
}
