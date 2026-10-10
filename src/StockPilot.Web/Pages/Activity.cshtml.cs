using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using StockPilot.Web.Domain;
using StockPilot.Web.Infrastructure;
namespace StockPilot.Web.Pages;

public class ActivityModel(AppDbContext db) : PageModel
{
    public List<AuditEntry> Entries { get; set; } = [];
    public async Task OnGetAsync() => Entries = await db.Audit.AsNoTracking().OrderByDescending(x => x.CreatedAt).Take(100).ToListAsync();
}
