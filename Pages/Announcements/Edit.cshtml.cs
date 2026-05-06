using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Announcement.Data;
using Announcement.Models;
using Announcement.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;

namespace Announcement.Pages.Announcements;

public class EditModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IMediaProcessingService _media;
    private readonly IImgBbUploadService _imgBb;
    private readonly ITempClipUploadService _tempClip;
    private readonly ILogger<EditModel> _logger;

    public EditModel(
        ApplicationDbContext db,
        IMediaProcessingService media,
        IImgBbUploadService imgBb,
        ITempClipUploadService tempClip,
        ILogger<EditModel> logger)
    {
        _db = db;
        _media = media;
        _imgBb = imgBb;
        _tempClip = tempClip;
        _logger = logger;
    }

    [BindProperty]
    public AnnouncementForm Input { get; set; } = new();

    public List<SelectListItem> CountryOptions { get; set; } = new();

    public class AnnouncementForm
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Вкажіть назву")]
        public string Title { get; set; } = string.Empty;

        [Required]
        public Country Country { get; set; }

        public List<CollageRowInput> Collages { get; set; } = new();
    }

    public class CollageRowInput
    {
        public int? Id { get; set; }

        public string? Caption1 { get; set; }

        public string? Caption2 { get; set; }

        public string? KeepMediaUrl { get; set; }

        public IFormFile? MediaFile { get; set; }
    }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        CountryOptions = CountryLabels.Labels.Select(kv => new SelectListItem(kv.Value, ((int)kv.Key).ToString())).ToList();

        var entity = await _db.Announcements.Include(a => a.Collages).FirstOrDefaultAsync(a => a.Id == id);
        if (entity == null)
            return NotFound();

        if (!await CanEditAsync(entity))
            return Forbid();

        Input = new AnnouncementForm
        {
            Id = entity.Id,
            Title = entity.Title,
            Country = entity.Country,
            Collages = entity.Collages.OrderBy(c => c.SortOrder).Select(c => new CollageRowInput
            {
                Id = c.Id,
                Caption1 = c.Caption1,
                Caption2 = c.Caption2,
                KeepMediaUrl = c.MediaUrl
            }).ToList()
        };

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        _logger.LogInformation("Announcement edit started. AnnouncementId={AnnouncementId}", id);
        CountryOptions = CountryLabels.Labels.Select(kv => new SelectListItem(kv.Value, ((int)kv.Key).ToString())).ToList();

        if (Input.Id != id)
            return BadRequest();

        var entity = await _db.Announcements.Include(a => a.Collages).FirstOrDefaultAsync(a => a.Id == id);
        if (entity == null)
            return NotFound();

        if (!await CanEditAsync(entity))
            return Forbid();

        if (!ModelState.IsValid)
            return Page();

        entity.Title = Input.Title.Trim();
        entity.Country = Input.Country;
        entity.TelegraphUrl1 = null;
        entity.TelegraphUrl2 = null;

        var rows = Input.Collages ?? new List<CollageRowInput>();
        var order = 0;
        var submittedExisting = new HashSet<int>();

        foreach (var row in rows)
        {
            order++;
            var hasFile = row.MediaFile != null && row.MediaFile.Length > 0;
            var hasKeep = !string.IsNullOrWhiteSpace(row.KeepMediaUrl);
            _logger.LogInformation(
                "Processing collage row. AnnouncementId={AnnouncementId}, Row={Row}, HasFile={HasFile}, HasKeepUrl={HasKeepUrl}, ExistingCollageId={CollageId}",
                id, order, hasFile, hasKeep, row.Id);

            if (!hasFile && !hasKeep)
            {
                ModelState.AddModelError(string.Empty, $"Колаж #{order}: додайте фото або відео.");
                return Page();
            }

            string mediaUrl;
            MediaType mediaType;

            if (hasFile)
            {
                mediaType = _media.DetectMediaType(row.MediaFile!.FileName, row.MediaFile.ContentType);
                try
                {
                    await using var stream = row.MediaFile.OpenReadStream();
                    var prepared = await _media.PrepareForUploadAsync(
                        stream,
                        row.MediaFile.FileName,
                        mediaType,
                        row.MediaFile.ContentType);

                    await using (prepared.Stream)
                    {
                        if (mediaType == MediaType.Photo)
                        {
                            _logger.LogInformation("Uploading photo to ImgBB. AnnouncementId={AnnouncementId}, Row={Row}, FileName={FileName}", id, order, prepared.FileName);
                            mediaUrl = await _imgBb.UploadImageAsync(prepared.Stream, prepared.FileName);
                        }
                        else
                        {
                            _logger.LogInformation("Uploading video to temp clip service. AnnouncementId={AnnouncementId}, Row={Row}, FileName={FileName}", id, order, prepared.FileName);
                            var videoResult = await _tempClip.UploadVideoAsync(prepared.Stream, prepared.FileName, prepared.ContentType);
                            if (!videoResult.Success)
                            {
                                _logger.LogWarning(
                                    "Video upload failed. AnnouncementId={AnnouncementId}, Row={Row}, Error={Error}",
                                    id, order, videoResult.ErrorMessage);
                                ModelState.AddModelError(string.Empty, videoResult.ErrorMessage ?? "Не вдалося завантажити відео.");
                                return Page();
                            }

                            mediaUrl = videoResult.DownloadUrl!;
                        }
                    }
                }
                catch (ImageFormatException)
                {
                    _logger.LogWarning("Invalid image format. AnnouncementId={AnnouncementId}, Row={Row}", id, order);
                    ModelState.AddModelError(string.Empty, $"Колаж #{order}: файл зображення пошкоджений або має некоректний формат.");
                    return Page();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Upload failed. AnnouncementId={AnnouncementId}, Row={Row}", id, order);
                    ModelState.AddModelError(string.Empty, $"Колаж #{order}: не вдалося завантажити файл ({ex.Message}).");
                    return Page();
                }
            }
            else
            {
                mediaUrl = row.KeepMediaUrl!.Trim();
                if (row.Id.HasValue && row.Id.Value > 0)
                {
                    var rid = row.Id.Value;
                    var existing = entity.Collages.FirstOrDefault(c => c.Id == rid);
                    mediaType = existing?.MediaType ?? _guessTypeFromUrl(mediaUrl);
                }
                else
                    mediaType = _guessTypeFromUrl(mediaUrl);
            }

            var cap1 = string.IsNullOrWhiteSpace(row.Caption1) ? null : row.Caption1;
            var cap2 = string.IsNullOrWhiteSpace(row.Caption2) ? null : row.Caption2;

            if (row.Id.HasValue && row.Id.Value > 0)
            {
                var cid = row.Id.Value;
                var c = entity.Collages.FirstOrDefault(x => x.Id == cid);
                if (c != null)
                {
                    c.SortOrder = order - 1;
                    c.MediaUrl = mediaUrl;
                    c.MediaType = mediaType;
                    c.Caption1 = cap1;
                    c.Caption2 = cap2;
                    submittedExisting.Add(cid);
                }
            }
            else
            {
                entity.Collages.Add(new CollageEntity
                {
                    SortOrder = order - 1,
                    MediaUrl = mediaUrl,
                    MediaType = mediaType,
                    Caption1 = cap1,
                    Caption2 = cap2
                });
            }
        }

        // Лише раніше збережені колажі (Id > 0). Нові щойно додані мають Id = 0 до SaveChanges — їх не можна Remove.
        var toRemove = entity.Collages.Where(c => c.Id > 0 && !submittedExisting.Contains(c.Id)).ToList();
        foreach (var c in toRemove)
            _db.Collages.Remove(c);

        var editorId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!string.IsNullOrEmpty(editorId))
            entity.LastUpdatedById = editorId;

        _logger.LogInformation(
            "Saving announcement changes. AnnouncementId={AnnouncementId}, RowsSubmitted={Rows}, ExistingToRemove={ToRemoveCount}",
            id, rows.Count, toRemove.Count);
        await _db.SaveChangesAsync();
        _logger.LogInformation("Announcement edit saved successfully. AnnouncementId={AnnouncementId}", id);
        return RedirectToPage("./Index");
    }

    public async Task<IActionResult> OnPostUpdateAnnouncementAsync(int id, [FromForm] string title, [FromForm] Country country)
    {
        var entity = await _db.Announcements.FirstOrDefaultAsync(a => a.Id == id);
        if (entity == null)
            return NotFound();
        if (!await CanEditAsync(entity))
            return Forbid();

        var safeTitle = (title ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(safeTitle))
            return new JsonResult(new { success = false, error = "Вкажіть назву анонса." });

        entity.Title = safeTitle;
        entity.Country = country;
        entity.TelegraphUrl1 = null;
        entity.TelegraphUrl2 = null;
        UpdateLastEditor(entity);
        await _db.SaveChangesAsync();

        return new JsonResult(new { success = true });
    }

    public async Task<IActionResult> OnPostAddCollageAsync(
        int id,
        IFormFile? mediaFile,
        [FromForm] string? caption1,
        [FromForm] string? caption2,
        [FromForm] int? sortOrder)
    {
        var entity = await _db.Announcements.Include(a => a.Collages).FirstOrDefaultAsync(a => a.Id == id);
        if (entity == null)
            return NotFound();
        if (!await CanEditAsync(entity))
            return Forbid();
        if (mediaFile == null || mediaFile.Length <= 0)
            return new JsonResult(new { success = false, error = "Додайте файл для завантаження." });

        try
        {
            var mediaType = _media.DetectMediaType(mediaFile.FileName, mediaFile.ContentType);
            await using var stream = mediaFile.OpenReadStream();
            var prepared = await _media.PrepareForUploadAsync(
                stream,
                mediaFile.FileName,
                mediaType,
                mediaFile.ContentType);

            string mediaUrl;
            await using (prepared.Stream)
            {
                if (mediaType == MediaType.Photo)
                {
                    mediaUrl = await _imgBb.UploadImageAsync(prepared.Stream, prepared.FileName);
                }
                else
                {
                    var videoResult = await _tempClip.UploadVideoAsync(prepared.Stream, prepared.FileName, prepared.ContentType);
                    if (!videoResult.Success || string.IsNullOrWhiteSpace(videoResult.DownloadUrl))
                        return new JsonResult(new { success = false, error = videoResult.ErrorMessage ?? "Не вдалося завантажити відео." });
                    mediaUrl = videoResult.DownloadUrl;
                }
            }

            var order = sortOrder.GetValueOrDefault();
            if (order < 0)
                order = 0;

            var collage = new CollageEntity
            {
                AnnouncementId = entity.Id,
                SortOrder = order,
                MediaUrl = mediaUrl,
                MediaType = mediaType,
                Caption1 = string.IsNullOrWhiteSpace(caption1) ? null : caption1,
                Caption2 = string.IsNullOrWhiteSpace(caption2) ? null : caption2
            };
            _db.Collages.Add(collage);

            entity.TelegraphUrl1 = null;
            entity.TelegraphUrl2 = null;
            UpdateLastEditor(entity);
            await _db.SaveChangesAsync();

            return new JsonResult(new
            {
                success = true,
                collage = new
                {
                    id = collage.Id,
                    keep = collage.MediaUrl,
                    mediaType = collage.MediaType.ToString()
                }
            });
        }
        catch (ImageFormatException)
        {
            return new JsonResult(new { success = false, error = "Файл зображення пошкоджений або має некоректний формат." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add collage. AnnouncementId={AnnouncementId}", id);
            return new JsonResult(new { success = false, error = $"Не вдалося завантажити файл ({ex.Message})." });
        }
    }

    public async Task<IActionResult> OnPostUpdateCollageAsync(
        int id,
        int collageId,
        [FromForm] string? caption1,
        [FromForm] string? caption2,
        [FromForm] int? sortOrder)
    {
        var entity = await _db.Announcements.FirstOrDefaultAsync(a => a.Id == id);
        if (entity == null)
            return NotFound();
        if (!await CanEditAsync(entity))
            return Forbid();

        var collage = await _db.Collages.FirstOrDefaultAsync(c => c.Id == collageId && c.AnnouncementId == id);
        if (collage == null)
            return NotFound();

        collage.Caption1 = string.IsNullOrWhiteSpace(caption1) ? null : caption1;
        collage.Caption2 = string.IsNullOrWhiteSpace(caption2) ? null : caption2;
        if (sortOrder.HasValue && sortOrder.Value >= 0)
            collage.SortOrder = sortOrder.Value;

        entity.TelegraphUrl1 = null;
        entity.TelegraphUrl2 = null;
        UpdateLastEditor(entity);
        await _db.SaveChangesAsync();

        return new JsonResult(new { success = true });
    }

    public async Task<IActionResult> OnPostDeleteCollageAsync(int id, int collageId)
    {
        var entity = await _db.Announcements.FirstOrDefaultAsync(a => a.Id == id);
        if (entity == null)
            return NotFound();
        if (!await CanEditAsync(entity))
            return Forbid();

        var collage = await _db.Collages.FirstOrDefaultAsync(c => c.Id == collageId && c.AnnouncementId == id);
        if (collage == null)
            return new JsonResult(new { success = true });

        _db.Collages.Remove(collage);
        entity.TelegraphUrl1 = null;
        entity.TelegraphUrl2 = null;
        UpdateLastEditor(entity);
        await _db.SaveChangesAsync();

        return new JsonResult(new { success = true });
    }

    public async Task<IActionResult> OnPostReorderCollagesAsync(int id, [FromBody] ReorderRequest? request)
    {
        var entity = await _db.Announcements.FirstOrDefaultAsync(a => a.Id == id);
        if (entity == null)
            return NotFound();
        if (!await CanEditAsync(entity))
            return Forbid();
        if (request == null || request.Items.Count == 0)
            return new JsonResult(new { success = true });

        var itemIds = request.Items.Select(i => i.Id).Distinct().ToList();
        var collages = await _db.Collages
            .Where(c => c.AnnouncementId == id && itemIds.Contains(c.Id))
            .ToListAsync();

        foreach (var item in request.Items)
        {
            if (item.SortOrder < 0)
                continue;
            var collage = collages.FirstOrDefault(c => c.Id == item.Id);
            if (collage != null)
                collage.SortOrder = item.SortOrder;
        }

        entity.TelegraphUrl1 = null;
        entity.TelegraphUrl2 = null;
        UpdateLastEditor(entity);
        await _db.SaveChangesAsync();

        return new JsonResult(new { success = true });
    }

    public class ReorderRequest
    {
        public List<ReorderItem> Items { get; set; } = new();
    }

    public class ReorderItem
    {
        public int Id { get; set; }
        public int SortOrder { get; set; }
    }

    private async Task<bool> CanEditAsync(AnnouncementEntity entity)
    {
        var uid = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(uid))
            return false;
        if (User.IsInRole(Authorization.AppRoles.Admin) || User.IsInRole(Authorization.AppRoles.Moderator))
            return true;
        return entity.CreatorId == uid;
    }

    private static MediaType _guessTypeFromUrl(string url)
    {
        var u = url.ToLowerInvariant();
        if (u.EndsWith(".mp4") || u.EndsWith(".webm") || u.Contains("video"))
            return MediaType.Video;
        return MediaType.Photo;
    }

    private void UpdateLastEditor(AnnouncementEntity entity)
    {
        var editorId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!string.IsNullOrEmpty(editorId))
            entity.LastUpdatedById = editorId;
    }
}
