using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using Announcement.Models;
using Announcement.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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

    public TelegramChannelService(
        IOptions<TelegramOptions> options,
        ICaptionPublishFormatter captions,
        ILogger<TelegramChannelService> logger)
    {
        _options = options.Value;
        _captions = captions;
        _logger = logger;
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
                if (string.IsNullOrEmpty(captionHtml))
                {
                    await ExecuteWithFloodWaitRetryAsync(
                        () => client.SendPhoto(chatId, InputFile.FromUri(c.MediaUrl), cancellationToken: cancellationToken),
                        cancellationToken);
                }
                else
                {
                    await ExecuteWithFloodWaitRetryAsync(
                        () => client.SendPhoto(
                            chatId,
                            InputFile.FromUri(c.MediaUrl),
                            caption: captionHtml,
                            parseMode: ParseMode.Html,
                            cancellationToken: cancellationToken),
                        cancellationToken);
                }
            }
            else
            {
                if (string.IsNullOrEmpty(captionHtml))
                {
                    await ExecuteWithFloodWaitRetryAsync(
                        () => client.SendVideo(chatId, InputFile.FromUri(c.MediaUrl), cancellationToken: cancellationToken),
                        cancellationToken);
                }
                else
                {
                    await ExecuteWithFloodWaitRetryAsync(
                        () => client.SendVideo(
                            chatId,
                            InputFile.FromUri(c.MediaUrl),
                            caption: captionHtml,
                            parseMode: ParseMode.Html,
                            cancellationToken: cancellationToken),
                        cancellationToken);
                }
            }

            if (collageIndex % CollageBatchSize == 0 && collageIndex < ordered.Count)
            {
                _logger.LogInformation(
                    "Telegram: пауза {PauseMs} мс після {Count} колажів (усього {Total}).",
                    PauseAfterCollageBatch.TotalMilliseconds, collageIndex, ordered.Count);
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

                _logger.LogWarning(
                    "Telegram FloodWait (429), пауза {Seconds} с перед наступною спробою ({Attempt}/{Max}).",
                    seconds, attempt, MaxFloodWaitRetries);

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
}
