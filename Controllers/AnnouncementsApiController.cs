using Announcement.Api.Contracts;
using Announcement.Authorization;
using Announcement.Data;
using Announcement.Models;
using Announcement.Options;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Announcement.Controllers;

[ApiController]
[Route("api/announcements")]
[Authorize(AuthenticationSchemes = BotApiKeyAuthenticationHandler.SchemeName)]
public class AnnouncementsApiController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _users;
    private readonly BotApiOptions _botApi;

    public AnnouncementsApiController(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        IOptions<BotApiOptions> botApi)
    {
        _db = db;
        _users = userManager;
        _botApi = botApi.Value;
    }

    /// <summary>Створити порожній анонс (без колажів).</summary>
    [HttpPost]
    [ProducesResponseType(typeof(CreateAnnouncementResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> CreateAsync([FromBody] CreateAnnouncementRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return BadRequest(new ApiErrorResponse { Error = "Некоректні дані запиту." });

        var title = request.Title.Trim();
        if (string.IsNullOrWhiteSpace(title))
            return BadRequest(new ApiErrorResponse { Error = "Вкажіть назву анонса." });

        if (!Enum.IsDefined(typeof(Country), request.Country))
            return BadRequest(new ApiErrorResponse { Error = "Некоректне значення Country." });

        var creatorId = await ResolveCreatorIdAsync(request.CreatorUserId, cancellationToken);
        if (creatorId == null)
            return BadRequest(new ApiErrorResponse { Error = "Користувача-автора не знайдено. Вкажіть CreatorUserId або BotApi:DefaultCreatorUserId." });

        var entity = new AnnouncementEntity
        {
            Title = title,
            Country = request.Country,
            CreatorId = creatorId,
            LastUpdatedById = creatorId
        };

        _db.Announcements.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);

        var response = new CreateAnnouncementResponse
        {
            Id = entity.Id,
            Title = entity.Title,
            Country = entity.Country,
            CreatorId = entity.CreatorId,
            CreatedAtUtc = entity.CreatedAtUtc
        };

        return CreatedAtAction(nameof(GetByIdAsync), new { id = entity.Id }, response);
    }

    /// <summary>Додати колаж до існуючого анонса (медіа вже має URL).</summary>
    [HttpPost("{id:int}/collages")]
    [ProducesResponseType(typeof(AddCollageResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> AddCollageAsync(
        int id,
        [FromBody] AddCollageRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return BadRequest(new ApiErrorResponse { Error = "Некоректні дані запиту." });

        var mediaUrl = request.MediaUrl.Trim();
        if (string.IsNullOrWhiteSpace(mediaUrl))
            return BadRequest(new ApiErrorResponse { Error = "Вкажіть MediaUrl." });

        if (!Enum.IsDefined(typeof(MediaType), request.MediaType))
            return BadRequest(new ApiErrorResponse { Error = "Некоректне значення MediaType." });

        var announcement = await _db.Announcements
            .Include(a => a.Collages)
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (announcement == null)
            return NotFound(new ApiErrorResponse { Error = $"Анонс {id} не знайдено." });

        var sortOrder = request.SortOrder;
        if (sortOrder.HasValue)
        {
            if (sortOrder.Value < 0)
                return BadRequest(new ApiErrorResponse { Error = "SortOrder не може бути від'ємним." });
        }
        else
        {
            sortOrder = announcement.Collages.Count == 0
                ? 0
                : announcement.Collages.Max(c => c.SortOrder) + 1;
        }

        var editorId = await ResolveCreatorIdAsync(null, cancellationToken);

        var collage = new CollageEntity
        {
            AnnouncementId = announcement.Id,
            SortOrder = sortOrder.Value,
            MediaUrl = mediaUrl,
            MediaType = request.MediaType,
            Caption1 = string.IsNullOrWhiteSpace(request.Caption1) ? null : request.Caption1,
            Caption2 = string.IsNullOrWhiteSpace(request.Caption2) ? null : request.Caption2
        };

        _db.Collages.Add(collage);
        announcement.TelegraphUrl1 = null;
        announcement.TelegraphUrl2 = null;
        if (!string.IsNullOrEmpty(editorId))
            announcement.LastUpdatedById = editorId;

        await _db.SaveChangesAsync(cancellationToken);

        var response = new AddCollageResponse
        {
            Id = collage.Id,
            AnnouncementId = collage.AnnouncementId,
            SortOrder = collage.SortOrder,
            MediaUrl = collage.MediaUrl,
            MediaType = collage.MediaType,
            Caption1 = collage.Caption1,
            Caption2 = collage.Caption2,
            CreatedAtUtc = collage.CreatedAtUtc
        };

        return CreatedAtAction(nameof(GetByIdAsync), new { id = announcement.Id }, response);
    }

    /// <summary>Мінімальна перевірка існування анонса (для CreatedAtAction).</summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(CreateAnnouncementResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetByIdAsync(int id, CancellationToken cancellationToken)
    {
        var entity = await _db.Announcements.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (entity == null)
            return NotFound(new ApiErrorResponse { Error = $"Анонс {id} не знайдено." });

        return Ok(new CreateAnnouncementResponse
        {
            Id = entity.Id,
            Title = entity.Title,
            Country = entity.Country,
            CreatorId = entity.CreatorId,
            CreatedAtUtc = entity.CreatedAtUtc
        });
    }

    private async Task<string?> ResolveCreatorIdAsync(string? requestCreatorId, CancellationToken cancellationToken)
    {
        var candidate = string.IsNullOrWhiteSpace(requestCreatorId)
            ? _botApi.DefaultCreatorUserId?.Trim()
            : requestCreatorId.Trim();

        if (string.IsNullOrEmpty(candidate))
            return null;

        var user = await _users.FindByIdAsync(candidate);
        return user == null ? null : user.Id;
    }
}
