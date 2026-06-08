using Announcement.Models;

namespace Announcement.Api.Contracts;

public class AddCollageResponse
{
    public int Id { get; set; }
    public int AnnouncementId { get; set; }
    public int SortOrder { get; set; }
    public string MediaUrl { get; set; } = string.Empty;
    public MediaType MediaType { get; set; }
    public string? Caption1 { get; set; }
    public string? Caption2 { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
