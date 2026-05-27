using Flowlio.Domain;
using Microsoft.EntityFrameworkCore;

namespace Flowlio.Application.Abstractions;

/// <summary>
/// Application-facing view of the persistence layer. Handlers depend on this abstraction
/// rather than the concrete EF Core context so the application layer stays infrastructure-agnostic.
/// </summary>
public interface IAppDbContext
{
    DbSet<Family> Families { get; }
    DbSet<FamilyMember> FamilyMembers { get; }
    DbSet<FamilyRolePermission> FamilyRolePermissions { get; }
    DbSet<FamilyInvitation> FamilyInvitations { get; }
    DbSet<BankAccount> BankAccounts { get; }
    DbSet<AccountAccess> AccountAccesses { get; }
    DbSet<BankCard> BankCards { get; }
    DbSet<Category> Categories { get; }
    DbSet<Transaction> Transactions { get; }
    DbSet<RecurringPayment> RecurringPayments { get; }
    DbSet<Subscription> Subscriptions { get; }
    DbSet<ImportBatch> ImportBatches { get; }
    DbSet<CategorizationRule> CategorizationRules { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
