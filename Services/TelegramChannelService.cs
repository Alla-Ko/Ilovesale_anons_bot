using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using Announcement.Models;
using Announcement.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Announcement.Services;

public interface ITelegramChannelService
{
    Task SendAnnouncementVariantAsync(AnnouncementEntity announcement, int variant, CancellationToken cancellationToken = default);
}

public class TelegramChannelService : ITelegramChannelService
{
    private const int TelegramCaptionMaxLength = 1024;
    private const int MaxFloodWaitRetries = 12;

    /// <summary>Пауза після кожних N відправлених колажів (зменшує Flood 429).</summary>
    private const int CollageBatchSize = 20;

    private static readonly TimeSpan PauseAfterCollageBatch = TimeSpan.FromMilliseconds(900);

    private readonly TelegramOptions _options;
    private readonly ICaptionPublishFormatter _captions;
    private readonly ILogger<TelegramChannelService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;

    public TelegramChannelService(
        IOptions<TelegramOptions> options,
        ICaptionPublishFormatter captions,
        ILogger<TelegramChannelService> logger,
        IHttpClientFactory httpClientFactory)
    {
        _options = options.Value;
        _captions = captions;
        _logger = logger;
        _httpClientFactory = httpClientFactory;
    }

    public async Task SendAnnouncementVariantAsync(AnnouncementEntity announcement, int variant, CancellationToken cancellationToken = default)
    {
        var token = _options.BotToken?.Trim();
        var channel = NormalizeChannelId(_options.ChannelId?.Trim());

        if (string.IsNullOrEmpty(token) || token.StartsWith("YOUR_", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrEmpty(channel) || channel.StartsWith("YOUR_", StringComparison.OrdinalIgnoreCase))
        {
            await Task.Delay(50, cancellationToken);
            return;
        }

        var enc = HtmlEncoder.Default;
        // У канал лише заголовок і далі колажі — посилання на Telegraf сюди не додаємо.
        var introHtml = enc.Encode(announcement.Title);
        var client = new TelegramBotClient(token);
        var chatId = new ChatId(channel);

        await ExecuteWithFloodWaitRetryAsync(
            () => client.SendMessage(
                chatId: chatId,
                text: introHtml,
                parseMode: ParseMode.Html,
                cancellationToken: cancellationToken),
            cancellationToken,
            channel);

        var ordered = announcement.Collages.OrderBy(c => c.SortOrder).ToList();
        var collageIndex = 0;
        foreach (var c in ordered)
        {
            collageIndex++;
            var capRaw = variant == 1 ? c.Caption1 : c.Caption2;
            var preparedCaption = CaptionPublishFormatter.PrepareForPublish(capRaw);
            ValidateTelegramCaptionLength(preparedCaption, collageIndex);
            var captionHtml = _captions.FormatCaptionForTelegramHtml(capRaw);

            if (c.MediaType == MediaType.Photo)
            {
                await ExecuteWithFloodWaitRetryAsync(
                    () => SendPhotoFromDownloadedContentAsync(client, chatId, c.MediaUrl, captionHtml, cancellationToken),
                    cancellationToken);
            }
            else
            {
                await ExecuteWithFloodWaitRetryAsync(
                    () => SendVideoFromDownloadedContentAsync(client, chatId, c.MediaUrl, captionHtml, cancellationToken),
                    cancellationToken);
            }

            if (collageIndex % CollageBatchSize == 0 && collageIndex < ordered.Count)
            {
                await Task.Delay(PauseAfterCollageBatch, cancellationToken);
            }
        }
    }

    /// <summary>
    /// Повтор при Flood control (429): чекаємо <c>retry_after</c> з відповіді API або парсимо з тексту помилки.
    /// </summary>
    private async Task ExecuteWithFloodWaitRetryAsync(Func<Task> send, CancellationToken cancellationToken, string? channel = null)
    {
        for (var attempt = 1; attempt <= MaxFloodWaitRetries; attempt++)
        {
            try
            {
                await send();
                return;
            }
            catch (ApiRequestException ex) when (ex.ErrorCode == 400 && ex.Message.Contains("chat not found", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Telegram: chat not found для CHANNEL_ID '{channel ?? "(null)"}'. Перевірте формат CHANNEL_ID (наприклад @channel_name або -1001234567890), додайте бота в канал/групу та дайте йому права на публікацію.",
                    ex);
            }
            catch (ApiRequestException ex) when (ex.ErrorCode == 429)
            {
                if (attempt >= MaxFloodWaitRetries)
                    throw;

                var seconds = ex.Parameters?.RetryAfter
                    ?? ParseRetryAfterSeconds(ex.Message)
                    ?? Math.Clamp(attempt * 2, 5, 120);

                await Task.Delay(TimeSpan.FromSeconds(seconds), cancellationToken);
            }
        }
    }

    private static string? NormalizeChannelId(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return raw;

        var v = raw.Trim();
        if (v.StartsWith("https://t.me/", StringComparison.OrdinalIgnoreCase))
            v = v[13..];
        else if (v.StartsWith("http://t.me/", StringComparison.OrdinalIgnoreCase))
            v = v[12..];
        else if (v.StartsWith("t.me/", StringComparison.OrdinalIgnoreCase))
            v = v[5..];

        v = v.Trim('/');
        if (v.StartsWith("@") || v.StartsWith("-"))
            return v;

        if (long.TryParse(v, out _))
            return v;

        return "@" + v;
    }

    private static int? ParseRetryAfterSeconds(string? message)
    {
        if (string.IsNullOrEmpty(message))
            return null;
        var m = Regex.Match(message, @"retry after (\d+)", RegexOptions.IgnoreCase);
        if (m.Success && int.TryParse(m.Groups[1].Value, out var sec) && sec > 0)
            return sec;
        return null;
    }

    /// <summary>
    /// Явно валідовує довжину підпису. Якщо перевищено ліміт Telegram, кидає помилку,
    /// щоб показати користувачу зрозуміле повідомлення, а не тихо обрізати текст.
    /// </summary>
    private static void ValidateTelegramCaptionLength(string? plainText, int collageIndex)
    {
        if (string.IsNullOrEmpty(plainText))
            return;
        if (plainText.Length <= TelegramCaptionMaxLength)
            return;

        throw new InvalidOperationException(
            $"У колажі №{collageIndex} довжина підпису {plainText.Length} символів, максимально допустиме значення {TelegramCaptionMaxLength}.");
    }

    private async Task SendPhotoFromDownloadedContentAsync(
        ITelegramBotClient client,
        ChatId chatId,
        string mediaUrl,
        string? captionHtml,
        CancellationToken cancellationToken)
    {
        await using var media = await DownloadMediaForTelegramAsync(mediaUrl, MediaType.Photo, cancellationToken);
        var input = InputFile.FromStream(media.Stream, media.FileName);

        if (string.IsNullOrEmpty(captionHtml))
        {
            await client.SendPhoto(chatId, input, cancellationToken: cancellationToken);
            return;
        }

        await client.SendPhoto(
            chatId,
            input,
            caption: captionHtml,
            parseMode: ParseMode.Html,
            cancellationToken: cancellationToken);
    }

    private async Task SendVideoFromDownloadedContentAsync(
        ITelegramBotClient client,
        ChatId chatId,
        string mediaUrl,
        string? captionHtml,
        CancellationToken cancellationToken)
    {
        await using var media = await DownloadMediaForTelegramAsync(mediaUrl, MediaType.Video, cancellationToken);
        var input = InputFile.FromStream(media.Stream, media.FileName);

        if (string.IsNullOrEmpty(captionHtml))
        {
            await client.SendVideo(chatId, input, cancellationToken: cancellationToken);
            return;
        }

        await client.SendVideo(
            chatId,
            input,
            caption: captionHtml,
            parseMode: ParseMode.Html,
            cancellationToken: cancellationToken);
    }

    private async Task<DownloadedMedia> DownloadMediaForTelegramAsync(
        string mediaUrl,
        MediaType expectedType,
        CancellationToken cancellationToken)
    {
        var http = _httpClientFactory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(30);

        using var response = await http.GetAsync(
            mediaUrl,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        var contentType = response.Content.Headers.ContentType?.MediaType;
        ValidateTelegramMediaContentType(mediaUrl, contentType, expectedType);

        var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var memory = new MemoryStream();
        await stream.CopyToAsync(memory, cancellationToken);
        memory.Position = 0;

        var fileName = BuildTelegramMediaFileName(mediaUrl, contentType, expectedType);
        return new DownloadedMedia(memory, fileName);
    }

    private static void ValidateTelegramMediaContentType(string mediaUrl, string? contentType, MediaType expectedType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
            return;

        var ok = expectedType switch
        {
            MediaType.Photo => contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase),
            MediaType.Video => contentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase),
            _ => false
        };

        if (ok)
            return;

        throw new InvalidOperationException(
            $"Медіа за URL '{mediaUrl}' має Content-Type '{contentType}', який не підходить для типу '{expectedType}'.");
    }

