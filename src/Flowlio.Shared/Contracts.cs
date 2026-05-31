using System.ComponentModel.DataAnnotations;
using Flowlio.Domain;

namespace Flowlio.Shared;

/// <summary>Shared validation constants so the client, server filter and DB constraints agree.</summary>
public static class ValidationRules
{
    /// <summary>ISO 4217 currency code: exactly three letters (case-insensitive; the server upper-cases).</summary>
    public const string CurrencyRegex = "^[A-Za-z]{3}$";

    /// <summary>Up to four digits (the displayed tail of a card number).</summary>
    public const string Last4Regex = "^[0-9]{0,4}$";

    public const int MinExpiryYear = 2000;
    public const int MaxExpiryYear = 2100;
    public const int MinPasswordLength = 8;
    public const double MaxMoney = 1_000_000_000d;
}

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

/// <summary>An archived (soft-deleted) bank account, listed so it can be restored.</summary>
public sealed record ArchivedAccountDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = "";
    public BankProvider Bank { get; init; }
    public string? AccountNumber { get; init; }
    public string Currency { get; init; } = "CZK";
    public DateTimeOffset ArchivedAt { get; init; }
}

public sealed record CreateBankAccountRequest
{
    [Required(ErrorMessage = "Název účtu je povinný.")]
    [StringLength(200, MinimumLength = 1, ErrorMessage = "Název účtu může mít nejvýše 200 znaků.")]
    public string Name { get; set; } = "";

    public BankProvider Bank { get; set; }

    [StringLength(64, ErrorMessage = "Číslo účtu může mít nejvýše 64 znaků.")]
    public string? AccountNumber { get; set; }

    [Required(ErrorMessage = "Měna je povinná.")]
    [RegularExpression(ValidationRules.CurrencyRegex, ErrorMessage = "Měna musí být třípísmenný kód (např. CZK).")]
    public string Currency { get; set; } = "CZK";

    [Range(-ValidationRules.MaxMoney, ValidationRules.MaxMoney, ErrorMessage = "Počáteční zůstatek je mimo povolený rozsah.")]
    public decimal OpeningBalance { get; set; }

    /// <summary>Member who owns the account. When that member is a child, this becomes a child account.</summary>
    public Guid? OwnerMemberId { get; set; }
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
    public DateOnly? ValueDate { get; init; }
    public decimal Amount { get; init; }
    public string Currency { get; init; } = "CZK";
    public TransactionDirection Direction { get; init; }
    public string? CounterpartyName { get; init; }
    public string? CounterpartyAccount { get; init; }
    public string? VariableSymbol { get; init; }
    public string? ConstantSymbol { get; init; }
    public string? SpecificSymbol { get; init; }
    public string? Description { get; init; }
    public string? Note { get; init; }
    public Guid? CategoryId { get; init; }
    public string? CategoryName { get; init; }
    public Guid? ImportBatchId { get; init; }

    /// <summary>The rule that auto-assigned the category (and its pattern, for the "why"), when applicable.</summary>
    public Guid? AppliedRuleId { get; init; }
    public string? AppliedRulePattern { get; init; }

    /// <summary>Optimistic-concurrency token (Postgres xmin); echo it back on update.</summary>
    public uint Version { get; init; }
}

public sealed record TransactionPageDto
{
    public IReadOnlyList<TransactionDto> Items { get; init; } = [];
    public int TotalCount { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
}

/// <summary>Editable fields shared by single-transaction create/edit and manual batch rows.</summary>
public sealed record TransactionFields
{
    public DateOnly BookingDate { get; init; }
    public DateOnly? ValueDate { get; init; }
    public decimal Amount { get; init; }
    // Currency is not editable: a transaction always uses its account's currency (set server-side).
    public string? CounterpartyName { get; init; }
    public string? CounterpartyAccount { get; init; }
    public string? VariableSymbol { get; init; }
    public string? ConstantSymbol { get; init; }
    public string? SpecificSymbol { get; init; }
    public string? Description { get; init; }
    public string? Note { get; init; }
    public Guid? CategoryId { get; init; }
}

/// <summary>Hand-entered transaction added directly to an account (no import file).</summary>
public sealed record CreateTransactionRequest
{
    public Guid BankAccountId { get; init; }
    public TransactionFields Fields { get; init; } = new();
}

/// <summary>Edits an existing transaction (manual or imported). The owning account is not changed.</summary>
public sealed record UpdateTransactionRequest
{
    public TransactionFields Fields { get; init; } = new();

