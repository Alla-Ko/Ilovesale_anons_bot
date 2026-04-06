using System.Text.Json;
using Announcement.Models;
using Announcement.Options;
using Microsoft.Extensions.Options;

namespace Announcement.Services;

public interface ITelegraphPageService
{
    Task<string> CreatePageAsync(AnnouncementEntity announcement, int variant, CancellationToken cancellationToken = default);
}

public class TelegraphPageService : ITelegraphPageService
{
    private readonly HttpClient _http;
    private readonly TelegraphOptions _options;
    private readonly ICaptionPublishFormatter _captions;

    public TelegraphPageService(
        HttpClient http,
        IOptions<TelegraphOptions> options,
        ICaptionPublishFormatter captions)
    {
        _http = http;
        _options = options.Value;
        _captions = captions;
    }

    public async Task<string> CreatePageAsync(AnnouncementEntity announcement, int variant, CancellationToken cancellationToken = default)
    {
        var token = _options.AccessToken?.Trim();
        if (string.IsNullOrEmpty(token) || token.StartsWith("YOUR_", StringComparison.OrdinalIgnoreCase))
        {
            await Task.Delay(50, cancellationToken);
            return $"https://telegra.ph/Stub-{announcement.Id}-v{variant}-{DateTime.UtcNow:yyyyMMddHHmmss}";
        }

        var ordered = announcement.Collages.OrderBy(c => c.SortOrder).ToList();
        var nodes = new List<object>();

        foreach (var c in ordered)
        {
            if (c.MediaType == MediaType.Photo)
                nodes.Add(new { tag = "img", attrs = new { src = c.MediaUrl } });
            else
                nodes.Add(new { tag = "video", attrs = new { src = c.MediaUrl } });

            var caption = variant == 1 ? c.Caption1 : c.Caption2;
            foreach (var paragraph in _captions.BuildTelegraphCaptionParagraphs(caption))
                nodes.Add(paragraph);
        }

        var contentJson = JsonSerializer.Serialize(nodes);
        var body = new Dictionary<string, string>
        {
            ["access_token"] = token,
            ["title"] = announcement.Title,
            ["content"] = contentJson,
            ["return_content"] = "false"
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.telegra.ph/createPage")
        {
            Content = new FormUrlEncodedContent(body)
        };

        var response = await _http.SendAsync(req, cancellationToken);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.GetProperty("ok").GetBoolean())
            throw new InvalidOperationException($"Telegraph: {json}");

        return doc.RootElement.GetProperty("result").GetProperty("url").GetString()
               ?? throw new InvalidOperationException("Telegraph: немає url у відповіді.");
    }
}
