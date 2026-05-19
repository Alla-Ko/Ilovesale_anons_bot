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

        try
        {
            return await CreateSinglePageAsync(
                token,
                announcement.Title,
                BuildNodes(ordered, variant),
                cancellationToken);
        }
        catch (InvalidOperationException ex) when (IsContentTooBig(ex))
        {
            if (ordered.Count < 2)
                throw;

            try
            {
                return await CreatePagesFromPartsAsync(
                    token,
                    announcement.Title,
                    SplitIntoParts(ordered, 2),
                    variant,
                    cancellationToken);
            }
            catch (InvalidOperationException ex2) when (IsContentTooBig(ex2))
            {
                if (ordered.Count < 3)
                    throw;

                return await CreatePagesFromPartsAsync(
                    token,
                    announcement.Title,
                    SplitIntoParts(ordered, 3),
                    variant,
                    cancellationToken);
            }
        }
    }

    private static bool IsContentTooBig(InvalidOperationException ex) =>
        ex.Message.Contains("CONTENT_TOO_BIG", StringComparison.OrdinalIgnoreCase);

    /// <summary>Розбиває колажі на послідовні частини без дублювання.</summary>
    private static List<List<CollageEntity>> SplitIntoParts(IReadOnlyList<CollageEntity> ordered, int partCount)
    {
        var n = ordered.Count;
        var result = new List<List<CollageEntity>>(partCount);
        var baseSize = n / partCount;
        var remainder = n % partCount;
        var index = 0;

        for (var p = 0; p < partCount; p++)
        {
            var size = baseSize + (p < remainder ? 1 : 0);
            if (size <= 0)
                continue;

            var chunk = new List<CollageEntity>(size);
            for (var i = 0; i < size; i++)
                chunk.Add(ordered[index++]);
            result.Add(chunk);
        }

        return result;
    }

    private async Task<string> CreatePagesFromPartsAsync(
        string token,
        string baseTitle,
        List<List<CollageEntity>> parts,
        int variant,
        CancellationToken cancellationToken)
    {
        var nonEmpty = parts.Where(p => p.Count > 0).ToList();
        if (nonEmpty.Count == 0)
            throw new InvalidOperationException("Telegraph: немає колажів для публікації.");

        var urls = new List<string>(nonEmpty.Count);
        for (var i = 0; i < nonEmpty.Count; i++)
        {
            var title = nonEmpty.Count == 1
                ? baseTitle
                : $"{baseTitle} ({i + 1}/{nonEmpty.Count})";

            var url = await CreateSinglePageAsync(
                token,
                title,
                BuildNodes(nonEmpty[i], variant),
                cancellationToken);
            urls.Add(url);
        }

        return string.Join(";", urls);
    }

    private List<object> BuildNodes(List<CollageEntity> collages, int variant)
    {
        var nodes = new List<object>();
        foreach (var c in collages)
        {
            if (c.MediaType == MediaType.Photo)
                nodes.Add(new { tag = "img", attrs = new { src = c.MediaUrl } });
            else
                nodes.Add(new { tag = "video", attrs = new { src = c.MediaUrl } });

            var caption = variant == 1 ? c.Caption1 : c.Caption2;
            foreach (var paragraph in _captions.BuildTelegraphCaptionParagraphs(caption))
                nodes.Add(paragraph);
        }

        return nodes;
    }

    private async Task<string> CreateSinglePageAsync(
        string token,
        string title,
        List<object> nodes,
        CancellationToken cancellationToken)
    {
        var contentJson = JsonSerializer.Serialize(nodes);
        var body = new Dictionary<string, string>
        {
            ["access_token"] = token,
            ["title"] = title,
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