    private static string BuildTelegramMediaFileName(string mediaUrl, string? contentType, MediaType expectedType)
    {
        var ext = GetExtensionFromContentType(contentType)
                  ?? Path.GetExtension(GetSafePathFromUrl(mediaUrl))
                  ?? string.Empty;

        if (string.IsNullOrWhiteSpace(ext) || ext == ".")
        {
            ext = expectedType == MediaType.Photo ? ".jpg" : ".mp4";
        }

        if (!ext.StartsWith(".", StringComparison.Ordinal))
            ext = "." + ext;

        return (expectedType == MediaType.Photo ? "photo" : "video") + ext.ToLowerInvariant();
    }

    private static string GetSafePathFromUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return string.Empty;

        return uri.AbsolutePath;
    }

    private static string? GetExtensionFromContentType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
            return null;

        if (MediaTypeHeaderValue.TryParse(contentType, out var parsed))
            contentType = parsed.MediaType;

        return contentType?.ToLowerInvariant() switch
        {
            "image/jpeg" => ".jpg",
            "image/jpg" => ".jpg",
            "image/png" => ".png",
            "image/webp" => ".webp",
            "image/gif" => ".gif",
            "video/mp4" => ".mp4",
            "video/webm" => ".webm",
            "video/quicktime" => ".mov",
            "video/x-matroska" => ".mkv",
            "video/x-msvideo" => ".avi",
            _ => null
        };
    }

    private sealed class DownloadedMedia : IAsyncDisposable
    {
        public DownloadedMedia(Stream stream, string fileName)
        {
            Stream = stream;
            FileName = fileName;
        }

        public Stream Stream { get; }
        public string FileName { get; }

        public async ValueTask DisposeAsync()
        {
            await Stream.DisposeAsync();
        }
    }
}
