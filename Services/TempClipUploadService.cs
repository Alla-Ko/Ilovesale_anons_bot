using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Announcement.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Announcement.Services;

public record TempClipUploadResult(bool Success, string? DownloadUrl, string? ErrorMessage)
{
    public static TempClipUploadResult Ok(string url) => new(true, url, null);
    public static TempClipUploadResult Fail(string message) => new(false, null, message);
}

public interface ITempClipUploadService
{
    /// <param name="fileContentType">MIME з браузера (наприклад video/webm)</param>
    Task<TempClipUploadResult> UploadVideoAsync(
        Stream videoStream,
        string fileName,
        string? fileContentType = null,
        CancellationToken cancellationToken = default);
}

public class TempClipUploadService : ITempClipUploadService
{
    private const long MaxFileBytes = 100L * 1024 * 1024;
    private const int MaxAttempts = 2;

    private readonly HttpClient _http;
    private readonly TempClipOptions _options;
    private readonly ILogger<TempClipUploadService> _logger;

    public TempClipUploadService(
        HttpClient http,
        IOptions<TempClipOptions> options,
        ILogger<TempClipUploadService> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<TempClipUploadResult> UploadVideoAsync(
        Stream videoStream,
        string fileName,
        string? fileContentType = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await UploadVideoCoreAsync(videoStream, fileName, fileContentType, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "tmpfile.link: помилка завантаження відео");
            return TempClipUploadResult.Fail(
                "Не вдалося завантажити відео. Спробуйте ще раз або перевірте файл (розмір до 100 МБ, поширений формат відео).");
        }
    }

    private async Task<TempClipUploadResult> UploadVideoCoreAsync(
        Stream videoStream,
        string fileName,
        string? fileContentType,
        CancellationToken cancellationToken)
    {
        var baseUrl = (_options.ApiBaseUrl ?? "https://tmpfile.link").Trim().TrimEnd('/');

        if (baseUrl.Contains("example", StringComparison.OrdinalIgnoreCase))
        {
            await Task.Delay(50, cancellationToken);
            return TempClipUploadResult.Ok($"https://tmpfile.link/placeholder/{Uri.EscapeDataString(fileName)}");
        }

        var safeName = SanitizeFileName(fileName);

        await using var bufferMs = new MemoryStream();
        await videoStream.CopyToAsync(bufferMs, cancellationToken);
        var fileBytes = bufferMs.ToArray();

        if (fileBytes.Length > MaxFileBytes)
        {
            return TempClipUploadResult.Fail(
                $"Відео завелике: {fileBytes.Length / (1024 * 1024)} МБ (максимум {MaxFileBytes / (1024 * 1024)} МБ).");
        }

        var uploadUrl = $"{baseUrl}/api/upload";
        var contentType = ResolveContentType(safeName, fileContentType);

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            var boundary = $"----FormBoundary{Guid.NewGuid():N}";

            var multipart = new MultipartFormDataContent(boundary);

            // .NET автоматично додає лапки навколо boundary:
            //   Content-Type: multipart/form-data; boundary="abc123"
            // tmpfile.link (Node.js busboy) їх не розуміє → "Failed to process file upload"
            // Тому прибираємо лапки вручну:
            multipart.Headers.ContentType!.Parameters.Clear();
            multipart.Headers.ContentType.Parameters.Add(
                new NameValueHeaderValue("boundary", boundary));

            var filePart = new ByteArrayContent(fileBytes);
            filePart.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            multipart.Add(filePart, "file");
            // Встановлюємо Content-Disposition вручну — без filename* який .NET додає автоматично
            filePart.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data")
            {
                Name = "\"file\"",
                FileName = $"\"{safeName}\""
            };
            // Читаємо в буфер для debug І для відправки
            var debugBytes = await multipart.ReadAsByteArrayAsync(cancellationToken);
            var debugText = Encoding.UTF8.GetString(debugBytes[..Math.Min(500, debugBytes.Length)]);
            _logger.LogWarning("tmpfile.link DEBUG request body:\n{Body}", debugText);

            // Відправляємо з буфера (не з multipart — він вже прочитаний!)
            using var bodyContent = new ByteArrayContent(debugBytes);
            bodyContent.Headers.TryAddWithoutValidation(
                "Content-Type",
                multipart.Headers.ContentType!.ToString());

            using var request = new HttpRequestMessage(HttpMethod.Post, uploadUrl)
            {
                Content = bodyContent,  // <-- з буфера
                Version = HttpVersion.Version11,
                VersionPolicy = HttpVersionPolicy.RequestVersionExact
            };

            using var response = await _http.SendAsync(
                request,
                HttpCompletionOption.ResponseContentRead,
                cancellationToken);

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var statusCode = (int)response.StatusCode;

            _logger.LogInformation(
                "tmpfile.link: спроба {Attempt}/{Max}, HTTP {Code}, тіло: {Body}",
                attempt, MaxAttempts, statusCode, TruncateForLog(body));

            if (statusCode >= 200 && statusCode < 300)
                return ParseResponse(body);

            if (statusCode is not (500 or 502 or 503 or 504))
            {
                return TempClipUploadResult.Fail(
                    $"Не вдалося завантажити відео (сервер відповів {statusCode}). Спробуйте пізніше.");
            }

            if (attempt < MaxAttempts)
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        }

        return TempClipUploadResult.Fail(
            "Не вдалося завантажити відео (сервер відповів 500). Спробуйте пізніше або інший файл.");
    }

    private static TempClipUploadResult ParseResponse(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            foreach (var prop in new[] { "downloadLink", "downloadLinkEncoded", "url" })
            {
                if (root.TryGetProperty(prop, out var el)
                    && el.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(el.GetString()))
                {
                    return TempClipUploadResult.Ok(el.GetString()!);
                }
            }

            return TempClipUploadResult.Fail("Сервіс не повернув посилання на відео.");
        }
        catch (JsonException)
        {
            return TempClipUploadResult.Fail("Некоректна відповідь сервісу. Спробуйте ще раз.");
        }
    }

    private static string ResolveContentType(string safeFileName, string? fromBrowser)
    {
        if (!string.IsNullOrWhiteSpace(fromBrowser))
        {
            var trimmed = fromBrowser.Trim();
            if (trimmed.StartsWith("video/", StringComparison.OrdinalIgnoreCase))
                return trimmed;
        }

        return Path.GetExtension(safeFileName).ToLowerInvariant() switch
        {
            ".webm" => "video/webm",
            ".mp4" => "video/mp4",
            ".mov" => "video/quicktime",
            ".mkv" => "video/x-matroska",
            ".avi" => "video/x-msvideo",
            _ => "application/octet-stream"
        };
    }

    private static string SanitizeFileName(string? fileName)
    {
        var ext = Path.GetExtension(fileName ?? "").ToLowerInvariant();
        if (string.IsNullOrEmpty(ext) || ext.Length > 12
            || ext is not (".mp4" or ".webm" or ".mov" or ".mkv" or ".avi"))
            ext = ".mp4";

        var baseName = Path.GetFileNameWithoutExtension(fileName ?? "");
        var sb = new StringBuilder();
        foreach (var ch in baseName)
        {
            if (ch is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-' or '_')
                sb.Append(ch);
        }

        var stem = sb.Length > 0 ? sb.ToString() : "video";
        if (stem.Length > 100) stem = stem[..100];

        return stem + ext;
    }

    private static string TruncateForLog(string? s, int max = 300)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Length <= max ? s : s[..max] + "…";
    }
}