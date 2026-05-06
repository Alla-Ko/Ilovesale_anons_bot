using System.Text.Json;
using Announcement.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Announcement.Services;

public interface IImgBbUploadService
{
    Task<string> UploadImageAsync(Stream imageStream, string fileName, CancellationToken cancellationToken = default);
}

public class ImgBbUploadService : IImgBbUploadService
{
    private readonly HttpClient _http;
    private readonly ImgBbOptions _options;
    private readonly ILogger<ImgBbUploadService> _logger;

    public ImgBbUploadService(HttpClient http, IOptions<ImgBbOptions> options, ILogger<ImgBbUploadService> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<string> UploadImageAsync(Stream imageStream, string fileName, CancellationToken cancellationToken = default)
    {
        var keys = _options.ApiKeys
            .Select(k => (k ?? string.Empty).Trim())
            .Where(k => !string.IsNullOrEmpty(k) && !k.StartsWith("YOUR_", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var singleKey = (_options.ApiKey ?? string.Empty).Trim();
        if (!string.IsNullOrEmpty(singleKey) &&
            !singleKey.StartsWith("YOUR_", StringComparison.OrdinalIgnoreCase) &&
            !keys.Contains(singleKey, StringComparer.Ordinal))
        {
            keys.Insert(0, singleKey);
        }

        if (keys.Count == 0)
        {
            _logger.LogWarning("ImgBB API key is not configured. Using placeholder url for file {FileName}", fileName);
            await Task.Delay(50, cancellationToken);
            return $"https://i.ibb.co/placeholder/{Uri.EscapeDataString(fileName)}";
        }

        await using var ms = new MemoryStream();
        await imageStream.CopyToAsync(ms, cancellationToken);
        var base64 = Convert.ToBase64String(ms.ToArray());

        var lastError = string.Empty;
        for (var i = 0; i < keys.Count; i++)
        {
            _logger.LogInformation("ImgBB upload attempt {Attempt}/{MaxAttempts} for file {FileName}", i + 1, keys.Count, fileName);
            using var content = new MultipartFormDataContent();
            content.Add(new StringContent(base64), "image");

            var requestUrl = $"https://api.imgbb.com/1/upload?key={Uri.EscapeDataString(keys[i])}";
            var response = await _http.PostAsync(requestUrl, content, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(responseBody);
                var url = doc.RootElement.GetProperty("data").GetProperty("url").GetString();
                if (string.IsNullOrEmpty(url))
                    throw new InvalidOperationException("ImgBB: порожня відповідь url.");
                _logger.LogInformation("ImgBB upload success for file {FileName}", fileName);
                return url;
            }

            lastError = BuildErrorMessage(response.StatusCode, responseBody);
            _logger.LogWarning(
                "ImgBB upload failed for file {FileName}. Attempt {Attempt}/{MaxAttempts}. Status={StatusCode}. Error={Error}",
                fileName, i + 1, keys.Count, (int)response.StatusCode, lastError);
            if (i < keys.Count - 1 && IsInvalidApiKeyResponse(responseBody))
                continue;

            throw new HttpRequestException(lastError, null, response.StatusCode);
        }

        throw new HttpRequestException(string.IsNullOrEmpty(lastError) ? "ImgBB: запит завершився помилкою." : lastError);
    }

    private static bool IsInvalidApiKeyResponse(string responseBody)
    {
        if (responseBody.Contains("Invalid API v1 key", StringComparison.OrdinalIgnoreCase))
            return true;

        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            if (!doc.RootElement.TryGetProperty("error", out var error))
                return false;

            var hasCode100 = error.TryGetProperty("code", out var codeElement) &&
                             codeElement.ValueKind == JsonValueKind.Number &&
                             codeElement.TryGetInt32(out var code) &&
                             code == 100;

            var hasInvalidMessage = error.TryGetProperty("message", out var messageElement) &&
                                    messageElement.ValueKind == JsonValueKind.String &&
                                    (messageElement.GetString() ?? string.Empty).Contains("Invalid API v1 key", StringComparison.OrdinalIgnoreCase);

            return hasCode100 || hasInvalidMessage;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string BuildErrorMessage(System.Net.HttpStatusCode statusCode, string responseBody)
    {
        var body = string.IsNullOrWhiteSpace(responseBody) ? string.Empty : $" Body: {responseBody}";
        return $"ImgBB upload failed: {(int)statusCode} ({statusCode}).{body}";
    }
}
