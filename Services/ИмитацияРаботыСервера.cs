// ============================================================================
// ИмитацияРаботыСервера.cs
// Учебный BackgroundService, который показывает, как сервер может сам отправлять
// события клиентам без нажатия кнопки в браузере.
// ============================================================================

using Microsoft.AspNetCore.SignalR;
using SignalRVisualizer.Hubs;

namespace SignalRVisualizer.Services;

/// <summary>
/// Фоновый сервис нужен для демонстрации важного сценария:
/// событие возникает НЕ в браузере, а на сервере.
///
/// Например, в реальном проекте это может быть:
/// - событие из PostgreSQL LISTEN/NOTIFY;
/// - изменение состояния печи;
/// - авария;
/// - новое значение датчика;
/// - сообщение от PLC/HMI.
///
/// Так как BackgroundService не является Hub-классом, он отправляет события через IHubContext.
/// </summary>
public class ИмитацияРаботыСервера : BackgroundService
{
    /// <summary>
    /// IHubContext позволяет обычному сервису отправлять SignalR-события.
    /// Без него отправлять Clients.All можно только внутри класса Hub.
    /// </summary>
    private readonly IHubContext<УчебныйSignalRHub> _hubContext;

    private readonly ILogger<ИмитацияРаботыСервера> _logger;

    public ИмитацияРаботыСервера(
        IHubContext<УчебныйSignalRHub> hubContext,
        ILogger<ИмитацияРаботыСервера> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    /// <summary>
    /// Метод запускается автоматически после старта приложения.
    /// Цикл работает, пока сайт не остановлен.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        int номерСобытия = 1;

        while (!stoppingToken.IsCancellationRequested)
        {
            // Имитация периодической серверной работы.
            await Task.Delay(TimeSpan.FromSeconds(7), stoppingToken);

            string время = DateTime.Now.ToString("HH:mm:ss");
            string текст = $"Фоновый сервис сервера отправил событие №{номерСобытия}";

            _logger.LogInformation("{Text}", текст);

            // Отправляем событие всем браузерам, которые сейчас подключены к учебному Hub.
            await _hubContext.Clients.All.SendAsync(
                "ПолучитьСобытиеФоновогоСервиса",
                номерСобытия,
                текст,
                время,
                cancellationToken: stoppingToken);

            номерСобытия++;
        }
    }
}
