using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using StockPilot.Web.Domain;
using StockPilot.Web.Features;

namespace StockPilot.Tests;

public sealed class TestApp : WebApplicationFactory<Program>
{
    private readonly string database = Path.Combine(Path.GetTempPath(), $"stockpilot-test-{Guid.NewGuid():N}.db");
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:StockPilot"] = $"Data Source={database};Default Timeout=30;Pooling=False",
            ["Database:Initialize"] = "true",
            ["Demo:Enabled"] = "false",
            ["Logging:LogLevel:Default"] = "Warning"
        }));
    }
    public HttpClient NewClient() => CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) foreach (var suffix in new[] { "", "-shm", "-wal" }) File.Delete(database + suffix);
    }
}

public class InventoryApiTests : IDisposable
{
    private readonly TestApp app = new();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private const string Password = "TestPassword!2026";
    public void Dispose() => app.Dispose();

    private static async Task<string> FormToken(HttpClient client, string path)
    {
        var html = await client.GetStringAsync(path);
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(match.Success, "The page must include an antiforgery token.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }
    private async Task<HttpClient> Workspace()
    {
        var client = app.NewClient();
        var token = await FormToken(client, "/Account/Register");
        var response = await client.PostAsync("/Account/Register", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Company"] = "Test workspace",
            ["Email"] = $"{Guid.NewGuid():N}@example.test",
            ["Password"] = Password
        }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        await Csrf(client);
        return client;
    }
    private static async Task Csrf(HttpClient client)
    {
        var document = await client.GetFromJsonAsync<JsonElement>("/api/csrf");
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", document.GetProperty("token").GetString());
    }
    private static async Task<Product> Product(HttpClient client, int stock = 10, string sku = "TEST-001")
    {
        var response = await client.PostAsJsonAsync("/api/products", new CreateProduct(sku, "Test keyboard", "Hardware", 1250, 3));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var product = (await response.Content.ReadFromJsonAsync<Product>(Json))!;
        if (stock > 0) Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync($"/api/products/{product.Id}/receive", new ReceiveStock(stock, "PO-TEST"))).StatusCode);
        return product;
    }
    private static async Task<SalesOrder> Reserve(HttpClient client, Product product, int quantity = 1, Guid? key = null)
    {
        var response = await client.PostAsJsonAsync("/api/orders", new CreateOrder(product.Id, quantity, "Customer", key ?? Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<SalesOrder>(Json))!;
    }
    private static async Task<Product> ReadProduct(HttpClient client) => Assert.Single((await client.GetFromJsonAsync<List<Product>>("/api/products", Json))!);

    [Fact]
    public async Task Anonymous_api_access_is_unauthorized()
    {
        using var client = app.NewClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/products")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
    }

    [Fact]
    public async Task Writes_require_antiforgery_token()
    {
        using var client = await Workspace();
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/products", new CreateProduct("X", "X", "X", 10, 1))).StatusCode);
    }

    [Fact]
    public async Task Companies_cannot_read_or_reserve_each_others_stock()
    {
        using var owner = await Workspace();
        using var other = await Workspace();
        var product = await Product(owner);
        var order = await Reserve(owner, product);
        Assert.Empty((await other.GetFromJsonAsync<List<Product>>("/api/products", Json))!);
        Assert.Empty((await other.GetFromJsonAsync<List<SalesOrder>>("/api/orders", Json))!);
        Assert.Empty((await other.GetFromJsonAsync<List<AuditEntry>>("/api/audit", Json))!);
        Assert.Empty((await other.GetFromJsonAsync<List<StockMovement>>("/api/movements", Json))!);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/orders/{order.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await other.PostAsJsonAsync("/api/orders", new CreateOrder(product.Id, 1, "Intruder", Guid.NewGuid()))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await other.PostAsync($"/api/orders/{order.Id}/cancel", null)).StatusCode);
        Assert.Equal(1, (await ReadProduct(owner)).Reserved);
    }

