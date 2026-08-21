using System;
using System.IO;
using System.IO.Pipes;
using System.Threading.Tasks;

namespace Kiriha.Infrastructure.Player;

public static class SonokoIntegration
{
    /// <summary>
    /// Отправляет текущий тайминг в Sonoko через Named Pipes.
    /// Вызывайте этот метод асинхронно при нажатии нужной кнопки в плеере (без блокировки UI).
    /// </summary>
    /// <param name="currentTime">Текущее время плеера</param>
    public static async Task SendTimingToSonokoAsync(TimeSpan currentTime)
    {
        try
        {
            // Форматируем время как hh:mm:ss.fff (например 00:01:23.456)
            string timeString = currentTime.ToString(@"hh\:mm\:ss\.fff");

            // Подключаемся к пайпу, созданному в Sonoko
            using var pipeClient = new NamedPipeClientStream(
                serverName: ".",
                pipeName: "SonokoPlayerIntegrationPipe",
                direction: PipeDirection.Out,
                options: PipeOptions.Asynchronous);

            // Пытаемся подключиться с таймаутом 500 мс, чтобы плеер не зависал, если Sonoko выключен
            await pipeClient.ConnectAsync(500);

            // Отправляем строку
            using var writer = new StreamWriter(pipeClient);
            await writer.WriteLineAsync(timeString);
            await writer.FlushAsync();
        }
        catch (TimeoutException)
        {
            // Sonoko не запущен или не отвечает. Можно игнорировать или логировать.
        }
        catch (Exception ex)
        {
            // Обработка других возможных ошибок (например, IOException)
            System.Diagnostics.Debug.WriteLine($"Sonoko send error: {ex.Message}");
        }
    }
}
