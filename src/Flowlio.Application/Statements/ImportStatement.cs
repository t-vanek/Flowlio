using Flowlio.Application.Abstractions;
using Flowlio.Domain;
using Flowlio.Shared;
using Microsoft.EntityFrameworkCore;
using Wolverine;

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
    public static async Task<ImportResultDto> Handle(
        ImportStatementCommand command,
        IAppDbContext db,
        IStatementParserFactory parserFactory,
        ICurrentFamily currentFamily,
        ICurrentUser currentUser,
        IMessageBus bus,
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
            var parser = parserFactory.Resolve(command.Bank, command.Format);
            using var stream = new MemoryStream(command.Content);
            statement = parser.Parse(stream, command.FileName);
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

        var rules = await db.CategorizationRules
            .Where(r => r.FamilyId == familyId && r.IsActive)
            .OrderByDescending(r => r.Priority)
            .ToListAsync(ct);

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

            db.Transactions.Add(new Transaction
            {
                FamilyId = familyId,
                BankAccountId = account.Id,
                BookingDate = parsed.BookingDate,
                ValueDate = parsed.ValueDate,
                Amount = parsed.Amount,
                Currency = parsed.Currency,
                Direction = parsed.Amount < 0 ? TransactionDirection.Outgoing : TransactionDirection.Incoming,
                CounterpartyName = parsed.CounterpartyName,
                CounterpartyAccount = parsed.CounterpartyAccount,
                VariableSymbol = parsed.VariableSymbol,
                ConstantSymbol = parsed.ConstantSymbol,
                SpecificSymbol = parsed.SpecificSymbol,
                Description = parsed.Description,
                CategoryId = MatchCategory(parsed, rules),
                ImportBatchId = batch.Id,
                DedupHash = hash,
            });
            imported++;
        }

        batch.Status = ImportStatus.Completed;
        batch.ImportedCount = imported;
        batch.DuplicateCount = duplicates;
        await db.SaveChangesAsync(ct);

        // Fan out the completion (RabbitMQ): invalidates cached views and notifies the family in real time.
        await bus.PublishAsync(new StatementImported
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
            Status = ImportStatus.Completed,
        };
    }

    private static Guid? MatchCategory(ParsedTransaction tx, IReadOnlyList<CategorizationRule> rules)
    {
        foreach (var rule in rules)
        {
            var value = rule.Field switch
            {
                RuleMatchField.CounterpartyName => tx.CounterpartyName,
                RuleMatchField.Description => tx.Description,
                RuleMatchField.VariableSymbol => tx.VariableSymbol,
                RuleMatchField.CounterpartyAccount => tx.CounterpartyAccount,
                _ => null,
            };

            if (value is not null &&
                value.Contains(rule.Pattern, StringComparison.OrdinalIgnoreCase))
            {
                return rule.CategoryId;
            }
        }
        return null;
    }
}
