using System.Security.Cryptography;
using Flowlio.Application.Abstractions;
using Flowlio.Domain;
using Flowlio.Shared;
using Microsoft.EntityFrameworkCore;
using Wolverine.Attributes;

namespace Flowlio.Application.Banking;

/// <summary>Starts an Open Banking consent for an account: asks the aggregator for the bank authorisation
/// URL and records a pending <see cref="BankConnection"/> the redirect callback can be correlated to.</summary>
public sealed record StartBankConnectionCommand
{
    public required Guid FamilyId { get; init; }
    public required Guid CreatedByUserId { get; init; }
    public required Guid BankAccountId { get; init; }
    public required string AspspName { get; init; }
    public required string Country { get; init; }
}

public sealed class StartBankConnectionHandler
{
    /// <summary>PSD2 consent lifetime requested from the bank; banks cap this (typically at ~90 days).</summary>
    private const int ConsentValidityDays = 90;

    [Transactional]
    public static async Task<StartBankConnectionResultDto> Handle(
        StartBankConnectionCommand command,
        IAppDbContext db,
        IBankDataProvider provider,
        IBankCredentialProvider credentialProvider,
        IAuditLog audit,
        CancellationToken ct)
    {
        var credentials = await credentialProvider.GetAsync(command.CreatedByUserId, ct)
            ?? throw new InvalidOperationException("Nemáte uložené přístupy k Enable Banking. Nejprve je vyplňte.");

        var account = await db.BankAccounts
            .FirstOrDefaultAsync(a => a.Id == command.BankAccountId && a.FamilyId == command.FamilyId, ct)
            ?? throw new InvalidOperationException("Bank account not found for the current family.");

        // Unguessable single-use token tying the bank's redirect callback back to this connection.
        var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var validUntil = DateTimeOffset.UtcNow.AddDays(ConsentValidityDays);

        var start = await provider.StartAuthorizationAsync(credentials, command.AspspName, command.Country, state, validUntil, ct);

        var connection = new BankConnection
        {
            FamilyId = command.FamilyId,
            BankAccountId = account.Id,
            AspspName = command.AspspName,
            AspspCountry = command.Country,
            AuthorizationId = start.AuthorizationId,
            State = state,
            ConsentValidUntil = validUntil,
            Status = BankConnectionStatus.Pending,
            CreatedByUserId = command.CreatedByUserId,
        };
        db.BankConnections.Add(connection);
        await db.SaveChangesAsync(ct);

        await audit.RecordAsync("bank.connect.start", "BankConnection", connection.Id.ToString(),
            command.AspspName, command.FamilyId, $"Zahájeno připojení banky {command.AspspName}", ct);

        return new StartBankConnectionResultDto
        {
            ConnectionId = connection.Id,
            AuthorizationUrl = start.AuthorizationUrl,
        };
    }
}
