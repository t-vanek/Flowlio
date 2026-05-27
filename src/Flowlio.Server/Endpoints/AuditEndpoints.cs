using Flowlio.Application.Abstractions;
using Flowlio.Domain;
using Flowlio.Shared;
using Microsoft.EntityFrameworkCore;
using static Flowlio.Server.Auth.MemberAuthorization;

namespace Flowlio.Server.Endpoints;

/// <summary>Read-only access to the audit log, gated by <see cref="SystemPermission.ViewAuditLog"/>.</summary>
public static class AuditEndpoints
{
    public static void MapAuditEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGroup("/api/admin")
           .RequireAuthorization(Auth.AdminRoles.AdminPolicy)
           .MapGet("/audit", GetAudit);
    }

    private static async Task<IResult> GetAudit(
        IAppDbContext db, ICurrentSystemAccess sys, CancellationToken ct,
        string? action = null, string? search = null, int page = 1, int pageSize = 50)
    {
        if (!await sys.CanAsync(SystemPermission.ViewAuditLog, ct))
            return Forbidden();

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query = db.AuditEntries.AsQueryable();
        if (!string.IsNullOrWhiteSpace(action))
            query = query.Where(a => a.Action == action);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(a =>
                (a.ActorName != null && EF.Functions.ILike(a.ActorName, $"%{term}%")) ||
                (a.TargetName != null && EF.Functions.ILike(a.TargetName, $"%{term}%")) ||
                (a.Details != null && EF.Functions.ILike(a.Details, $"%{term}%")));
        }

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(a => a.OccurredAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new AuditEntryDto
            {
                Id = a.Id,
                OccurredAt = a.OccurredAt,
                ActorName = a.ActorName,
                Action = a.Action,
                TargetType = a.TargetType,
                TargetName = a.TargetName,
                Details = a.Details,
            })
            .ToListAsync(ct);

        return Results.Ok(new AuditPageDto { Items = items, TotalCount = total, Page = page, PageSize = pageSize });
    }
}
