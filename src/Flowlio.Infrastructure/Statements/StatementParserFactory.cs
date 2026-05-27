using Flowlio.Application.Statements;
using Flowlio.Domain;

namespace Flowlio.Infrastructure.Statements;

/// <summary>Resolves a parser from the requested bank and file format.</summary>
public sealed class StatementParserFactory : IStatementParserFactory
{
    public IStatementParser Resolve(BankProvider bank, ImportFormat format) => format switch
    {
        ImportFormat.Csv => new CsvStatementParser(BankCsvProfiles.For(bank)),
        ImportFormat.Pdf => new PdfStatementParser(),
        ImportFormat.PdfOcr => throw new NotSupportedException(
            "OCR import requires the Tesseract native library, which is not yet configured in this environment."),
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported statement format."),
    };
}
