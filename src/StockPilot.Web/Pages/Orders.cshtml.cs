using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using StockPilot.Web.Domain;
using StockPilot.Web.Features;
using StockPilot.Web.Infrastructure;
namespace StockPilot.Web.Pages;

public class OrdersModel(AppDbContext db, InventoryService inventory) : PageModel
{
    public List<SalesOrder> Orders { get; set; } = [];
    public List<Product> Products { get; set; } = [];
    [BindProperty] public Guid RequestId { get; set; } = Guid.NewGuid();
    public async Task OnGetAsync()
    {
        Orders = await db.Orders.AsNoTracking().OrderByDescending(x => x.CreatedAt).Take(100).ToListAsync();
        Products = await db.Products.AsNoTracking().OrderBy(x => x.Name).Take(250).ToListAsync();
    }
    public async Task<IActionResult> OnPostCreateAsync(Guid productId, int quantity, string customer)
    {
        try { await inventory.Reserve(new(productId, quantity, customer, RequestId)); TempData["Success"] = "Order reserved. Confirm shipment or cancel to release the stock."; return RedirectToPage(); }
        catch (BusinessException error) { ModelState.AddModelError("", error.Message); await OnGetAsync(); return Page(); }
    }
    public async Task<IActionResult> OnPostTransitionAsync(Guid orderId, OrderStatus next)
    {
        try { await inventory.Transition(orderId, next); TempData["Success"] = $"Order {next.ToString().ToLowerInvariant()}. Inventory updated."; }
        catch (BusinessException error) { TempData["Error"] = error.Message; }
        return RedirectToPage();
    }
}
