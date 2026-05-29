using Flowlio.Application.Abstractions;
using Flowlio.Application.Banking;
using Flowlio.Domain;
using Flowlio.Shared;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Wolverine;
using static Flowlio.Server.Auth.MemberAuthorization;

namespace Flowlio.Server.Endpoints;

/// <summary>
/// Open Banking (PSD2) connections via Enable Banking: list available banks, start a consent, finalize it
/// from the redirect callback, sync transactions on demand and disconnect. The heavy lifting runs in the
/// Wolverine handlers (transactional, with the same completion event as a file import).
/// </summary>
public static class BankConnectionEndpoints
{
    public static void MapBankConnectionEndpoints(this IEndpointRouteBuilder api)
    {
        api.MapGet("/bank-connections/banks", ListBanks);
        api.MapGet("/bank-connections", ListConnections);
        api.MapPost("/bank-connections", StartConnection);
        api.MapPost("/bank-connections/complete", CompleteConnection);
        api.MapPost("/bank-connections/{id:guid}/sync", SyncConnection);
        api.MapDelete("/bank-connections/{id:guid}", Disconnect);
    }

    private static async Task<IResult> ListBanks(
        [FromQuery] string? country, IBankDataProvider provider, ICurrentFamily family, CancellationToken ct)
    {
        if (!await family.CanAsync(Permission.ImportStatements, ct))
            return Forbidden();
        if (!provider.IsConfigured)
            return NotConfigured();

        var banks = await provider.ListBanksAsync(country ?? "CZ", ct);
        return Results.Ok(banks.Select(b => new BankAspspDto { Name = b.Name, Country = b.Country }).ToList());
    }

    private static async Task<IResult> ListConnections(IAppDbContext db, ICurrentFamily family, CancellationToken ct)
    {
        var familyId = await family.RequireAsync(ct);
        if (!await family.CanAsync(Permission.ViewFinances, ct))
            return Forbidden();

        var connections = await db.BankConnections
            .Include(c => c.BankAccount)
            .Where(c => c.FamilyId == familyId)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync(ct);

        return Results.Ok(connections.Select(c => new BankConnectionDto
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
        }).ToList());
    }

    private static async Task<IResult> StartConnection(
        StartBankConnectionRequest request, IBankDataProvider provider, ICurrentFamily family,
        ICurrentUser user, IMessageBus bus, CancellationToken ct)
    {
        var familyId = await family.RequireAsync(ct);
        if (!await family.CanAsync(Permission.ImportStatements, ct))
            return Forbidden();
        if (!provider.IsConfigured)
            return NotConfigured();

        var result = await bus.InvokeAsync<StartBankConnectionResultDto>(new StartBankConnectionCommand
        {
            FamilyId = familyId,
            CreatedByUserId = user.UserId ?? Guid.Empty,
            BankAccountId = request.BankAccountId,
            AspspName = request.AspspName,
            Country = request.Country.ToUpperInvariant(),
        }, ct);

        return Results.Ok(result);
    }

    private static async Task<IResult> CompleteConnection(
        CompleteBankConnectionRequest request, ICurrentFamily family, IMessageBus bus, CancellationToken ct)
    {
        var familyId = await family.RequireAsync(ct);
        if (!await family.CanAsync(Permission.ImportStatements, ct))
            return Forbidden();

        var result = await bus.InvokeAsync<BankConnectionDto>(new CompleteBankConnectionCommand
        {
            FamilyId = familyId,
            Code = request.Code,
            State = request.State,
        }, ct);

        return Results.Ok(result);
    }

    private static async Task<IResult> SyncConnection(
        Guid id, ICurrentFamily family, ICurrentUser user, IAppDbContext db, IMessageBus bus, CancellationToken ct)
    {
        var familyId = await family.RequireAsync(ct);
        if (!await family.CanAsync(Permission.ImportStatements, ct))
            return Forbidden();
        if (!await db.BankConnections.AnyAsync(c => c.Id == id && c.FamilyId == familyId, ct))
            return Results.NotFound();

        var result = await bus.InvokeAsync<ImportResultDto>(new SyncBankAccountCommand
        {
            BankConnectionId = id,
            TriggeredByUserId = user.UserId,
        }, ct);

        return Results.Ok(result);
    }

    private static async Task<IResult> Disconnect(
        Guid id, IAppDbContext db, ICurrentFamily family, IAuditLog audit, CancellationToken ct)
    {
        var familyId = await family.RequireAsync(ct);
        if (!await family.CanAsync(Permission.ImportStatements, ct))
            return Forbidden();

        var connection = await db.BankConnections.FirstOrDefaultAsync(c => c.Id == id && c.FamilyId == familyId, ct);
        if (connection is null)
            return Results.NotFound();

        connection.DeletedAt = DateTimeOffset.UtcNow;
        connection.State = null;
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync("bank.disconnect", "BankConnection", connection.Id.ToString(),
            connection.AspspName, familyId, $"Odpojena banka {connection.AspspName}", ct);
        return Results.NoContent();
    }

    private static IResult NotConfigured() =>
        Results.Problem(detail: "Připojení k bance (Enable Banking) není na serveru nakonfigurováno.",
            statusCode: StatusCodes.Status503ServiceUnavailable);
}
