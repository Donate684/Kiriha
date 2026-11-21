using System.Net;
using System.Security.Cryptography;
using System.Text;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Utils.Async;
using Kiriha.Core.Streaming.Constants;
using Serilog;

namespace Kiriha.Services.Image;

public class ImageDownloader : IDisposable
{
    private readonly string _cacheRoot;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IImageUrlRewriter? _urlRewriter;
    private readonly SemaphoreSlim _downloadSemaphore = new(6, 6);
    private readonly SharedAsyncWork<string, string> _activeDownloads = new();

    public ImageDownloader(IHttpClientFactory httpClientFactory, string cacheRoot, IImageUrlRewriter? urlRewriter = null)
    {
        _httpClientFactory = httpClientFactory;
        _cacheRoot = cacheRoot;
        _urlRewriter = urlRewriter;
    }

    public Task<string> GetLocalPathOrDownload(string url, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(url)) return Task.FromResult(string.Empty);

        // Rewrite the URL before everything else so that the cache key, the
        // dedup dictionary, and the actual HTTP request all use the same host.
        var resolvedUrl = _urlRewriter?.Rewrite(url) ?? url;
        return _activeDownloads.RunAsync(resolvedUrl, () => DownloadAsync(resolvedUrl), ct);
    }

    private async Task<string> DownloadAsync(string url)
    {
        try
        {
            return await DownloadCoreAsync(url);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Image download failed for {Url}", url);
            return string.Empty;
        }
    }

    private async Task<string> DownloadCoreAsync(string url, CancellationToken ct = default)
    {
        string fileName = GetFileNameForUrl(url);

        string localPath = Path.Combine(_cacheRoot, fileName);
        string tmpPath = localPath + ".tmp";

        if (File.Exists(localPath))
        {
            var fileInfo = new FileInfo(localPath);
            if (fileInfo.Length > 0) return localPath;
            try { fileInfo.Delete(); } catch (Exception ex) { Log.Debug(ex, "Failed to delete old file {FilePath}", localPath); }
        }

        int retryCount = 2;
        for (int i = 0; i <= retryCount; i++)
        {
            try
            {
                await _downloadSemaphore.WaitAsync(ct);
                try
                {
                    if (File.Exists(localPath))
                    {
                        var fileInfo = new FileInfo(localPath);
                        if (fileInfo.Length > 0) return localPath;
                        try { fileInfo.Delete(); } catch (Exception ex) { Log.Debug(ex, "Failed to delete file {FilePath} on collision", localPath); }
                    }

                    if (File.Exists(tmpPath))
                    {
                        try { File.Delete(tmpPath); } catch (Exception ex) { Log.Debug(ex, "Failed to delete temp file {TmpPath} after copy error", tmpPath); }
                    }

                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    cts.CancelAfter(TimeSpan.FromSeconds(30));
                    try
                    {
                        using var client = _httpClientFactory.CreateClient("ImageClient");
                        using var request = CreateImageRequest(url);
                        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseContentRead, cts.Token);
                        if ((int)response.StatusCode is >= 400 and < 500 &&
                            response.StatusCode is not (HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests))
                            return string.Empty;
                        response.EnsureSuccessStatusCode();
                        var bytes = await response.Content.ReadAsByteArrayAsync(cts.Token);
                        if (bytes.Length == 0) return string.Empty;
                        await File.WriteAllBytesAsync(tmpPath, bytes, cts.Token);

                        File.Move(tmpPath, localPath, true);
                        return localPath;
                    }
                    catch
                    {
                        if (File.Exists(tmpPath))
                        {
                            try { File.Delete(tmpPath); } catch (Exception ex) { Log.Debug(ex, "Failed to delete temp file {TmpPath} in finally", tmpPath); }
                        }
                        throw;
                    }
                }
                finally
                {
                    _downloadSemaphore.Release();
                }
            }
            catch (Exception ex)
            {
                if (i == retryCount)
                {
                    Log.Error(ex, "Failed to download image after {Retry} retries: {Url}", retryCount, url);
                    return string.Empty;
                }
                await Task.Delay(1000 * (i + 1), ct); // Exponential-ish backoff
            }
        }

        return string.Empty;
    }

    internal static HttpRequestMessage CreateImageRequest(string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);

        if (url.Contains("imglib.info", StringComparison.OrdinalIgnoreCase) ||
            url.Contains("cdnlibs.org", StringComparison.OrdinalIgnoreCase) ||
            url.Contains("animelib.org", StringComparison.OrdinalIgnoreCase) ||
            url.Contains("mangalib.me", StringComparison.OrdinalIgnoreCase) ||
            url.Contains("anilib.me", StringComparison.OrdinalIgnoreCase))
        {
            request.Headers.TryAddWithoutValidation(StreamingConstants.Headers.Referer, StreamingConstants.AnimeLib.RefererUrl);
            request.Headers.TryAddWithoutValidation(StreamingConstants.Headers.Origin, StreamingConstants.AnimeLib.SiteUrl);
        }

        return request;
    }

    public static string GetHashString(string inputString)
    {
        if (string.IsNullOrEmpty(inputString)) return string.Empty;

        int maxBytes = Encoding.UTF8.GetMaxByteCount(inputString.Length);
        Span<byte> utf8Bytes = maxBytes <= 512 ? stackalloc byte[maxBytes] : new byte[maxBytes];
        int actualBytes = Encoding.UTF8.GetBytes(inputString, utf8Bytes);

        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(utf8Bytes[..actualBytes], hash);
        return Convert.ToHexString(hash);
    }

    public static string GetFileNameForUrl(string url)
    {
        if (string.IsNullOrEmpty(url)) return string.Empty;
        int queryIdx = url.IndexOf('?');
        ReadOnlySpan<char> pathSpan = queryIdx >= 0 ? url.AsSpan(0, queryIdx) : url.AsSpan();
        ReadOnlySpan<char> extSpan = Path.GetExtension(pathSpan);
        string ext = extSpan.IsEmpty ? ".jpg" : extSpan.ToString();
        return GetHashString(url) + ext;
    }

    public void Dispose()
    {
        _downloadSemaphore.Dispose();
    }
}
