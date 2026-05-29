// ============================================================================
// УчебныйSignalRHub.cs
// Демонстрационный Hub для объяснения базовой работы SignalR:
// подключение клиента, отключение клиента, вызов методов Hub из JavaScript и
// отправка событий обратно одному или всем клиентам.
// ============================================================================

using Microsoft.AspNetCore.SignalR;

namespace SignalRVisualizer.Hubs;

/// <summary>
/// Учебный Hub для демонстрации обычного обмена клиент ↔ сервер.
///
/// Этот Hub не связан с PostgreSQL. Он нужен как наглядная шпаргалка:
/// - Clients.Caller отправляет событие только текущему клиенту;
/// - Clients.Others отправляет всем, кроме текущего клиента;
/// - Clients.All отправляет всем подключенным клиентам.
/// </summary>
public class УчебныйSignalRHub : Hub
{
    private readonly ILogger<УчебныйSignalRHub> _logger;

    public УчебныйSignalRHub(ILogger<УчебныйSignalRHub> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Автоматически вызывается SignalR после успешного подключения браузера.
    ///
    /// Важно: метод должен быть override. Если сделать обычный метод с похожим
    /// названием, SignalR сам его не вызовет.
    /// </summary>
    public override async Task OnConnectedAsync()
    {
        string idПодключения = Context.ConnectionId;
        string время = DateTime.Now.ToString("HH:mm:ss");

        _logger.LogInformation("Клиент подключился к SignalR. ConnectionId: {ConnectionId}", idПодключения);

        // Сообщение только тому браузеру, который сейчас подключился.
        await Clients.Caller.SendAsync(
            "ПринятьСистемноеСообщение",
            $"Вы подключились к Hub. Ваш ConnectionId: {idПодключения}",
            время);

        // Сообщение всем остальным вкладкам/браузерам.
        await Clients.Others.SendAsync(
            "ПринятьСистемноеСообщение",
            $"Подключился новый клиент: {idПодключения}",
            время);

        // Отдельное событие для подсветки схемы на странице.
        await Clients.Caller.SendAsync("ПодсветитьЭлементСхемы", "hub");

        await base.OnConnectedAsync();
    }

    /// <summary>
    /// Автоматически вызывается при отключении браузера от учебного Hub.
    /// </summary>
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        string idПодключения = Context.ConnectionId;
        string время = DateTime.Now.ToString("HH:mm:ss");

        _logger.LogInformation("Клиент отключился от SignalR. ConnectionId: {ConnectionId}", idПодключения);

        await Clients.Others.SendAsync(
            "ПринятьСистемноеСообщение",
            $"Клиент отключился: {idПодключения}",
            время);

        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Метод вызывается из JavaScript через:
    /// подключение.invoke("ОтправитьСообщениеНаСервер", имя, текст)
    ///
    /// Сервер принимает данные и рассылает событие всем клиентам.
    /// </summary>
    public async Task ОтправитьСообщениеНаСервер(string имяПользователя, string текстСообщения)
    {
        string время = DateTime.Now.ToString("HH:mm:ss");
        string idПодключения = Context.ConnectionId;

        _logger.LogInformation(
            "Клиент {ConnectionId} отправил сообщение: {Text}",
            idПодключения,
            текстСообщения);

        await Clients.All.SendAsync(
            "ПолучитьСообщениеОтСервера",
            имяПользователя,
            текстСообщения,
            idПодключения,
            время);
    }

    /// <summary>
    /// Пример ответа только тому клиенту, который сделал запрос.
    /// </summary>
    public async Task ЗапроситьТекущееВремяСервера()
    {
        string время = DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss");

        await Clients.Caller.SendAsync(
            "ПолучитьВремяСервера",
            $"Текущее время на сервере: {время}");
    }

    /// <summary>
    /// Пример рассылки команды всем клиентам.
    /// В реальном проекте таким способом можно уведомлять страницы, что нужно
    /// обновить таблицу, график, статусную строку и т.д.
    /// </summary>
    public async Task ОтправитьКомандуВсемКлиентам(string команда)
    {
        string время = DateTime.Now.ToString("HH:mm:ss");
        string idПодключения = Context.ConnectionId;

        await Clients.All.SendAsync(
            "ПолучитьКомандуОтКлиента",
            idПодключения,
            команда,
            время);
    }
}
