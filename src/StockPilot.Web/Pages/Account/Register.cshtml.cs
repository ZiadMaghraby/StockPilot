using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using StockPilot.Web.Domain;
namespace StockPilot.Web.Pages.Account;

[AllowAnonymous, EnableRateLimiting("auth")]
public class RegisterModel(UserManager<AppUser> users, SignInManager<AppUser> signIn) : PageModel
{
    [BindProperty, Required, StringLength(100)] public string Company { get; set; } = "";
    [BindProperty, Required, EmailAddress, StringLength(256)] public string Email { get; set; } = "";
    [BindProperty, Required, MinLength(12), DataType(DataType.Password)] public string Password { get; set; } = "";
    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();
        var user = new AppUser { UserName = Email.Trim(), Email = Email.Trim(), CompanyName = Company.Trim(), TenantId = Guid.NewGuid(), AccessRole = "Admin" };
        var result = await users.CreateAsync(user, Password);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors) ModelState.AddModelError("", error.Description);
            return Page();
        }
        await signIn.SignInAsync(user, false);
        return RedirectToPage("/Index");
    }
}
