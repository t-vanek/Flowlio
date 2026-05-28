namespace Flowlio.Infrastructure.Statements.Pdf;

/// <summary>
/// A positioned view of a PDF: pages of visual rows, each a left-to-right sequence of words with their
/// horizontal extent. This is the seam between extraction (which touches PdfPig) and parsing (pure logic),
/// so the table parser can be unit-tested with hand-built fixtures and no real PDF.
/// </summary>
internal sealed record PdfWord(string Text, double Left, double Right, double Bottom);

internal sealed record PdfTextRow(double Y, IReadOnlyList<PdfWord> Words);

internal sealed record PdfTextPage(IReadOnlyList<PdfTextRow> Rows);
