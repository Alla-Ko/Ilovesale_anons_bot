using System.Globalization;
using System.Security.Claims;
using Announcement.Authorization;
using Announcement.Data;
using Announcement.Models;
using Announcement.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Announcement.Pages.Announcements;

public class IndexModel : PageModel
{
    private static readonly CultureInfo UkCulture = CultureInfo.GetCultureInfo("uk-UA");

    private readonly ApplicationDbContext _db;
    private readonly ITelegraphPageService _telegraph;
    private readonly ITelegramChannelService _telegram;

    public IndexModel(
        ApplicationDbContext db,
        ITelegraphPageService telegraph,
        ITelegramChannelService telegram)
    {
        _db = db;
        _telegraph = telegraph;
        _telegram = telegram;
    }

    [BindProperty(SupportsGet = true)]
    public string? Sort { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Dir { get; set; }

    /// <summary>Фільтр за календарним днем створення (UTC, без часу).</summary>
    [BindProperty(SupportsGet = true)]
    public DateOnly? CreatedDay { get; set; }

    public IList<Row> Items { get; set; } = new List<Row>();

    /// <summary>Календарні дні (UTC), за які є хоча б один видимий анонс — від новіших до старіших.</summary>
    public IReadOnlyList<DateOnly> AvailableDays { get; private set; } = Array.Empty<DateOnly>();

    public bool IsStaff { get; set; }

    public string FormatDayLabel(DateOnly day) =>
        day.ToDateTime(TimeOnly.MinValue).ToString("d MMMM", UkCulture);

    public class Row
    {
        public int Id { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public DateTime UpdatedAtUtc { get; set; }
        public string Title { get; set; } = string.Empty;
        public Country Country { get; set; }
        public int CollageCount { get; set; }
        public string CreatorLogin { get; set; } = "—";
        public string LastUpdatedLogin { get; set; } = "—";
        public string? TelegraphUrl1 { get; set; }
        public string? TelegraphUrl2 { get; set; }
        public bool CanEdit { get; set; }
    }

    public string SortUrl(string column)
    {
        Sort ??= "updated";
        Dir ??= "desc";
        var nextDir = string.Equals(Sort, column, StringComparison.OrdinalIgnoreCase)
            && string.Equals(Dir, "asc", StringComparison.OrdinalIgnoreCase)
            ? "desc"
            : "asc";
        return Url.Page("/Announcements/Index", new { sort = column, dir = nextDir, createdDay = CreatedDay })!;
    }

    public string HeaderIndicator(string column)
    {
        Sort ??= "updated";
        Dir ??= "desc";
        if (!string.Equals(Sort, column, StringComparison.OrdinalIgnoreCase))
            return string.Empty;
        return string.Equals(Dir, "asc", StringComparison.OrdinalIgnoreCase) ? " ▲" : " ▼";
    }

    public async Task OnGetAsync()
    {
        Sort ??= "updated";
        Dir ??= "desc";

        IsStaff = User.IsInRole(AppRoles.Admin) || User.IsInRole(AppRoles.Moderator);
        var uid = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        var daysScope = _db.Announcements.AsNoTracking();
        if (!IsStaff)
            daysScope = daysScope.Where(a => a.CreatorId == uid);
        var distinctUtcDates = await daysScope
            .Select(a => a.CreatedAtUtc.Date)
            .Distinct()
            .OrderByDescending(d => d)
            .ToListAsync();
        AvailableDays = distinctUtcDates.Select(DateOnly.FromDateTime).ToList();

        var query = _db.Announcements.AsNoTracking()
            .Include(a => a.Collages)
            .Include(a => a.Creator)
            .Include(a => a.LastUpdatedBy)
            .AsQueryable();
        if (!IsStaff)
            query = query.Where(a => a.CreatorId == uid);

        if (CreatedDay.HasValue)
        {
            var d = CreatedDay.Value;
            var startUtc = new DateTime(d.Year, d.Month, d.Day, 0, 0, 0, DateTimeKind.Utc);
            var endUtc = startUtc.AddDays(1);
            query = query.Where(a => a.CreatedAtUtc >= startUtc && a.CreatedAtUtc < endUtc);
        }

        var sort = Sort.ToLowerInvariant();
        var dir = Dir.ToLowerInvariant();
        var desc = dir == "desc";

        query = sort switch
        {
            "title" => desc ? query.OrderByDescending(a => a.Title) : query.OrderBy(a => a.Title),
            "country" => desc ? query.OrderByDescending(a => a.Country) : query.OrderBy(a => a.Country),
            "created" => desc ? query.OrderByDescending(a => a.CreatedAtUtc) : query.OrderBy(a => a.CreatedAtUtc),
            "updated" => desc ? query.OrderByDescending(a => a.UpdatedAtUtc) : query.OrderBy(a => a.UpdatedAtUtc),
            "collages" => desc
                ? query.OrderByDescending(a => a.Collages.Count)
                : query.OrderBy(a => a.Collages.Count),
            _ => desc ? query.OrderByDescending(a => a.UpdatedAtUtc) : query.OrderBy(a => a.UpdatedAtUtc)
        };

        var list = await query.ToListAsync();
        foreach (var a in list)
        {
            Items.Add(new Row
            {
                Id = a.Id,
                CreatedAtUtc = a.CreatedAtUtc,
                UpdatedAtUtc = a.UpdatedAtUtc,
                Title = a.Title,
                Country = a.Country,
                CollageCount = a.Collages.Count,
                CreatorLogin = a.Creator?.UserName ?? "—",
                LastUpdatedLogin = a.LastUpdatedBy?.UserName ?? a.Creator?.UserName ?? "—",
                TelegraphUrl1 = a.TelegraphUrl1,
                TelegraphUrl2 = a.TelegraphUrl2,
                CanEdit = IsStaff || a.CreatorId == uid
            });
        }
    }

    private async Task<AnnouncementEntity?> LoadAuthorizedAsync(int id)
    {
        var entity = await _db.Announcements.Include(a => a.Collages).FirstOrDefaultAsync(a => a.Id == id);
        if (entity == null)
            return null;

        var uid = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var isStaff = User.IsInRole(AppRoles.Admin) || User.IsInRole(AppRoles.Moderator);
        if (!isStaff && entity.CreatorId != uid)
            return null;

        return entity;
    }

    private IActionResult RedirectToIndex()
    {
        Sort ??= "updated";
        Dir ??= "desc";
        return RedirectToPage("./Index", new { sort = Sort, dir = Dir, createdDay = CreatedDay });
    }

    public async Task<IActionResult> OnPostTelegraph1Async(int id)
    {
        if (!User.IsInRole(AppRoles.Admin) && !User.IsInRole(AppRoles.Moderator))
            return Forbid();

        var entity = await LoadAuthorizedAsync(id);
        if (entity == null)
            return NotFound();
        if (entity.Collages.Count == 0)
            return BadRequest();

        var url = await _telegraph.CreatePageAsync(entity, 1);
        entity.TelegraphUrl1 = url;
        var staffId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!string.IsNullOrEmpty(staffId))
            entity.LastUpdatedById = staffId;
        await _db.SaveChangesAsync();
        return RedirectToIndex();
    }

    public async Task<IActionResult> OnPostTelegraph2Async(int id)
    {
        if (!User.IsInRole(AppRoles.Admin) && !User.IsInRole(AppRoles.Moderator))
            return Forbid();

        var entity = await LoadAuthorizedAsync(id);
        if (entity == null)
            return NotFound();
        if (entity.Collages.Count == 0)
            return BadRequest();

        var url = await _telegraph.CreatePageAsync(entity, 2);
        entity.TelegraphUrl2 = url;
        var staffId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!string.IsNullOrEmpty(staffId))
            entity.LastUpdatedById = staffId;
        await _db.SaveChangesAsync();
        return RedirectToIndex();
    }

