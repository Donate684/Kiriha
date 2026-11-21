using System.Diagnostics;
using System.Text.Json;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Infrastructure.Settings;

namespace Kiriha.Tests;

public sealed class SettingsPersistenceTests : IDisposable
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(30);
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("Kiriha.SettingsWrites.");

    [Fact]
    public async Task TwoProcesses_MergeIndependentChangesInTheSameFile()
    {
        using var initial = new SettingsService(_directory.FullName);
        using var timeout = new CancellationTokenSource(TestTimeout);
        var ct = timeout.Token;
        using var volume = StartWriter("volume");
        using var speed = StartWriter("speed");
        try
        {
            Assert.Equal(SettingsTestProcess.Ready, await volume.StandardOutput.ReadLineAsync(ct));
            Assert.Equal(SettingsTestProcess.Ready, await speed.StandardOutput.ReadLineAsync(ct));

            await Task.WhenAll(CommitAsync(volume, ct), CommitAsync(speed, ct));
            await Task.WhenAll(AssertSuccessAsync(volume, ct), AssertSuccessAsync(speed, ct));

            using var reopened = new SettingsService(_directory.FullName);
            Assert.Equal(47, reopened.Current.Player.Volume);
            Assert.Equal(1.5, reopened.Current.Player.PlaybackSpeed);
            Assert.False(File.Exists(reopened.PlayerSettingsPath + ".tmp"));
        }
        finally
        {
            await StopAsync(volume);
            await StopAsync(speed);
        }
    }

    [Fact]
    public async Task WriterKilledAfterFlush_RestartKeepsCommittedFileAndCanSaveAgain()
    {
        using var initial = new SettingsService(_directory.FullName);
        initial.Update(s => s.Player.Volume = 63, SettingsSection.Player, save: false);
        initial.SaveImmediate();
        using var timeout = new CancellationTokenSource(TestTimeout);
        var ct = timeout.Token;
        using (var writer = StartWriter("interrupt"))
        {
            try
            {
                Assert.Equal(SettingsTestProcess.Ready, await writer.StandardOutput.ReadLineAsync(ct));
                await CommitAsync(writer, ct);
                Assert.Equal(SettingsTestProcess.Flushed, await writer.StandardOutput.ReadLineAsync(ct));
                Assert.Equal(19, await ReadVolumeAsync(initial.PlayerSettingsPath + ".tmp", ct));
                Assert.Equal(63, await ReadVolumeAsync(initial.PlayerSettingsPath, ct));
            }
            finally
            {
                await StopAsync(writer);
            }
        }

        using (var restarted = new SettingsService(_directory.FullName))
        {
            Assert.Equal(63, restarted.Current.Player.Volume);
            restarted.Update(s => s.Player.PlaybackSpeed = 1.25, SettingsSection.Player, save: false);
            await restarted.SaveAsync();
            Assert.False(File.Exists(restarted.PlayerSettingsPath + ".tmp"));
        }

        using var verified = new SettingsService(_directory.FullName);
        Assert.Equal(63, verified.Current.Player.Volume);
        Assert.Equal(1.25, verified.Current.Player.PlaybackSpeed);
        Assert.Equal(63, await ReadVolumeAsync(verified.PlayerSettingsPath + ".bak", ct));
    }

    [Fact]
    public async Task RetryingAfterAFileReplaceFails_PreservesLaterChangesAndDoesNotReplayCommittedWrites()
    {
        using var initial = new SettingsService(_directory.FullName);
        bool fail = true;
        using var writer = new SettingsService(_directory.FullName, path =>
        {
            if (Path.GetFileName(path) == Path.GetFileName(initial.PlayerSettingsPath) && fail)
                throw new IOException("Deliberate failure before replacing the player file");
        });
        writer.Update(s =>
        {
            s.UI.LanguageCode = "ru";
            s.Player.Volume = 47;
        }, SettingsSection.UI | SettingsSection.Player, save: false);

        await Assert.ThrowsAsync<IOException>(writer.SaveAsync);
        Assert.False(File.Exists(writer.PlayerSettingsPath + ".tmp"));
        Assert.Equal(initial.Current.Player.Volume, await ReadVolumeAsync(writer.PlayerSettingsPath, CancellationToken.None));

        using (var other = new SettingsService(_directory.FullName))
        {
            Assert.Equal("ru", other.Current.UI.LanguageCode);
            other.Update(s =>
            {
                s.UI.LanguageCode = "uk";
                s.Player.PlaybackSpeed = 1.5;
            }, SettingsSection.UI | SettingsSection.Player, save: false);
            await other.SaveAsync();
        }
        fail = false;
        await writer.SaveAsync();

        using var verified = new SettingsService(_directory.FullName);
        Assert.Equal("uk", verified.Current.UI.LanguageCode);
        Assert.Equal(47, verified.Current.Player.Volume);
        Assert.Equal(1.5, verified.Current.Player.PlaybackSpeed);
    }

    [Fact]
    public async Task LockedFileRejectsSaveAndThePendingEditCanBeRetried()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var settings = new SettingsService(_directory.FullName);
        var original = File.ReadAllText(settings.PlayerSettingsPath);
        settings.Update(s => s.Player.Volume = 47, SettingsSection.Player, save: false);

        using (var locked = new FileStream(settings.PlayerSettingsPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            await Assert.ThrowsAsync<IOException>(settings.SaveAsync);

        Assert.Equal(original, File.ReadAllText(settings.PlayerSettingsPath));
        await settings.SaveAsync();
        using var reopened = new SettingsService(_directory.FullName);
        Assert.Equal(47, reopened.Current.Player.Volume);
    }

    [Fact]
    public void SavingFromBackupDoesNotReplaceItWithTruncatedPrimaryJson()
    {
        using var settings = new SettingsService(_directory.FullName);
        var originalVolume = settings.Current.Player.Volume;
        settings.Update(s => s.Player.Volume = 76, SettingsSection.Player, save: false);
        settings.SaveImmediate();
        var backupPath = settings.PlayerSettingsPath + ".bak";
        var backup = File.ReadAllText(backupPath);
        File.WriteAllText(settings.PlayerSettingsPath, """{"Volume":""");

        using (var recovered = new SettingsService(_directory.FullName))
        {
            Assert.Equal(originalVolume, recovered.Current.Player.Volume);
            recovered.Update(s => s.Player.PlaybackSpeed = 1.5, SettingsSection.Player, save: false);
            recovered.SaveImmediate();
        }

        Assert.Equal(backup, File.ReadAllText(backupPath));
        using var reopened = new SettingsService(_directory.FullName);
        Assert.Equal(originalVolume, reopened.Current.Player.Volume);
        Assert.Equal(1.5, reopened.Current.Player.PlaybackSpeed);
    }

    [Fact]
    public void UnrelatedWritePreservesLatestEncryptedTokenAndUnknownProperties()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var first = new SettingsService(_directory.FullName);
        using var second = new SettingsService(_directory.FullName);
        const string token = "fixture-streaming-token";
        first.Update(s => s.Torrents.OnlineStreaming.AnimeLibToken = token, SettingsSection.Torrents, save: false);
        first.SaveImmediate();
        var before = File.ReadAllText(first.TorrentsSettingsPath);
        var original = System.Text.Json.Nodes.JsonNode.Parse(before)?.AsObject()
            ?? throw new JsonException("Expected torrents settings");
        original["FutureOption"] = 456;
        File.WriteAllText(first.TorrentsSettingsPath, original.ToJsonString());
        var ciphertext = ReadAnimeLibToken();
        Assert.NotEmpty(ciphertext);
        Assert.NotEqual(token, ciphertext);

        second.Update(s => s.Torrents.OnlineStreaming.PreferDirectMp4 = !first.Current.Torrents.OnlineStreaming.PreferDirectMp4,
            SettingsSection.Torrents, save: false);
        second.SaveImmediate();

        Assert.Equal(ciphertext, ReadAnimeLibToken());
        using var document = JsonDocument.Parse(File.ReadAllText(first.TorrentsSettingsPath));
        Assert.Equal(456, document.RootElement.GetProperty("FutureOption").GetInt32());
        using var verified = new SettingsService(_directory.FullName);
        Assert.Equal(token, verified.Current.Torrents.OnlineStreaming.AnimeLibToken);
        Assert.NotEqual(first.Current.Torrents.OnlineStreaming.PreferDirectMp4, verified.Current.Torrents.OnlineStreaming.PreferDirectMp4);

        string ReadAnimeLibToken()
        {
            using var json = JsonDocument.Parse(File.ReadAllText(first.TorrentsSettingsPath));
            return json.RootElement.GetProperty("OnlineStreaming").GetProperty("AnimeLibToken").GetString() ?? string.Empty;
        }
    }

    private Process StartWriter(string operation)
    {
        var info = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        info.ArgumentList.Add(typeof(SettingsTestProcess).Assembly.Location);
        info.ArgumentList.Add(SettingsTestProcess.Command);
        info.ArgumentList.Add(_directory.FullName);
        info.ArgumentList.Add(operation);
        return Process.Start(info) ?? throw new InvalidOperationException("Could not launch the settings test process");
    }

    private static async Task CommitAsync(Process writer, CancellationToken ct)
    {
        await writer.StandardInput.WriteLineAsync("save".AsMemory(), ct);
        await writer.StandardInput.FlushAsync(ct);
    }

    private static async Task AssertSuccessAsync(Process writer, CancellationToken ct)
    {
        await writer.WaitForExitAsync(ct);
        Assert.True(writer.ExitCode == 0, await writer.StandardError.ReadToEndAsync(ct));
    }

    private static async Task StopAsync(Process writer)
    {
        if (!writer.HasExited)
            writer.Kill(entireProcessTree: true);

        await writer.WaitForExitAsync().WaitAsync(TestTimeout);
    }

    private static async Task<int> ReadVolumeAsync(string path, CancellationToken ct)
    {
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(path, ct));
        return json.RootElement.GetProperty("Volume").GetInt32();
    }

    public void Dispose() => _directory.Delete(recursive: true);
}
