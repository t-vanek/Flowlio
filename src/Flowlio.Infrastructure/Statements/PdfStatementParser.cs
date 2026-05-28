using Flowlio.Application.Statements;
using Flowlio.Domain;
using Flowlio.Infrastructure.Statements.Pdf;

namespace Flowlio.Infrastructure.Statements;

/// <summary>
/// Orchestrates PDF statement parsing: extracts a positioned text model, resolves a bank layout (from the
/// caller's hint or by detection), and runs the coordinate-based <see cref="PdfTableParser"/>. When no layout
/// matches it falls back to the <see cref="PdfHeuristicParser"/> and flags the result as experimental.
/// </summary>
internal sealed class PdfStatementParser
{
    private readonly IPdfTextExtractor _extractor;
    private readonly PdfLayoutRegistry _layouts;
    private readonly PdfTableParser _tableParser;
    private readonly PdfHeuristicParser _heuristic;

    public PdfStatementParser(
        IPdfTextExtractor extractor,
        PdfLayoutRegistry layouts,
        PdfTableParser tableParser,
        PdfHeuristicParser heuristic)
    {
        _extractor = extractor;
        _layouts = layouts;
        _tableParser = tableParser;
        _heuristic = heuristic;
    }

    public ParsedStatement Parse(Stream content, string fileName, BankProvider bankHint)
    {
        var pages = _extractor.Extract(content);

        if (pages.Count == 0 || pages.All(p => p.Rows.Count == 0))
            throw new NotSupportedException(
                "PDF neobsahuje čitelný text (patrně sken). Pro skenované výpisy je potřeba OCR.");

        var layout = _layouts.Resolve(bankHint, pages);
        if (layout is not null)
            return _tableParser.Parse(pages, layout);

        // Unknown layout: best-effort heuristic, clearly flagged.
        var transactions = _heuristic.Parse(pages);
        return new ParsedStatement
        {
            Transactions = transactions,
            Diagnostics =
            [
                new ParseDiagnostic
                {
                    Severity = ParseSeverity.Info,
                    Message = "PDF této banky není přesně podporováno; výsledek je experimentální, zkontrolujte jej.",
                },
            ],
        };
    }
}
