using Announcement.Models;

namespace Announcement.Api.Contracts;

public class CreateAnnouncementResponse
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public Country Country { get; set; }
    public string CreatorId { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
}
