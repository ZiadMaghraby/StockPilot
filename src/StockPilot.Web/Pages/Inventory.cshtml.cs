using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using StockPilot.Web.Domain;
using StockPilot.Web.Features;
using StockPilot.Web.Infrastructure;
namespace StockPilot.Web.Pages;

public class InventoryModel(AppDbContext db, InventoryService inventory) : PageModel
{
    public List<Product> Products { get; set; } = [];
    [BindProperty(SupportsGet = true)] public string? Search { get; set; }
    [BindProperty] public string Sku { get; set; } = "";
    [BindProperty] public string ProductName { get; set; } = "";
    [BindProperty] public string Category { get; set; } = "General";
    [BindProperty] public decimal Price { get; set; }
    [BindProperty] public int ReorderPoint { get; set; } = 10;
    public async Task OnGetAsync()
    {
        var query = db.Products.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(Search)) query = query.Where(x => x.Name.Contains(Search) || x.Sku.Contains(Search));
        Products = await query.OrderBy(x => x.Name).Take(250).ToListAsync();
    }
    public async Task<IActionResult> OnPostCreateAsync()
    {
        try
        {
            if (!ModelState.IsValid || Price is < 0 or > 1_000_000 || decimal.Round(Price, 2) != Price)
                throw new BusinessException("Enter a valid price with at most two decimal places.");
            await inventory.AddProduct(new(Sku, ProductName, Category, (long)(Price * 100), ReorderPoint));
            TempData["Success"] = "Product created. Receive stock to make it available for orders.";
            return RedirectToPage();
        }
        catch (BusinessException error) { ModelState.AddModelError("", error.Message); await OnGetAsync(); return Page(); }
    }
    public async Task<IActionResult> OnPostReceiveAsync(Guid productId, int quantity, string note)
    {
        try { await inventory.Receive(productId, new(quantity, note)); TempData["Success"] = "Stock received and recorded in the activity log."; }
        catch (BusinessException error) { TempData["Error"] = error.Message; }
        return RedirectToPage();
    }
}
