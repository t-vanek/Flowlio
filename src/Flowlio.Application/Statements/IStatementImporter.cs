using Flowlio.Domain;

namespace Flowlio.Application.Statements;

/// <summary>
/// Turns an uploaded statement file into a canonical <see cref="ParsedStatement"/>. Implementations own
/// the full pipeline: detect the format, extract raw rows, pick the bank profile (honouring the caller's
/// hint or auto-detecting), and map rows to the canonical shape. The rest of the app depends only on this.
/// </summary>
public interface IStatementImporter
{
    /// <param name="bankHint">
    /// The bank the user picked for the account. <see cref="BankProvider.Other"/> means "unknown — auto-detect".
    /// </param>
    /// <param name="format">The format the user selected; treated as a hint and refined from the file content.</param>
    ParsedStatement Parse(Stream content, string fileName, BankProvider bankHint, ImportFormat format);
}
