using Announcement.Authorization;
using Announcement.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Announcement.Data;

public static class DbSeeder
{
    /// <summary>
    /// Застосовує міграції та створює ролі й першого адміна з .env (якщо ще немає).
    /// </summary>
    public static async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await ctx.Database.MigrateAsync(cancellationToken);
        await SeedRolesAndAdminAsync(scope.ServiceProvider, cancellationToken);
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
