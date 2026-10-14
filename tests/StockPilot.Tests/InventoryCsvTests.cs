using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.VisualBasic.FileIO;
using StockPilot.Web.Domain;
using StockPilot.Web.Features;

namespace StockPilot.Tests;

public partial class InventoryApiTests
{
    private static List<string[]> ParseCsv(string text)
    {
        using var parser = new TextFieldParser(new StringReader(text.TrimStart('\uFEFF')))
        {
            TextFieldType = FieldType.Delimited,
            HasFieldsEnclosedInQuotes = true,
            TrimWhiteSpace = false
        };
        parser.SetDelimiters(",");
        var rows = new List<string[]>();
        while (!parser.EndOfData) rows.Add(parser.ReadFields()!);
        return rows;
    }

    [Fact]
    public async Task Inventory_csv_preserves_cells_and_neutralizes_formulas()
    {
        using var client = await Workspace();
        var input = new CreateProduct("@SUM(1+1)", "=SUM(1,2)", "أجهزة,\n\"quoted\"", 12345, 3);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/products", input)).StatusCode);
        var response = await client.GetAsync("/api/products/export.csv");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType!.MediaType);
        Assert.Contains("stockpilot-inventory.csv", response.Content.Headers.ContentDisposition!.ToString());
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.True(bytes.AsSpan().StartsWith(Encoding.UTF8.GetPreamble()));
        var rows = ParseCsv(Encoding.UTF8.GetString(bytes));
        Assert.Equal(2, rows.Count);
        Assert.Equal(new[] { "'@SUM(1+1)", "'=SUM(1,2)", input.Category, "12345", "0", "0", "0", "3" }, rows[1]);
    }

    [Fact]
    public async Task Inventory_csv_exports_all_matching_rows_and_isolates_companies()
    {
        using var owner = await Workspace();
        var product = await Product(owner);
        await SeedPagedData(product);
        using var other = await Workspace();
        await Product(other, 0, "SECRET-OTHER-COMPANY");

        var csv = await owner.GetStringAsync("/api/products/export.csv?search=PAGE-&lowStock=true");
        var rows = ParseCsv(csv);
        Assert.Equal(261, rows.Count); // Header plus all 260 matching products, beyond API page size.
        Assert.Equal(260, rows.Skip(1).Select(row => row[0]).Distinct().Count());
        Assert.DoesNotContain("SECRET-OTHER-COMPANY", csv);
        Assert.All(rows.Skip(1), row => Assert.StartsWith("PAGE-", row[0]));
        Assert.Single(ParseCsv(await owner.GetStringAsync("/api/products/export.csv?search=PAGE-&lowStock=false")));
        using var empty = await Workspace();
        Assert.Single(ParseCsv(await empty.GetStringAsync("/api/products/export.csv")));
    }

    [Fact]
    public async Task Inventory_csv_requires_authentication_and_validates_filters()
    {
        using var anonymous = app.NewClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/products/export.csv")).StatusCode);
        using var owner = await Workspace();
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.GetAsync("/api/products/export.csv?lowStock=invalid")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.GetAsync("/api/products/export.csv?search=" + new string('a', 101))).StatusCode);
    }

    [Theory]
    [InlineData("=1+2")]
    [InlineData("+1+2")]
    [InlineData("-1+2")]
    [InlineData("@SUM(1,2)")]
    [InlineData("\ttext")]
    [InlineData("\rtext")]
    [InlineData("\ntext")]
    [InlineData("  =1+2")]
    [InlineData("\uFEFF=1+2")]
    [InlineData("＝1+2")]
    [InlineData("＋1+2")]
    [InlineData("－1+2")]
    [InlineData("＠SUM(1,2)")]
    public void Csv_text_cells_protect_formula_prefixes(string input)
    {
        var row = Assert.Single(ParseCsv(InventoryCsv.Cell(input)));
        Assert.Equal("'" + input, Assert.Single(row));
    }
}
