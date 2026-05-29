// ============================================================================
// МоделиЗапросаНаОбновлениеДанных.cs
// DTO-модель для ответа Hub на ручной запрос обновления таблицы.
// DTO = простой объект передачи данных между сервером и клиентом.
// ============================================================================

using System.Text.Json.Serialization;

namespace SignalRVisualizer.Models;

/// <summary>
/// Объект, который ТаблицаSignalRHub возвращает JavaScript-клиенту.
///
/// Он говорит клиенту:
/// - какая команда подтверждена;
/// - к какому PostgreSQL-каналу она относится;
/// - какие таблицы логически нужно перечитать.
/// </summary>
public class МоделиЗапросаНаОбновлениеДанных
{
    /// <summary>
    /// Название команды. Сейчас используется значение "РазовоеОбновлениеТаблицы".
    /// </summary>
    public string Команда { get; set; } = "";

    /// <summary>
    /// Канал PostgreSQL/SignalR.
    /// JsonPropertyName нужен, чтобы в JavaScript можно было читать ответ.channel.
    /// </summary>
    [JsonPropertyName("channel")]
    public string Канал { get; set; } = "";

    /// <summary>
    /// Список таблиц, которые изменились или которые надо перечитать.
    /// JsonPropertyName нужен, чтобы в JavaScript можно было читать ответ.tables.
    /// </summary>
    [JsonPropertyName("tables")]
    public List<string> Таблицы { get; set; } = new();
}