    /// <summary>The row version the edit is based on; a stale value fails with HTTP 409.</summary>
    public uint Version { get; init; }
}

/// <summary>Bulk operation over a set of transactions (delete / restore).</summary>
public sealed record BulkTransactionRequest
{
    public IReadOnlyList<Guid> Ids { get; init; } = [];
}

/// <summary>A cluster of uncategorized transactions sharing a counterparty, for the triage ("to categorize")
/// inbox: assign them all at once and optionally turn the merchant into a rule.</summary>
public sealed record UncategorizedGroupDto
{
    public string Counterparty { get; init; } = "";
    public int Count { get; init; }

    /// <summary>Sum of the group when all rows share a currency; null for a mixed-currency group.</summary>
    public decimal? TotalAmount { get; init; }
    public string? Currency { get; init; }

    public IReadOnlyList<Guid> TransactionIds { get; init; } = [];
}

/// <summary>Bulk re-categorisation of a set of transactions (null category clears it).</summary>
public sealed record BulkCategorizeRequest
{
    public IReadOnlyList<Guid> Ids { get; init; } = [];
    public Guid? CategoryId { get; init; }
}

/// <summary>Result of a bulk transaction operation.</summary>
public sealed record BulkResultDto
{
    public int Count { get; init; }
}

// ---- Categorization rules ---------------------------------------------------

/// <summary>A user-defined rule that auto-assigns a category to transactions whose chosen field
/// contains the pattern. Higher priority wins; <see cref="RuleMatchField.Any"/> matches across all text.</summary>
public sealed record CategorizationRuleDto
{
    public Guid Id { get; init; }
    public RuleMatchField Field { get; init; }
    public RuleMatchMode MatchMode { get; init; }

    /// <summary>Text pattern; null/empty when the rule matches on amount alone.</summary>
    public string? Pattern { get; init; }

    /// <summary>Optional amount condition (inclusive bounds, in <see cref="AmountCurrency"/>, absolute value).</summary>
    public decimal? MinAmount { get; init; }
    public decimal? MaxAmount { get; init; }
    public string? AmountCurrency { get; init; }

    public Guid CategoryId { get; init; }
    public string? CategoryName { get; init; }
    public int Priority { get; init; }
    public bool IsActive { get; init; }

    /// <summary>Who the rule applies to (personal / account / family).</summary>
    public RuleScope Scope { get; init; }

    /// <summary>Target account for an account-scoped rule (with its name for display); null otherwise.</summary>
    public Guid? BankAccountId { get; init; }
    public string? BankAccountName { get; init; }

    /// <summary>Owning member for a personal rule (with display name); null otherwise.</summary>
    public Guid? OwnerMemberId { get; init; }
    public string? OwnerName { get; init; }

    /// <summary>True when the current member may edit/delete this rule (owner of it, or family owner).</summary>
    public bool CanManage { get; init; }

    /// <summary>How many transactions currently have their category attributed to this rule. Zero is a hint
    /// the rule may be dead (never matches or shadowed by another).</summary>
    public int UsageCount { get; init; }

    /// <summary>Row-version concurrency token (Postgres xmin); echoed back on update.</summary>
    public uint Version { get; init; }

    /// <summary>When the rule was soft-deleted; null for live rules. Set on the "deleted rules" listing.</summary>
    public DateTimeOffset? DeletedAt { get; init; }
}

/// <summary>Create or update a categorization rule (shared shape; rules carry no concurrency token).</summary>
public sealed record CategorizationRuleRequest
{
    /// <summary>Scope of the rule. Personal is forced to the current member; Account/Family are owner-only.</summary>
    public RuleScope Scope { get; set; } = RuleScope.Personal;

