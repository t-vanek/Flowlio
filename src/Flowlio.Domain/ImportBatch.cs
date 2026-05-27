using Flowlio.Domain.Common;

namespace Flowlio.Domain;

/// <summary>Record of one statement file imported into an account, for audit and undo.</summary>
public class ImportBatch : AuditableEntity
{
    public Guid FamilyId { get; set; }

    public Guid BankAccountId { get; set; }
    public BankAccount? BankAccount { get; set; }

    public required string FileName { get; set; }
    public ImportFormat Format { get; set; }
    public BankProvider Bank { get; set; }
    public ImportStatus Status { get; set; } = ImportStatus.Pending;

    /// <summary>Identity user who uploaded the statement.</summary>
    public Guid ImportedByUserId { get; set; }

    /// <summary>Number of new transactions persisted from this file (excludes detected duplicates).</summary>
    public int ImportedCount { get; set; }

    /// <summary>Number of rows skipped because they already existed.</summary>
    public int DuplicateCount { get; set; }

    public string? Error { get; set; }

    public ICollection<Transaction> Transactions { get; set; } = [];
}
