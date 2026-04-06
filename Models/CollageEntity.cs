using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Announcement.Models;

public class CollageEntity : IAuditable
{
    public int Id { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public int SortOrder { get; set; }

    public int AnnouncementId { get; set; }

    [ForeignKey(nameof(AnnouncementId))]
    public AnnouncementEntity? Announcement { get; set; }

    public MediaType MediaType { get; set; }

    [Required]
    [MaxLength(4000)]
    public string MediaUrl { get; set; } = string.Empty;

    public string? Caption1 { get; set; }

    public string? Caption2 { get; set; }
}
