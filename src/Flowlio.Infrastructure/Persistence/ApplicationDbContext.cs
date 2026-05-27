using Flowlio.Application.Abstractions;
using Flowlio.Domain;
using Flowlio.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Flowlio.Infrastructure.Persistence;

/// <summary>
/// Single EF Core context hosting ASP.NET Identity, OpenIddict (via <c>UseOpenIddict()</c> at
/// registration time) and the Flowlio domain. Implements <see cref="IAppDbContext"/> for the
/// application layer.
/// </summary>
public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options), IAppDbContext
{
    public DbSet<Family> Families => Set<Family>();
    public DbSet<FamilyMember> FamilyMembers => Set<FamilyMember>();
    public DbSet<FamilyRolePermission> FamilyRolePermissions => Set<FamilyRolePermission>();
    public DbSet<SystemRolePermission> SystemRolePermissions => Set<SystemRolePermission>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
    public DbSet<FamilyInvitation> FamilyInvitations => Set<FamilyInvitation>();
    public DbSet<BankAccount> BankAccounts => Set<BankAccount>();
    public DbSet<AccountAccess> AccountAccesses => Set<AccountAccess>();
    public DbSet<BankCard> BankCards => Set<BankCard>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<RecurringPayment> RecurringPayments => Set<RecurringPayment>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<ImportBatch> ImportBatches => Set<ImportBatch>();
    public DbSet<CategorizationRule> CategorizationRules => Set<CategorizationRule>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);

        // Soft-deleted rows are hidden from all queries (sign-in, lookups, listings); views that need
        // them (the admin "deleted users" list, account restore) use IgnoreQueryFilters explicitly.
        builder.Entity<ApplicationUser>().HasQueryFilter(u => u.DeletedAt == null);
        builder.Entity<BankAccount>().HasQueryFilter(a => a.DeletedAt == null);
        builder.Entity<FamilyMember>().HasQueryFilter(m => m.DeletedAt == null);
        builder.Entity<BankCard>().HasQueryFilter(c => c.DeletedAt == null);
    }
}
