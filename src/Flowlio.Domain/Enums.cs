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
    /// <summary>Can manage accounts, transactions, budgets.</summary>
    Adult = 1,
    /// <summary>Read-only access to shared dashboards.</summary>
    Viewer = 2,
}

/// <summary>File format a statement was imported from.</summary>
public enum ImportFormat
{
    Csv = 0,
    Pdf = 1,
    PdfOcr = 2,
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

/// <summary>Which transaction field a categorization rule inspects.</summary>
public enum RuleMatchField
{
    CounterpartyName = 0,
    Description = 1,
    VariableSymbol = 2,
    CounterpartyAccount = 3,
}
