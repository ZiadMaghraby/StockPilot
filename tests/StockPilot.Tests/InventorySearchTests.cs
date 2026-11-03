using System.Net;
namespace StockPilot.Tests;
public partial class InventoryApiTests
{
    [Theory]
    [InlineData("  TEST-001  ")]
    [InlineData("  keyboard  ")]
    public async Task Inventory_page_trims_pasted_search_terms(string search)
    {
        using var client = await Workspace();
        await Product(client);
        var response = await client.GetAsync("/Inventory?Search=" + Uri.EscapeDataString(search));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Test keyboard", await response.Content.ReadAsStringAsync());
    }
}
