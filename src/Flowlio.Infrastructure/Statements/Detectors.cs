using Flowlio.Domain;

namespace Flowlio.Infrastructure.Statements;

/// <summary>Guesses the bank from an extracted statement's headers, so the user need not pick it manually.</summary>
internal interface IBankDetector
{
    BankProfile? Detect(RawStatement raw);
}

/// <summary>Guesses the file format from the content (magic bytes) and extension.</summary>
internal interface IFormatDetector
{
    ImportFormat Detect(byte[] content, string fileName, ImportFormat fallback);
}

internal sealed class BankDetector(BankProfileRegistry registry) : IBankDetector
{
    public BankProfile? Detect(RawStatement raw)
    {
        var headers = raw.Headers.Select(StatementText.Normalize).ToHashSet();

        BankProfile? best = null;
        var bestScore = 0;
        foreach (var profile in registry.All)
        {
            var required = profile.Fingerprint.RequiredHeaders;
            if (required.Length == 0)
                continue; // no fingerprint (universal) — never auto-selected

            var score = required.Count(h => headers.Contains(StatementText.Normalize(h)));
            if (score == required.Length && score > bestScore)
            {
                best = profile;
                bestScore = score;
            }
        }
        return best;
    }
}

internal sealed class FormatDetector : IFormatDetector
{
    public ImportFormat Detect(byte[] content, string fileName, ImportFormat fallback)
    {
        // CSV/XLSX are deprecated (hidden from the UI) but still detected so existing uploads keep working.
#pragma warning disable CS0618 // Type or member is obsolete
        // XLSX is a ZIP container ("PK").
        if (content.Length >= 2 && content[0] == 0x50 && content[1] == 0x4B)
            return ImportFormat.Xlsx;

        // "%PDF"
        if (content.Length >= 4 && content[0] == 0x25 && content[1] == 0x50 && content[2] == 0x44 && content[3] == 0x46)
            return ImportFormat.Pdf;

        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".csv" or ".txt" => ImportFormat.Csv,
            ".xlsx" or ".xls" => ImportFormat.Xlsx,
            ".pdf" => ImportFormat.Pdf,
            _ => fallback,
        };
#pragma warning restore CS0618
    }
}
