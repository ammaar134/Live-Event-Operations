using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Fieldroom;

public sealed class LibraryDb(DbContextOptions<LibraryDb> options) : IdentityDbContext<Member>(options)
{
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Asset> Assets => Set<Asset>();
    public DbSet<Allocation> Allocations => Set<Allocation>();
    public DbSet<Checkout> Checkouts => Set<Checkout>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<Idempotency> Idempotency => Set<Idempotency>();
    public DbSet<LendingPolicy> Policies => Set<LendingPolicy>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        b.HasPostgresExtension("btree_gist");
        b.Entity<Product>().Property(x => x.Name).HasMaxLength(120);
        b.Entity<Asset>().HasIndex(x => x.Tag).IsUnique();
        b.Entity<Asset>().HasOne(x => x.Product).WithMany(x => x.Assets).HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Allocation>().Property(x => x.Occupied).HasColumnType("tstzrange");
        b.Entity<Allocation>().Property(x => x.PolicyJson).HasColumnType("jsonb");
        b.Entity<Allocation>().HasIndex(x => x.Reference).IsUnique();
        b.Entity<Allocation>().HasIndex(x => new { x.MemberId, x.State });
        b.Entity<Allocation>().HasOne(x => x.Asset).WithMany().HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Allocation>().HasOne(x => x.Member).WithMany().HasForeignKey(x => x.MemberId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Allocation>().HasAlternateKey(x => new { x.Id, x.AssetId });
        b.Entity<Checkout>().HasOne(x => x.Allocation).WithMany().HasForeignKey(x => new { x.AllocationId, x.AssetId }).HasPrincipalKey(x => new { x.Id, x.AssetId }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Checkout>().HasOne(x => x.Asset).WithMany().HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Checkout>().HasIndex(x => x.AllocationId).IsUnique();
        b.Entity<Checkout>().HasIndex(x => x.AssetId).IsUnique().HasFilter("\"ReturnedAt\" IS NULL");
        b.Entity<Idempotency>().HasKey(x => new { x.MemberId, x.Operation, x.Key });
        b.Entity<Idempotency>().Property(x => x.Key).HasMaxLength(100);
        b.Entity<Idempotency>().HasOne<Member>().WithMany().HasForeignKey(x => x.MemberId).OnDelete(DeleteBehavior.Restrict);
    }
}
