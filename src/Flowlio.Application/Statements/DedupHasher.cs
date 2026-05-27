using System.Security.Cryptography;
using System.Text;

namespace Flowlio.Application.Statements;

/// <summary>Computes the stable fingerprint used to detect already-imported transactions.</summary>
public static class DedupHasher
{
    public static string Compute(Guid bankAccountId, ParsedTransaction tx)
    {
        var raw = string.Join('|',
            bankAccountId,
            tx.BookingDate.ToString("yyyy-MM-dd"),
            tx.Amount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            tx.Currency,
            tx.VariableSymbol ?? string.Empty,
            tx.SpecificSymbol ?? string.Empty,
            tx.CounterpartyAccount ?? string.Empty,
            tx.Description ?? string.Empty);

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes);
    }
}
