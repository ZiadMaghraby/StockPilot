using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using StockPilot.Web.Domain;
using StockPilot.Web.Infrastructure;

namespace StockPilot.Tests;

public class TenantWriteGuardTests
{
    public static IEnumerable<object[]> ForbiddenWrites =>
        from overload in Enumerable.Range(0, 6)
        from operation in new[] { "insert", "update", "delete", "reassign", "no-tenant" }
        select new object[] { overload, operation };

    private static Task<int> Save(AppDbContext db, int overload) => overload switch
    {
        0 => Task.FromResult(db.SaveChanges()),
        1 => Task.FromResult(db.SaveChanges(true)),
        2 => Task.FromResult(db.SaveChanges(false)),
        3 => db.SaveChangesAsync(CancellationToken.None),
        4 => db.SaveChangesAsync(true, CancellationToken.None),
        _ => db.SaveChangesAsync(false, CancellationToken.None)
    };

    private static void SetTenant(HttpContextAccessor accessor, Guid tenantId) =>
        accessor.HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("tenant", tenantId.ToString()) }, "test"))
        };

    [Theory]
    [MemberData(nameof(ForbiddenWrites))]
    public async Task Every_save_overload_rejects_foreign_or_missing_tenant_writes(int overload, string operation)
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var accessor = new HttpContextAccessor();
        var owner = Guid.NewGuid();
        SetTenant(accessor, owner);
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using var db = new AppDbContext(options, new TenantSession(accessor));
        await db.Database.EnsureCreatedAsync();
        var original = new AuditEntry { TenantId = owner, Action = "Original" };
        db.Audit.Add(original);
        await db.SaveChangesAsync();

        var intruder = Guid.NewGuid();
        SetTenant(accessor, operation == "no-tenant" ? Guid.Empty : intruder);
        switch (operation)
        {
            case "insert": db.Audit.Add(new AuditEntry { TenantId = owner, Action = "Foreign" }); break;
            case "update": original.Action = "Changed"; break;
            case "delete": db.Audit.Remove(original); break;
            case "reassign": original.TenantId = intruder; break;
            case "no-tenant": db.Audit.Add(new AuditEntry { TenantId = Guid.Empty, Action = "No tenant" }); break;
        }

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Save(db, overload));
        Assert.Equal("Cross-tenant writes are prohibited.", error.Message);
        db.ChangeTracker.Clear();
        var stored = Assert.Single(await db.Audit.IgnoreQueryFilters().AsNoTracking().ToListAsync());
        Assert.Equal(owner, stored.TenantId);
        Assert.Equal("Original", stored.Action);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public async Task Every_save_overload_allows_owned_writes_and_preserves_accept_flag(int overload)
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var accessor = new HttpContextAccessor();
        var owner = Guid.NewGuid();
        SetTenant(accessor, owner);
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using var db = new AppDbContext(options, new TenantSession(accessor));
        await db.Database.EnsureCreatedAsync();
        var entry = new AuditEntry { TenantId = owner, Action = "Allowed" };
        db.Audit.Add(entry);
        Assert.Equal(1, await Save(db, overload));
        Assert.Equal(overload is 2 or 5 ? EntityState.Added : EntityState.Unchanged, db.Entry(entry).State);
        db.ChangeTracker.Clear();
        Assert.Equal("Allowed", (await db.Audit.SingleAsync()).Action);
    }
}
