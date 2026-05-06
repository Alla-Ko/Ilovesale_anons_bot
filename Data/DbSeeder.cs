using Announcement.Authorization;
using Announcement.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.IO;
using System.Net.Sockets;

namespace Announcement.Data;

public static class DbSeeder
{
    private const int MigrationMaxAttempts = 5;

    /// <summary>
    /// Застосовує міграції та створює ролі й першого адміна з .env (якщо ще немає).
    /// </summary>
    public static async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DbSeeder");

        for (var attempt = 1; attempt <= MigrationMaxAttempts; attempt++)
        {
            try
            {
                await ctx.Database.MigrateAsync(cancellationToken);
                break;
            }
            catch (Exception ex) when (IsTransientDatabaseException(ex) && attempt < MigrationMaxAttempts)
            {
                var delay = TimeSpan.FromSeconds(Math.Min(2 * attempt, 10));
                logger.LogWarning(
                    ex,
                    "Transient DB error during migration. Attempt {Attempt}/{MaxAttempts}. Retrying in {DelaySeconds}s",
                    attempt,
                    MigrationMaxAttempts,
                    delay.TotalSeconds);
                await Task.Delay(delay, cancellationToken);
            }
            catch (Exception ex) when (IsTransientDatabaseException(ex))
            {
                logger.LogError(
                    ex,
                    "Migration failed after {MaxAttempts} attempts due to transient DB errors.",
                    MigrationMaxAttempts);
                throw;
            }
        }

        await SeedRolesAndAdminAsync(scope.ServiceProvider, cancellationToken);
    }

    private static bool IsTransientDatabaseException(Exception ex)
    {
        for (Exception? current = ex; current != null; current = current.InnerException)
        {
            if (current is NpgsqlException || current is IOException || current is SocketException)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Лише ролі та адмін (без міграцій). База вже має існувати з актуальною схемою.
    /// </summary>
    public static async Task SeedRolesAndAdminAsync(IServiceProvider scopedServices, CancellationToken cancellationToken = default)
    {
        var roleManager = scopedServices.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = scopedServices.GetRequiredService<UserManager<ApplicationUser>>();
        var configuration = scopedServices.GetRequiredService<IConfiguration>();
        var logger = scopedServices.GetRequiredService<ILoggerFactory>().CreateLogger("DbSeeder");

        foreach (var role in new[] { AppRoles.Admin, AppRoles.Moderator, AppRoles.User })
        {
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new IdentityRole(role));
        }

        var adminUser = configuration["ADMIN_USERNAME"]?.Trim();
        var adminPass = configuration["ADMIN_PASSWORD"]?.Trim();
        if (string.IsNullOrEmpty(adminUser) || string.IsNullOrEmpty(adminPass)
            || adminPass.StartsWith("YOUR_", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogInformation("ADMIN_USERNAME / ADMIN_PASSWORD не задані — початкового адміна не створено.");
            return;
        }

        var existing = await userManager.FindByNameAsync(adminUser);
        if (existing != null)
            return;

        var user = new ApplicationUser { UserName = adminUser, EmailConfirmed = true };
        var result = await userManager.CreateAsync(user, adminPass);
        if (!result.Succeeded)
        {
            logger.LogError("Не вдалося створити адміна: {Errors}", string.Join("; ", result.Errors.Select(e => e.Description)));
            return;
        }

        await userManager.AddToRoleAsync(user, AppRoles.Admin);
        logger.LogInformation("Створено адміністратора {User}", adminUser);
    }
}
