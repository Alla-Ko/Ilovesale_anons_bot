using System.ComponentModel.DataAnnotations;
using Announcement.Models;

namespace Announcement.Api.Contracts;

public class AddCollageRequest
{
    [Required]
    [MaxLength(4000)]
    public string MediaUrl { get; set; } = string.Empty;

    [Required]
    public MediaType MediaType { get; set; }

    public string? Caption1 { get; set; }

    public string? Caption2 { get; set; }

    /// <summary>Позиція в списку (0-based). Якщо не вказано — додається в кінець.</summary>
    public int? SortOrder { get; set; }
}