    [Fact]
    public async Task Last_unit_can_only_be_reserved_once_under_concurrent_requests()
    {
        using var client = await Workspace();
        var product = await Product(client, stock: 1);
        var attempts = Enumerable.Range(0, 8).Select(_ => client.PostAsJsonAsync("/api/orders", new CreateOrder(product.Id, 1, "Concurrent buyer", Guid.NewGuid())));
        var responses = await Task.WhenAll(attempts);
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Created);
        Assert.Equal(7, responses.Count(x => x.StatusCode == HttpStatusCode.Conflict));
        var remaining = await ReadProduct(client);
        Assert.Equal(0, remaining.Available);
        Assert.Equal(1, remaining.OnHand);
        Assert.Equal(1, remaining.Reserved);
    }

    [Fact]
    public async Task Retrying_the_same_order_returns_original_without_reserving_twice()
    {
        using var client = await Workspace();
        var product = await Product(client);
        var requestId = Guid.NewGuid();
        var first = await Reserve(client, product, 3, requestId);
        var retry = await Reserve(client, product, 3, requestId);
        Assert.Equal(first.Id, retry.Id);
        Assert.Equal(3, (await ReadProduct(client)).Reserved);
        Assert.Single((await client.GetFromJsonAsync<List<SalesOrder>>("/api/orders", Json))!);
    }

    [Fact]
    public async Task Idempotency_key_reuse_with_different_payload_is_rejected()
    {
        using var client = await Workspace();
        var product = await Product(client);
        var key = Guid.NewGuid();
        await Reserve(client, product, 2, key);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/orders", new CreateOrder(product.Id, 3, "Customer", key))).StatusCode);
        Assert.Equal(2, (await ReadProduct(client)).Reserved);
    }

    [Fact]
    public async Task Confirming_ships_stock_and_cannot_be_repeated_or_cancelled()
    {
        using var client = await Workspace();
        var product = await Product(client);
        var order = await Reserve(client, product, 3);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync($"/api/orders/{order.Id}/confirm", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync($"/api/orders/{order.Id}/confirm", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync($"/api/orders/{order.Id}/cancel", null)).StatusCode);
        var stock = await ReadProduct(client);
        Assert.Equal(7, stock.OnHand);
        Assert.Equal(0, stock.Reserved);
        var movements = (await client.GetFromJsonAsync<List<StockMovement>>("/api/movements", Json))!;
        Assert.Equal(3, movements.Count);
        Assert.Single(movements, x => x.Kind == "Shipped");
    }

    [Fact]
    public async Task Cancellation_releases_stock_without_reducing_on_hand()
    {
        using var client = await Workspace();
        var product = await Product(client);
        var order = await Reserve(client, product, 4);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync($"/api/orders/{order.Id}/cancel", null)).StatusCode);
        var stock = await ReadProduct(client);
        Assert.Equal(10, stock.OnHand);
        Assert.Equal(0, stock.Reserved);
        await Reserve(client, product, 10);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(10001)]
    public async Task Invalid_quantities_leave_stock_unchanged(int quantity)
    {
        using var client = await Workspace();
        var product = await Product(client);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/orders", new CreateOrder(product.Id, quantity, "Customer", Guid.NewGuid()))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/products/{product.Id}/receive", new ReceiveStock(quantity, "Invalid"))).StatusCode);
        Assert.Equal(10, (await ReadProduct(client)).Available);
    }

    [Fact]
    public async Task Overselling_rolls_back_order_and_audit_records()
    {
        using var client = await Workspace();
        var product = await Product(client, 2);
        var before = (await client.GetFromJsonAsync<List<AuditEntry>>("/api/audit", Json))!.Count;
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/orders", new CreateOrder(product.Id, 3, "Customer", Guid.NewGuid()))).StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<List<SalesOrder>>("/api/orders", Json))!);
        Assert.Equal(before, (await client.GetFromJsonAsync<List<AuditEntry>>("/api/audit", Json))!.Count);
        Assert.Equal(2, (await ReadProduct(client)).Available);
    }

    [Fact]
    public async Task Sku_is_unique_within_company_but_reusable_in_another_company()
    {
        using var first = await Workspace();
        using var second = await Workspace();
        await Product(first);
        Assert.Equal(HttpStatusCode.Conflict, (await first.PostAsJsonAsync("/api/products", new CreateProduct("test-001", "Duplicate", "Hardware", 100, 3))).StatusCode);
        await Product(second);
    }

    [Theory]
    [InlineData("Viewer", HttpStatusCode.Forbidden)]
    [InlineData("Operator", HttpStatusCode.Created)]
    public async Task Roles_restrict_writes_and_admin_pages(string role, HttpStatusCode expectedOrderStatus)
    {
        using var admin = await Workspace();
        var product = await Product(admin);
        var email = $"{role.ToLowerInvariant()}@example.test";
        var token = await FormToken(admin, "/Team");
        Assert.Equal(HttpStatusCode.Redirect, (await admin.PostAsync("/Team", new FormUrlEncodedContent(new Dictionary<string, string>
        { ["__RequestVerificationToken"] = token, ["Email"] = email, ["Password"] = Password, ["AccessRole"] = role }))).StatusCode);
        using var member = app.NewClient();
        var loginToken = await FormToken(member, "/Account/Login");
        Assert.Equal(HttpStatusCode.Redirect, (await member.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        { ["__RequestVerificationToken"] = loginToken, ["Email"] = email, ["Password"] = Password }))).StatusCode);
        await Csrf(member);
        Assert.Equal(HttpStatusCode.OK, (await member.GetAsync("/api/products")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.PostAsJsonAsync("/api/products", new CreateProduct("X", "X", "X", 10, 1))).StatusCode);
        Assert.Equal(expectedOrderStatus, (await member.PostAsJsonAsync("/api/orders", new CreateOrder(product.Id, 1, "Customer", Guid.NewGuid()))).StatusCode);
        var team = await member.GetAsync("/Team");
        Assert.Equal(HttpStatusCode.Redirect, team.StatusCode);
        Assert.StartsWith("https://localhost/Account/Denied", team.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Concurrent_retries_share_one_order_and_one_reservation()
    {
        using var client = await Workspace();
        var product = await Product(client);
        var request = new CreateOrder(product.Id, 3, "Customer", Guid.NewGuid());
        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => client.PostAsJsonAsync("/api/orders", request)));
        Assert.All(responses, response => Assert.Equal(HttpStatusCode.Created, response.StatusCode));
        var orders = await Task.WhenAll(responses.Select(response => response.Content.ReadFromJsonAsync<SalesOrder>(Json)));
        Assert.Single(orders.Select(order => order!.Id).Distinct());
        Assert.Equal(3, (await ReadProduct(client)).Reserved);
    }

    [Fact]
    public async Task Concurrent_ship_and_cancel_have_exactly_one_winner()
    {
        using var client = await Workspace();
        var product = await Product(client, 10);
        var order = await Reserve(client, product, 3);
        var responses = await Task.WhenAll(client.PostAsync($"/api/orders/{order.Id}/confirm", null), client.PostAsync($"/api/orders/{order.Id}/cancel", null));
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.NoContent);
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
        var final = (await client.GetFromJsonAsync<SalesOrder>($"/api/orders/{order.Id}", Json))!;
        var stock = await ReadProduct(client);
        Assert.Equal(0, stock.Reserved);
        Assert.Equal(final.Status == OrderStatus.Confirmed ? 7 : 10, stock.OnHand);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(100000001)]
    public async Task Invalid_prices_do_not_create_products(long price)
    {
        using var client = await Workspace();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/products", new CreateProduct("INVALID", "Invalid price", "General", price, 1))).StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<List<Product>>("/api/products", Json))!);
    }

    [Fact]
    public async Task Dashboard_pages_and_openapi_render_for_authenticated_admin()
    {
        using var client = await Workspace();
        await Product(client);
        foreach (var path in new[] { "/", "/Inventory", "/Orders", "/Activity", "/Team", "/openapi/v1.json" })
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(path)).StatusCode);
    }

    [Theory]
    [InlineData("RequestId", "not-a-guid")]
    [InlineData("productId", "not-a-guid")]
    [InlineData("quantity", "three")]
    public async Task Malformed_order_form_does_not_create_an_order_or_reserve_stock(string field, string invalidValue)
    {
        using var client = await Workspace();
        var product = await Product(client);
        var token = await FormToken(client, "/Orders");
        var auditBefore = (await client.GetFromJsonAsync<List<AuditEntry>>("/api/audit", Json))!.Count;
        var movementsBefore = (await client.GetFromJsonAsync<List<StockMovement>>("/api/movements", Json))!.Count;

        var form = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["RequestId"] = Guid.NewGuid().ToString(),
            ["productId"] = product.Id.ToString(),
            ["quantity"] = "3",
            ["customer"] = "Form customer"
        };
        form[field] = invalidValue;
        var response = await client.PostAsync("/Orders?handler=Create", new FormUrlEncodedContent(form));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(invalidValue, await response.Content.ReadAsStringAsync());
        Assert.Empty((await client.GetFromJsonAsync<List<SalesOrder>>("/api/orders", Json))!);
        var stock = await ReadProduct(client);
        Assert.Equal(10, stock.OnHand);
        Assert.Equal(0, stock.Reserved);
        Assert.Equal(auditBefore, (await client.GetFromJsonAsync<List<AuditEntry>>("/api/audit", Json))!.Count);
        Assert.Equal(movementsBefore, (await client.GetFromJsonAsync<List<StockMovement>>("/api/movements", Json))!.Count);
    }

    [Fact]
    public async Task Valid_order_form_retries_keep_one_reservation()
    {
        using var client = await Workspace();
        var product = await Product(client);
        var token = await FormToken(client, "/Orders");
        var form = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["RequestId"] = Guid.NewGuid().ToString(),
            ["productId"] = product.Id.ToString(),
            ["quantity"] = "3",
            ["customer"] = "Form customer"
        };
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var response = await client.PostAsync("/Orders?handler=Create", new FormUrlEncodedContent(form));
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        }
        Assert.Single((await client.GetFromJsonAsync<List<SalesOrder>>("/api/orders", Json))!);
        var stock = await ReadProduct(client);
        Assert.Equal(10, stock.OnHand);
        Assert.Equal(3, stock.Reserved);
        Assert.Single((await client.GetFromJsonAsync<List<StockMovement>>("/api/movements", Json))!, x => x.Kind == "Reserved");
    }
}

