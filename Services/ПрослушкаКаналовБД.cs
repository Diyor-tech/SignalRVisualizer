// ============================================================================
// ПрослушкаКаналовБД.cs
// BackgroundService для PostgreSQL LISTEN/NOTIFY.
// Сервис держит отдельное подключение к БД, слушает разрешенные каналы и при
// NOTIFY отправляет SignalR-событие нужной группе клиентов.
// ============================================================================

using Microsoft.AspNetCore.SignalR;
using Npgsql;
using SignalRVisualizer.Hubs;
using SignalRVisualizer.Models;
using System.Diagnostics;

namespace SignalRVisualizer.Services;

/// <summary>
/// Фоновая прослушка PostgreSQL LISTEN/NOTIFY.
///
/// Важное исправление: если БД недоступна, сервис больше не роняет весь сайт.
/// Он пишет ошибку в Debug и повторяет попытку подключения через несколько секунд.
/// </summary>
public class ПрослушкаКаналовБД : BackgroundService
{
    /// <summary>
    /// Строка подключения к БД, в которой выполняется LISTEN.
    /// </summary>
    private readonly string Строка_Подключения_К_ТА_34898Н2;

    /// <summary>
    /// Контекст Hub таблицы. Через него фоновый сервис отправляет события клиентам.
    /// </summary>
    private readonly IHubContext<ТаблицаSignalRHub> _хабконтекст;

    /// <summary>
    /// Блокировка для потокобезопасного накопления пришедших изменений.
    /// PostgreSQL уведомления могут приходить быстро, поэтому список защищаем lock-ом.
    /// </summary>
    private readonly object _lock = new();

    /// <summary>
    /// Накопитель изменений: ключ = канал, значение = набор измененных таблиц.
    /// HashSet убирает дубли, если за короткое время пришло несколько одинаковых NOTIFY.
    /// </summary>
    private readonly Dictionary<string, HashSet<string>> _измененнаяИзКанала = new();

    /// <summary>
    /// Таймер небольшой задержки. Он позволяет собрать несколько уведомлений БД
    /// в один пакет и не дергать браузер десятки раз подряд.
    /// </summary>
    private Timer? _таймер;

    public ПрослушкаКаналовБД(IConfiguration configuration, IHubContext<ТаблицаSignalRHub> хабконтекст)
    {
        _хабконтекст = хабконтекст;
        Строка_Подключения_К_ТА_34898Н2 = configuration.GetConnectionString("ТА_34898Н2") ?? "";
    }

