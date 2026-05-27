using Flowlio.Domain;

namespace Flowlio.Shared;

public sealed record BankAccountDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = "";
    public BankProvider Bank { get; init; }
    public string? AccountNumber { get; init; }
    public string Currency { get; init; } = "CZK";
    public decimal OpeningBalance { get; init; }
    public decimal CurrentBalance { get; init; }
    public Guid? OwnerMemberId { get; init; }
    public string? OwnerName { get; init; }
    public bool IsChildAccount { get; init; }
    public int CardCount { get; init; }
    public int DisponentCount { get; init; }
}

public sealed record CreateBankAccountRequest
{
    public string Name { get; init; } = "";
    public BankProvider Bank { get; init; }
    public string? AccountNumber { get; init; }
    public string Currency { get; init; } = "CZK";
    public decimal OpeningBalance { get; init; }

    /// <summary>Member who owns the account. When that member is a child, this becomes a child account.</summary>
    public Guid? OwnerMemberId { get; init; }
}

public sealed record CategoryDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = "";
    public CategoryKind Kind { get; init; }
    public string Color { get; init; } = "#64748b";
    public string? Icon { get; init; }
    public Guid? ParentId { get; init; }
}

public sealed record TransactionDto
{
    public Guid Id { get; init; }
    public Guid BankAccountId { get; init; }
    public DateOnly BookingDate { get; init; }
    public decimal Amount { get; init; }
    public string Currency { get; init; } = "CZK";
    public TransactionDirection Direction { get; init; }
    public string? CounterpartyName { get; init; }
    public string? CounterpartyAccount { get; init; }
    public string? VariableSymbol { get; init; }
    public string? Description { get; init; }
    public string? Note { get; init; }
    public Guid? CategoryId { get; init; }
    public string? CategoryName { get; init; }
}

public sealed record TransactionPageDto
{
    public IReadOnlyList<TransactionDto> Items { get; init; } = [];
    public int TotalCount { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
}

public sealed record RecurringPaymentDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = "";
    public decimal ExpectedAmount { get; init; }
    public string Currency { get; init; } = "CZK";
    public RecurrenceFrequency Frequency { get; init; }
    public int? DayOfMonth { get; init; }
    public DateOnly? NextDueDate { get; init; }
    public Guid? CategoryId { get; init; }
    public bool IsActive { get; init; }
}

public sealed record SubscriptionDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = "";
    public string? Provider { get; init; }
    public decimal Amount { get; init; }
    public string Currency { get; init; } = "CZK";
    public RecurrenceFrequency BillingCycle { get; init; }
    public DateOnly? NextRenewalDate { get; init; }
    public bool IsActive { get; init; }
}

public sealed record ImportResultDto
{
    public Guid ImportBatchId { get; init; }
    public int ImportedCount { get; init; }
    public int DuplicateCount { get; init; }
    public ImportStatus Status { get; init; }
    public string? Error { get; init; }
}

public sealed record CategorySpendDto
{
    public string CategoryName { get; init; } = "";
    public string Color { get; init; } = "#64748b";
    public decimal Amount { get; init; }
}

public sealed record UpcomingPaymentDto
{
    public string Name { get; init; } = "";
    public decimal Amount { get; init; }
    public DateOnly? DueDate { get; init; }
}

public sealed record DashboardSummaryDto
{
    public decimal TotalBalance { get; init; }
    public decimal IncomeThisMonth { get; init; }
    public decimal ExpenseThisMonth { get; init; }
    public decimal NetThisMonth { get; init; }
    public IReadOnlyList<CategorySpendDto> TopExpenseCategories { get; init; } = [];
    public IReadOnlyList<UpcomingPaymentDto> Upcoming { get; init; } = [];
}

/// <summary>The signed-in member together with the effective permissions their role grants.</summary>
public sealed record CurrentUserDto
{
    public Guid MemberId { get; init; }
    public string DisplayName { get; init; } = "";
    public MemberRole Role { get; init; }

