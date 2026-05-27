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
    DbSet<SystemRolePermission> SystemRolePermissions { get; }
    DbSet<AuditEntry> AuditEntries { get; }
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

    /// <summary>Current value of the entity's row-version concurrency token (Postgres xmin).</summary>
    uint GetRowVersion(object entity);

    /// <summary>
    /// Sets the original concurrency token used in the UPDATE's WHERE clause, so a save fails with a
    /// <see cref="Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException"/> if the row changed since
    /// the client loaded the given <paramref name="version"/>.
    /// </summary>
    void SetOriginalRowVersion(object entity, uint version);
}
