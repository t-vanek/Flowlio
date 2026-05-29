using Flowlio.Application.Abstractions;
using Flowlio.Application.Statements;
using Flowlio.Domain;
using Flowlio.Shared;
using Microsoft.EntityFrameworkCore;
using Wolverine;
using Wolverine.Attributes;

namespace Flowlio.Application.Banking;

/// <summary>Pulls new transactions for an active connection from the bank's Open Banking API, persisting
/// them through the shared dedup/categorize pipeline. Triggered on demand and by the background sync.</summary>
public sealed record SyncBankAccountCommand
{
    public required Guid BankConnectionId { get; init; }

    /// <summary>The user who triggered an on-demand sync; null for the background scheduler.</summary>
    public Guid? TriggeredByUserId { get; init; }
}

public sealed class SyncBankAccountHandler
{
    /// <summary>How far back to look on the first sync, when there is no previous watermark.</summary>
    private const int InitialLookbackDays = 90;

    /// <summary>Small overlap re-fetched on each sync so a late-booked entry near the last run isn't missed
    /// (the dedup hash drops the re-fetched duplicates).</summary>
    private const int OverlapDays = 3;

    [Transactional]
    public static async Task<ImportResultDto> Handle(
        SyncBankAccountCommand command,
        IAppDbContext db,
        IBankDataProvider provider,
        IBankCredentialProvider credentialProvider,
        IAuditLog audit,
        IMessageContext messaging,
        CancellationToken ct)
    {
        var connection = await db.BankConnections
            .Include(c => c.BankAccount)
            .FirstOrDefaultAsync(c => c.Id == command.BankConnectionId, ct)
            ?? throw new InvalidOperationException("Připojení banky nebylo nalezeno.");

        if (connection.Status is BankConnectionStatus.Pending or BankConnectionStatus.Expired
            || connection.AccountUid is null || connection.SessionId is null)
        {
            return new ImportResultDto
            {
                Status = ImportStatus.Failed,
                Error = "Připojení banky není aktivní – nejprve je dokončete nebo znovu autorizujte.",
            };
        }

        var account = connection.BankAccount
            ?? throw new InvalidOperationException("Připojení banky odkazuje na neexistující účet.");

        // The connection is synced with the credentials of the user who created it (their Enable Banking app).
        var credentials = await credentialProvider.GetAsync(connection.CreatedByUserId, ct);
        if (credentials is null)
        {
            connection.Status = BankConnectionStatus.Error;
            connection.LastError = "Chybí přístupy k Enable Banking.";
            await db.SaveChangesAsync(ct);
            return new ImportResultDto { Status = ImportStatus.Failed, Error = connection.LastError };
        }

        var since = connection.LastSyncedAt is { } last
            ? DateOnly.FromDateTime(last.UtcDateTime).AddDays(-OverlapDays)
            : DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-InitialLookbackDays);

        var batch = new ImportBatch
        {
            FamilyId = connection.FamilyId,
            BankAccountId = account.Id,
            Origin = BatchOrigin.BankSync,
            Format = ImportFormat.BankApi,
            Bank = account.Bank,
            FileName = $"{connection.AspspName} (Open Banking)",
            ImportedByUserId = command.TriggeredByUserId ?? Guid.Empty,
            Status = ImportStatus.Parsing,
        };
        db.ImportBatches.Add(batch);

        IReadOnlyList<ParsedTransaction> fetched;
        try
        {
            fetched = await provider.FetchTransactionsAsync(credentials, connection.AccountUid, since, ct);
        }
        catch (BankConsentExpiredException ex)
        {
            connection.Status = BankConnectionStatus.Expired;
            connection.LastError = ex.Message;
            batch.Status = ImportStatus.Failed;
            batch.Error = ex.Message;
            await db.SaveChangesAsync(ct);
            return new ImportResultDto
            {
                ImportBatchId = batch.Id,
                Status = ImportStatus.Failed,
                Error = "Souhlas s přístupem vypršel – banku je třeba znovu připojit.",
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            connection.Status = BankConnectionStatus.Error;
            connection.LastError = ex.Message;
            batch.Status = ImportStatus.Failed;
            batch.Error = ex.Message;
            await db.SaveChangesAsync(ct);
            return new ImportResultDto { ImportBatchId = batch.Id, Status = ImportStatus.Failed, Error = ex.Message };
        }

        var (imported, duplicates) = await TransactionPersister.PersistAsync(
            db, connection.FamilyId, account, batch, fetched, ct);

        batch.Status = ImportStatus.Completed;
        batch.ImportedCount = imported;
        batch.DuplicateCount = duplicates;
        connection.LastSyncedAt = DateTimeOffset.UtcNow;
        connection.Status = BankConnectionStatus.Active;
        connection.LastError = null;
        connection.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        await audit.RecordAsync("bank.sync", "BankConnection", connection.Id.ToString(),
            connection.AspspName, connection.FamilyId,
            $"Synchronizace banky ({imported} pohybů, {duplicates} duplicit)", ct);

        // Same completion event as a file import: invalidates the dashboard cache and pushes a live
        // SignalR notification, via the transactional outbox.
        await messaging.PublishAsync(new StatementImported
        {
            FamilyId = connection.FamilyId,
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
}
