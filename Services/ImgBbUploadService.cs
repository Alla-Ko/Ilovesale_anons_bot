using System.Text.Json;
using Announcement.Options;
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

    public ImgBbUploadService(HttpClient http, IOptions<ImgBbOptions> options)
    {
        _http = http;
        _options = options.Value;
    }

    public async Task<string> UploadImageAsync(Stream imageStream, string fileName, CancellationToken cancellationToken = default)
    {
        var key = _options.ApiKey?.Trim();
        if (string.IsNullOrEmpty(key) || key.StartsWith("YOUR_", StringComparison.OrdinalIgnoreCase))
        {
            await Task.Delay(50, cancellationToken);
            return $"https://i.ibb.co/placeholder/{Uri.EscapeDataString(fileName)}";
        }

        await using var ms = new MemoryStream();
        await imageStream.CopyToAsync(ms, cancellationToken);
        var base64 = Convert.ToBase64String(ms.ToArray());

        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(key), "key");
        content.Add(new StringContent(base64), "image");

        var response = await _http.PostAsync("https://api.imgbb.com/1/upload", content, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var doc = await JsonDocument.ParseAsync(responseStream, cancellationToken: cancellationToken);
        var url = doc.RootElement.GetProperty("data").GetProperty("url").GetString();
        if (string.IsNullOrEmpty(url))
            throw new InvalidOperationException("ImgBB: порожня відповідь url.");
        return url;
    }
}
