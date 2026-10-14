using System.Net;
using System.Net.Http.Json;
using StockPilot.Web.Domain;

namespace StockPilot.Tests;

public partial class InventoryApiTests
{
    [Fact]
    public async Task Product_filters_use_available_stock_and_filter_before_paging()
    {
        using var owner = await Workspace();
        var low = await Product(owner, 10, "FILTER-LOW");
        await Reserve(owner, low, 7); // Available equals the reorder point, despite ten units on hand.
        var empty = await Product(owner, 0, "FILTER-EMPTY");
        var high = await Product(owner, 10, "FILTER-HIGH");
        using var other = await Workspace();
        await Product(other, 0, "FILTER-LOW");

        var response = await owner.GetAsync("/api/products?search=%20FILTER-%20&lowStock=true&pageSize=1&page=2");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("2", Assert.Single(response.Headers.GetValues("X-Total-Count")));
        var second = Assert.Single((await response.Content.ReadFromJsonAsync<List<Product>>(Json))!);
        var first = Assert.Single((await owner.GetFromJsonAsync<List<Product>>("/api/products?search=FILTER-&lowStock=true&pageSize=1", Json))!);
        Assert.Equal(new[] { low.Id, empty.Id }.Order(), new[] { first.Id, second.Id }.Order());

        var above = Assert.Single((await owner.GetFromJsonAsync<List<Product>>("/api/products?lowStock=false", Json))!);
        Assert.Equal(high.Id, above.Id);
        Assert.Equal(3, (await owner.GetFromJsonAsync<List<Product>>("/api/products?search=keyboard", Json))!.Count);
        Assert.Equal(3, (await owner.GetFromJsonAsync<List<Product>>("/api/products?search=%20%20", Json))!.Count);
        Assert.Empty((await owner.GetFromJsonAsync<List<Product>>("/api/products?search=%25", Json))!);
        Assert.Empty((await owner.GetFromJsonAsync<List<Product>>("/api/products?search=missing", Json))!);
    }

    [Theory]
    [InlineData("Reserved", 87)]
    [InlineData("Confirmed", 87)]
    [InlineData("Cancelled", 86)]
    public async Task Order_filters_combine_status_utc_dates_and_pagination(string status, int count)
    {
        var timestamp = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);
        using var owner = await Workspace();
        var product = await Product(owner);
        await SeedPagedData(product, timestamp);
        using var other = await Workspace();
        await SeedPagedData(await Product(other), timestamp);

        // 03:00 +03:00 is the exact inclusive midnight UTC boundary.
        var query = $"status={status}&createdFrom=2026-01-02T03:00:00%2B03:00&createdBefore=2026-01-03T00:00:00Z";
        var response = await owner.GetAsync($"/api/orders?{query}&page=2&pageSize=50");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(count.ToString(), Assert.Single(response.Headers.GetValues("X-Total-Count")));
        var orders = (await response.Content.ReadFromJsonAsync<List<SalesOrder>>(Json))!;
        Assert.Equal(count - 50, orders.Count);
        Assert.All(orders, order =>
        {
            Assert.Equal(product.TenantId, order.TenantId);
            Assert.Equal(status, order.Status.ToString());
            Assert.Equal(timestamp, order.CreatedAt);
        });
        Assert.Empty((await owner.GetFromJsonAsync<List<SalesOrder>>("/api/orders?createdBefore=2026-01-02T00:00:00Z", Json))!);
        Assert.Empty((await owner.GetFromJsonAsync<List<SalesOrder>>("/api/orders?createdFrom=2026-01-02T00:00:01Z", Json))!);
    }

    [Theory]
    [InlineData("products?lowStock=maybe")]
    [InlineData("orders?status=Unknown")]
    [InlineData("orders?status=99")]
    [InlineData("orders?createdFrom=invalid")]
    [InlineData("orders?createdFrom=2026-01-03T00:00:00Z&createdBefore=2026-01-02T00:00:00Z")]
    [InlineData("orders?createdFrom=2026-01-02T00:00:00Z&createdBefore=2026-01-02T00:00:00Z")]
    public async Task Invalid_list_filters_return_bad_request(string path)
    {
        using var client = await Workspace();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/api/{path}")).StatusCode);
    }

    [Fact]
    public async Task Product_search_length_is_bounded()
    {
        using var client = await Workspace();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/products?search=" + new string('a', 100))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/products?search=" + new string('a', 101))).StatusCode);
    }
}
