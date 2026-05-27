using Flowlio.Application.Statements;
using Flowlio.Domain;

namespace Flowlio.Infrastructure.Statements;

/// <summary>
/// The statement import pipeline: detect the format, extract a <see cref="RawStatement"/> with the matching
/// reader, resolve the bank profile (the caller's hint wins; otherwise auto-detect, else fall back to the
/// universal profile), and map to the canonical shape. PDF is handled by a separate heuristic parser and
/// flagged as experimental.
/// </summary>
internal sealed class StatementImporter : IStatementImporter
{
    private readonly BankProfileRegistry _registry;
    private readonly IReadOnlyDictionary<ImportFormat, IStatementReader> _readers;
    private readonly StatementMapper _mapper;
    private readonly IBankDetector _bankDetector;
    private readonly IFormatDetector _formatDetector;
    private readonly PdfStatementParser _pdf;

    public StatementImporter(
        BankProfileRegistry registry,
        IEnumerable<IStatementReader> readers,
        StatementMapper mapper,
        IBankDetector bankDetector,
        IFormatDetector formatDetector,
        PdfStatementParser pdf)
    {
        _registry = registry;
        _readers = readers.ToDictionary(r => r.Format);
        _mapper = mapper;
        _bankDetector = bankDetector;
        _formatDetector = formatDetector;
        _pdf = pdf;
    }

    public ParsedStatement Parse(Stream content, string fileName, BankProvider bankHint, ImportFormat format)
    {
        using var ms = new MemoryStream();
        content.CopyTo(ms);
        var bytes = ms.ToArray();

        var resolvedFormat = _formatDetector.Detect(bytes, fileName, format);

        if (resolvedFormat is ImportFormat.Pdf)
        {
            var pdf = _pdf.Parse(new MemoryStream(bytes), fileName);
            return pdf with
            {
                Diagnostics =
                [
                    new ParseDiagnostic
                    {
                        Severity = ParseSeverity.Info,
                        Message = "PDF import je experimentální; doporučujeme výsledek zkontrolovat.",
                    },
                    .. pdf.Diagnostics,
                ],
            };
        }

        if (resolvedFormat is ImportFormat.PdfOcr)
            throw new NotSupportedException(
                "OCR import vyžaduje nativní knihovnu Tesseract, která zatím není v tomto prostředí nakonfigurována.");

        if (!_readers.TryGetValue(resolvedFormat, out var reader))
            throw new NotSupportedException($"Formát {resolvedFormat} není podporován.");

        // A concrete bank choice pre-selects encoding/delimiter; "Other" means auto-detect after extraction.
        var hinted = bankHint != BankProvider.Other ? _registry.ForEnum(bankHint) : null;
        var options = new ReaderOptions
        {
            Encoding = hinted?.Encoding,
            Delimiter = hinted?.CsvDelimiter,
            KnownHeaderTokens = _registry.KnownHeaderTokens,
        };

        var raw = reader.Read(new MemoryStream(bytes), fileName, options);
        var profile = hinted ?? _bankDetector.Detect(raw) ?? _registry.Universal;
        return _mapper.Map(raw, profile);
    }
}