    /// <summary>Required when <see cref="Scope"/> is <see cref="RuleScope.Account"/>: the target account.</summary>
    public Guid? BankAccountId { get; set; }

    public RuleMatchField Field { get; set; } = RuleMatchField.Any;

    public RuleMatchMode MatchMode { get; set; } = RuleMatchMode.Substring;

    /// <summary>Text pattern (optional). A rule must have a pattern and/or an amount range.</summary>
    [StringLength(200, ErrorMessage = "Vzor může mít nejvýše 200 znaků.")]
    public string? Pattern { get; set; }

    /// <summary>Optional amount condition (inclusive, absolute value); needs <see cref="AmountCurrency"/>.</summary>
    [Range(0, ValidationRules.MaxMoney, ErrorMessage = "Částka je mimo povolený rozsah.")]
    public decimal? MinAmount { get; set; }

    [Range(0, ValidationRules.MaxMoney, ErrorMessage = "Částka je mimo povolený rozsah.")]
    public decimal? MaxAmount { get; set; }

    [RegularExpression(ValidationRules.CurrencyRegex, ErrorMessage = "Měna musí být třípísmenný kód (např. CZK).")]
    public string? AmountCurrency { get; set; }

    public Guid CategoryId { get; set; }

    public int Priority { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>Row-version token from the loaded rule; a stale value makes the update fail with HTTP 409.</summary>
    public uint Version { get; set; }
}

/// <summary>A rule Flowlio suggests after seeing the same counterparty categorized by hand more than once,
/// so the next import classifies it automatically. The user confirms or dismisses it.</summary>
public sealed record RuleSuggestionDto
{
    /// <summary>The counterparty text the suggested rule would match on (whole word, across all fields).</summary>
    public string Pattern { get; init; } = "";
    public Guid CategoryId { get; init; }
    public string CategoryName { get; init; } = "";

    /// <summary>How many manually-categorized transactions back this suggestion.</summary>
    public int MatchCount { get; init; }
}

/// <summary>New priority order for rules (highest priority first). Used by the up/down reorder UI.</summary>
public sealed record ReorderRulesRequest
{
    public IReadOnlyList<Guid> OrderedIds { get; init; } = [];
}

/// <summary>A portable rule definition for export/import: references the category and account by name (not id)
/// so a rule set can be backed up or shared between families.</summary>
public sealed record RuleExportDto
{
    public RuleScope Scope { get; init; }
    public string? BankAccountName { get; init; }
    public RuleMatchField Field { get; init; }
    public RuleMatchMode MatchMode { get; init; }
    public string? Pattern { get; init; }
    public decimal? MinAmount { get; init; }
    public decimal? MaxAmount { get; init; }
    public string? AmountCurrency { get; init; }
    public string CategoryName { get; init; } = "";
    public int Priority { get; init; }
    public bool IsActive { get; init; }
}

/// <summary>Result of importing a rule set.</summary>
public sealed record RuleImportResultDto
{
    public int Imported { get; init; }
    public int Skipped { get; init; }
}

/// <summary>Dry-run impact of a rule before saving: how many transactions it would match and how many already
/// have a different (non-manual) category it would change, with a few examples.</summary>
public sealed record RulePreviewDto
{
    public int Matches { get; init; }
    public int WouldRecategorize { get; init; }
    public IReadOnlyList<RulePreviewSampleDto> Samples { get; init; } = [];
}

public sealed record RulePreviewSampleDto
{
    public DateOnly BookingDate { get; init; }
    public string? Counterparty { get; init; }
    public decimal Amount { get; init; }
    public string Currency { get; init; } = "CZK";
    public string? CurrentCategoryName { get; init; }
}

/// <summary>Permanently dismiss a learned suggestion for a counterparty + category so it isn't offered again.</summary>
public sealed record RuleSuggestionDismissRequest
{
    public string Pattern { get; init; } = "";
    public Guid CategoryId { get; init; }
}

/// <summary>Re-runs the family's rules over existing transactions. By default only fills transactions
/// that have no category yet, so manual categorizations are preserved.</summary>
public sealed record RecategorizeRequest
{
    public bool OnlyUncategorized { get; init; } = true;
}

/// <summary>Creates a manually entered batch ("pohyby") of movements on one account.</summary>
public sealed record CreateMovementBatchRequest
{
    public Guid BankAccountId { get; init; }
    public string? Label { get; init; }
    public IReadOnlyList<TransactionFields> Movements { get; init; } = [];
}

public sealed record MovementBatchResultDto
{
    public Guid BatchId { get; init; }
    public int CreatedCount { get; init; }
}

/// <summary>A batch of transactions on an account — either an imported file or a hand-entered set of movements.</summary>
public sealed record ImportBatchDto
{
    public Guid Id { get; init; }
    public BatchOrigin Origin { get; init; }
    public Guid BankAccountId { get; init; }
    public string? AccountName { get; init; }

