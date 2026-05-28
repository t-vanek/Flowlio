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
    Csv = 0,
    Pdf = 1,
    PdfOcr = 2,
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
