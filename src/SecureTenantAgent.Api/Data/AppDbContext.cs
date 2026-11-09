using Microsoft.EntityFrameworkCore;
using SecureTenantAgent.Api.Domain;
using SecureTenantAgent.Api.Tenancy;

namespace SecureTenantAgent.Api.Data;

/// <summary>
/// Tenant isolation lives here, in the data layer: every query on tenant-owned data is
/// filtered by the caller's tenant, so a tool or a prompt cannot "forget" to filter.
/// </summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options, ITenantContext tenant) : DbContext(options)
{
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<KnowledgeDocument> Documents => Set<KnowledgeDocument>();
    public DbSet<PendingAction> PendingActions => Set<PendingAction>();
    public DbSet<AgentAuditEntry> AuditEntries => Set<AgentAuditEntry>();

    // Read per query, so the filter always uses the current request's tenant.
    private string CurrentTenantId => tenant.TenantId;
    private bool CurrentUserIsReviewer => tenant.IsReviewer;

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<KnowledgeDocument>(e =>
        {
            e.HasIndex(d => d.TenantId);
            e.HasQueryFilter(d => d.TenantId == CurrentTenantId && (!d.Restricted || CurrentUserIsReviewer));
        });
        model.Entity<PendingAction>(e =>
        {
            e.HasIndex(a => a.TenantId);
            e.HasQueryFilter(a => a.TenantId == CurrentTenantId);
        });
        model.Entity<AgentAuditEntry>(e =>
        {
            e.HasIndex(a => a.TenantId);
            e.HasQueryFilter(a => a.TenantId == CurrentTenantId);
        });
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        GuardTenantWrites();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken ct = default)
    {
        GuardTenantWrites();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, ct);
    }

    // Filters protect reads; this protects writes. Nothing can be saved into another tenant.
    private void GuardTenantWrites()
    {
        foreach (var entry in ChangeTracker.Entries<ITenantOwned>())
        {
            if (entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted
                && entry.Entity.TenantId != CurrentTenantId)
            {
                throw new UnauthorizedAccessException(
                    $"Cross-tenant write blocked: {entry.Entity.GetType().Name} belongs to another tenant.");
            }
        }
    }
}
