using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Announcement.Data;
using Announcement.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Announcement.Pages.Announcements;

public class CreateModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public CreateModel(ApplicationDbContext db)
    {
        _db = db;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public List<SelectListItem> CountryOptions { get; set; } = new();

    public class InputModel
    {
        [Required(ErrorMessage = "Вкажіть назву")]
        [Display(Name = "Назва")]
        public string Title { get; set; } = string.Empty;

        [Required]
        [Display(Name = "Країна")]
        public Country Country { get; set; }
    }

    public void OnGet()
    {
        CountryOptions = CountryLabels.Labels.Select(kv => new SelectListItem(kv.Value, ((int)kv.Key).ToString())).ToList();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        CountryOptions = CountryLabels.Labels.Select(kv => new SelectListItem(kv.Value, ((int)kv.Key).ToString())).ToList();
        if (!ModelState.IsValid)
            return Page();

        var uid = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(uid))
            return Unauthorized();

        var entity = new AnnouncementEntity
        {
            Title = Input.Title.Trim(),
            Country = Input.Country,
            CreatorId = uid,
            LastUpdatedById = uid
        };
        _db.Announcements.Add(entity);
        await _db.SaveChangesAsync();

        return RedirectToPage("./Edit", new { id = entity.Id });
    }
}
