using System.Globalization;
using System.Text;
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
        var group = app.MapGroup("/api/admin").RequireAuthorization(Auth.AdminRoles.AdminPolicy);
        group.MapGet("/audit", GetAudit);
        group.MapGet("/audit/actions", GetActions);
        group.MapGet("/audit/export", ExportCsv);
    }

    private static async Task<IResult> GetAudit(
        IAppDbContext db, ICurrentSystemAccess sys, CancellationToken ct,
        string? action = null, string? search = null,
        DateTimeOffset? from = null, DateTimeOffset? to = null,
        int page = 1, int pageSize = 50)
    {
        if (!await sys.CanAsync(SystemPermission.ViewAuditLog, ct))
            return Forbidden();

        (page, pageSize) = Paging.Normalize(page, pageSize);

        var query = ApplyFilters(db.AuditEntries.AsQueryable(), action, search, from, to);

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

    /// <summary>Distinct action names recorded so far — used to populate the filter dropdown in the UI.</summary>
    private static async Task<IResult> GetActions(
        IAppDbContext db, ICurrentSystemAccess sys, CancellationToken ct)
    {
        if (!await sys.CanAsync(SystemPermission.ViewAuditLog, ct))
            return Forbidden();

        var actions = await db.AuditEntries
            .Select(a => a.Action)
            .Distinct()
            .OrderBy(a => a)
            .ToListAsync(ct);
        return Results.Ok(actions);
    }

    /// <summary>
    /// Streams the filtered audit log as UTF-8 CSV (with BOM so Excel opens it as Unicode).
    /// Same filters as <see cref="GetAudit"/>, but no pagination — capped at 10 000 rows
    /// to keep the file size sane.
    /// </summary>
    private static async Task<IResult> ExportCsv(
        IAppDbContext db, ICurrentSystemAccess sys, CancellationToken ct,
        string? action = null, string? search = null,
        DateTimeOffset? from = null, DateTimeOffset? to = null)
    {
        if (!await sys.CanAsync(SystemPermission.ViewAuditLog, ct))
            return Forbidden();

        var items = await ApplyFilters(db.AuditEntries.AsQueryable(), action, search, from, to)
            .OrderByDescending(a => a.OccurredAt)
            .Take(10_000)
            .Select(a => new
            {
                a.OccurredAt,
                a.ActorName,
                a.Action,
                a.TargetType,
                a.TargetName,
                a.Details,
            })
            .ToListAsync(ct);

        var csv = new StringBuilder();
        csv.Append('﻿'); // UTF-8 BOM for Excel
        csv.AppendLine("OccurredAtUtc,Actor,Action,TargetType,TargetName,Details");
        foreach (var e in items)
        {
            csv.Append(e.OccurredAt.UtcDateTime.ToString("O", CultureInfo.InvariantCulture)).Append(',');
            csv.Append(Csv(e.ActorName)).Append(',');
            csv.Append(Csv(e.Action)).Append(',');
            csv.Append(Csv(e.TargetType)).Append(',');
            csv.Append(Csv(e.TargetName)).Append(',');
            csv.Append(Csv(e.Details)).AppendLine();
        }

        var fileName = $"flowlio-audit-{DateTime.UtcNow:yyyyMMdd-HHmmss}.csv";
        return Results.File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv; charset=utf-8", fileName);
    }

    private static IQueryable<AuditEntry> ApplyFilters(
        IQueryable<AuditEntry> query,
        string? action, string? search, DateTimeOffset? from, DateTimeOffset? to)
    {
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

        if (from is { } fromValue)
            query = query.Where(a => a.OccurredAt >= fromValue);
        if (to is { } toValue)
            query = query.Where(a => a.OccurredAt < toValue);

        return query;
    }

    /// <summary>Escapes a string for CSV: wrap in double quotes if it contains a comma, quote or newline.</summary>
    private static string Csv(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var needs = value.IndexOfAny([',', '"', '\n', '\r']) >= 0;
        var escaped = value.Replace("\"", "\"\"");
        return needs ? $"\"{escaped}\"" : escaped;
    }
}
