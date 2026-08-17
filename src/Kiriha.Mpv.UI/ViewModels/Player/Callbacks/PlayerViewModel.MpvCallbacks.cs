
using System;
using Kiriha.Core.Domain.Models;
using Kiriha.Services.Data;

namespace Kiriha.Mpv.UI.ViewModels.Player;

public partial class PlayerViewModel
{
    partial void OnMpvVideoPresetChanged(string value)
    {
        if (_isApplyingSettings || _settingsService == null) return;
        var preset = NormalizeMpvOption(value, "default");
        
        _isApplyingSettings = true;
        try
        {
            if (preset == "default")
            {
                MpvScale = "bilinear";
                MpvChromaScale = "bilinear";
                MpvDeband = false;
                MpvDebandIterations = 1;
                MpvCorrectDownscaling = false;
            }
            else if (preset == "balanced")
            {
                MpvScale = "spline36";
                MpvChromaScale = "spline36";
                MpvDeband = true;
                MpvDebandIterations = 1;
                MpvCorrectDownscaling = false;
            }
            else if (preset == "quality")
            {
                MpvScale = "ewa_lanczossharp";
                MpvChromaScale = "ewa_lanczossharp";
                MpvDeband = true;
                MpvDebandIterations = 3;
                MpvCorrectDownscaling = true;
            }
        }
        finally
        {
            _isApplyingSettings = false;
        }

        SaveVideoProcessingOption(x =>
        {
            x.MpvVideoPreset = preset;
            x.MpvScale = NormalizeMpvOption(MpvScale, "bilinear");
            x.MpvChromaScale = NormalizeMpvOption(MpvChromaScale, "bilinear");
            x.MpvDeband = MpvDeband;
            x.MpvDebandIterations = Math.Clamp(MpvDebandIterations, 0, 16);
            x.MpvCorrectDownscaling = MpvCorrectDownscaling;
        });
    }

    partial void OnMpvScaleChanged(string value) =>
        SaveVideoProcessingOption(x => x.MpvScale = NormalizeMpvOption(value, "bilinear"));
    partial void OnMpvChromaScaleChanged(string value) =>
        SaveVideoProcessingOption(x => x.MpvChromaScale = NormalizeMpvOption(value, "bilinear"));
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
    partial void OnMpvVideoSyncChanged(bool value) =>
        SaveMpvOption(x => x.MpvVideoSync = value);
    partial void OnMpvInterpolationChanged(bool value) =>
        SaveMpvOption(x => x.MpvInterpolation = value);

    private void SaveMpvOption(Action<AppSettings.PlayerConfig> update)
    {
        if (_isApplyingSettings || _settingsService == null) return;
        _settingsService.Update(settings => update(settings.Player), Kiriha.Core.Abstractions.Services.SettingsSection.Player);
    }

    private void SaveVideoProcessingOption(Action<AppSettings.PlayerConfig> update)
    {
        if (_isApplyingSettings || _settingsService == null) return;
        _settingsService.Update(settings => update(settings.Player), Kiriha.Core.Abstractions.Services.SettingsSection.Player);
        ApplyVideoProcessingOptions();
    }

    private void ApplyVideoProcessingOptions()
    {
        _settingsApplier.ApplyVideoProcessing(new PlayerVideoProcessingOptions(
            NormalizeMpvOption(MpvScale, "bilinear"),
            NormalizeMpvOption(MpvChromaScale, "bilinear"),
            NormalizeMpvOption(MpvDitherDepth, "auto"),
            MpvCorrectDownscaling,
            MpvDeband,
            Math.Clamp(MpvDebandIterations, 0, 16),
            Math.Clamp(MpvDebandThreshold, 0, 4096)));
    }
}