    /// <summary>Whether the user holds the system-wide administrator role (cross-family user management).</summary>
    public bool IsAdmin { get; init; }

    public IReadOnlyList<Permission> Permissions { get; init; } = [];

    /// <summary>How often the client should re-poll for access changes as a fallback to live push (seconds).</summary>
    public int PollIntervalSeconds { get; init; } = 60;

    public bool Can(Permission permission) => Permissions.Contains(permission);
}

/// <summary>Whether a family member has their own login, a pending invite, or is guardian-managed.</summary>
public enum MemberStatus
{
    Active = 0,
    Pending = 1,
    Managed = 2,
}

public sealed record FamilyMemberDto
{
    public Guid Id { get; init; }
    public string DisplayName { get; init; } = "";
    public string? Email { get; init; }
    public MemberRole Role { get; init; }
    public MemberStatus Status { get; init; }
    public Guid? GuardianMemberId { get; init; }
    public string? GuardianName { get; init; }
    public bool IsCurrentUser { get; init; }
    public bool IsActive { get; init; } = true;
}

/// <summary>Owner-initiated edit of an existing member's profile and role.</summary>
public sealed record UpdateMemberRequest
{
    public string DisplayName { get; init; } = "";
    public string? Email { get; init; }
    public MemberRole Role { get; init; } = MemberRole.Adult;

    /// <summary>Controlling guardian; required when <see cref="Role"/> is <see cref="MemberRole.Child"/>.</summary>
    public Guid? GuardianMemberId { get; init; }
}

public sealed record CreateMemberRequest
{
    public string DisplayName { get; init; } = "";
    public string? Email { get; init; }
    public MemberRole Role { get; init; } = MemberRole.Adult;

    /// <summary>Required when <see cref="Role"/> is <see cref="MemberRole.Child"/>: the controlling guardian member.</summary>
    public Guid? GuardianMemberId { get; init; }
}

public sealed record InvitationDto
{
    public Guid Id { get; init; }
    public Guid MemberId { get; init; }
    public string MemberName { get; init; } = "";
    public string Email { get; init; } = "";
    public MemberRole Role { get; init; }
    public InvitationStatus Status { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }

    /// <summary>Sign-up link with the raw token. Populated only in the response that creates the invite.</summary>
    public string? InviteUrl { get; init; }
}

public sealed record CreateMemberResultDto
{
    public FamilyMemberDto Member { get; init; } = new();
    public InvitationDto? Invitation { get; init; }
}

public sealed record AccountAccessDto
{
    public Guid Id { get; init; }
    public Guid BankAccountId { get; init; }
    public Guid FamilyMemberId { get; init; }
    public string MemberName { get; init; } = "";
    public AccountAccessLevel Level { get; init; }
}

public sealed record AccountAccessOverviewDto
{
    public Guid AccountId { get; init; }
    public Guid? OwnerMemberId { get; init; }
    public string? OwnerName { get; init; }
    public bool IsChildAccount { get; init; }
    public IReadOnlyList<AccountAccessDto> Grants { get; init; } = [];
}

public sealed record GrantAccessRequest
{
    public Guid MemberId { get; init; }
    public AccountAccessLevel Level { get; init; } = AccountAccessLevel.Disponent;
}

public sealed record BankCardDto
{
    public Guid Id { get; init; }
    public Guid BankAccountId { get; init; }
    public Guid? HolderMemberId { get; init; }
    public string? HolderName { get; init; }
    public string CardholderName { get; init; } = "";
    public string? Last4 { get; init; }
    public CardType Type { get; init; }
    public int ExpiryMonth { get; init; }
    public int ExpiryYear { get; init; }
    public CardStatus Status { get; init; }
    public decimal? MonthlyLimit { get; init; }
}

