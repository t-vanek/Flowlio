using Flowlio.Domain;

namespace Flowlio.Application.Statements;

/// <summary>Parses a bank statement file into transactions. One implementation per bank/format.</summary>
public interface IStatementParser
{
    BankProvider Bank { get; }
    ImportFormat Format { get; }

    ParsedStatement Parse(Stream content, string fileName);
}

/// <summary>Resolves the right <see cref="IStatementParser"/> for a given bank and file format.</summary>
public interface IStatementParserFactory
{
    IStatementParser Resolve(BankProvider bank, ImportFormat format);
}
