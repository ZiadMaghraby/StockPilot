using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using StockPilot.Web.Domain;

namespace StockPilot.Web.Features;

public static class InventoryCsv
{
    public static IResult Export(IQueryable<Product> query, CancellationToken cancellationToken) => Results.Stream(async stream =>
    {
        await using var writer = new StreamWriter(stream, new UTF8Encoding(true), 1024, leaveOpen: true) { NewLine = "\r\n" };
        await writer.WriteLineAsync("SKU,Name,Category,PriceCents,OnHand,Reserved,Available,ReorderPoint".AsMemory(), cancellationToken);
        await foreach (var product in query.OrderBy(x => x.Name).ThenBy(x => x.Id).AsAsyncEnumerable().WithCancellation(cancellationToken))
        {
            var row = string.Join(",", Cell(product.Sku), Cell(product.Name), Cell(product.Category),
                product.PriceCents.ToString(CultureInfo.InvariantCulture), product.OnHand.ToString(CultureInfo.InvariantCulture),
                product.Reserved.ToString(CultureInfo.InvariantCulture), product.Available.ToString(CultureInfo.InvariantCulture),
                product.ReorderPoint.ToString(CultureInfo.InvariantCulture));
            await writer.WriteLineAsync(row.AsMemory(), cancellationToken);
        }
    }, contentType: "text/csv; charset=utf-8", fileDownloadName: "stockpilot-inventory.csv");

    public static string Cell(string value)
    {
        var start = 0;
        while (start < value.Length && (char.IsWhiteSpace(value[start]) || value[start] == '\uFEFF')) start++;
        if ((value.Length > 0 && value[0] is '\t' or '\r' or '\n') ||
            (start < value.Length && "=+-@＝＋－＠".Contains(value[start])))
            value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