public sealed record CreateCardRequest
{
    public Guid? HolderMemberId { get; init; }
    public string CardholderName { get; init; } = "";
    public string? Last4 { get; init; }
    public CardType Type { get; init; } = CardType.Debit;
    public int ExpiryMonth { get; init; }
    public int ExpiryYear { get; init; }
    public decimal? MonthlyLimit { get; init; }
}

public sealed record UpdateCardRequest
{
    public Guid? HolderMemberId { get; init; }
    public string CardholderName { get; init; } = "";
    public string? Last4 { get; init; }
    public CardType Type { get; init; }
    public int ExpiryMonth { get; init; }
    public int ExpiryYear { get; init; }
    public CardStatus Status { get; init; }
    public decimal? MonthlyLimit { get; init; }
}

// ---- Roles & permissions (per-family, editable by the owner) ----------------

public sealed record RolePermissionsDto
{
    public MemberRole Role { get; init; }
    public IReadOnlyList<Permission> Permissions { get; init; } = [];

    /// <summary>Whether the owner may edit this role's permissions (false for the Owner role).</summary>
    public bool Editable { get; init; }
}

public sealed record FamilyRolesDto
{
    /// <summary>Every permission that exists, so the client can render the full matrix.</summary>
    public IReadOnlyList<Permission> AllPermissions { get; init; } = [];
    public IReadOnlyList<RolePermissionsDto> Roles { get; init; } = [];
}

public sealed record UpdateRolePermissionsRequest
{
    public IReadOnlyList<Permission> Permissions { get; init; } = [];
}

// ---- Family management ------------------------------------------------------

public sealed record FamilyDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = "";
    public string BaseCurrency { get; init; } = "CZK";
    public int MemberCount { get; init; }
    public Guid? OwnerMemberId { get; init; }
    public string? OwnerName { get; init; }
}

public sealed record UpdateFamilyRequest
{
    public string Name { get; init; } = "";
    public string BaseCurrency { get; init; } = "CZK";
}

public sealed record TransferOwnershipRequest
{
    public Guid NewOwnerMemberId { get; init; }
}

/// <summary>Deletes the whole family; <see cref="ConfirmName"/> must match the family name exactly.</summary>
public sealed record DeleteFamilyRequest
{
    public string ConfirmName { get; init; } = "";
}

// ---- System administration (cross-family user accounts) ---------------------

public sealed record AdminUserDto
{
    public Guid Id { get; init; }
    public string? Email { get; init; }
    public string? DisplayName { get; init; }
    public bool IsAdmin { get; init; }
    public bool IsLockedOut { get; init; }

    /// <summary>When the lockout ends; <c>DateTimeOffset.MaxValue</c> indicates an indefinite block.</summary>
    public DateTimeOffset? LockoutEndUtc { get; init; }
    public bool MustChangePassword { get; init; }
    public bool IsCurrentUser { get; init; }
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>Set when the account is soft-deleted (shown only in the deleted-accounts view).</summary>
    public DateTimeOffset? DeletedAtUtc { get; init; }
    public IReadOnlyList<string> Families { get; init; } = [];
}

public sealed record CreateUserRequest
{
    public string Email { get; init; } = "";
    public string? DisplayName { get; init; }
    public string Password { get; init; } = "";
    public bool IsAdmin { get; init; }

    /// <summary>Require the user to choose a new password on first sign-in.</summary>
    public bool MustChangePassword { get; init; } = true;
}

public sealed record SetUserAdminRequest
{
    public bool IsAdmin { get; init; }
}

/// <summary>Temporary lock; the account unlocks itself after the given number of minutes.</summary>
public sealed record LockUserRequest
{
    public int Minutes { get; init; } = 60;
}

public sealed record AdminSetPasswordRequest
{
    public string NewPassword { get; init; } = "";

    /// <summary>Also require the user to change this password on next sign-in.</summary>
    public bool MustChangePassword { get; init; } = true;
}
