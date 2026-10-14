using StockPilot.Web.Domain;

namespace StockPilot.Web.Features;

public static class ListFilters
{
    public static IQueryable<Product> Products(IQueryable<Product> query, string? search, bool? lowStock)
    {
        var term = search?.Trim();
        if (term?.Length > 100) throw new BusinessException("Search must contain at most 100 characters.");
        if (!string.IsNullOrEmpty(term))
            query = query.Where(x => x.Name.Contains(term) || x.Sku.Contains(term));
        if (lowStock is true) query = query.Where(x => x.OnHand - x.Reserved <= x.ReorderPoint);
        if (lowStock is false) query = query.Where(x => x.OnHand - x.Reserved > x.ReorderPoint);
        return query;
    }

    public static IQueryable<SalesOrder> Orders(IQueryable<SalesOrder> query, OrderStatus? status, DateTimeOffset? createdFrom, DateTimeOffset? createdBefore)
    {
        if (status.HasValue && !Enum.IsDefined(status.Value)) throw new BusinessException("Select a valid order status.");
        if (createdFrom.HasValue && createdBefore.HasValue && createdFrom >= createdBefore)
            throw new BusinessException("createdFrom must be earlier than createdBefore.");
        if (status.HasValue) query = query.Where(x => x.Status == status.Value);
        if (createdFrom.HasValue)
        {
            var from = createdFrom.Value.UtcDateTime;
            query = query.Where(x => x.CreatedAt >= from);
        }
        if (createdBefore.HasValue)
        {
            var before = createdBefore.Value.UtcDateTime;
            query = query.Where(x => x.CreatedAt < before);
        }
        return query;
    }
}
