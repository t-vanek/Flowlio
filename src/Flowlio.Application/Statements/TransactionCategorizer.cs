using System.Globalization;
using System.Text;
using Flowlio.Domain;

namespace Flowlio.Application.Statements;

/// <summary>
/// Assigns a category to a transaction by matching it against the family's <see cref="CategorizationRule"/>s.
/// Shared by statement import and the retroactive recategorization endpoint so both behave identically.
/// </summary>
public static class TransactionCategorizer
{
    /// <summary>Returns the category of the first matching rule, or null when none match.
    /// <paramref name="rules"/> must already be ordered by descending priority (the caller's responsibility).</summary>
    public static Guid? Match(
        string? counterpartyName,
        string? description,
        string? variableSymbol,
        string? counterpartyAccount,
        IReadOnlyList<CategorizationRule> rules)
    {
        // Czech statements arrive with or without diacritics and in mixed case ("LEKARNA" vs "Lékárna"),
        // so match on a diacritics-folded form. Computed once per field, reused across every rule.
        var name = Fold(counterpartyName);
        var desc = Fold(description);
        var vs = Fold(variableSymbol);
        var account = Fold(counterpartyAccount);
        var any = string.Join(
            '\n',
            new[] { name, desc, vs, account }.Where(s => !string.IsNullOrEmpty(s)));

        foreach (var rule in rules)
        {
            var haystack = rule.Field switch
            {
                RuleMatchField.CounterpartyName => name,
                RuleMatchField.Description => desc,
                RuleMatchField.VariableSymbol => vs,
                RuleMatchField.CounterpartyAccount => account,
                RuleMatchField.Any => any,
                _ => null,
            };

            if (!string.IsNullOrEmpty(haystack) &&
                haystack.Contains(Fold(rule.Pattern), StringComparison.OrdinalIgnoreCase))
            {
                return rule.CategoryId;
            }
        }

        return null;
    }

    /// <summary>Strips diacritics so "Lékárna" matches "LEKARNA"; case is still handled by the caller's
    /// <see cref="StringComparison.OrdinalIgnoreCase"/> compare. Returns "" for null/blank input.</summary>
    internal static string Fold(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var decomposed = value.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                sb.Append(ch);
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}
