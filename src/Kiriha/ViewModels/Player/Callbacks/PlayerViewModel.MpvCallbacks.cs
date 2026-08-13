using Kiriha.Services.Data.Settings;
using System;
using Kiriha.Models;
using Kiriha.Core.Abstractions.Models;
using Kiriha.Services.Data;

namespace Kiriha.ViewModels.Player;

public partial class PlayerViewModel
{
    partial void OnMpvScaleChanged(string value) =>
        SaveVideoProcessingOption(x => x.MpvScale = NormalizeMpvOption(value, "ewa_lanczossharp"));
    partial void OnMpvChromaScaleChanged(string value) =>
        SaveVideoProcessingOption(x => x.MpvChromaScale = NormalizeMpvOption(value, "ewa_lanczossharp"));
    partial void OnMpvDitherDepthChanged(string value) =>
        SaveVideoProcessingOption(x => x.MpvDitherDepth = NormalizeMpvOption(value, "auto"));
    partial void OnMpvCorrectDownscalingChanged(bool value) =>
        SaveVideoProcessingOption(x => x.MpvCorrectDownscaling = value);
    partial void OnMpvDebandChanged(bool value) =>
        SaveVideoProcessingOption(x => x.MpvDeband = value);
    partial void OnMpvDebandIterationsChanged(int value) =>
        SaveVideoProcessingOption(x => x.MpvDebandIterations = Math.Clamp(value, 0, 16));
    partial void OnMpvDebandThresholdChanged(int value) =>
        SaveVideoProcessingOption(x => x.MpvDebandThreshold = Math.Clamp(value, 0, 4096));
    partial void OnMpvHwdecChanged(string value)
    {
        var normalized = NormalizeMpvOption(value, "auto");
        SaveMpvOption(x => x.MpvHwdec = normalized);
        _playback.SetOptionString("hwdec", normalized);
        RefreshMpvRuntimeInfo();
    }
    partial void OnMpvVideoOutputChanged(string value) =>
        SaveMpvOption(x => x.MpvVideoOutput = NormalizeMpvOption(value, "gpu-next"));
    partial void OnMpvGpuApiChanged(string value) =>
        SaveMpvOption(x => x.MpvGpuApi = NormalizeMpvOption(value, "auto"));
    partial void OnMpvGpuContextChanged(string value) =>
        SaveMpvOption(x => x.MpvGpuContext = NormalizeMpvOption(value, "auto"));

    private void SaveMpvOption(Action<AppSettings.PlayerConfig> update)
    {
        if (_isApplyingSettings || _settingsService == null) return;
        _settingsService.Update(settings => update(settings.Player), Kiriha.Core.Services.SettingsSection.Player);
    }

    private void SaveVideoProcessingOption(Action<AppSettings.PlayerConfig> update)
    {
        if (_isApplyingSettings || _settingsService == null) return;
        _settingsService.Update(settings => update(settings.Player), Kiriha.Core.Services.SettingsSection.Player);
        ApplyVideoProcessingOptions();
    }

    private void ApplyVideoProcessingOptions()
    {
        _settingsApplier.ApplyVideoProcessing(new PlayerVideoProcessingOptions(
            NormalizeMpvOption(MpvScale, "ewa_lanczossharp"),
            NormalizeMpvOption(MpvChromaScale, "ewa_lanczossharp"),
            NormalizeMpvOption(MpvDitherDepth, "auto"),
            MpvCorrectDownscaling,
            MpvDeband,
            Math.Clamp(MpvDebandIterations, 0, 16),
            Math.Clamp(MpvDebandThreshold, 0, 4096)));
    }
}
