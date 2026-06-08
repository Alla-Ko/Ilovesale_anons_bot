using System.ComponentModel.DataAnnotations;
using Announcement.Models;

namespace Announcement.Api.Contracts;

public class CreateAnnouncementRequest
{
    [Required]
    [MaxLength(500)]
    public string Title { get; set; } = string.Empty;

    [Required]
    public Country Country { get; set; }

    /// <summary>Id користувача (AspNetUsers). Якщо не вказано — BotApi:DefaultCreatorUserId.</summary>
    [MaxLength(450)]
    public string? CreatorUserId { get; set; }
}
