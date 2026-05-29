using Flowlio.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NpgsqlTypes;

namespace Flowlio.Infrastructure.Persistence.Configurations;

public class FamilyConfiguration : IEntityTypeConfiguration<Family>
{
    public void Configure(EntityTypeBuilder<Family> b)
    {
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.BaseCurrency).HasMaxLength(3);
        b.HasMany(x => x.Members).WithOne(x => x.Family!).HasForeignKey(x => x.FamilyId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(x => x.Accounts).WithOne(x => x.Family!).HasForeignKey(x => x.FamilyId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(x => x.Categories).WithOne(x => x.Family!).HasForeignKey(x => x.FamilyId).OnDelete(DeleteBehavior.Cascade);
        b.Property<uint>("xmin").IsRowVersion();
        b.ToTable(t =>
        {
            t.HasCheckConstraint("CK_Family_Name", "char_length(btrim(\"Name\")) > 0");
            t.HasCheckConstraint("CK_Family_BaseCurrency", "char_length(\"BaseCurrency\") = 3");
        });
    }
}

public class FamilyMemberConfiguration : IEntityTypeConfiguration<FamilyMember>
{
    public void Configure(EntityTypeBuilder<FamilyMember> b)
    {
        b.Property(x => x.DisplayName).HasMaxLength(120).IsRequired();
        b.Property(x => x.Email).HasMaxLength(256);
        b.HasIndex(x => x.UserId);
        // Postgres treats NULLs as distinct, so multiple pending/managed members (UserId == null) per family are allowed.
        // Scoped to live rows so a soft-deleted member does not block the same user rejoining the family.
        b.HasIndex(x => new { x.FamilyId, x.UserId }).IsUnique().HasFilter("\"DeletedAt\" IS NULL");

        b.HasOne(x => x.Guardian)
            .WithMany(x => x.Dependents)
            .HasForeignKey(x => x.GuardianMemberId)
            .OnDelete(DeleteBehavior.Restrict);

        b.Property<uint>("xmin").IsRowVersion();
    }
}

public class FamilyRolePermissionConfiguration : IEntityTypeConfiguration<FamilyRolePermission>
{
    public void Configure(EntityTypeBuilder<FamilyRolePermission> b)
    {
        b.HasOne(x => x.Family).WithMany().HasForeignKey(x => x.FamilyId).OnDelete(DeleteBehavior.Cascade);

        // Each (family, role, permission) grant is stored at most once.
        b.HasIndex(x => new { x.FamilyId, x.Role, x.Permission }).IsUnique();
    }
}

public class SystemRolePermissionConfiguration : IEntityTypeConfiguration<SystemRolePermission>
{
    public void Configure(EntityTypeBuilder<SystemRolePermission> b)
    {
        b.HasOne<IdentityRole<Guid>>().WithMany().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.RoleId, x.Permission }).IsUnique();
    }
}

public class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> b)
    {
        b.Property(x => x.Action).HasMaxLength(80).IsRequired();
        b.Property(x => x.ActorName).HasMaxLength(256);
        b.Property(x => x.TargetType).HasMaxLength(80);
        b.Property(x => x.TargetId).HasMaxLength(64);
        b.Property(x => x.TargetName).HasMaxLength(256);
        b.Property(x => x.Details).HasMaxLength(1000);
        b.HasIndex(x => x.OccurredAt);
        b.HasIndex(x => x.Action);
        b.HasIndex(x => x.ActorUserId);
        b.HasIndex(x => x.FamilyId);
    }
}

public class BankAccountConfiguration : IEntityTypeConfiguration<BankAccount>
{
    public void Configure(EntityTypeBuilder<BankAccount> b)
    {
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.AccountNumber).HasMaxLength(64);
        b.Property(x => x.Currency).HasMaxLength(3);
        b.Property(x => x.OpeningBalance).HasPrecision(18, 2);
        b.HasMany(x => x.Transactions).WithOne(x => x.BankAccount!).HasForeignKey(x => x.BankAccountId).OnDelete(DeleteBehavior.Cascade);

