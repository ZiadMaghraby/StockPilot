using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using StockPilot.Web.Domain;
using StockPilot.Web.Features;
using StockPilot.Web.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<TenantSession>();
builder.Services.AddDbContext<AppDbContext>(options =>
{
    var connection = builder.Configuration.GetConnectionString("StockPilot") ?? "Data Source=stockpilot.db";
    if (builder.Configuration["Database:Provider"] == "SqlServer") options.UseSqlServer(connection);
    else options.UseSqlite(connection);
});
builder.Services.AddIdentity<AppUser, IdentityRole>(options =>
{
    options.Password.RequiredLength = 12;
    options.User.RequireUniqueEmail = true;
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(10);
}).AddEntityFrameworkStores<AppDbContext>().AddDefaultTokenProviders();
builder.Services.AddScoped<IUserClaimsPrincipalFactory<AppUser>, AppClaimsFactory>();
builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/Denied";
    options.Cookie.Name = "StockPilot.Session";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.Events.OnRedirectToLogin = context =>
    {
        if (context.Request.Path.StartsWithSegments("/api")) context.Response.StatusCode = 401;
        else context.Response.Redirect(context.RedirectUri);
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied = context =>
    {
        if (context.Request.Path.StartsWithSegments("/api")) context.Response.StatusCode = 403;
        else context.Response.Redirect(context.RedirectUri);
        return Task.CompletedTask;
    };
});
builder.Services.AddAntiforgery(options => options.HeaderName = "X-CSRF-TOKEN");
builder.Services.AddRazorPages(options => options.Conventions.AuthorizeFolder("/"));
builder.Services.AddScoped<InventoryService>();
builder.Services.AddOpenApi();
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database");
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
var app = builder.Build();
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
    app.UseHttpsRedirection();
}
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; style-src 'self'; script-src 'self'; img-src 'self' data:; frame-ancestors 'none'; form-action 'self'; base-uri 'self'";
    try { await next(); }
    catch (BusinessException error) when (context.Request.Path.StartsWithSegments("/api"))
    { await Results.Problem(statusCode: error.StatusCode, title: error.Message).ExecuteAsync(context); }
    catch (AntiforgeryValidationException) when (context.Request.Path.StartsWithSegments("/api"))
    { await Results.Problem(statusCode: 400, title: "A valid CSRF token is required.").ExecuteAsync(context); }
});
app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapRazorPages();
app.MapHealthChecks("/health").AllowAnonymous();
app.MapOpenApi().RequireAuthorization();
var api = app.MapGroup("/api").RequireAuthorization();
api.MapGet("/csrf", (IAntiforgery csrf, HttpContext context) => Results.Ok(new { token = csrf.GetAndStoreTokens(context).RequestToken }));
api.MapGet("/products", async (AppDbContext db) => await db.Products.AsNoTracking().OrderBy(x => x.Name).Take(250).ToListAsync());
api.MapGet("/orders", async (AppDbContext db) => await db.Orders.AsNoTracking().OrderByDescending(x => x.CreatedAt).Take(250).ToListAsync());
api.MapGet("/orders/{id:guid}", async (Guid id, AppDbContext db) =>
    await db.Orders.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id) is { } order ? Results.Ok(order) : Results.NotFound());
api.MapGet("/movements", async (AppDbContext db) => await db.Movements.AsNoTracking().OrderByDescending(x => x.CreatedAt).Take(250).ToListAsync());
api.MapGet("/audit", async (AppDbContext db) => await db.Audit.AsNoTracking().OrderByDescending(x => x.CreatedAt).Take(100).ToListAsync());
var writes = api.MapGroup("").RequireAuthorization(policy => policy.RequireRole("Admin", "Operator"));
writes.AddEndpointFilter(async (context, next) =>
{
    await context.HttpContext.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context.HttpContext);
    return await next(context);
});
writes.MapPost("/products", async (CreateProduct input, InventoryService service) => Results.Ok(await service.AddProduct(input)))
    .RequireAuthorization(policy => policy.RequireRole("Admin"));
writes.MapPost("/products/{id:guid}/receive", async (Guid id, ReceiveStock input, InventoryService service) =>
{ await service.Receive(id, input); return Results.NoContent(); });
writes.MapPost("/orders", async (CreateOrder input, InventoryService service) =>
{ var order = await service.Reserve(input); return Results.Created($"/api/orders/{order.Id}", order); });
writes.MapPost("/orders/{id:guid}/confirm", async (Guid id, InventoryService service) =>
{ await service.Transition(id, OrderStatus.Confirmed); return Results.NoContent(); });
writes.MapPost("/orders/{id:guid}/cancel", async (Guid id, InventoryService service) =>
{ await service.Transition(id, OrderStatus.Cancelled); return Results.NoContent(); });
await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    if (app.Configuration.GetValue<bool>("Database:Initialize")) await db.Database.EnsureCreatedAsync();
    if (app.Environment.IsDevelopment() && app.Configuration.GetValue<bool>("Demo:Enabled"))
        await DemoSeed.Run(scope.ServiceProvider);
}
app.Run();
public partial class Program;
