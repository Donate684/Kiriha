using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace Kiriha.Services.Data.Image;

public class ImageDiskCache
{
    private readonly string _cacheRoot;
    private readonly ImageDownloader _downloader;

    public ImageDiskCache(string cacheRoot, ImageDownloader downloader)
    {
        _cacheRoot = cacheRoot;
        _downloader = downloader;
    }

    public async Task<string> ResolveLocalPathAsync(string url, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(url)) return string.Empty;

        string localPath = string.Empty;
        bool isUrl = url.StartsWith("http", StringComparison.OrdinalIgnoreCase);

        if (!isUrl && File.Exists(url))
        {
            localPath = url;
        }
        else if (isUrl)
        {
            string fileName = ImageDownloader.GetHashString(url) + Path.GetExtension(url.Split('?')[0]);
            if (string.IsNullOrEmpty(Path.GetExtension(fileName))) fileName += ".jpg";
            string candidatePath = Path.Combine(_cacheRoot, fileName);

            if (File.Exists(candidatePath))
            {
                var fileInfo = new FileInfo(candidatePath);
                if (fileInfo.Length > 0)
                {
                    try { fileInfo.LastWriteTime = DateTime.Now; } catch (Exception ex) { Log.Debug(ex, "Failed to update LastWriteTime for {FilePath}", candidatePath); }
                    localPath = candidatePath;
                }
                else
                {
                    try { fileInfo.Delete(); } catch (Exception ex) { Log.Debug(ex, "Failed to delete corrupted file {FilePath}", candidatePath); }
                    localPath = await _downloader.GetLocalPathOrDownload(url, ct);
                }
            }
            else
            {
                localPath = await _downloader.GetLocalPathOrDownload(url, ct);
            }
        }

        return localPath;
    }
}
