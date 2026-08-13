using Kiriha.Models;

namespace Kiriha.Models.Presentation;

public record PlayerMouseActionOption(string Name, PlayerMouseAction Value);
public record PlayerWheelActionOption(string Name, PlayerWheelAction Value);
public record ScreenshotResolutionOption(string Name, string Value);