    public async Task<IActionResult> OnPostSendTelegram1Async(int id)
    {
        if (!User.IsInRole(AppRoles.Admin) && !User.IsInRole(AppRoles.Moderator))
            return Forbid();

        var entity = await LoadAuthorizedAsync(id);
        if (entity == null)
            return NotFound();
        if (entity.Collages.Count == 0)
            return BadRequest();

        await _telegram.SendAnnouncementVariantAsync(entity, 1);
        return RedirectToIndex();
    }

    public async Task<IActionResult> OnPostSendTelegram2Async(int id)
    {
        if (!User.IsInRole(AppRoles.Admin) && !User.IsInRole(AppRoles.Moderator))
            return Forbid();

        var entity = await LoadAuthorizedAsync(id);
        if (entity == null)
            return NotFound();
        if (entity.Collages.Count == 0)
            return BadRequest();

        await _telegram.SendAnnouncementVariantAsync(entity, 2);
        return RedirectToIndex();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var entity = await _db.Announcements.Include(a => a.Collages).FirstOrDefaultAsync(a => a.Id == id);
        if (entity == null)
            return NotFound();

        var uid = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var isStaff = User.IsInRole(AppRoles.Admin) || User.IsInRole(AppRoles.Moderator);
        if (!isStaff && entity.CreatorId != uid)
            return Forbid();

        _db.Announcements.Remove(entity);
        await _db.SaveChangesAsync();
        return RedirectToIndex();
    }
}
