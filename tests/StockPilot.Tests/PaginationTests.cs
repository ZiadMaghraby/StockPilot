using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StockPilot.Web.Domain;
using StockPilot.Web.Infrastructure;

namespace StockPilot.Tests;

public partial class InventoryApiTests
{
    private async Task SeedPagedData(Product product)
    {
        using var scope = app.Services.CreateScope();
        var accessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("tenant", product.TenantId.ToString())]))
            }
        };
        await using var db = new AppDbContext(scope.ServiceProvider.GetRequiredService<DbContextOptions<AppDbContext>>(), new TenantSession(accessor));
        var createdAt = DateTime.UtcNow;
        for (var i = 0; i < 260; i++)
        {
            db.Products.Add(new Product { TenantId = product.TenantId, Name = product.Name, Sku = $"PAGE-{i:D3}" });
            db.Orders.Add(new SalesOrder { TenantId = product.TenantId, ProductId = product.Id, RequestId = Guid.NewGuid(), Quantity = 1, CreatedAt = createdAt });
            db.Movements.Add(new StockMovement { TenantId = product.TenantId, ProductId = product.Id, Quantity = 1, CreatedAt = createdAt });
            db.Audit.Add(new AuditEntry { TenantId = product.TenantId, CreatedAt = createdAt });
        }
        await db.SaveChangesAsync();
    }

    [Theory]
    [InlineData("products", 261, 250)]
    [InlineData("orders", 260, 250)]
    [InlineData("movements", 261, 250)]
    [InlineData("audit", 262, 100)]
    public async Task Lists_page_beyond_old_limits_without_duplicates_or_tenant_leaks(string route, int total, int defaultSize)
    {
        using var owner = await Workspace();
        var product = await Product(owner);
        await SeedPagedData(product);
        using var other = await Workspace();
        await Product(other);

        var defaultResponse = await owner.GetAsync($"/api/{route}");
        Assert.Equal(defaultSize, (await defaultResponse.Content.ReadFromJsonAsync<JsonElement[]>())!.Length);
        Assert.Equal(total.ToString(), Assert.Single(defaultResponse.Headers.GetValues("X-Total-Count")));
        Assert.Equal(defaultSize.ToString(), Assert.Single(defaultResponse.Headers.GetValues("X-Page-Size")));

        var ids = new List<string>();
        for (var page = 1; page <= 6; page++)
        {
            var response = await owner.GetAsync($"/api/{route}?page={page}&pageSize=50");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(page.ToString(), Assert.Single(response.Headers.GetValues("X-Page")));
            Assert.Equal(total.ToString(), Assert.Single(response.Headers.GetValues("X-Total-Count")));
            var rows = (await response.Content.ReadFromJsonAsync<JsonElement[]>())!;
            Assert.Equal(Math.Min(50, Math.Max(0, total - (page - 1) * 50)), rows.Length);
            Assert.All(rows, row => Assert.Equal(product.TenantId, row.GetProperty("tenantId").GetGuid()));
            ids.AddRange(rows.Select(row => row.GetProperty("id").ToString()));
            Assert.Equal(await response.Content.ReadAsStringAsync(), await owner.GetStringAsync($"/api/{route}?page={page}&pageSize=50"));
        }
        Assert.Equal(total, ids.Distinct().Count());
        // The first 260 seeded orders/movements/audit records share one timestamp.
        if (route == "products") Assert.Equal(ids.OrderBy(id => id, StringComparer.Ordinal), ids);
        else if (route == "orders") Assert.Equal(ids.OrderByDescending(id => id, StringComparer.Ordinal), ids);
        else Assert.Equal(ids.Take(260).Select(long.Parse).OrderDescending(), ids.Take(260).Select(long.Parse));

        var beyond = await owner.GetAsync($"/api/{route}?page=2147483647&pageSize=250");
        Assert.Equal(HttpStatusCode.OK, beyond.StatusCode);
        Assert.Empty((await beyond.Content.ReadFromJsonAsync<JsonElement[]>())!);

        using var empty = await Workspace();
        var emptyResponse = await empty.GetAsync($"/api/{route}?page=2&pageSize=50");
        Assert.Equal("0", Assert.Single(emptyResponse.Headers.GetValues("X-Total-Count")));
        Assert.Empty((await emptyResponse.Content.ReadFromJsonAsync<JsonElement[]>())!);
    }

    [Theory]
    [InlineData("page=0")]
    [InlineData("page=-1")]
    [InlineData("page=not-a-number")]
    [InlineData("page=2147483648")]
    [InlineData("pageSize=0")]
    [InlineData("pageSize=-1")]
    [InlineData("pageSize=251")]
    public async Task Invalid_pagination_is_rejected_on_every_list(string query)
    {
        using var client = await Workspace();
        foreach (var route in new[] { "products", "orders", "movements", "audit" })
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/api/{route}?{query}")).StatusCode);
    }

    [Fact]
    public async Task Pagination_requires_authentication_and_is_described_in_openapi()
    {
        using var anonymous = app.NewClient();
        using var member = await Workspace();
        var openapi = await member.GetFromJsonAsync<JsonElement>("/openapi/v1.json");
        foreach (var route in new[] { "products", "orders", "movements", "audit" })
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/{route}?page=2")).StatusCode);
            var operation = openapi.GetProperty("paths").GetProperty($"/api/{route}").GetProperty("get");
            var parameters = operation.GetProperty("parameters").EnumerateArray().Select(p => p.GetProperty("name").GetString()).ToList();
            Assert.Contains("page", parameters);
            Assert.Contains("pageSize", parameters);
            Assert.Contains("X-Total-Count", operation.GetProperty("description").GetString());
        }
    }
}
