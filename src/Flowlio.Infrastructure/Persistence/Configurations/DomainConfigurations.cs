using Flowlio.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

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

        // Prevents re-importing the same booked entry into the same account.
        b.HasIndex(x => new { x.BankAccountId, x.DedupHash }).IsUnique();
        b.HasIndex(x => new { x.FamilyId, x.BookingDate });
        b.ToTable(t => t.HasCheckConstraint("CK_Transaction_Currency", "char_length(\"Currency\") = 3"));
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
        b.Property(x => x.Pattern).HasMaxLength(200).IsRequired();
        b.HasOne(x => x.Category).WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<Family>().WithMany().HasForeignKey(x => x.FamilyId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => x.FamilyId);
    }
}
