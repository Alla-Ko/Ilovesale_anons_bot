using Microsoft.EntityFrameworkCore;

namespace Announcement.Data;

/// <summary>
/// Для локальної розробки: повністю видаляє БД, застосовує міграції з нуля, викликає сід ролей і першого адміна.
/// У <c>Program.cs</c> виклик зазвичай тимчасовий — після першого запуску закоментуйте.
/// </summary>
public static class DevDatabaseRecreateSeeder
{
    public static async Task RunAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger("DevDatabaseRecreateSeeder");

        logger.LogWarning(
            "DEV ONLY: видалення бази даних «{Database}» і повторне створення за міграціями.",
            ctx.Database.GetDbConnection().Database);

        await ctx.Database.EnsureDeletedAsync(cancellationToken);
        await ctx.Database.MigrateAsync(cancellationToken);

        await DbSeeder.SeedRolesAndAdminAsync(scope.ServiceProvider, cancellationToken);
        logger.LogInformation("DEV: сід ролей і адміна виконано.");
    }
}
