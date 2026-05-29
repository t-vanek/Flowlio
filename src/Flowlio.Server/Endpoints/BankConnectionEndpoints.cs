using System.Security.Cryptography;
using Flowlio.Application.Abstractions;
using Flowlio.Application.Banking;
using Flowlio.Domain;
using Flowlio.Shared;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Wolverine;
using static Flowlio.Server.Auth.MemberAuthorization;

namespace Flowlio.Server.Endpoints;

/// <summary>
/// Open Banking (PSD2) connections via Enable Banking, with per-user "bring your own" credentials: store your
/// Enable Banking application, list available banks, start a consent, sync transactions on demand and
/// disconnect. The consent is finalized by the bank's redirect to the public <c>/bank-connections/callback</c>.
/// </summary>
public static class BankConnectionEndpoints
{
    public static void MapBankConnectionEndpoints(this IEndpointRouteBuilder api)
    {
        api.MapGet("/bank-connections/credentials", GetCredentialStatus);
        api.MapPut("/bank-connections/credentials", SaveCredentials);
        api.MapDelete("/bank-connections/credentials", DeleteCredentials);

        api.MapGet("/bank-connections/banks", ListBanks);
        api.MapGet("/bank-connections", ListConnections);
        api.MapPost("/bank-connections", StartConnection);
        api.MapPost("/bank-connections/{id:guid}/sync", SyncConnection);
        api.MapDelete("/bank-connections/{id:guid}", Disconnect);
    }

    /// <summary>The public redirect target the bank sends the user back to after SCA. Anonymous: it is
    /// correlated to a pending connection purely by the unguessable state token, then redirects to the SPA.</summary>
    public static void MapBankConnectionCallback(this IEndpointRouteBuilder app) =>
        app.MapGet("/bank-connections/callback", Callback).AllowAnonymous();

    // ---- Credentials --------------------------------------------------------

    private static async Task<IResult> GetCredentialStatus(
        ICurrentUser user, IAppDbContext db, ICurrentFamily family,
        IOptions<Flowlio.Infrastructure.Banking.EnableBankingOptions> options, CancellationToken ct)
    {
        if (!await family.CanAsync(Permission.ImportStatements, ct))
            return Forbidden();

        var userId = user.UserId ?? Guid.Empty;
        var credential = await db.EnableBankingCredentials.FirstOrDefaultAsync(c => c.UserId == userId, ct);
        return Results.Ok(new EnableBankingCredentialStatusDto
        {
            Configured = credential is not null,
            ApplicationId = credential?.ApplicationId,
            CallbackUrl = options.Value.RedirectUrl,
        });
    }

    private static async Task<IResult> SaveCredentials(
        SaveEnableBankingCredentialRequest request, ICurrentUser user, ICurrentFamily family,
        IAppDbContext db, ISecretProtector protector, IAuditLog audit, CancellationToken ct)
    {
        var familyId = await family.RequireAsync(ct);
        if (!await family.CanAsync(Permission.ImportStatements, ct))
            return Forbidden();

        // Validate the PEM up front so a bad paste fails clearly instead of at the first bank call.
        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(request.PrivateKeyPem);
        }
        catch (Exception ex) when (ex is ArgumentException or CryptographicException)
        {
            return Results.BadRequest("Neplatný privátní klíč – očekává se obsah PEM souboru z Enable Banking.");
        }

        var userId = user.UserId ?? Guid.Empty;
        var encrypted = protector.Protect(request.PrivateKeyPem);

        var credential = await db.EnableBankingCredentials.FirstOrDefaultAsync(c => c.UserId == userId, ct);
        if (credential is null)
        {
            db.EnableBankingCredentials.Add(new EnableBankingCredential
            {
                UserId = userId,
                FamilyId = familyId,
                ApplicationId = request.ApplicationId.Trim(),
                PrivateKeyEncrypted = encrypted,
            });
        }
        else
        {
            credential.ApplicationId = request.ApplicationId.Trim();
            credential.PrivateKeyEncrypted = encrypted;
            credential.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(ct);
        await audit.RecordAsync("bank.credentials.save", "EnableBankingCredential", userId.ToString(),
            null, familyId, "Uloženy přístupy k Enable Banking", ct);
        return Results.NoContent();
    }

    private static async Task<IResult> DeleteCredentials(
        ICurrentUser user, ICurrentFamily family, IAppDbContext db, IAuditLog audit, CancellationToken ct)
    {
        var familyId = await family.RequireAsync(ct);
        if (!await family.CanAsync(Permission.ImportStatements, ct))
            return Forbidden();

        var userId = user.UserId ?? Guid.Empty;
        var credential = await db.EnableBankingCredentials.FirstOrDefaultAsync(c => c.UserId == userId, ct);
        if (credential is null)
            return Results.NotFound();

        db.EnableBankingCredentials.Remove(credential);
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync("bank.credentials.delete", "EnableBankingCredential", userId.ToString(),
            null, familyId, "Smazány přístupy k Enable Banking", ct);
        return Results.NoContent();
    }

    // ---- Banks & connections ------------------------------------------------

    private static async Task<IResult> ListBanks(
        [FromQuery] string? country, IBankDataProvider provider, IBankCredentialProvider credentials,
        ICurrentUser user, ICurrentFamily family, CancellationToken ct)
    {
        if (!await family.CanAsync(Permission.ImportStatements, ct))
            return Forbidden();

        var creds = await credentials.GetAsync(user.UserId ?? Guid.Empty, ct);
        if (creds is null)
            return NotConfigured();

        var banks = await provider.ListBanksAsync(creds, country ?? "CZ", ct);
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
        StartBankConnectionRequest request, IBankCredentialProvider credentials, ICurrentFamily family,
        ICurrentUser user, IMessageBus bus, CancellationToken ct)
    {
        var familyId = await family.RequireAsync(ct);
        if (!await family.CanAsync(Permission.ImportStatements, ct))
            return Forbidden();

        var userId = user.UserId ?? Guid.Empty;
        if (!await credentials.HasAsync(userId, ct))
            return NotConfigured();

        var result = await bus.InvokeAsync<StartBankConnectionResultDto>(new StartBankConnectionCommand
        {
            FamilyId = familyId,
            CreatedByUserId = userId,
            BankAccountId = request.BankAccountId,
            AspspName = request.AspspName,
            Country = request.Country.ToUpperInvariant(),
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

    private static async Task<IResult> Callback(
        [FromQuery] string? code, [FromQuery] string? state, IMessageBus bus, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state))
            return Results.Redirect("/bank-connect?bank=error");

        try
        {
            await bus.InvokeAsync(new CompleteBankConnectionCommand { Code = code, State = state }, ct);
            return Results.Redirect("/bank-connect?bank=connected");
        }
        catch
        {
            return Results.Redirect("/bank-connect?bank=error");
        }
    }

    private static IResult NotConfigured() =>
        Results.Problem(detail: "Nemáte uložené přístupy k Enable Banking. Nejprve je vyplňte.",
            statusCode: StatusCodes.Status400BadRequest);
}
