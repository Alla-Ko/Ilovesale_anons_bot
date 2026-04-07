using System.Globalization;
using System.Security.Claims;
using Announcement.Authorization;
using Announcement.Data;
using Announcement.Models;
using Announcement.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Announcement.Pages.Announcements;

public class IndexModel : PageModel
{
    private static readonly CultureInfo UkCulture = CultureInfo.GetCultureInfo("uk-UA");

    private readonly ApplicationDbContext _db;
    private readonly ITelegraphPageService _telegraph;
    private readonly ITelegramChannelService _telegram;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<IndexModel> _logger;

    public IndexModel(
        ApplicationDbContext db,
        ITelegraphPageService telegraph,
        ITelegramChannelService telegram,
        IHttpClientFactory httpClientFactory,
        ILogger<IndexModel> logger)
    {
        _db = db;
        _telegraph = telegraph;
        _telegram = telegram;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    [BindProperty(SupportsGet = true)]
    public string? Sort { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Dir { get; set; }

    /// <summary>Фільтр за календарним днем створення (UTC, без часу).</summary>
    [BindProperty(SupportsGet = true)]
    public DateOnly? CreatedDay { get; set; }
    
    [TempData]
    public string? ErrorMessage { get; set; }

    public IList<Row> Items { get; set; } = new List<Row>();

    /// <summary>Календарні дні (UTC), за які є хоча б один видимий анонс — від новіших до старіших.</summary>
    public IReadOnlyList<DateOnly> AvailableDays { get; private set; } = Array.Empty<DateOnly>();

    public bool IsStaff { get; set; }
    public MonoRatesView? MonoRates { get; private set; }
    public PrivatRatesView? PrivatRates { get; private set; }

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

    public class MonoRatesView
    {
        public decimal? UsdBuy { get; set; }
        public decimal? UsdSell { get; set; }
        public decimal? EurBuy { get; set; }
        public decimal? EurSell { get; set; }
        public decimal? PlnBuy { get; set; }
        public decimal? PlnSell { get; set; }
        public decimal? GbpBuy { get; set; }
        public decimal? GbpSell { get; set; }
    }

    public class PrivatRatesView
    {
        public decimal? UsdBuy { get; set; }
        public decimal? UsdSell { get; set; }
        public decimal? EurBuy { get; set; }
        public decimal? EurSell { get; set; }
    }

    private sealed class MonoRateItem
    {
        public int CurrencyCodeA { get; set; }
        public int CurrencyCodeB { get; set; }
        public decimal? RateBuy { get; set; }
        public decimal? RateSell { get; set; }
        public decimal? RateCross { get; set; }
    }

    private sealed class PrivatRateItem
    {
        public string? Ccy { get; set; }
        public string? Buy { get; set; }
        public string? Sale { get; set; }
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
        var monoRatesTask = LoadMonoRatesAsync();
        var privatRatesTask = LoadPrivatRatesAsync();

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

        MonoRates = await monoRatesTask;
        PrivatRates = await privatRatesTask;
    }

    private async Task<MonoRatesView?> LoadMonoRatesAsync()
    {
        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(4);
            using var resp = await client.GetAsync("https://api.monobank.ua/bank/currency");
            if (!resp.IsSuccessStatusCode)
                return null;

            await using var stream = await resp.Content.ReadAsStreamAsync();
            var data = await JsonSerializer.DeserializeAsync<List<MonoRateItem>>(
                stream,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (data == null || data.Count == 0)
                return null;

            MonoRateItem? Pick(int codeA) =>
                data.FirstOrDefault(x => x.CurrencyCodeA == codeA && x.CurrencyCodeB == 980);

            var usd = Pick(840);
            var eur = Pick(978);
            var pln = Pick(985);
            var gbp = Pick(826);

            var result = new MonoRatesView
            {
                UsdBuy = usd?.RateBuy,
                UsdSell = usd?.RateSell ?? usd?.RateCross,
                EurBuy = eur?.RateBuy,
                EurSell = eur?.RateSell ?? eur?.RateCross,
                PlnBuy = pln?.RateBuy,
                PlnSell = pln?.RateSell ?? pln?.RateCross,
                GbpBuy = gbp?.RateBuy,
                GbpSell = gbp?.RateSell ?? gbp?.RateCross
            };

            if (result.UsdBuy is null && result.UsdSell is null &&
                result.EurBuy is null && result.EurSell is null &&
                result.PlnBuy is null && result.PlnSell is null &&
                result.GbpBuy is null && result.GbpSell is null)
                return null;

            return result;
        }
        catch
        {
            return null;
        }
    }

    private async Task<PrivatRatesView?> LoadPrivatRatesAsync()
    {
        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(4);
            using var resp = await client.GetAsync("https://api.privatbank.ua/p24api/pubinfo?json&exchange&coursid=11");
            if (!resp.IsSuccessStatusCode)
                return null;

            await using var stream = await resp.Content.ReadAsStreamAsync();
            var data = await JsonSerializer.DeserializeAsync<List<PrivatRateItem>>(
                stream,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (data == null || data.Count == 0)
                return null;

            static decimal? ParseDecimal(string? value) =>
                decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : null;

            PrivatRateItem? Pick(string ccy) =>
                data.FirstOrDefault(x => string.Equals(x.Ccy, ccy, StringComparison.OrdinalIgnoreCase));

            var usd = Pick("USD");
            var eur = Pick("EUR");

            return new PrivatRatesView
            {
                UsdBuy = ParseDecimal(usd?.Buy),
                UsdSell = ParseDecimal(usd?.Sale),
                EurBuy = ParseDecimal(eur?.Buy),
                EurSell = ParseDecimal(eur?.Sale)
            };
        }
        catch
        {
            return null;
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

        try
        {
            var url = await _telegraph.CreatePageAsync(entity, 1);
            entity.TelegraphUrl1 = url;
            var staffId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!string.IsNullOrEmpty(staffId))
                entity.LastUpdatedById = staffId;
            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Помилка створення Telegraf (валюта) для announcementId={AnnouncementId}", id);
            ErrorMessage = $"Помилка Telegraf (валюта): {ex.Message}";
        }

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

        try
        {
            var url = await _telegraph.CreatePageAsync(entity, 2);
            entity.TelegraphUrl2 = url;
            var staffId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!string.IsNullOrEmpty(staffId))
                entity.LastUpdatedById = staffId;
            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Помилка створення Telegraf (грн) для announcementId={AnnouncementId}", id);
            ErrorMessage = $"Помилка Telegraf (грн): {ex.Message}";
        }

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

        try
        {
            await _telegram.SendAnnouncementVariantAsync(entity, 1);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Помилка надсилання в Telegram (валюта) для announcementId={AnnouncementId}", id);
            ErrorMessage = $"Помилка Telegram (валюта): {ex.Message}";
        }

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

        try
        {
            await _telegram.SendAnnouncementVariantAsync(entity, 2);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Помилка надсилання в Telegram (грн) для announcementId={AnnouncementId}", id);
            ErrorMessage = $"Помилка Telegram (грн): {ex.Message}";
        }

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
