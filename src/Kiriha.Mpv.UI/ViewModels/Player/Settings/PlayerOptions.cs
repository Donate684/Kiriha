using Kiriha.Core.Domain.Models;

namespace Kiriha.Mpv.UI.ViewModels.Player.Settings;

public record PlayerMouseActionOption(string Name, PlayerMouseAction Value);
public record PlayerWheelActionOption(string Name, PlayerWheelAction Value);
public record ScreenshotResolutionOption(string Name, string Value);