    /// <summary>File name for imports; the user-given label for manual batches.</summary>
    public string? Name { get; init; }
    public BankProvider Bank { get; init; }
    public ImportFormat Format { get; init; }
    public ImportStatus Status { get; init; }
    public int ImportedCount { get; init; }
    public int DuplicateCount { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public string? Error { get; init; }
}

/// <summary>Renames a manual movement batch.</summary>
public sealed record UpdateBatchRequest
{
    public string? Label { get; init; }
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

    /// <summary>Rows that looked like data but could not be parsed (bad date/amount, missing columns).</summary>
    public int SkippedCount { get; init; }

    /// <summary>Human-readable warnings from parsing (capped), shown so dropped rows are not invisible.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    public ImportStatus Status { get; init; }
    public string? Error { get; init; }
}

/// <summary>A bank (ASPSP) available through Enable Banking for a given country.</summary>
public sealed record BankAspspDto
{
    public string Name { get; init; } = "";
    public string Country { get; init; } = "";
}

/// <summary>Starts an Open Banking connection for an account against a chosen bank.</summary>
public sealed record StartBankConnectionRequest
{
    [Required]
    public Guid BankAccountId { get; init; }

    [Required, StringLength(200, MinimumLength = 1)]
    public string AspspName { get; init; } = "";

    [Required, RegularExpression("^[A-Za-z]{2}$")]
    public string Country { get; init; } = "";
}

/// <summary>The redirect URL the user must visit to authorise the connection at their bank.</summary>
public sealed record StartBankConnectionResultDto
{
    public Guid ConnectionId { get; init; }
    public string AuthorizationUrl { get; init; } = "";
}

/// <summary>Stores the current user's own Enable Banking application credentials ("bring your own").</summary>
public sealed record SaveEnableBankingCredentialRequest
{
    [Required, StringLength(200, MinimumLength = 1)]
    public string ApplicationId { get; init; } = "";

    /// <summary>RSA private key in PEM form (the file downloaded from the Enable Banking control panel).</summary>
    [Required, StringLength(100_000, MinimumLength = 1)]
    public string PrivateKeyPem { get; init; } = "";
}

/// <summary>Whether the current user has Enable Banking credentials stored (the private key is never returned).</summary>
public sealed record EnableBankingCredentialStatusDto
{
    public bool Configured { get; init; }
    public string? ApplicationId { get; init; }

