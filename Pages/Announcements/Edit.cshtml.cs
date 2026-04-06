using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Announcement.Data;
using Announcement.Models;
using Announcement.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace Announcement.Pages.Announcements;

public class EditModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IMediaProcessingService _media;
    private readonly IImgBbUploadService _imgBb;
    private readonly ITempClipUploadService _tempClip;

    public EditModel(
        ApplicationDbContext db,
        IMediaProcessingService media,
        IImgBbUploadService imgBb,
        ITempClipUploadService tempClip)
    {
        _db = db;
        _media = media;
        _imgBb = imgBb;
        _tempClip = tempClip;
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
                await using var stream = row.MediaFile.OpenReadStream();
                var prepared = await _media.PrepareForUploadAsync(stream, row.MediaFile.FileName, mediaType, row.MediaFile.ContentType);
                await using (prepared.Stream)
                {
                    if (mediaType == MediaType.Photo)
                    {
                        mediaUrl = await _imgBb.UploadImageAsync(prepared.Stream, prepared.FileName);
                    }
                    else
                    {
                        var videoResult = await _tempClip.UploadVideoAsync(prepared.Stream, prepared.FileName, prepared.ContentType);
                        if (!videoResult.Success)
                        {
                            ModelState.AddModelError(string.Empty, videoResult.ErrorMessage ?? "Не вдалося завантажити відео.");
                            return Page();
                        }

                        mediaUrl = videoResult.DownloadUrl!;
                    }
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

        await _db.SaveChangesAsync();
        return RedirectToPage("./Index");
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
}