        b.HasOne(x => x.OwnerMember)
            .WithMany()
            .HasForeignKey(x => x.OwnerMemberId)
            .OnDelete(DeleteBehavior.SetNull);

        b.HasMany(x => x.AccessGrants).WithOne(x => x.BankAccount!).HasForeignKey(x => x.BankAccountId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(x => x.Cards).WithOne(x => x.BankAccount!).HasForeignKey(x => x.BankAccountId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => x.FamilyId);
        b.HasIndex(x => x.OwnerMemberId);
        b.ToTable(t =>
        {
            t.HasCheckConstraint("CK_BankAccount_Name", "char_length(btrim(\"Name\")) > 0");
            t.HasCheckConstraint("CK_BankAccount_Currency", "char_length(\"Currency\") = 3");
        });
    }
}

public class AccountAccessConfiguration : IEntityTypeConfiguration<AccountAccess>
{
    public void Configure(EntityTypeBuilder<AccountAccess> b)
    {
        b.HasOne(x => x.Member)
            .WithMany(x => x.AccountAccesses)
            .HasForeignKey(x => x.FamilyMemberId)
            .OnDelete(DeleteBehavior.Cascade);

        // A member has at most one access grant per account.
        b.HasIndex(x => new { x.BankAccountId, x.FamilyMemberId }).IsUnique();
        b.HasIndex(x => x.FamilyMemberId);
        b.Property<uint>("xmin").IsRowVersion();
    }
}

public class BankCardConfiguration : IEntityTypeConfiguration<BankCard>
{
    public void Configure(EntityTypeBuilder<BankCard> b)
    {
        b.Property(x => x.CardholderName).HasMaxLength(120).IsRequired();
        b.Property(x => x.Last4).HasMaxLength(4);
        b.Property(x => x.MonthlyLimit).HasPrecision(18, 2);

        b.HasOne(x => x.Holder)
            .WithMany()
            .HasForeignKey(x => x.HolderMemberId)
            .OnDelete(DeleteBehavior.SetNull);

        b.HasIndex(x => x.BankAccountId);
        b.HasIndex(x => x.HolderMemberId);
        b.Property<uint>("xmin").IsRowVersion();
        b.ToTable(t =>
        {
            t.HasCheckConstraint("CK_BankCard_ExpiryMonth", "\"ExpiryMonth\" BETWEEN 1 AND 12");
            t.HasCheckConstraint("CK_BankCard_ExpiryYear", "\"ExpiryYear\" BETWEEN 2000 AND 2100");
            t.HasCheckConstraint("CK_BankCard_MonthlyLimit", "\"MonthlyLimit\" IS NULL OR \"MonthlyLimit\" >= 0");
            t.HasCheckConstraint("CK_BankCard_Last4", "\"Last4\" IS NULL OR \"Last4\" ~ '^[0-9]{1,4}$'");
        });
    }
}

public class FamilyInvitationConfiguration : IEntityTypeConfiguration<FamilyInvitation>
{
    public void Configure(EntityTypeBuilder<FamilyInvitation> b)
    {
        b.Property(x => x.Email).HasMaxLength(256).IsRequired();
        b.Property(x => x.TokenHash).HasMaxLength(64).IsRequired();

        b.HasOne(x => x.Family).WithMany().HasForeignKey(x => x.FamilyId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Member).WithMany().HasForeignKey(x => x.MemberId).OnDelete(DeleteBehavior.Cascade);

        b.HasIndex(x => x.TokenHash).IsUnique();
        b.HasIndex(x => x.FamilyId);
    }
}

public class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> b)
    {
        b.Property(x => x.Name).HasMaxLength(120).IsRequired();
        b.Property(x => x.Color).HasMaxLength(9);
        b.Property(x => x.Icon).HasMaxLength(80);
        b.HasOne(x => x.Parent).WithMany(x => x.Children).HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => x.FamilyId);
    }
}