    /// <summary>The callback URL the user must register as the redirect URL in their Enable Banking app.</summary>
    public string? CallbackUrl { get; init; }
}

/// <summary>An Open Banking connection between an account and a bank, with its consent status.</summary>
public sealed record BankConnectionDto
{
    public Guid Id { get; init; }
    public Guid BankAccountId { get; init; }
    public string? AccountName { get; init; }
    public string AspspName { get; init; } = "";
    public string AspspCountry { get; init; } = "";
    public BankConnectionStatus Status { get; init; }
    public DateTimeOffset? ConsentValidUntil { get; init; }
    public DateTimeOffset? LastSyncedAt { get; init; }
    public string? LastError { get; init; }
}

public sealed record CategorySpendDto
{
    public string CategoryName { get; init; } = "";
    public string Color { get; init; } = "#64748b";
    public decimal Amount { get; init; }
}

/// <summary>Income, expense (both reported as positive magnitudes) and net for a period — drives the
/// dashboard's income-vs-expense chart with its period switcher.</summary>
public sealed record CashFlowDto
{
    public decimal Income { get; init; }
    public decimal Expense { get; init; }
    public decimal Net { get; init; }
}

public sealed record UpcomingPaymentDto
{
    public string Name { get; init; } = "";
    public decimal Amount { get; init; }
    public string Currency { get; init; } = "CZK";
    public DateOnly? DueDate { get; init; }
}

public sealed record DashboardSummaryDto
{
    public decimal TotalBalance { get; init; }
    public decimal IncomeThisMonth { get; init; }
    public decimal ExpenseThisMonth { get; init; }
    public decimal NetThisMonth { get; init; }

    /// <summary>Currency the headline totals are expressed in (the family's base currency).</summary>
    public string Currency { get; init; } = "CZK";

    /// <summary>Amounts that couldn't be converted to the base currency because a rate was missing,
    /// summed per original currency. Surfaced to the user instead of being assumed 1:1.</summary>
    public IReadOnlyList<CurrencyAmountDto> Unconverted { get; init; } = [];

    public IReadOnlyList<CategorySpendDto> TopExpenseCategories { get; init; } = [];
    public IReadOnlyList<UpcomingPaymentDto> Upcoming { get; init; } = [];
}

/// <summary>An amount in a specific currency (used for residual, un-converted dashboard sums).</summary>
public sealed record CurrencyAmountDto
{
    public string Currency { get; init; } = "CZK";
    public decimal Amount { get; init; }
}

// ---- Budgets & goals --------------------------------------------------------

/// <summary>A spending limit for an expense category with its actual spend in the current period.</summary>
public sealed record BudgetDto
{
    public Guid Id { get; init; }
    public Guid CategoryId { get; init; }
    public string CategoryName { get; init; } = "";
    public string? Color { get; init; }
    public BudgetPeriod Period { get; init; }

    /// <summary>Limit and actual spend for the current period, in the family's base currency.</summary>
    public decimal Amount { get; init; }
    public decimal Spent { get; init; }
    public string Currency { get; init; } = "CZK";

    public DateOnly PeriodStart { get; init; }
    public DateOnly PeriodEnd { get; init; }

    /// <summary>Concurrency token (Postgres xmin) echoed back on update to detect concurrent edits.</summary>
    public uint Version { get; init; }
}

public sealed record BudgetRequest
{
    public Guid CategoryId { get; set; }

    [Range(0, ValidationRules.MaxMoney, MinimumIsExclusive = true, ErrorMessage = "Částka musí být kladná.")]
    public decimal Amount { get; set; }

    public BudgetPeriod Period { get; set; } = BudgetPeriod.Monthly;

    /// <summary>Concurrency token loaded with the budget; the update is rejected (409) if it no longer matches.</summary>
    public uint Version { get; set; }
}

/// <summary>A savings goal tied to an account, with computed progress and the contribution needed to hit it.</summary>
public sealed record GoalDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = "";
    public Guid BankAccountId { get; init; }
    public string AccountName { get; init; } = "";
    public string Currency { get; init; } = "CZK";

    public decimal TargetAmount { get; init; }
    public decimal BaselineAmount { get; init; }

    /// <summary>How much has been saved toward the goal so far (current balance − baseline).</summary>
    public decimal Saved { get; init; }
    public DateOnly? TargetDate { get; init; }

    /// <summary>Contribution per month needed to reach the target by <see cref="TargetDate"/>, when set.</summary>
    public decimal? RequiredMonthly { get; init; }

    /// <summary>Concurrency token (Postgres xmin) echoed back on update to detect concurrent edits.</summary>
    public uint Version { get; init; }
}

public sealed record GoalRequest
{
    [Required(ErrorMessage = "Název je povinný.")]
    [StringLength(200, MinimumLength = 1)]
    public string Name { get; set; } = "";

