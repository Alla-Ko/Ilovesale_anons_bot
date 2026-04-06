using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Announcement.Models;

public class AnnouncementEntity : IAuditable
{
    public int Id { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    [Required]
    [MaxLength(500)]
    public string Title { get; set; } = string.Empty;

    public Country Country { get; set; }

    [MaxLength(2000)]
    public string? TelegraphUrl1 { get; set; }

    [MaxLength(2000)]
    public string? TelegraphUrl2 { get; set; }

    [Required]
    public string CreatorId { get; set; } = string.Empty;

    [ForeignKey(nameof(CreatorId))]
    public ApplicationUser? Creator { get; set; }

    /// <summary>Хто останній зберіг анонс (редагування або колажі). Якщо null — показуємо автора створення.</summary>
    public string? LastUpdatedById { get; set; }

    [ForeignKey(nameof(LastUpdatedById))]
    public ApplicationUser? LastUpdatedBy { get; set; }

    public ICollection<CollageEntity> Collages { get; set; } = new List<CollageEntity>();
}
