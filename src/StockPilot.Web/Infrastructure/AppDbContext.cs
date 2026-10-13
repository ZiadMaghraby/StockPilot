using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using StockPilot.Web.Domain;

namespace StockPilot.Web.Infrastructure;

public class AppDbContext(DbContextOptions<AppDbContext> options, TenantSession tenant)
    : IdentityDbContext<AppUser>(options)
{
    private Guid TenantId => tenant.Id;
    public DbSet<Product> Products => Set<Product>();
    public DbSet<SalesOrder> Orders => Set<SalesOrder>();
    public DbSet<StockMovement> Movements => Set<StockMovement>();
    public DbSet<AuditEntry> Audit => Set<AuditEntry>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        b.Entity<Product>().HasQueryFilter(x => x.TenantId == TenantId);
        b.Entity<SalesOrder>().HasQueryFilter(x => x.TenantId == TenantId);
        b.Entity<StockMovement>().HasQueryFilter(x => x.TenantId == TenantId);
        b.Entity<AuditEntry>().HasQueryFilter(x => x.TenantId == TenantId);
        b.Entity<Product>().HasIndex(x => new { x.TenantId, x.Sku }).IsUnique();
        b.Entity<Product>().HasAlternateKey(x => new { x.TenantId, x.Id });
        b.Entity<Product>().ToTable(t =>
        {
            t.HasCheckConstraint("CK_Product_Stock", "[OnHand] >= 0 AND [Reserved] >= 0 AND [Reserved] <= [OnHand]");
            t.HasCheckConstraint("CK_Product_Price", "[PriceCents] >= 0");
        });
        b.Entity<SalesOrder>().HasIndex(x => new { x.TenantId, x.RequestId }).IsUnique();
        b.Entity<SalesOrder>().HasIndex(x => new { x.TenantId, x.CreatedAt });
        b.Entity<SalesOrder>().Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Entity<SalesOrder>().HasOne<Product>().WithMany().HasForeignKey(x => new { x.TenantId, x.ProductId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<SalesOrder>().ToTable(t => t.HasCheckConstraint("CK_Order_Quantity", "[Quantity] > 0"));
        b.Entity<StockMovement>().HasOne<Product>().WithMany().HasForeignKey(x => new { x.TenantId, x.ProductId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<AuditEntry>().HasIndex(x => new { x.TenantId, x.CreatedAt });
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ValidateTenantWrites();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ValidateTenantWrites();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void ValidateTenantWrites()
    {
        foreach (var entry in ChangeTracker.Entries<ITenantOwned>().Where(x => x.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
        {
            if (TenantId == Guid.Empty || entry.Entity.TenantId != TenantId ||
                (entry.State != EntityState.Added && entry.Property(nameof(ITenantOwned.TenantId)).OriginalValue is Guid original && original != TenantId))
                throw new InvalidOperationException("Cross-tenant writes are prohibited.");
        }
    }
}
