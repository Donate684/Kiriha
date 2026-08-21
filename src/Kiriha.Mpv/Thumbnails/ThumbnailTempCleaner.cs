using System;
using System.IO;
using System.Threading.Tasks;
using Serilog;

namespace Kiriha.Mpv;

internal static class ThumbnailTempCleaner
{
    private static readonly TimeSpan FileAgeThreshold = TimeSpan.FromSeconds(10);
    public static void StartCleanupTask()
    {
        Task.Run(() =>
        {
            try
            {
                var baseDir = Path.Combine(Path.GetTempPath(), "Kiriha", "timeline-thumbs");
                if (Directory.Exists(baseDir))
                {
                    foreach (var dir in Directory.GetDirectories(baseDir))
                    {
                        try
                        {
                            var lockFilePath = Path.Combine(dir, ".lock");
                            bool isLocked = false;

                            if (File.Exists(lockFilePath))
                            {
                                try
                                {
                                    using var fs = new FileStream(lockFilePath, FileMode.Open, FileAccess.Write, FileShare.None);
                                }
                                catch (IOException)
                                {
                                    isLocked = true;
                                }
                            }
                            else if (DateTime.UtcNow - Directory.GetCreationTimeUtc(dir) < FileAgeThreshold)
                            {
                                isLocked = true;
                            }

                            if (!isLocked)
                            {
                                Directory.Delete(dir, recursive: true);
                            }
                        }
                        catch (Exception ex) { Log.Debug(ex, "Failed to clean up thumbnail directory: {Dir}", dir); }
                    }
                }
            }
            catch (Exception ex) { Log.Debug(ex, "Failed to enumerate root thumbnail directory"); }
        });
    }
}
