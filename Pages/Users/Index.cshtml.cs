using Announcement.Authorization;
using Announcement.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Announcement.Pages.Users;

[Authorize(Roles = AppRoles.Admin)]
public class IndexModel : PageModel
{
    private readonly UserManager<ApplicationUser> _userManager;

    public IndexModel(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    [BindProperty(SupportsGet = true)]
    public string? Sort { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Dir { get; set; }

    public IList<Row> Users { get; set; } = new List<Row>();

    public class Row
    {
        public string Id { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public string Roles { get; set; } = string.Empty;
        public DateTime CreatedAtUtc { get; set; }
        public DateTime UpdatedAtUtc { get; set; }
    }

    public string SortUrl(string column)
    {
        Sort ??= "username";
        Dir ??= "asc";
        var nextDir = string.Equals(Sort, column, StringComparison.OrdinalIgnoreCase)
            && string.Equals(Dir, "asc", StringComparison.OrdinalIgnoreCase)
            ? "desc"
            : "asc";
        return Url.Page("/Users/Index", new { sort = column, dir = nextDir })!;
    }

    public string HeaderIndicator(string column)
    {
        Sort ??= "username";
        Dir ??= "asc";
        if (!string.Equals(Sort, column, StringComparison.OrdinalIgnoreCase))
            return string.Empty;
        return string.Equals(Dir, "asc", StringComparison.OrdinalIgnoreCase) ? " ▲" : " ▼";
    }

    public async Task OnGetAsync()
    {
        Sort ??= "username";
        Dir ??= "asc";

        IQueryable<ApplicationUser> query = _userManager.Users;

        query = (Sort.ToLowerInvariant(), Dir.ToLowerInvariant()) switch
        {
            ("created", "desc") => query.OrderByDescending(u => u.CreatedAtUtc),
            ("created", _) => query.OrderBy(u => u.CreatedAtUtc),
            ("updated", "desc") => query.OrderByDescending(u => u.UpdatedAtUtc),
            ("updated", _) => query.OrderBy(u => u.UpdatedAtUtc),
            ("username", "desc") => query.OrderByDescending(u => u.UserName),
            _ => query.OrderBy(u => u.UserName)
        };

        var users = await query.ToListAsync();
        foreach (var u in users)
        {
            var roles = await _userManager.GetRolesAsync(u);
            Users.Add(new Row
            {
                Id = u.Id,
                UserName = u.UserName ?? u.Id,
                Roles = string.Join(", ", roles),
                CreatedAtUtc = u.CreatedAtUtc,
                UpdatedAtUtc = u.UpdatedAtUtc
            });
        }
    }
}
