using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using StockPilot.Web.Domain;

namespace StockPilot.Web.Pages.Account;

[AllowAnonymous, EnableRateLimiting("auth")]
public class LoginModel(SignInManager<AppUser> signIn, IWebHostEnvironment environment, IConfiguration configuration) : PageModel
{
    [BindProperty, Required, EmailAddress] public string Email { get; set; } = "";
    [BindProperty, Required, DataType(DataType.Password)] public string Password { get; set; } = "";
    public bool DemoEnabled => environment.IsDevelopment() && configuration.GetValue<bool>("Demo:Enabled");
    public async Task<IActionResult> OnPostAsync(string? returnUrl)
    {
        if (!ModelState.IsValid) return Page();
        var result = await signIn.PasswordSignInAsync(Email.Trim(), Password, false, lockoutOnFailure: true);
        if (result.Succeeded) return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl! : "/");
        ModelState.AddModelError("", result.IsLockedOut ? "Too many attempts. Try again in 10 minutes." : "The email or password is incorrect.");
        return Page();
    }
}
