using Announcement.Data;
using Microsoft.EntityFrameworkCore;

namespace Announcement.Services;

public class AnnouncementCleanupBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AnnouncementCleanupBackgroundService> _logger;

    public AnnouncementCleanupBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<AnnouncementCleanupBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RunCleanupAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = DelayUntilNextLocalNineAm();
            _logger.LogInformation("Наступне планове очищення анонсів через {Delay}", delay);
            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }

            await RunCleanupAsync(stoppingToken);
        }
    }

    private async Task RunCleanupAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var cutoff = DateTime.UtcNow.AddDays(-7);
            var old = await db.Announcements.Where(a => a.CreatedAtUtc < cutoff).ToListAsync(cancellationToken);
            if (old.Count == 0)
            {
                _logger.LogInformation("Очищення: немає анонсів старіших за 7 днів.");
                return;
            }

            db.Announcements.RemoveRange(old);
            await db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Очищення: видалено {Count} анонсів (старіші за {Cutoff:O}).", old.Count, cutoff);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Помилка очищення анонсів.");
        }
    }

    private static TimeSpan DelayUntilNextLocalNineAm()
    {
        var now = DateTime.Now;
        var next = new DateTime(now.Year, now.Month, now.Day, 9, 0, 0, DateTimeKind.Local);
        if (now >= next)
            next = next.AddDays(1);
        return next - now;
    }
}
