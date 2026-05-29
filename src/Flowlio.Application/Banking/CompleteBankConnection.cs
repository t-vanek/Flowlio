using Flowlio.Application.Abstractions;
using Flowlio.Domain;
using Flowlio.Shared;
using Microsoft.EntityFrameworkCore;
using Wolverine.Attributes;

namespace Flowlio.Application.Banking;

/// <summary>Finalizes a pending connection from the authorisation code on the bank's redirect callback:
/// opens the aggregator session, binds the matching account and marks the connection active.</summary>
public sealed record CompleteBankConnectionCommand
{
    public required Guid FamilyId { get; init; }
    public required string Code { get; init; }
    public required string State { get; init; }
}

public sealed class CompleteBankConnectionHandler
{
    [Transactional]
    public static async Task<BankConnectionDto> Handle(
        CompleteBankConnectionCommand command,
        IAppDbContext db,
        IBankDataProvider provider,
        IAuditLog audit,
        CancellationToken ct)
    {
        var connection = await db.BankConnections
            .Include(c => c.BankAccount)
            .FirstOrDefaultAsync(c => c.State == command.State
                                      && c.FamilyId == command.FamilyId
                                      && c.Status == BankConnectionStatus.Pending, ct)
            ?? throw new InvalidOperationException("Připojení banky nebylo nalezeno nebo už bylo dokončeno.");

        var session = await provider.CreateSessionAsync(command.Code, ct);

        // Bind the authorised account that matches the Flowlio account (by IBAN/number) — else the first.
        var match = session.Accounts.FirstOrDefault(a => MatchesAccount(a, connection.BankAccount))
                    ?? session.Accounts.FirstOrDefault()
                    ?? throw new InvalidOperationException("Autorizovaná relace nevrátila žádný účet.");

        connection.SessionId = session.SessionId;
        connection.AccountUid = match.Uid;
        connection.ConsentValidUntil = session.ConsentValidUntil ?? connection.ConsentValidUntil;
        connection.Status = BankConnectionStatus.Active;
        connection.LastError = null;
        connection.State = null; // single-use: consume it so the callback cannot be replayed
        connection.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        await audit.RecordAsync("bank.connect.complete", "BankConnection", connection.Id.ToString(),
            connection.AspspName, connection.FamilyId, $"Připojena banka {connection.AspspName}", ct);

        return Map(connection);
    }

    private static bool MatchesAccount(BankAccountRef reference, BankAccount? account)
    {
        if (account?.AccountNumber is not { } number || reference.Iban is null)
            return false;
        return string.Equals(Normalize(reference.Iban), Normalize(number), StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string value) => value.Replace(" ", "").Replace("-", "").Replace("/", "");

    internal static BankConnectionDto Map(BankConnection c) => new()
    {
        Id = c.Id,
        BankAccountId = c.BankAccountId,
        AccountName = c.BankAccount?.Name,
        AspspName = c.AspspName,
        AspspCountry = c.AspspCountry,
        Status = c.Status,
        ConsentValidUntil = c.ConsentValidUntil,
        LastSyncedAt = c.LastSyncedAt,
        LastError = c.LastError,
    };
}
