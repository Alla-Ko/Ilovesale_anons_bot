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
public class EditModel : PageModel
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;

    public EditModel(UserManager<ApplicationUser> userManager, RoleManager<IdentityRole> roleManager)
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

        [DataType(DataType.Password)]
        [Display(Name = "Новий пароль (залиште порожнім, щоб не змінювати)")]
        public string? NewPassword { get; set; }

        [Required]
        [Display(Name = "Роль")]
        public string Role { get; set; } = AppRoles.User;
    }

    public async Task<IActionResult> OnGetAsync(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null)
            return NotFound();

        var roles = await _userManager.GetRolesAsync(user);
        Input = new InputModel
        {
            UserName = user.UserName ?? "",
            Role = roles.FirstOrDefault() ?? AppRoles.User
        };
        RoleOptions = _roleManager.Roles.OrderBy(r => r.Name).Select(r => new SelectListItem(r.Name!, r.Name!)).ToList();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string id)
    {
        RoleOptions = _roleManager.Roles.OrderBy(r => r.Name).Select(r => new SelectListItem(r.Name!, r.Name!)).ToList();
        var user = await _userManager.FindByIdAsync(id);
        if (user == null)
            return NotFound();

        if (!ModelState.IsValid)
            return Page();

        user.UserName = Input.UserName;
        var updateResult = await _userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            foreach (var e in updateResult.Errors)
                ModelState.AddModelError(string.Empty, e.Description);
            return Page();
        }

        if (!string.IsNullOrWhiteSpace(Input.NewPassword))
        {
            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var pwdResult = await _userManager.ResetPasswordAsync(user, token, Input.NewPassword!);
            if (!pwdResult.Succeeded)
            {
                foreach (var e in pwdResult.Errors)
                    ModelState.AddModelError(string.Empty, e.Description);
                return Page();
            }
        }

        var currentRoles = await _userManager.GetRolesAsync(user);
        await _userManager.RemoveFromRolesAsync(user, currentRoles);
        await _userManager.AddToRoleAsync(user, Input.Role);

        return RedirectToPage("./Index");
    }
}
