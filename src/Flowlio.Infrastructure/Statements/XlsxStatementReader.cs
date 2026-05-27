using System.Globalization;
using ClosedXML.Excel;
using Flowlio.Application.Statements;
using Flowlio.Domain;

namespace Flowlio.Infrastructure.Statements;

/// <summary>
/// Reads the first worksheet of an XLSX statement into a <see cref="RawStatement"/>. Locates the header
/// row the same way the CSV reader does (first row with at least two recognised header tokens) so banks
/// that prepend metadata rows are handled. Date and numeric cells are emitted in an invariant, unambiguous
/// form so the shared mapper parses them correctly regardless of the sheet's display formatting.
/// </summary>
internal sealed class XlsxStatementReader : IStatementReader
{
    public ImportFormat Format => ImportFormat.Xlsx;

    public RawStatement Read(Stream content, string fileName, ReaderOptions options)
    {
        using var workbook = new XLWorkbook(content);
        var sheet = workbook.Worksheets.FirstOrDefault();
        var used = sheet?.RangeUsed();
        if (sheet is null || used is null)
        {
            return new RawStatement
            {
                Diagnostics = [new ParseDiagnostic
                {
                    Severity = ParseSeverity.Error,
                    Message = "Sešit neobsahuje žádná data.",
                }],
            };
        }

        var firstRow = used.RangeAddress.FirstAddress.RowNumber;
        var lastRow = used.RangeAddress.LastAddress.RowNumber;
        var firstCol = used.RangeAddress.FirstAddress.ColumnNumber;
        var lastCol = used.RangeAddress.LastAddress.ColumnNumber;

        var headerRowNum = LocateHeaderRow(sheet, firstRow, lastRow, firstCol, lastCol, options);
        if (headerRowNum < 0)
        {
            return new RawStatement
            {
                Diagnostics = [new ParseDiagnostic
                {
                    Severity = ParseSeverity.Error,
                    Message = "Nepodařilo se najít řádek se záhlavím sloupců.",
                }],
            };
        }

        var headers = new List<string>();
        for (var c = firstCol; c <= lastCol; c++)
            headers.Add(sheet.Cell(headerRowNum, c).GetString().Trim());

        var rows = new List<RawRow>();
        for (var r = headerRowNum + 1; r <= lastRow; r++)
        {
            var cells = new string[headers.Count];
            for (var c = firstCol; c <= lastCol; c++)
                cells[c - firstCol] = CellText(sheet.Cell(r, c));
            rows.Add(new RawRow { Cells = cells, SourceLine = r });
        }

        return new RawStatement { Headers = headers, Rows = rows };
    }

    private static int LocateHeaderRow(IXLWorksheet sheet, int firstRow, int lastRow, int firstCol, int lastCol, ReaderOptions options)
    {
        for (var r = firstRow; r <= lastRow; r++)
        {
            var matches = 0;
            for (var c = firstCol; c <= lastCol; c++)
            {
                if (options.KnownHeaderTokens.Contains(StatementText.Normalize(sheet.Cell(r, c).GetString())))
                    matches++;
            }
            if (matches >= 2)
                return r;
        }

        // Fallback: first row with at least two non-empty text cells.
        for (var r = firstRow; r <= lastRow; r++)
        {
            var nonEmpty = 0;
            for (var c = firstCol; c <= lastCol; c++)
            {
                if (!string.IsNullOrWhiteSpace(sheet.Cell(r, c).GetString()))
                    nonEmpty++;
            }
            if (nonEmpty >= 2)
                return r;
        }

        return -1;
    }

    /// <summary>Renders a cell as text the mapper can parse: dates as ISO, numbers as invariant decimals,
    /// everything else as its displayed string.</summary>
    private static string CellText(IXLCell cell)
    {
        if (cell.DataType == XLDataType.DateTime && cell.TryGetValue<DateTime>(out var dt))
            return dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        if (cell.DataType == XLDataType.Number && cell.TryGetValue<double>(out var num))
            return num.ToString("0.############", CultureInfo.InvariantCulture);

        return cell.GetString().Trim();
    }
}