public class TransactionConfiguration : IEntityTypeConfiguration<Transaction>
{
    public void Configure(EntityTypeBuilder<Transaction> b)
    {
        b.Property(x => x.Amount).HasPrecision(18, 2);
        b.Property(x => x.Currency).HasMaxLength(3);
        b.Property(x => x.CounterpartyName).HasMaxLength(200);
        b.Property(x => x.CounterpartyAccount).HasMaxLength(64);
        b.Property(x => x.VariableSymbol).HasMaxLength(20);
        b.Property(x => x.ConstantSymbol).HasMaxLength(20);
        b.Property(x => x.SpecificSymbol).HasMaxLength(20);
        b.Property(x => x.Description).HasMaxLength(1000);
        b.Property(x => x.Note).HasMaxLength(1000);
        b.Property(x => x.DedupHash).HasMaxLength(64).IsRequired();

        b.HasOne(x => x.Category).WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.SetNull);
        b.HasOne(x => x.ImportBatch).WithMany(x => x.Transactions).HasForeignKey(x => x.ImportBatchId).OnDelete(DeleteBehavior.SetNull);
        // The rule that assigned the category; clearing it on rule delete is fine — re-evaluation runs explicitly.
        b.HasOne(x => x.AppliedRule).WithMany().HasForeignKey(x => x.AppliedRuleId).OnDelete(DeleteBehavior.SetNull);

        // Prevents re-importing the same booked entry into the same account. Scoped to live rows so a
        // soft-deleted transaction does not block re-importing (or re-creating) the same movement.
        b.HasIndex(x => new { x.BankAccountId, x.DedupHash }).IsUnique().HasFilter("\"DeletedAt\" IS NULL");
        b.HasIndex(x => new { x.FamilyId, x.BookingDate });

        // Optimistic concurrency via the Postgres xmin system column (same as family/member/card).
        b.Property<uint>("xmin").IsRowVersion();
        b.ToTable(t => t.HasCheckConstraint("CK_Transaction_Currency", "char_length(\"Currency\") = 3"));

        // Full-text search: a STORED generated tsvector over the searchable fields, diacritics-folded
        // via flowlio_immutable_unaccent and tokenised with the 'simple' config, plus a GIN index.
        // Shadow property keeps the Npgsql type out of the pure domain entity.
        b.Property<NpgsqlTsVector>("SearchVector")
            .HasComputedColumnSql(
                "to_tsvector('simple', flowlio_immutable_unaccent(" +
                "coalesce(\"CounterpartyName\", '') || ' ' || coalesce(\"Description\", '') || ' ' || " +
                "coalesce(\"Note\", '') || ' ' || coalesce(\"CounterpartyAccount\", '') || ' ' || " +
                "coalesce(\"VariableSymbol\", '')))",
                stored: true);
        b.HasIndex("SearchVector").HasMethod("gin");
    }
}

public class RecurringPaymentConfiguration : IEntityTypeConfiguration<RecurringPayment>
{
    public void Configure(EntityTypeBuilder<RecurringPayment> b)
    {
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.ExpectedAmount).HasPrecision(18, 2);
        b.Property(x => x.Currency).HasMaxLength(3);
        b.Property(x => x.CounterpartyMatch).HasMaxLength(200);
        b.Property(x => x.VariableSymbolMatch).HasMaxLength(20);
        b.HasOne(x => x.Category).WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.SetNull);
        b.HasOne<Family>().WithMany().HasForeignKey(x => x.FamilyId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => x.FamilyId);
        b.ToTable(t =>
        {
            t.HasCheckConstraint("CK_RecurringPayment_ExpectedAmount", "\"ExpectedAmount\" >= 0");
            t.HasCheckConstraint("CK_RecurringPayment_DayOfMonth", "\"DayOfMonth\" IS NULL OR \"DayOfMonth\" BETWEEN 1 AND 31");
        });
    }
}

