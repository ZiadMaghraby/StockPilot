using System.Globalization;
using Microsoft.EntityFrameworkCore;
using StockPilot.Web.Domain;

namespace StockPilot.Web.Features;

public static class PagedQuery
{
    public static async Task<List<T>> Read<T>(IOrderedQueryable<T> query, HttpContext context, int page, int pageSize)
    {
        if (page < 1 || pageSize is < 1 or > 250)
            throw new BusinessException("Page must be at least 1 and pageSize must be between 1 and 250.");

        var total = await query.CountAsync(context.RequestAborted);
        context.Response.Headers["X-Total-Count"] = total.ToString(CultureInfo.InvariantCulture);
        context.Response.Headers["X-Page"] = page.ToString(CultureInfo.InvariantCulture);
        context.Response.Headers["X-Page-Size"] = pageSize.ToString(CultureInfo.InvariantCulture);

        // Calculate in 64 bits so even int.MaxValue pages cannot wrap into an earlier page.
        var offset = ((long)page - 1) * pageSize;
        return offset >= total ? [] : await query.Skip((int)offset).Take(pageSize).ToListAsync(context.RequestAborted);
    }
}
