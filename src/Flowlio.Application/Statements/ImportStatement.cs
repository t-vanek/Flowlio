using Flowlio.Application.Abstractions;
using Flowlio.Domain;
using Flowlio.Shared;
using Microsoft.EntityFrameworkCore;
using Wolverine;
using Wolverine.Attributes;

namespace Flowlio.Application.Statements;

/// <summary>Imports a bank statement file into an account, deduplicating and auto-categorizing rows.</summary>
public sealed record ImportStatementCommand
{
    public required Guid BankAccountId { get; init; }
    public required BankProvider Bank { get; init; }
    public required ImportFormat Format { get; init; }
    public required string FileName { get; init; }
    public required byte[] Content { get; init; }
}

public sealed class ImportStatementHandler
{
    [Transactional]
    public static async Task<ImportResultDto> Handle(
        ImportStatementCommand command,
        IAppDbContext db,
        IStatementImporter importer,
        ICurrentFamily currentFamily,
        ICurrentUser currentUser,
        IAuditLog audit,
        IMessageContext messaging,
        CancellationToken ct)
    {
        var familyId = await currentFamily.RequireAsync(ct);

        var account = await db.BankAccounts
            .FirstOrDefaultAsync(a => a.Id == command.BankAccountId && a.FamilyId == familyId, ct)
            ?? throw new InvalidOperationException("Bank account not found for the current family.");

        var batch = new ImportBatch
        {
            FamilyId = familyId,
            BankAccountId = account.Id,
            FileName = command.FileName,
            Format = command.Format,
            Bank = command.Bank,
            ImportedByUserId = currentUser.UserId ?? Guid.Empty,
            Status = ImportStatus.Parsing,
        };
        db.ImportBatches.Add(batch);

        ParsedStatement statement;
        try
        {
            using var stream = new MemoryStream(command.Content);
            statement = importer.Parse(stream, command.FileName, command.Bank, command.Format);
        }
        catch (Exception ex)
        {
            batch.Status = ImportStatus.Failed;
            batch.Error = ex.Message;
            await db.SaveChangesAsync(ct);
            return new ImportResultDto
            {
                ImportBatchId = batch.Id,
                Status = ImportStatus.Failed,
                Error = ex.Message,
            };
        }

        var familyRules = await db.CategorizationRules
            .Include(r => r.Category)
            .Where(r => r.FamilyId == familyId && r.IsActive)
            .ToListAsync(ct);
        // All imported rows land on this one account, so resolve the applicable, scope-ordered rules once.
        var rules = TransactionCategorizer.ForAccount(familyRules, account.Id, account.OwnerMemberId);

        var existingHashes = await db.Transactions
            .Where(t => t.BankAccountId == account.Id)
            .Select(t => t.DedupHash)
            .ToHashSetAsync(ct);

        var imported = 0;
        var duplicates = 0;
        var seenInFile = new HashSet<string>();

        foreach (var parsed in statement.Transactions)
        {
            var hash = DedupHasher.Compute(account.Id, parsed);
            if (!existingHashes.Add(hash) || !seenInFile.Add(hash))
            {
                duplicates++;
                continue;
            }

            var direction = parsed.Amount < 0 ? TransactionDirection.Outgoing : TransactionDirection.Incoming;
            var matchedCategory = TransactionCategorizer.Match(
                parsed.CounterpartyName, parsed.Description, parsed.VariableSymbol, parsed.CounterpartyAccount,
                direction, rules);

            db.Transactions.Add(new Transaction
            {
                FamilyId = familyId,
                BankAccountId = account.Id,
                BookingDate = parsed.BookingDate,
                ValueDate = parsed.ValueDate,
                Amount = parsed.Amount,
                Currency = parsed.Currency,
                Direction = direction,
                CounterpartyName = parsed.CounterpartyName,
                CounterpartyAccount = parsed.CounterpartyAccount,
                VariableSymbol = parsed.VariableSymbol,
                ConstantSymbol = parsed.ConstantSymbol,
                SpecificSymbol = parsed.SpecificSymbol,
                Description = parsed.Description,
                CategoryId = matchedCategory,
                CategorySource = matchedCategory is null ? CategorySource.None : CategorySource.Rule,
                ImportBatchId = batch.Id,
                DedupHash = hash,
            });
            imported++;
        }

        batch.Status = ImportStatus.Completed;
        batch.ImportedCount = imported;
        batch.DuplicateCount = duplicates;
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync("import.statement", "ImportBatch", batch.Id.ToString(),
            batch.FileName, familyId, $"Import výpisu ({imported} pohybů, {duplicates} duplicit)", ct);

        // Fan out the completion via the transactional outbox: the event is stored together with the
        // transactions above and delivered to RabbitMQ only after commit (guaranteed, crash-safe).
        await messaging.PublishAsync(new StatementImported
        {
            FamilyId = familyId,
            BankAccountId = account.Id,
            ImportBatchId = batch.Id,
            ImportedCount = imported,
            DuplicateCount = duplicates,
        });

        return new ImportResultDto
        {
            ImportBatchId = batch.Id,
            ImportedCount = imported,
            DuplicateCount = duplicates,
            SkippedCount = statement.SkippedRowCount,
            Warnings = statement.Diagnostics
                .Where(d => d.Severity >= ParseSeverity.Warning)
                .Select(d => d.Line is { } line ? $"Řádek {line}: {d.Message}" : d.Message)
                .Take(20)
                .ToList(),
            Status = ImportStatus.Completed,
        };
    }
}
