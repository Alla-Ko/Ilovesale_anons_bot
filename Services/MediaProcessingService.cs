using Announcement.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace Announcement.Services;

public interface IMediaProcessingService
{
    MediaType DetectMediaType(string fileName, string? contentType);

    /// <summary>
    /// Для фото: за потреби стискає JPEG. Для відео повертає оригінальний потік без змін.
    /// </summary>
    Task<(Stream Stream, string FileName, string ContentType)> PrepareForUploadAsync(
        Stream input,
        string originalFileName,
        MediaType mediaType,
        string? clientContentType = null,
        CancellationToken cancellationToken = default);
}

public class MediaProcessingService : IMediaProcessingService
{
    private const int MaxDimension = 1920;
    private const int JpegQuality = 82;

    public MediaType DetectMediaType(string fileName, string? contentType)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        if (contentType?.StartsWith("video/", StringComparison.OrdinalIgnoreCase) == true)
            return MediaType.Video;
        if (contentType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true)
            return MediaType.Photo;

        return ext switch
        {
            ".mp4" or ".webm" or ".mov" or ".mkv" or ".avi" => MediaType.Video,
            _ => MediaType.Photo
        };
    }

    public async Task<(Stream Stream, string FileName, string ContentType)> PrepareForUploadAsync(
        Stream input,
        string originalFileName,
        MediaType mediaType,
        string? clientContentType = null,
        CancellationToken cancellationToken = default)
    {
        if (mediaType == MediaType.Video)
        {
            var ms = new MemoryStream();
            await input.CopyToAsync(ms, cancellationToken);
            ms.Position = 0;
            var ct = string.IsNullOrWhiteSpace(clientContentType)
                ? "application/octet-stream"
                : clientContentType.Trim();
            return (ms, originalFileName, ct);
        }

        await using var memory = new MemoryStream();
        await input.CopyToAsync(memory, cancellationToken);
        memory.Position = 0;

        using var image = await Image.LoadAsync(memory, cancellationToken);

        if (image.Width > MaxDimension || image.Height > MaxDimension)
        {
            var ratio = Math.Min((double)MaxDimension / image.Width, (double)MaxDimension / image.Height);
            var w = Math.Max(1, (int)(image.Width * ratio));
            var h = Math.Max(1, (int)(image.Height * ratio));
            image.Mutate(x => x.Resize(w, h));
        }

        var outMs = new MemoryStream();
        await image.SaveAsJpegAsync(outMs, new JpegEncoder { Quality = JpegQuality }, cancellationToken);
        outMs.Position = 0;

        var name = Path.ChangeExtension(Path.GetFileNameWithoutExtension(originalFileName), ".jpg");
        return (outMs, string.IsNullOrEmpty(name) ? "image.jpg" : name, "image/jpeg");
    }
}
