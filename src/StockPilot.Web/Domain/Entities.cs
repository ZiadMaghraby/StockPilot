using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;

namespace StockPilot.Web.Domain;

public class AppUser : IdentityUser
{
    public Guid TenantId { get; set; }
    [MaxLength(100)] public string CompanyName { get; set; } = "";
    [MaxLength(20)] public string AccessRole { get; set; } = "Viewer";
}

public interface ITenantOwned { Guid TenantId { get; set; } }

public class Product : ITenantOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    [MaxLength(40)] public string Sku { get; set; } = "";
    [MaxLength(100)] public string Name { get; set; } = "";
    [MaxLength(40)] public string Category { get; set; } = "General";
    public long PriceCents { get; set; }
    public int OnHand { get; set; }
    public int Reserved { get; set; }
    public int ReorderPoint { get; set; } = 10;
    public int Available => OnHand - Reserved;
}

public enum OrderStatus { Reserved, Confirmed, Cancelled }

public class SalesOrder : ITenantOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid RequestId { get; set; }
    [MaxLength(100)] public string Customer { get; set; } = "";
    public Guid ProductId { get; set; }
    [MaxLength(100)] public string ProductName { get; set; } = "";
    public int Quantity { get; set; }
    public long UnitPriceCents { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.Reserved;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public long TotalCents => Quantity * UnitPriceCents;
}

public class StockMovement : ITenantOwned
{
    public long Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ProductId { get; set; }
    [MaxLength(30)] public string Kind { get; set; } = "";
    public int Quantity { get; set; }
    [MaxLength(200)] public string Note { get; set; } = "";
    [MaxLength(256)] public string Actor { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class AuditEntry : ITenantOwned
{
    public long Id { get; set; }
    public Guid TenantId { get; set; }
    [MaxLength(256)] public string Actor { get; set; } = "";
    [MaxLength(50)] public string Action { get; set; } = "";
    [MaxLength(300)] public string Detail { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class BusinessException(string message, int statusCode = 400) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
