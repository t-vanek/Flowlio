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
        foreach (var rule in rules)
        {
            var haystack = rule.Field switch
            {
                RuleMatchField.CounterpartyName => counterpartyName,
                RuleMatchField.Description => description,
                RuleMatchField.VariableSymbol => variableSymbol,
                RuleMatchField.CounterpartyAccount => counterpartyAccount,
                RuleMatchField.Any => string.Join(
                    '\n',
                    new[] { counterpartyName, description, variableSymbol, counterpartyAccount }
                        .Where(s => !string.IsNullOrWhiteSpace(s))),
                _ => null,
            };

            if (!string.IsNullOrEmpty(haystack) &&
                haystack.Contains(rule.Pattern, StringComparison.OrdinalIgnoreCase))
            {
                return rule.CategoryId;
            }
        }

        return null;
    }
}
