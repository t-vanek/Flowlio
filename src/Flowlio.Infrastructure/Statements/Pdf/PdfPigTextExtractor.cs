using UglyToad.PdfPig;

namespace Flowlio.Infrastructure.Statements.Pdf;

/// <summary>Extracts a positioned <see cref="PdfTextPage"/> model from a PDF stream.</summary>
internal interface IPdfTextExtractor
{
    IReadOnlyList<PdfTextPage> Extract(Stream content);
}

/// <summary>
/// PdfPig-backed extractor — the only piece of the coordinate-based PDF path that touches PdfPig. Words are
/// clustered into visual rows by their baseline (Y) within a small tolerance, rows ordered top-to-bottom and
/// words left-to-right, so downstream parsing can reconstruct columns from horizontal positions.
/// </summary>
internal sealed class PdfPigTextExtractor : IPdfTextExtractor
{
    /// <summary>Two words belong to the same visual row when their baselines differ by at most this (pt).</summary>
    private const double RowTolerance = 3.0;

    public IReadOnlyList<PdfTextPage> Extract(Stream content)
    {
        using var ms = new MemoryStream();
        content.CopyTo(ms);
        ms.Position = 0;

        using var document = PdfDocument.Open(ms);
        var pages = new List<PdfTextPage>();

        foreach (var page in document.GetPages())
        {
            var rows = new List<(double Y, List<PdfWord> Words)>();
            foreach (var word in page.GetWords())
            {
                if (string.IsNullOrWhiteSpace(word.Text))
                    continue;

                var y = word.BoundingBox.Bottom;
                var row = rows.FirstOrDefault(r => Math.Abs(r.Y - y) <= RowTolerance);
                if (row.Words is null)
                {
                    row = (y, new List<PdfWord>());
                    rows.Add(row);
                }
                row.Words.Add(new PdfWord(word.Text, word.BoundingBox.Left, word.BoundingBox.Right, y));
            }

            var orderedRows = rows
                .OrderByDescending(r => r.Y)
                .Select(r => new PdfTextRow(r.Y, r.Words.OrderBy(w => w.Left).ToList()))
                .ToList();

            pages.Add(new PdfTextPage(orderedRows));
        }

        return pages;
    }
}