    public Guid BankAccountId { get; set; }

    [Range(0, ValidationRules.MaxMoney, MinimumIsExclusive = true, ErrorMessage = "Cílová částka musí být kladná.")]
    public decimal TargetAmount { get; set; }

    public DateOnly? TargetDate { get; set; }

    /// <summary>Optional starting balance; defaults to the account's current balance at creation.</summary>
    [Range(0, ValidationRules.MaxMoney, ErrorMessage = "Počáteční částka je mimo povolený rozsah.")]
    public decimal? BaselineAmount { get; set; }

    /// <summary>Concurrency token loaded with the goal; the update is rejected (409) if it no longer matches.</summary>
    public uint Version { get; set; }
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

    /// <summary>The user's effective cross-family system permissions (account administration).</summary>
    public IReadOnlyList<SystemPermission> SystemPermissions { get; init; } = [];

    /// <summary>How often the client should re-poll for access changes as a fallback to live push (seconds).</summary>
    public int PollIntervalSeconds { get; init; } = 60;

    /// <summary>Whether the user has two-factor authentication enabled.</summary>
    public bool TwoFactorEnabled { get; init; }

    /// <summary>Admin-set deadline by which the user must enable 2FA, if any. Drives the in-app reminder.</summary>
    public DateTimeOffset? Require2faByUtc { get; init; }

    /// <summary>Whether Open Banking (bank connections + automatic sync via Enable Banking) is switched on
    /// server-side. Off by default because it is a paid integration; gates the "Připojení banky" UI.</summary>
    public bool OpenBankingEnabled { get; init; }

    public bool Can(Permission permission) => Permissions.Contains(permission);

    public bool CanSystem(SystemPermission permission) => SystemPermissions.Contains(permission);
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

    /// <summary>Concurrency token (Postgres xmin) the client echoes back on update to detect concurrent edits.</summary>
    public uint Version { get; init; }
}

/// <summary>Owner-initiated edit of an existing member's profile and role.</summary>
public sealed record UpdateMemberRequest
{
    [Required(ErrorMessage = "Jméno je povinné.")]
    [StringLength(120, MinimumLength = 1, ErrorMessage = "Jméno může mít nejvýše 120 znaků.")]
    public string DisplayName { get; set; } = "";

    [EmailAddress(ErrorMessage = "Zadejte platný e-mail.")]
    [StringLength(256, ErrorMessage = "E-mail může mít nejvýše 256 znaků.")]
    public string? Email { get; set; }

    public MemberRole Role { get; set; } = MemberRole.Adult;

    /// <summary>Controlling guardian; required when <see cref="Role"/> is <see cref="MemberRole.Child"/>.</summary>
    public Guid? GuardianMemberId { get; set; }

    /// <summary>Concurrency token loaded with the member; the update is rejected (409) if it no longer matches.</summary>
    public uint Version { get; set; }
}

public sealed record CreateMemberRequest
{
    [Required(ErrorMessage = "Jméno je povinné.")]
    [StringLength(120, MinimumLength = 1, ErrorMessage = "Jméno může mít nejvýše 120 znaků.")]
    public string DisplayName { get; set; } = "";

    [EmailAddress(ErrorMessage = "Zadejte platný e-mail.")]
    [StringLength(256, ErrorMessage = "E-mail může mít nejvýše 256 znaků.")]
    public string? Email { get; set; }

    public MemberRole Role { get; set; } = MemberRole.Adult;

    /// <summary>Required when <see cref="Role"/> is <see cref="MemberRole.Child"/>: the controlling guardian member.</summary>
    public Guid? GuardianMemberId { get; set; }
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

    /// <summary>Concurrency token (Postgres xmin) echoed back on update to detect concurrent edits.</summary>
    public uint Version { get; init; }
}

public sealed record CreateCardRequest
{
    public Guid? HolderMemberId { get; set; }

    [Required(ErrorMessage = "Jméno na kartě je povinné.")]
    [StringLength(120, MinimumLength = 1, ErrorMessage = "Jméno na kartě může mít nejvýše 120 znaků.")]
    public string CardholderName { get; set; } = "";

