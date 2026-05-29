// ============================================================================
// ГлавнаяController.cs
// MVC-контроллер главной страницы. Его задача — прочитать данные из PostgreSQL,
// заполнить модель и передать ее в Razor-представление.
// ============================================================================

using Microsoft.AspNetCore.Mvc;
using Npgsql;
using SignalRVisualizer.Models;
using System.Diagnostics;

namespace SignalRVisualizer.Controllers;

/// <summary>
/// Контроллер страницы с таблицей.
///
/// Важный принцип MVC:
/// Controller получает данные → кладет их в Model → возвращает View(model).
/// Если вернуть просто View(), а Razor ожидает @model, страница может упасть.
/// </summary>
public class ГлавнаяController : Controller
{
    /// <summary>
    /// IConfiguration дает доступ к appsettings.json.
    /// Через него читаем строку подключения из секции ConnectionStrings.
    /// </summary>
    private readonly IConfiguration _configuration;

    /// <summary>
    /// Основная строка подключения к базе ТА_34898Н2.
    /// Название ключа должно совпадать с appsettings.json → ConnectionStrings → ТА_34898Н2.
    /// </summary>
    private readonly string connectstr;

    public ГлавнаяController(IConfiguration configuration)
    {
        _configuration = configuration;
        connectstr = _configuration.GetConnectionString("ТА_34898Н2") ?? "";
    }

    /// <summary>
    /// Полная первичная отрисовка страницы.
    /// Этот метод вызывается при открытии /Главная/ПерваяСтраница.
    ///
    /// Здесь таблица загружается сразу, чтобы пользователь уже при первом открытии
    /// видел актуальное состояние БД, даже если SignalR еще выключен.
    /// </summary>
    public async Task<IActionResult> ПерваяСтраница(CancellationToken cancellationToken)
    {
        // Получаем модель с данными или с текстом ошибки.
        var model = await ДанныеИзТаблицы(cancellationToken);

        // обязательно передаем model в View.
        return View(model);
    }

    /// <summary>
    /// Частичная отрисовка таблицы.
    ///
    /// Этот метод НЕ возвращает всю страницу с layout, шапкой и журналом событий.
    /// Он возвращает только HTML-строки tbody таблицы из ТаблицаPartialView.cshtml.
    /// JavaScript получает этот HTML через fetch() и заменяет содержимое #Таблица.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> ЧастичнаяОтрисовка(CancellationToken cancellationToken)
    {
        var model = await ДанныеИзТаблицы(cancellationToken);

        // Запрещаем кеширование partial HTML. Без этого браузер/прокси иногда могут
        // вернуть старый фрагмент таблицы, и будет казаться, что обновление не работает.
        Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
        Response.Headers.Pragma = "no-cache";
        Response.Headers.Expires = "0";

        // Второе важное исправление: partial тоже получает model.
        return PartialView("ТаблицаPartialView", model);
    }

    /// <summary>
    /// Читает строки из PostgreSQL и собирает модель для Razor.
    ///
    /// Метод специально возвращает модель даже при ошибке. Это сделано для учебной
    /// устойчивости: если БД выключена, сайт должен открыть страницу и показать
    /// понятное сообщение, а не белый экран/исключение.
    /// </summary>
    private async Task<МодельДляСтрокиИзБазы> ДанныеИзТаблицы(CancellationToken cancellationToken)
    {
        var model = new МодельДляСтрокиИзБазы
        {
            ВремяПоследнейЗагрузки = DateTime.Now
        };

        if (string.IsNullOrWhiteSpace(connectstr))
        {
            model.ОшибкаЗагрузки = "В appsettings.json не найдена строка подключения 'ТА_34898Н2'.";
            return model;
        }

        try
        {
            // await using корректно закроет соединение даже при ошибке.
            await using var conn = new NpgsqlConnection(connectstr);
            await conn.OpenAsync(cancellationToken);

            // Таблица и столбцы заключены в двойные кавычки, потому что PostgreSQL
            // воспринимает такие имена как регистрозависимые идентификаторы.
            // Это особенно важно для русских имен и имен со смешанным регистром.
            const string sql = """
                SELECT
                    "N_поз_ТА",
                    "N_ряда",
                    "Прав_поток_номер_поковки",
                    "Прав_поток_на_поддоне_количество",
                    "Прав_поток_на_поддоне_ед_измерения",
                    "Лев_поток_номер_поковки",
                    "Лев_поток_на_поддоне_количество",
                    "Лев_поток_на_поддоне_ед_измерения",
                    "Старт"
                FROM "_Диспозиция_поковок_ТА"
                WHERE "N_поз_ТА" >= 1 AND "N_поз_ТА" <= 4
                ORDER BY "N_поз_ТА";
                """;

            await using var cmd = new NpgsqlCommand(sql, conn);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

            // Каждая строка reader превращается в одну строку таблицы Razor.
            while (await reader.ReadAsync(cancellationToken))
            {
                model.ЛистДляТаблицыИзБазы.Add(new МодельДляСтрокиИзБазы.Лист
                {
                    N_поз_ТА = ПолучитьСтроку(reader, "N_поз_ТА"),
                    N_ряда = ПолучитьСтроку(reader, "N_ряда"),
                    Прав_поток_номер_поковки = ПолучитьСтроку(reader, "Прав_поток_номер_поковки"),
                    Прав_поток_на_поддоне_количество = ПолучитьСтроку(reader, "Прав_поток_на_поддоне_количество"),
                    Прав_поток_на_поддоне_ед_измерения = ПолучитьСтроку(reader, "Прав_поток_на_поддоне_ед_измерения"),
                    Лев_поток_номер_поковки = ПолучитьСтроку(reader, "Лев_поток_номер_поковки"),
                    Лев_поток_на_поддоне_количество = ПолучитьСтроку(reader, "Лев_поток_на_поддоне_количество"),
                    Лев_поток_на_поддоне_ед_измерения = ПолучитьСтроку(reader, "Лев_поток_на_поддоне_ед_измерения"),
                    Старт = ПолучитьСтроку(reader, "Старт")
                });
            }
        }
        catch (Exception ex)
        {
            // В учебном проекте пишем ошибку в окно Output Visual Studio.
            // В рабочем проекте сюда можно подключить запись в Журнал_ошибок_сайта.
            Debug.WriteLine(ex);
            model.ОшибкаЗагрузки = ex.Message;
        }

        return model;
    }

    /// <summary>
    /// Безопасно получает значение столбца из NpgsqlDataReader.
    ///
    /// Зачем отдельный метод:
    /// - DBNull превращаем в пустую строку;
    /// - отсутствующий столбец не роняет страницу;
    /// - в Razor проще выводить уже готовые строки.
    /// </summary>
    private static string ПолучитьСтроку(NpgsqlDataReader reader, string имяСтолбца)
    {
        try
        {
            int индекс = reader.GetOrdinal(имяСтолбца);
            return reader.IsDBNull(индекс) ? "" : reader.GetValue(индекс)?.ToString() ?? "";
        }
        catch (IndexOutOfRangeException)
        {
            // Если в учебной/тестовой БД нет какого-то столбца, страница не должна падать.
            return "";
        }
    }

    /// <summary>
    /// Простая страница ошибки для production-режима.
    /// Подключена в Program.cs через app.UseExceptionHandler("/Главная/Ошибка").
    /// </summary>
    public IActionResult Ошибка()
    {
        return Content("Произошла ошибка в учебном проекте SignalR.");
    }
}
