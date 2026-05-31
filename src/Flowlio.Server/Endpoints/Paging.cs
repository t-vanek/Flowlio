namespace Flowlio.Server.Endpoints;

/// <summary>Bounds for caller-supplied paging, applied consistently across the paged list endpoints.</summary>
internal static class Paging
{
    public const int MaxPageSize = 200;

    /// <summary>Clamps paging into safe bounds: page ≥ 1 and pageSize in [1, <see cref="MaxPageSize"/>].</summary>
    public static (int Page, int PageSize) Normalize(int page, int pageSize) =>
        (Math.Max(1, page), Math.Clamp(pageSize, 1, MaxPageSize));
}
