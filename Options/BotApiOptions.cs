namespace Announcement.Options;

/// <summary>
/// Доступ для зовнішнього Telegram-бота (REST API).
/// </summary>
public class BotApiOptions
{
    public const string SectionName = "BotApi";

    /// <summary>Секретний ключ. Передавати в заголовку X-Api-Key або Authorization: Bearer.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Id користувача Identity (AspNetUsers), якщо в запиті не вказано CreatorUserId.</summary>
    public string? DefaultCreatorUserId { get; set; }
}
