namespace Announcement.Options;

public class ImgBbOptions
{
    public const string SectionName = "ImgBB";

    public string ApiKey { get; set; } = string.Empty;
    public List<string> ApiKeys { get; set; } = new();
}

/// <summary>
/// Завантаження відео через tmpfile.link (POST /api/upload, поле file).
/// </summary>
public class TempClipOptions
{
    public const string SectionName = "TempClip";

    /// <summary>Базовий URL, наприклад https://tmpfile.link (без /api/upload).</summary>
    public string ApiBaseUrl { get; set; } = "https://tmpfile.link";
}

public class TelegraphOptions
{
    public const string SectionName = "Telegraph";

    public string AccessToken { get; set; } = string.Empty;
}

public class TelegramOptions
{
    public const string SectionName = "Telegram";

    public string BotToken { get; set; } = string.Empty;
    public string ChannelId { get; set; } = string.Empty;
}
