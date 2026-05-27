using Flowlio.Application.Statements;

namespace Flowlio.Infrastructure.Statements;

/// <summary>
/// A bank- and format-neutral tabular view of a statement: header names plus data rows, before any
/// per-bank field mapping. This is the seam between extraction (which knows the file format) and
/// mapping (which knows the bank). Produced by an <see cref="IStatementReader"/>.
/// </summary>
internal sealed record RawStatement
{
    public string? AccountNumber { get; init; }
    public IReadOnlyList<string> Headers { get; init; } = [];
    public IReadOnlyList<RawRow> Rows { get; init; } = [];

    /// <summary>Problems found during extraction (e.g. no header row, unreadable workbook).</summary>
    public IReadOnlyList<ParseDiagnostic> Diagnostics { get; init; } = [];
}

internal sealed record RawRow
{
    public required IReadOnlyList<string> Cells { get; init; }

    /// <summary>1-based source line (CSV) or row number (XLSX), for diagnostics.</summary>
    public required int SourceLine { get; init; }

    public string? Cell(int index) =>
        index >= 0 && index < Cells.Count ? Cells[index] : null;
}
