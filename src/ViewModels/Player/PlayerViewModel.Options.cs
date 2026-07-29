using System.Collections.Generic;
using Kiriha.Models;

namespace Kiriha.ViewModels.Player;

public partial class PlayerViewModel
{
    public List<PlayerMouseActionOption> MouseActionOptions { get; } = new()
    {
        new("Ничего", PlayerMouseAction.None),
        new("Пауза / воспроизведение", PlayerMouseAction.TogglePlayPause),
        new("Полноэкранный режим", PlayerMouseAction.ToggleFullscreen),
        new("Показать панель", PlayerMouseAction.ShowControls),
        new("Открыть настройки", PlayerMouseAction.OpenSettings),
        new("Назад на 10 секунд", PlayerMouseAction.SeekBackward10),
        new("Вперёд на 10 секунд", PlayerMouseAction.SeekForward10),
        new("Следующая аудиодорожка", PlayerMouseAction.CycleAudio),
        new("Следующие субтитры", PlayerMouseAction.CycleSubtitle)
    };
    public List<PlayerWheelActionOption> WheelActionOptions { get; } = new()
    {
        new("Ничего", PlayerWheelAction.None),
        new("Громче", PlayerWheelAction.VolumeUp),
        new("Тише", PlayerWheelAction.VolumeDown),
        new("Вперёд", PlayerWheelAction.SeekForward),
        new("Назад", PlayerWheelAction.SeekBackward),
        new("Скорость выше", PlayerWheelAction.SpeedUp),
        new("Скорость ниже", PlayerWheelAction.SpeedDown)
    };
    public List<int> WheelStepOptions { get; } = new() { 1, 2, 5, 10 };
    public List<int> SeekStepOptions { get; } = new() { 1, 3, 5, 10, 15, 30 };
    public List<string> ScreenshotFormatOptions { get; } = new() { "png", "jpg", "webp" };
    public List<ScreenshotResolutionOption> ScreenshotResolutionOptions { get; } = new()
    {
        new("Исходное видео", "video"),
        new("Размер окна", "window")
    };
}
