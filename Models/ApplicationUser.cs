using Microsoft.AspNetCore.Identity;

namespace Announcement.Models;

public class ApplicationUser : IdentityUser, IAuditable
{
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
