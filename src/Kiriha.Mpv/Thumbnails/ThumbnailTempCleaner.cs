using System;
using System.IO;
using System.Threading.Tasks;

namespace Kiriha.Mpv;

/// <summary>
/// One-shot cleaner for legacy temporary directories created by older versions of Kiriha.
/// MpvThumbnailer now generates all frames purely in RAM with 0 bytes written to disk.
/// </summary>
public static class ThumbnailTempCleaner
{
    public static void StartCleanupTask()
    {
        Task.Run(() =>
        {
            try
            {
                var baseDir = Path.Combine(Path.GetTempPath(), "Kiriha", "timeline-thumbs");
                if (Directory.Exists(baseDir))
                {
                    Directory.Delete(baseDir, recursive: true);
                }
            }
            catch
            {
                // Best effort cleanup only
            }
        });
    }
}