    [RegularExpression(ValidationRules.Last4Regex, ErrorMessage = "Zadejte nejvýše 4 číslice.")]
    public string? Last4 { get; set; }

    public CardType Type { get; set; } = CardType.Debit;

    [Range(1, 12, ErrorMessage = "Měsíc platnosti musí být 1–12.")]
    public int ExpiryMonth { get; set; }

    [Range(ValidationRules.MinExpiryYear, ValidationRules.MaxExpiryYear, ErrorMessage = "Neplatný rok platnosti.")]
    public int ExpiryYear { get; set; }

    [Range(0, ValidationRules.MaxMoney, ErrorMessage = "Měsíční limit nesmí být záporný.")]
    public decimal? MonthlyLimit { get; set; }
}

public sealed record UpdateCardRequest
{
    public Guid? HolderMemberId { get; set; }

    [Required(ErrorMessage = "Jméno na kartě je povinné.")]
    [StringLength(120, MinimumLength = 1, ErrorMessage = "Jméno na kartě může mít nejvýše 120 znaků.")]
    public string CardholderName { get; set; } = "";

    [RegularExpression(ValidationRules.Last4Regex, ErrorMessage = "Zadejte nejvýše 4 číslice.")]
    public string? Last4 { get; set; }

    public CardType Type { get; set; }

    [Range(1, 12, ErrorMessage = "Měsíc platnosti musí být 1–12.")]
    public int ExpiryMonth { get; set; }

    [Range(ValidationRules.MinExpiryYear, ValidationRules.MaxExpiryYear, ErrorMessage = "Neplatný rok platnosti.")]
    public int ExpiryYear { get; set; }

    public CardStatus Status { get; set; }

    [Range(0, ValidationRules.MaxMoney, ErrorMessage = "Měsíční limit nesmí být záporný.")]
    public decimal? MonthlyLimit { get; set; }

    /// <summary>Concurrency token loaded with the card; the update is rejected (409) if it no longer matches.</summary>
    public uint Version { get; set; }
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

    /// <summary>Concurrency token (Postgres xmin) echoed back on update to detect concurrent edits.</summary>
    public uint Version { get; init; }
}

public sealed record UpdateFamilyRequest
{
    [Required(ErrorMessage = "Název rodiny je povinný.")]
    [StringLength(200, MinimumLength = 1, ErrorMessage = "Název rodiny může mít nejvýše 200 znaků.")]
    public string Name { get; set; } = "";

    [Required(ErrorMessage = "Měna je povinná.")]
    [RegularExpression(ValidationRules.CurrencyRegex, ErrorMessage = "Měna musí být třípísmenný kód (např. CZK).")]
    public string BaseCurrency { get; set; } = "CZK";

    /// <summary>Concurrency token loaded with the family; the update is rejected (409) if it no longer matches.</summary>
    public uint Version { get; set; }
}

public sealed record TransferOwnershipRequest
{
    public Guid NewOwnerMemberId { get; init; }
}

/// <summary>Deletes the whole family; <see cref="ConfirmName"/> must match the family name exactly.</summary>
public sealed record DeleteFamilyRequest
{
    [Required(ErrorMessage = "Pro potvrzení zadejte název rodiny.")]
    public string ConfirmName { get; set; } = "";
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
    public bool TwoFactorEnabled { get; init; }
    public bool IsCurrentUser { get; init; }
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>Set when the account is soft-deleted (shown only in the deleted-accounts view).</summary>
    public DateTimeOffset? DeletedAtUtc { get; init; }
    public IReadOnlyList<string> Families { get; init; } = [];

    /// <summary>System role names the account holds.</summary>
    public IReadOnlyList<string> Roles { get; init; } = [];

