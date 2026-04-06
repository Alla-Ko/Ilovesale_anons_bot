using System.ComponentModel.DataAnnotations;
using Announcement.Authorization;
using Announcement.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Announcement.Pages.Users;

[Authorize(Roles = AppRoles.Admin)]
public class CreateModel : PageModel
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;

    public CreateModel(UserManager<ApplicationUser> userManager, RoleManager<IdentityRole> roleManager)
    {
        _userManager = userManager;
        _roleManager = roleManager;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public List<SelectListItem> RoleOptions { get; set; } = new();

    public class InputModel
    {
        [Required]
        [Display(Name = "Логін")]
        public string UserName { get; set; } = string.Empty;

        [Required]
        [DataType(DataType.Password)]
        [Display(Name = "Пароль")]
        public string Password { get; set; } = string.Empty;

        [Required]
        [Display(Name = "Роль")]
        public string Role { get; set; } = AppRoles.User;
    }

    public void OnGet()
    {
        RoleOptions = _roleManager.Roles.OrderBy(r => r.Name).Select(r => new SelectListItem(r.Name!, r.Name!)).ToList();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        RoleOptions = _roleManager.Roles.OrderBy(r => r.Name).Select(r => new SelectListItem(r.Name!, r.Name!)).ToList();
        if (!ModelState.IsValid)
            return Page();

        var user = new ApplicationUser { UserName = Input.UserName, EmailConfirmed = true };
        var result = await _userManager.CreateAsync(user, Input.Password);
        if (!result.Succeeded)
        {
            foreach (var e in result.Errors)
                ModelState.AddModelError(string.Empty, e.Description);
            return Page();
        }

        await _userManager.AddToRoleAsync(user, Input.Role);
        return RedirectToPage("./Index");
    }
}
