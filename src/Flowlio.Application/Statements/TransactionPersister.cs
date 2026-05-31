using Flowlio.Application.Abstractions;
using Flowlio.Domain;
using Microsoft.EntityFrameworkCore;

namespace Flowlio.Application.Statements;

/// <summary>
/// Persists a set of <see cref="ParsedTransaction"/> rows into an account: deduplicates against the rows
/// already on the account (and within the same batch), auto-categorizes via the family's scope-ordered
/// rules, and links each new row to the import batch. Shared by statement-file import and Open Banking
/// sync so both paths deduplicate and categorize identically.
/// </summary>
public static class TransactionPersister
{
    /// <summary>
    /// Adds new transactions to the EF change set (the caller owns <c>SaveChangesAsync</c>) and returns how
    /// many were imported versus skipped as duplicates.
    /// </summary>
    public static async Task<(int Imported, int Duplicates)> PersistAsync(
        IAppDbContext db,
        Guid familyId,
        BankAccount account,
        ImportBatch batch,
        IReadOnlyList<ParsedTransaction> transactions,
        CancellationToken ct)
    {
        var familyRules = await db.CategorizationRules
            .Include(r => r.Category)
            .Where(r => r.FamilyId == familyId && r.IsActive)
            .ToListAsync(ct);
        // All rows land on this one account, so resolve the applicable, scope-ordered rules once.
        var rules = TransactionCategorizer.ForAccount(familyRules, account.Id, account.OwnerMemberId);

        // Compute the batch's dedup hashes up front and load only those already on the account (the
        // intersection) instead of pulling every historical hash for the account into memory. On Npgsql
        // the Contains translates to `= ANY(@hashes)` (a single array parameter), so the batch size — not
        // the account's full history — bounds both the query and the working set.
        var batchHashes = transactions.Select(parsed => DedupHasher.Compute(account.Id, parsed)).ToList();
        var existingHashes = await db.Transactions
            .Where(t => t.BankAccountId == account.Id && batchHashes.Contains(t.DedupHash))
            .Select(t => t.DedupHash)
            .ToHashSetAsync(ct);

        var imported = 0;
        var duplicates = 0;
        var seenInFile = new HashSet<string>();

        for (var i = 0; i < transactions.Count; i++)
        {
            var parsed = transactions[i];
            var hash = batchHashes[i];
            if (!existingHashes.Add(hash) || !seenInFile.Add(hash))
            {
                duplicates++;
                continue;
            }

            var direction = parsed.Amount < 0 ? TransactionDirection.Outgoing : TransactionDirection.Incoming;
            var matchedRule = TransactionCategorizer.MatchRule(
                parsed.CounterpartyName, parsed.Description, parsed.VariableSymbol, parsed.CounterpartyAccount,
                parsed.Amount, account.Currency, direction, rules);

            db.Transactions.Add(new Transaction
            {
                FamilyId = familyId,
                BankAccountId = account.Id,
                BookingDate = parsed.BookingDate,
                ValueDate = parsed.ValueDate,
                Amount = parsed.Amount,
                // A transaction always uses its account's currency, not whatever the source guessed.
                Currency = account.Currency,
                Direction = direction,
                CounterpartyName = parsed.CounterpartyName,
                CounterpartyAccount = parsed.CounterpartyAccount,
                VariableSymbol = parsed.VariableSymbol,
                ConstantSymbol = parsed.ConstantSymbol,
                SpecificSymbol = parsed.SpecificSymbol,
                Description = parsed.Description,
                CategoryId = matchedRule?.CategoryId,
                CategorySource = matchedRule is null ? CategorySource.None : CategorySource.Rule,
                AppliedRuleId = matchedRule?.Id,
                ImportBatchId = batch.Id,
                DedupHash = hash,
            });
            imported++;
        }

        return (imported, duplicates);
    }
}