    /// <summary>
    /// Deadline by which the user must enrol in 2FA. Null = no deadline. Set when an
    /// admin called the require-2fa endpoint. Login is blocked once this passes.
    /// </summary>
    public DateTimeOffset? Require2faByUtc { get; init; }
}

public sealed record Require2faRequest
{
    /// <summary>UTC deadline. Pass null to clear an existing requirement.</summary>
    public DateTimeOffset? DeadlineUtc { get; init; }
}

public sealed record AdminUserPageDto
{
    public IReadOnlyList<AdminUserDto> Items { get; init; } = [];
    public int TotalCount { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
}

public sealed record SetUserRolesRequest
{
    public IReadOnlyList<string> RoleNames { get; init; } = [];
}

// ---- System roles & permissions (cross-family, managed by a system admin) ----

public sealed record SystemRoleDto
{
    public Guid RoleId { get; init; }
    public string Name { get; init; } = "";

    /// <summary>The built-in administrator role: always all permissions, cannot be edited or deleted.</summary>
    public bool IsAdministrator { get; init; }
    public IReadOnlyList<SystemPermission> Permissions { get; init; } = [];
    public int UserCount { get; init; }
}

public sealed record SystemRolesDto
{
    public IReadOnlyList<SystemPermission> AllPermissions { get; init; } = [];
    public IReadOnlyList<SystemRoleDto> Roles { get; init; } = [];
}

public sealed record CreateSystemRoleRequest
{
    [Required(ErrorMessage = "Název role je povinný.")]
    [StringLength(256, MinimumLength = 1, ErrorMessage = "Název role může mít nejvýše 256 znaků.")]
    public string Name { get; set; } = "";
}

public sealed record RenameSystemRoleRequest
{
    [Required(ErrorMessage = "Název role je povinný.")]
    [StringLength(256, MinimumLength = 1, ErrorMessage = "Název role může mít nejvýše 256 znaků.")]
    public string Name { get; set; } = "";
}

public sealed record UpdateSystemRolePermissionsRequest
{
    public IReadOnlyList<SystemPermission> Permissions { get; init; } = [];
}

// ---- Audit log -------------------------------------------------------------

public sealed record AuditEntryDto
{
    public Guid Id { get; init; }
    public DateTimeOffset OccurredAt { get; init; }
    public string? ActorName { get; init; }
    public string Action { get; init; } = "";
    public string? TargetType { get; init; }
    public string? TargetName { get; init; }
    public string? Details { get; init; }
}

public sealed record AuditPageDto
{
    public IReadOnlyList<AuditEntryDto> Items { get; init; } = [];
    public int TotalCount { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
}

public sealed record CreateUserRequest
{
    [Required(ErrorMessage = "E-mail je povinný.")]
    [EmailAddress(ErrorMessage = "Zadejte platný e-mail.")]
    [StringLength(256, ErrorMessage = "E-mail může mít nejvýše 256 znaků.")]
    public string Email { get; set; } = "";

    [StringLength(120, ErrorMessage = "Jméno může mít nejvýše 120 znaků.")]
    public string? DisplayName { get; set; }

    [Required(ErrorMessage = "Heslo je povinné.")]
    [StringLength(100, MinimumLength = ValidationRules.MinPasswordLength, ErrorMessage = "Heslo musí mít alespoň 8 znaků.")]
    public string Password { get; set; } = "";

    public bool IsAdmin { get; set; }

    /// <summary>Require the user to choose a new password on first sign-in.</summary>
    public bool MustChangePassword { get; set; } = true;
}

public sealed record SetUserAdminRequest
{
    public bool IsAdmin { get; init; }
}

/// <summary>Temporary lock; the account unlocks itself after the given number of minutes.</summary>
public sealed record LockUserRequest
{
    [Range(1, 60 * 24 * 365, ErrorMessage = "Doba zamčení musí být 1 minuta až 1 rok.")]
    public int Minutes { get; set; } = 60;
}

public sealed record AdminSetPasswordRequest
{
    [Required(ErrorMessage = "Nové heslo je povinné.")]
    [StringLength(100, MinimumLength = ValidationRules.MinPasswordLength, ErrorMessage = "Heslo musí mít alespoň 8 znaků.")]
    public string NewPassword { get; set; } = "";

    /// <summary>Also require the user to change this password on next sign-in.</summary>
    public bool MustChangePassword { get; set; } = true;
}
