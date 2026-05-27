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
    public DbSet<BankAccount> BankAccounts => Set<BankAccount>();
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
    }
}
