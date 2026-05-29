namespace Flowlio.Domain;

/// <summary>Supported banks. Drives which statement parser is used during import.</summary>
public enum BankProvider
{
    Other = 0,
    Csob = 1,
    KomercniBanka = 2,
    CeskaSporitelna = 3,
    Fio = 4,
    AirBank = 5,
    Revolut = 6,
}

/// <summary>Whether a category groups income or expenses.</summary>
public enum CategoryKind
{
    Expense = 0,
    Income = 1,
}

/// <summary>Direction of money flow for a transaction.</summary>
public enum TransactionDirection
{
    Outgoing = 0,
    Incoming = 1,
}

/// <summary>How a transaction's category was assigned. Lets rule recategorization leave human choices
/// untouched, and lets Flowlio learn rule suggestions from repeated manual categorizations.</summary>
public enum CategorySource
{
    /// <summary>No category assigned yet.</summary>
    None = 0,
    /// <summary>Assigned automatically by a categorization rule (at import or recategorization).</summary>
    Rule = 1,
    /// <summary>Set by a person (manual edit or bulk categorize). Never overwritten by rules.</summary>
    Manual = 2,
}

/// <summary>How often a recurring payment or subscription repeats.</summary>
public enum RecurrenceFrequency
{
    Weekly = 0,
    Monthly = 1,
    Quarterly = 2,
    SemiAnnually = 3,
    Annually = 4,
}

/// <summary>Role of a member within a family, controlling what they may see and do.</summary>
public enum MemberRole
{
    /// <summary>Full control including managing members and deleting the family.</summary>
    Owner = 0,
    /// <summary>Can manage accounts, transactions, budgets. Spouses join with this role.</summary>
    Adult = 1,
    /// <summary>Read-only access to shared dashboards.</summary>
    Viewer = 2,
    /// <summary>A child whose accounts and cards are controlled by a guardian (parent) member.</summary>
    Child = 3,
}

/// <summary>
/// What a non-owner family member may do on a specific bank account. The account's primary owner
/// is tracked separately via <c>BankAccount.OwnerMemberId</c>.
/// </summary>
public enum AccountAccessLevel
{
    /// <summary>Authorized user ("disponent") — may view the account and manage its cards/transactions.</summary>
    Disponent = 0,
    /// <summary>Read-only access to the account.</summary>
    Viewer = 1,
}

/// <summary>Kind of payment card issued on a bank account.</summary>
public enum CardType
{
    Debit = 0,
    Credit = 1,
    Prepaid = 2,
}

/// <summary>Operational state of a payment card.</summary>
public enum CardStatus
{
    Active = 0,
    Blocked = 1,
    Expired = 2,
}

/// <summary>Lifecycle of a family invitation sent so another person can join with their own login.</summary>
public enum InvitationStatus
{
    Pending = 0,
    Accepted = 1,
    Revoked = 2,
    Expired = 3,
}

/// <summary>File format a statement was imported from.</summary>
public enum ImportFormat
{
    /// <summary>Deprecated: CSV import proved unreliable across banks and is hidden from the UI.
    /// Still parsed server-side for backward compatibility; prefer <see cref="Pdf"/>.</summary>
    [Obsolete("CSV import is unstable and hidden from the UI; import PDF statements instead. Kept for backward compatibility only.")]
    Csv = 0,
    Pdf = 1,
    PdfOcr = 2,

    /// <summary>Deprecated: XLSX import proved unreliable across banks and is hidden from the UI.
    /// Still parsed server-side for backward compatibility; prefer <see cref="Pdf"/>.</summary>
    [Obsolete("XLSX import is unstable and hidden from the UI; import PDF statements instead. Kept for backward compatibility only.")]
    Xlsx = 3,
}

/// <summary>Lifecycle of a statement import.</summary>
public enum ImportStatus
{
    Pending = 0,
    Parsing = 1,
    AwaitingReview = 2,
    Completed = 3,
    Failed = 4,
}

/// <summary>How a batch of transactions came to exist on an account.</summary>
public enum BatchOrigin
{
    /// <summary>Created by parsing an uploaded statement file.</summary>
    FileImport = 0,
    /// <summary>Entered by hand by a family member (no source file).</summary>
    Manual = 1,
}

/// <summary>Which transaction field a categorization rule inspects.</summary>
public enum RuleMatchField
{
    CounterpartyName = 0,
    Description = 1,
    VariableSymbol = 2,
    CounterpartyAccount = 3,

    /// <summary>Match the pattern against every text field at once (counterparty, description, symbols).
    /// Card payments rarely carry a counterparty, so the merchant lives in the description — this is the
    /// sensible default so a rule still matches when the counterparty is empty.</summary>
    Any = 4,
}

/// <summary>How a categorization rule's <c>Pattern</c> is matched against the chosen field.</summary>
public enum RuleMatchMode
{
    /// <summary>Pattern matches anywhere in the field (default). Simple but prone to false positives on
    /// short patterns: "PID" would match "rapid", "Plat" would match "platba".</summary>
    Substring = 0,

    /// <summary>Pattern matches only as a whole word (bounded by word boundaries), so "Plat" no longer
    /// matches "platba" and "PID" no longer matches "rapid".</summary>
    WholeWord = 1,

    /// <summary>Pattern is a regular expression (case-insensitive, evaluated against the diacritics-folded
    /// text). For power users, e.g. "albert|billa|lidl".</summary>
    Regex = 2,
}

/// <summary>
/// Who a categorization rule applies to. More specific scopes win over broader ones when several match
/// the same transaction (Account &gt; Personal &gt; Family), with rule priority breaking ties within a scope.
/// </summary>
public enum RuleScope
{
    /// <summary>Belongs to one member; applies only to transactions on accounts that member owns.</summary>
    Personal = 0,

    /// <summary>Applies only to transactions on one specific bank account. Managed by the family owner.</summary>
    Account = 1,

    /// <summary>Applies to every account in the family. Managed by the family owner.</summary>
    Family = 2,
}

/// <summary>How often a budget's spending limit resets.</summary>
public enum BudgetPeriod
{
    Weekly = 0,
    Monthly = 1,
    Yearly = 2,
}