    /// <summary>
    /// Главный цикл фонового сервиса.
    ///
    /// Если соединение с PostgreSQL оборвалось или БД временно недоступна,
    /// СлушатьКанал() выбросит исключение. Мы его ловим, ждем 5 секунд и
    /// пробуем подключиться заново.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken стоп)
    {
        while (!стоп.IsCancellationRequested)
        {
            try
            {
                await СлушатьКанал(стоп);
            }
            catch (OperationCanceledException) when (стоп.IsCancellationRequested)
            {
                // Приложение штатно останавливается.
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ошибка прослушки PostgreSQL LISTEN/NOTIFY: {ex}");

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), стоп);
                }
                catch (OperationCanceledException) when (стоп.IsCancellationRequested)
                {
                    // Ничего не делаем: приложение останавливается.
                }
            }
        }
    }

    /// <summary>
    /// Открывает одно подключение к PostgreSQL и подписывает его на все разрешенные каналы.
    ///
    /// Важно: подключение для LISTEN должно оставаться открытым. Если закрыть conn,
    /// PostgreSQL перестанет отправлять уведомления этому приложению.
    /// </summary>
    private async Task СлушатьКанал(CancellationToken стоп)
    {
        if (string.IsNullOrWhiteSpace(Строка_Подключения_К_ТА_34898Н2))
        {
            throw new InvalidOperationException("В appsettings.json не найдена строка подключения 'ТА_34898Н2'.");
        }

        await using var подключениекБД = new NpgsqlConnection(Строка_Подключения_К_ТА_34898Н2);
        await подключениекБД.OpenAsync(стоп);

        // Событие Notification срабатывает каждый раз, когда PostgreSQL присылает NOTIFY.
        подключениекБД.Notification += (_, e) =>
        {
            string имяКанала = e.Channel?.Trim() ?? "";
            string сообщение = e.Payload?.Trim() ?? "";

            Debug.WriteLine($"Имя канала: {имяКанала}, payload: {сообщение}");

            if (string.IsNullOrWhiteSpace(имяКанала) || string.IsNullOrWhiteSpace(сообщение))
                return;

            ДобавитьИзменение(имяКанала, сообщение);
        };

        // Подписываемся на каждый разрешенный канал.
        foreach (var канал in МодельДляОпределенияКаналов.Allowed)
        {
            string безопасныйКанал = канал.Replace("\"", "\"\"");

            // Между LISTEN и именем канала обязательно нужен пробел.
            // Кавычки нужны, чтобы PostgreSQL правильно понял имя канала.
            await using var команда = new NpgsqlCommand($@"LISTEN ""{безопасныйКанал}"";", подключениекБД);
            await команда.ExecuteNonQueryAsync(стоп);

            Debug.WriteLine($"Прослушка канала запущена: {канал}");
        }

        // WaitAsync блокирует выполнение и ждет уведомлений от PostgreSQL.
        // Когда уведомление приходит, Npgsql вызывает обработчик Notification выше.
        while (!стоп.IsCancellationRequested)
        {
            await подключениекБД.WaitAsync(стоп);
        }
    }

    /// <summary>
    /// Добавляет пришедшее изменение в накопитель и запускает таймер отправки.
    /// Небольшая задержка 200 мс нужна, чтобы объединить близкие NOTIFY в один пакет.
    /// </summary>
    private void ДобавитьИзменение(string канал, string сообщение)
    {
        var таблицы = РазобратьPayload(сообщение);

        lock (_lock)
        {
            if (!_измененнаяИзКанала.ContainsKey(канал))
            {
                _измененнаяИзКанала[канал] = new HashSet<string>();
            }

            foreach (var table in таблицы)
            {
                _измененнаяИзКанала[канал].Add(table);
            }

            _таймер?.Dispose();
            _таймер = new Timer(
                async _ => await ОтправитьНакопленныеИзменения(),
                null,
                TimeSpan.FromMilliseconds(200),
                Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>
    /// Разбирает payload из PostgreSQL NOTIFY.
    /// Можно отправить одну таблицу:
    /// NOTIFY "та_34898_n2_channel", '_Диспозиция_поковок_ТА';
    ///
    /// Можно отправить несколько через запятую или точку с запятой:
    /// NOTIFY "та_34898_n2_channel", '_Состояние,_Ошибки';
    /// </summary>
    private static List<string> РазобратьPayload(string сообщение)
    {
        return сообщение.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();
    }

    /// <summary>
    /// Отправляет накопленные изменения клиентам SignalR.
    ///
    /// Здесь сервер не отправляет HTML таблицы. Он отправляет короткое событие:
    /// «в такой-то таблице были изменения». После этого браузер сам вызывает
    /// /Главная/ЧастичнаяОтрисовка и получает новый HTML partial.
    /// </summary>
    private async Task ОтправитьНакопленныеИзменения()
    {
        Dictionary<string, List<string>> изменения;

        lock (_lock)
        {
            изменения = _измененнаяИзКанала.ToDictionary(x => x.Key, x => x.Value.ToList());
            _измененнаяИзКанала.Clear();
            _таймер?.Dispose();
            _таймер = null;
        }

        foreach (var item in изменения)
        {
            string канал = item.Key;
            List<string> tables = item.Value;

            if (tables.Count == 0)
                continue;

            string имяГруппы = МодельДляОпределенияКаналов.ПолучитьИмяГруппы(канал);

            Debug.WriteLine($"Канал: {канал}, группа: {имяГруппы}, изменения в: {string.Join(", ", tables)}");

            await _хабконтекст.Clients.Group(имяГруппы).SendAsync("ИзмененияДанных", new
            {
                channel = канал,
                tables,
                receivedAt = DateTime.Now.ToString("G")
            });
        }
    }

    /// <summary>
    /// Освобождаем таймер при остановке приложения.
    /// </summary>
    public override void Dispose()
    {
        _таймер?.Dispose();
        base.Dispose();
    }
}
