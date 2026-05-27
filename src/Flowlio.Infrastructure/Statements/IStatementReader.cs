using System.Text;
using Flowlio.Domain;

namespace Flowlio.Infrastructure.Statements;

/// <summary>Extracts a <see cref="RawStatement"/> from a file of one specific format. Knows nothing about banks.</summary>
internal interface IStatementReader
{
    ImportFormat Format { get; }

    RawStatement Read(Stream content, string fileName, ReaderOptions options);
}

/// <summary>Hints a reader can use: encoding/delimiter when the bank is already known, plus the union of
/// every profile's header tokens so the reader can locate the header row regardless of which bank it is.</summary>
internal sealed record ReaderOptions
{
    public Encoding? Encoding { get; init; }
    public char? Delimiter { get; init; }
    public required IReadOnlySet<string> KnownHeaderTokens { get; init; }
}
