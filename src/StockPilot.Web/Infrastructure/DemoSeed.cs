using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using StockPilot.Web.Domain;
using StockPilot.Web.Features;

namespace StockPilot.Web.Infrastructure;

public static class DemoSeed
{
    public static async Task Run(IServiceProvider services)
    {
        var users = services.GetRequiredService<UserManager<AppUser>>();
        if (await users.FindByEmailAsync("admin@stockpilot.demo") is not null) return;
        var tenant = services.GetRequiredService<TenantSession>();
        var db = services.GetRequiredService<AppDbContext>();
        var inventory = services.GetRequiredService<InventoryService>();
        tenant.SeedTenantId = Guid.NewGuid();
        foreach (var (email, role) in new[] { ("admin@stockpilot.demo", "Admin"), ("operator@stockpilot.demo", "Operator"), ("viewer@stockpilot.demo", "Viewer") })
        {
            var result = await users.CreateAsync(new AppUser { UserName = email, Email = email, TenantId = tenant.Id, CompanyName = "Northstar Supply", AccessRole = role }, "StockPilot!2026");
            if (!result.Succeeded) throw new InvalidOperationException(string.Join("; ", result.Errors.Select(x => x.Description)));
        }
        var samples = new[]
        {
            ("KB-104", "Mechanical keyboard", "Peripherals", 8900L, 56, 12),
            ("DS-200", "USB-C docking station", "Connectivity", 12900L, 32, 10),
            ("MS-310", "Wireless precision mouse", "Peripherals", 4900L, 8, 15),
            ("MN-270", "27-inch studio display", "Displays", 32900L, 24, 6),
            ("CB-060", "Braided USB-C cable", "Connectivity", 1900L, 124, 30),
            ("HD-500", "Studio headphones", "Audio", 15900L, 5, 8)
        };
        foreach (var (sku, name, category, price, quantity, reorder) in samples)
        {
            var product = await inventory.AddProduct(new(sku, name, category, price, reorder));
            await inventory.Receive(product.Id, new(quantity, "Opening inventory"));
            db.ChangeTracker.Clear();
        }
        var products = await db.Products.AsNoTracking().OrderBy(x => x.Sku).ToListAsync();
        var first = await inventory.Reserve(new(products[0].Id, 4, "Acme Studio", Guid.NewGuid()));
        await inventory.Transition(first.Id, OrderStatus.Confirmed);
        await inventory.Reserve(new(products[1].Id, 3, "Orbit Technologies", Guid.NewGuid()));
        await inventory.Reserve(new(products[3].Id, 2, "Forma Design", Guid.NewGuid()));
        tenant.SeedTenantId = null;
    }
}
