using Microsoft.EntityFrameworkCore;
using StockPilot.Web.Domain;
using StockPilot.Web.Infrastructure;

namespace StockPilot.Web.Features;

public record CreateProduct(string Sku, string Name, string Category, long PriceCents, int ReorderPoint);
public record ReceiveStock(int Quantity, string Note);
public record CreateOrder(Guid ProductId, int Quantity, string Customer, Guid RequestId);

public class InventoryService(AppDbContext db, TenantSession tenant)
{
    public async Task<Product> AddProduct(CreateProduct input)
    {
        tenant.RequireWrite(adminOnly: true);
        var sku = Clean(input.Sku, 40, "SKU").ToUpperInvariant();
        var name = Clean(input.Name, 100, "Product name");
        var category = Clean(input.Category, 40, "Category");
        if (input.PriceCents is < 0 or > 100_000_000 || input.ReorderPoint is < 0 or > 1_000_000)
            throw new BusinessException("Price or reorder point is outside the supported range.");
        var product = new Product { TenantId = tenant.Id, Sku = sku, Name = name, Category = category, PriceCents = input.PriceCents, ReorderPoint = input.ReorderPoint };
        db.Products.Add(product);
        Log("Product created", $"{sku} · {name}");
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException)
        {
            if (await db.Products.AsNoTracking().AnyAsync(x => x.Sku == sku))
                throw new BusinessException("This SKU already exists in your workspace.", 409);
            throw;
        }
        return product;
    }

    public async Task Receive(Guid productId, ReceiveStock input)
    {
        tenant.RequireWrite();
        CheckQuantity(input.Quantity);
        var note = Clean(input.Note, 200, "Stock receipt reference");
        await using var tx = await db.Database.BeginTransactionAsync();
        var changed = await db.Products.Where(x => x.Id == productId && x.OnHand <= 1_000_000 - input.Quantity)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.OnHand, x => x.OnHand + input.Quantity));
        if (changed == 0) throw new BusinessException("Product not found or stock limit exceeded.", 409);
        Movement(productId, "Received", input.Quantity, note);
        Log("Stock received", $"+{input.Quantity} units · {note}");
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    public async Task<SalesOrder> Reserve(CreateOrder input)
    {
        tenant.RequireWrite();
        CheckQuantity(input.Quantity);
        var customer = Clean(input.Customer, 100, "Customer");
        if (input.RequestId == Guid.Empty) throw new BusinessException("A non-empty request ID is required.");
        await using var tx = await db.Database.BeginTransactionAsync();
        var existing = await db.Orders.AsNoTracking().SingleOrDefaultAsync(x => x.RequestId == input.RequestId);
        if (existing is not null)
        {
            if (existing.ProductId != input.ProductId || existing.Quantity != input.Quantity || existing.Customer != customer)
                throw new BusinessException("This request ID was already used with different order details.", 409);
            return existing;
        }
        // The predicate and increment execute together in the database: no read-modify-write race.
        var changed = await db.Products.Where(x => x.Id == input.ProductId && x.OnHand - x.Reserved >= input.Quantity)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Reserved, x => x.Reserved + input.Quantity));
        if (changed == 0) throw new BusinessException("Product unavailable or insufficient available stock.", 409);
        var product = await db.Products.AsNoTracking().SingleAsync(x => x.Id == input.ProductId);
        var order = new SalesOrder
        {
            TenantId = tenant.Id,
            RequestId = input.RequestId,
            Customer = customer,
            ProductId = product.Id,
            ProductName = product.Name,
            Quantity = input.Quantity,
            UnitPriceCents = product.PriceCents
        };
        db.Orders.Add(order);
        Movement(product.Id, "Reserved", input.Quantity, $"Order {order.Id.ToString()[..8]}");
        Log("Order reserved", $"{customer} · {input.Quantity} × {product.Name}");
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return order;
    }

    public async Task Transition(Guid orderId, OrderStatus next)
    {
        tenant.RequireWrite();
        if (next is not (OrderStatus.Confirmed or OrderStatus.Cancelled)) throw new BusinessException("Invalid transition.");
        await using var tx = await db.Database.BeginTransactionAsync();
        var changed = await db.Orders.Where(x => x.Id == orderId && x.Status == OrderStatus.Reserved)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, next));
        if (changed == 0) throw new BusinessException("Order not found or no longer reserved.", 409);
        var order = await db.Orders.AsNoTracking().SingleAsync(x => x.Id == orderId);
        var products = db.Products.Where(x => x.Id == order.ProductId && x.Reserved >= order.Quantity);
        var stockChanged = next == OrderStatus.Confirmed
            ? await products.ExecuteUpdateAsync(s => s.SetProperty(x => x.Reserved, x => x.Reserved - order.Quantity).SetProperty(x => x.OnHand, x => x.OnHand - order.Quantity))
            : await products.ExecuteUpdateAsync(s => s.SetProperty(x => x.Reserved, x => x.Reserved - order.Quantity));
        if (stockChanged != 1) throw new BusinessException("Inventory state conflict. No changes were saved.", 409);
        Movement(order.ProductId, next == OrderStatus.Confirmed ? "Shipped" : "Released", order.Quantity, $"Order {order.Id.ToString()[..8]}");
        Log($"Order {next.ToString().ToLowerInvariant()}", $"{order.Customer} · {order.Quantity} × {order.ProductName}");
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    private void Movement(Guid productId, string kind, int quantity, string note) => db.Movements.Add(new StockMovement
    { TenantId = tenant.Id, ProductId = productId, Kind = kind, Quantity = quantity, Note = note, Actor = tenant.Actor });
    private void Log(string action, string detail) => db.Audit.Add(new AuditEntry { TenantId = tenant.Id, Actor = tenant.Actor, Action = action, Detail = detail });
    private static void CheckQuantity(int quantity)
    { if (quantity is < 1 or > 10_000) throw new BusinessException("Quantity must be between 1 and 10,000."); }
    private static string Clean(string? text, int max, string name)
    {
        var value = text?.Trim() ?? "";
        if (value.Length is 0 || value.Length > max) throw new BusinessException($"{name} must contain 1–{max} characters.");
        return value;
    }
}
