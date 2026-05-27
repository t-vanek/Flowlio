using Flowlio.Domain.Common;

namespace Flowlio.Domain;

/// <summary>
/// Record of one batch of transactions added to an account, for audit and undo. A batch is either a
/// parsed statement file (<see cref="BatchOrigin.FileImport"/>) or a hand-entered set of movements
/// (<see cref="BatchOrigin.Manual"/>).
/// </summary>
public class ImportBatch : AuditableEntity
{
    public Guid FamilyId { get; set; }

    public Guid BankAccountId { get; set; }
    public BankAccount? BankAccount { get; set; }

    public BatchOrigin Origin { get; set; } = BatchOrigin.FileImport;

    /// <summary>Name of the uploaded file; null for manually entered batches.</summary>
    public string? FileName { get; set; }

    /// <summary>User-given name for a manually entered batch of movements; null for file imports.</summary>
    public string? Label { get; set; }

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
