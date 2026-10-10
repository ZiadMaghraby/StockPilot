using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using StockPilot.Web.Domain;
using StockPilot.Web.Infrastructure;
namespace StockPilot.Web.Pages;

public class IndexModel(AppDbContext db) : PageModel
{
    public int ProductCount { get; set; }
    public long AvailableUnits { get; set; }
    public int PendingOrders { get; set; }
    public long InventoryValueCents { get; set; }
    public long ShippedRevenueCents { get; set; }
    public List<Product> LowStock { get; set; } = [];
    public List<SalesOrder> RecentOrders { get; set; } = [];
    public List<AuditEntry> RecentActivity { get; set; } = [];
    public List<(string Name, long Units)> Categories { get; set; } = [];
    public async Task OnGetAsync()
    {
        ProductCount = await db.Products.CountAsync();
        AvailableUnits = await db.Products.SumAsync(x => (long)(x.OnHand - x.Reserved));
        InventoryValueCents = await db.Products.SumAsync(x => x.PriceCents * x.OnHand);
        PendingOrders = await db.Orders.CountAsync(x => x.Status == OrderStatus.Reserved);
        ShippedRevenueCents = await db.Orders.Where(x => x.Status == OrderStatus.Confirmed).SumAsync(x => x.UnitPriceCents * x.Quantity);
        LowStock = await db.Products.AsNoTracking().Where(x => x.OnHand - x.Reserved <= x.ReorderPoint).OrderBy(x => x.OnHand - x.Reserved).Take(5).ToListAsync();
        RecentOrders = await db.Orders.AsNoTracking().OrderByDescending(x => x.CreatedAt).Take(5).ToListAsync();
        RecentActivity = await db.Audit.AsNoTracking().OrderByDescending(x => x.CreatedAt).Take(4).ToListAsync();
        var categories = await db.Products.GroupBy(x => x.Category).Select(x => new { Name = x.Key, Units = x.Sum(p => (long)p.OnHand - p.Reserved) }).OrderByDescending(x => x.Units).Take(5).ToListAsync();
        Categories = categories.Select(x => (x.Name, x.Units)).ToList();
    }
}
