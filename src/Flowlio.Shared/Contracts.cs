using Flowlio.Domain;

namespace Flowlio.Shared;

public sealed record FamilyMemberDto
{
    public Guid Id { get; init; }
    public string DisplayName { get; init; } = "";
    public MemberRole Role { get; init; }
    public bool IsCurrentUser { get; init; }
    public int AccountCount { get; init; }
}

public sealed record CreateFamilyMemberRequest
{
    public string DisplayName { get; init; } = "";
    public MemberRole Role { get; init; } = MemberRole.Adult;
}

public sealed record BankAccountDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = "";
    public BankProvider Bank { get; init; }
    public string? AccountNumber { get; init; }
    public Currency Currency { get; init; } = Currency.CZK;
    public decimal OpeningBalance { get; init; }
    public decimal CurrentBalance { get; init; }
    public Guid OwnerMemberId { get; init; }
    public string OwnerMemberName { get; init; } = "";
}

public sealed record CreateBankAccountRequest
{
    public string Name { get; init; } = "";
    public BankProvider Bank { get; init; }
    public string? AccountNumber { get; init; }
    public Currency Currency { get; init; } = Currency.CZK;
    public decimal OpeningBalance { get; init; }
    public Guid OwnerMemberId { get; init; }
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
    public Currency Currency { get; init; } = Currency.CZK;
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
    public Currency Currency { get; init; } = Currency.CZK;
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
    public Currency Currency { get; init; } = Currency.CZK;
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