public class SubscriptionConfiguration : IEntityTypeConfiguration<Subscription>
{
    public void Configure(EntityTypeBuilder<Subscription> b)
    {
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.Provider).HasMaxLength(200);
        b.Property(x => x.Amount).HasPrecision(18, 2);
        b.Property(x => x.Currency).HasMaxLength(3);
        b.Property(x => x.Notes).HasMaxLength(1000);
        b.HasOne(x => x.Category).WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.SetNull);
        b.HasOne<Family>().WithMany().HasForeignKey(x => x.FamilyId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => x.FamilyId);
        b.ToTable(t => t.HasCheckConstraint("CK_Subscription_Amount", "\"Amount\" >= 0"));
    }
}

public class ImportBatchConfiguration : IEntityTypeConfiguration<ImportBatch>
{
    public void Configure(EntityTypeBuilder<ImportBatch> b)
    {
        b.Property(x => x.FileName).HasMaxLength(260);
        b.Property(x => x.Label).HasMaxLength(200);
        b.Property(x => x.Error).HasMaxLength(2000);
        b.HasOne(x => x.BankAccount).WithMany().HasForeignKey(x => x.BankAccountId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => x.FamilyId);
    }
}

public class CategorizationRuleConfiguration : IEntityTypeConfiguration<CategorizationRule>
{
    public void Configure(EntityTypeBuilder<CategorizationRule> b)
    {
        // Pattern is optional: a rule may match on amount alone.
        b.Property(x => x.Pattern).HasMaxLength(200);
        b.Property(x => x.MinAmount).HasPrecision(18, 2);
        b.Property(x => x.MaxAmount).HasPrecision(18, 2);
        b.Property(x => x.AmountCurrency).HasMaxLength(3);
        b.HasOne(x => x.Category).WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<Family>().WithMany().HasForeignKey(x => x.FamilyId).OnDelete(DeleteBehavior.Cascade);

        // Personal rules belong to a member; account rules to an account. Removing either takes its rules.
        b.HasOne(x => x.OwnerMember).WithMany().HasForeignKey(x => x.OwnerMemberId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.BankAccount).WithMany().HasForeignKey(x => x.BankAccountId).OnDelete(DeleteBehavior.Cascade);

        b.HasIndex(x => x.FamilyId);
        b.HasIndex(x => x.OwnerMemberId);
        b.HasIndex(x => x.BankAccountId);

        // Optimistic concurrency via the Postgres xmin system column (same as transaction/card/member).
        b.Property<uint>("xmin").IsRowVersion();
        b.ToTable(t =>
        {
            // Pattern, when present, must not be blank (NULL passes — an amount-only rule has no pattern).
            t.HasCheckConstraint("CK_CategorizationRule_Pattern", "\"Pattern\" IS NULL OR char_length(btrim(\"Pattern\")) > 0");
            t.HasCheckConstraint("CK_CategorizationRule_AmountCurrency", "\"AmountCurrency\" IS NULL OR char_length(\"AmountCurrency\") = 3");
            t.HasCheckConstraint("CK_CategorizationRule_Amounts",
                "(\"MinAmount\" IS NULL OR \"MinAmount\" >= 0) AND (\"MaxAmount\" IS NULL OR \"MaxAmount\" >= 0) " +
                "AND (\"MinAmount\" IS NULL OR \"MaxAmount\" IS NULL OR \"MinAmount\" <= \"MaxAmount\")");
        });
    }
}

