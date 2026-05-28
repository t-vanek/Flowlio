namespace Flowlio.Infrastructure.Persistence;

/// <summary>
/// Maps to the <c>flowlio_immutable_unaccent</c> PostgreSQL function (an IMMUTABLE wrapper over the
/// <c>unaccent</c> extension) so search terms can be diacritics-normalised the same way the stored
/// <c>SearchVector</c> column is. Registered via <c>HasDbFunction</c>; never executed in-process.
/// </summary>
public static class FtsFunctions
{
    public static string Unaccent(string input) =>
        throw new NotSupportedException("Only callable inside an EF Core query (maps to flowlio_immutable_unaccent).");
}
