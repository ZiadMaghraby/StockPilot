using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using StockPilot.Web.Domain;
using StockPilot.Web.Infrastructure;
namespace StockPilot.Web.Pages;

[Authorize(Roles = "Admin")]
public class TeamModel(UserManager<AppUser> users, AppDbContext db, TenantSession tenant) : PageModel
{
    public List<AppUser> Members { get; set; } = [];
    [BindProperty, Required, EmailAddress] public string Email { get; set; } = "";
    [BindProperty, Required, MinLength(12), DataType(DataType.Password)] public string Password { get; set; } = "";
    [BindProperty, Required] public string AccessRole { get; set; } = "Viewer";
    public async Task OnGetAsync() => Members = await users.Users.Where(x => x.TenantId == tenant.Id).OrderBy(x => x.Email).ToListAsync();
    public async Task<IActionResult> OnPostAsync()
    {
        tenant.RequireWrite(adminOnly: true);
        if (AccessRole is not ("Viewer" or "Operator" or "Admin")) ModelState.AddModelError("", "Select a valid role.");
        if (!ModelState.IsValid) { await OnGetAsync(); return Page(); }
        await using var transaction = await db.Database.BeginTransactionAsync();
        var result = await users.CreateAsync(new AppUser
        {
            UserName = Email.Trim(),
            Email = Email.Trim(),
            TenantId = tenant.Id,
            CompanyName = User.FindFirstValue("company") ?? "",
            AccessRole = AccessRole
        }, Password);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors) ModelState.AddModelError("", error.Description);
            await OnGetAsync(); return Page();
        }
        db.Audit.Add(new AuditEntry { TenantId = tenant.Id, Actor = tenant.Actor, Action = "Team member added", Detail = $"{Email.Trim()} · {AccessRole}" });
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        TempData["Success"] = "Team member created.";
        return RedirectToPage();
    }
}