public class BudgetConfiguration : IEntityTypeConfiguration<Budget>
{
    public void Configure(EntityTypeBuilder<Budget> b)
    {
        b.Property(x => x.Amount).HasPrecision(18, 2);
        b.HasOne(x => x.Category).WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<Family>().WithMany().HasForeignKey(x => x.FamilyId).OnDelete(DeleteBehavior.Cascade);
        // One budget per category per family.
        b.HasIndex(x => new { x.FamilyId, x.CategoryId }).IsUnique();
        b.ToTable(t => t.HasCheckConstraint("CK_Budget_Amount", "\"Amount\" > 0"));
    }
}

public class GoalConfiguration : IEntityTypeConfiguration<Goal>
{
    public void Configure(EntityTypeBuilder<Goal> b)
    {
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.TargetAmount).HasPrecision(18, 2);
        b.Property(x => x.BaselineAmount).HasPrecision(18, 2);
        b.HasOne(x => x.BankAccount).WithMany().HasForeignKey(x => x.BankAccountId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<Family>().WithMany().HasForeignKey(x => x.FamilyId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => x.FamilyId);
        b.ToTable(t =>
        {
            t.HasCheckConstraint("CK_Goal_Name", "char_length(btrim(\"Name\")) > 0");
            t.HasCheckConstraint("CK_Goal_TargetAmount", "\"TargetAmount\" > 0");
        });
    }
}

public class ExchangeRateConfiguration : IEntityTypeConfiguration<ExchangeRate>
{
    public void Configure(EntityTypeBuilder<ExchangeRate> b)
    {
        b.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        b.Property(x => x.CzkPerUnit).HasPrecision(18, 6);
        // One rate per currency per day; lookups fetch the latest rate on/before a date.
        b.HasIndex(x => new { x.Currency, x.Date }).IsUnique();
        b.ToTable(t =>
        {
            t.HasCheckConstraint("CK_ExchangeRate_Currency", "char_length(\"Currency\") = 3");
            t.HasCheckConstraint("CK_ExchangeRate_CzkPerUnit", "\"CzkPerUnit\" > 0");
        });
    }
}

public class BankConnectionConfiguration : IEntityTypeConfiguration<BankConnection>
{
    public void Configure(EntityTypeBuilder<BankConnection> b)
    {
        b.Property(x => x.AspspName).HasMaxLength(200).IsRequired();
        b.Property(x => x.AspspCountry).HasMaxLength(2).IsRequired();
        b.Property(x => x.AuthorizationId).HasMaxLength(200);
        b.Property(x => x.State).HasMaxLength(128);
        b.Property(x => x.SessionId).HasMaxLength(200);
        b.Property(x => x.AccountUid).HasMaxLength(256);
        b.Property(x => x.LastError).HasMaxLength(2000);

        b.HasOne(x => x.BankAccount).WithMany().HasForeignKey(x => x.BankAccountId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<Family>().WithMany().HasForeignKey(x => x.FamilyId).OnDelete(DeleteBehavior.Cascade);

        b.HasIndex(x => x.FamilyId);
        b.HasIndex(x => x.BankAccountId);
        // The redirect callback looks a pending connection up by its state token; scoped to live rows.
        b.HasIndex(x => x.State).HasFilter("\"State\" IS NOT NULL AND \"DeletedAt\" IS NULL");

        b.Property<uint>("xmin").IsRowVersion();
        b.ToTable(t => t.HasCheckConstraint("CK_BankConnection_Country", "char_length(\"AspspCountry\") = 2"));
    }
}

public class RuleSuggestionDismissalConfiguration : IEntityTypeConfiguration<RuleSuggestionDismissal>
{
    public void Configure(EntityTypeBuilder<RuleSuggestionDismissal> b)
    {
        b.Property(x => x.CounterpartyKey).HasMaxLength(200).IsRequired();
        b.HasOne<Category>().WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<Family>().WithMany().HasForeignKey(x => x.FamilyId).OnDelete(DeleteBehavior.Cascade);
        // One dismissal per family + counterparty + category; lookups filter by family.
        b.HasIndex(x => new { x.FamilyId, x.CounterpartyKey, x.CategoryId }).IsUnique();
    }
}
