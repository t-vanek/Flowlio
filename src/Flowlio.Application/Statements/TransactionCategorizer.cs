using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Flowlio.Domain;

namespace Flowlio.Application.Statements;

/// <summary>
/// Assigns a category to a transaction by matching it against the family's <see cref="CategorizationRule"/>s.
/// Shared by statement import and the retroactive recategorization endpoint so both behave identically.
/// </summary>
public static class TransactionCategorizer
{
    // Compiled WholeWord/Regex patterns are cached so a full recategorize (rules × transactions) doesn't
    // rebuild the same Regex on every row. Keyed by mode + folded pattern; null means an invalid user regex.
    private static readonly ConcurrentDictionary<string, Regex?> RegexCache = new();
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);

    /// <summary>Returns the category of the first matching rule, or null when none match.
    /// <paramref name="rules"/> must already be ordered by descending priority (the caller's responsibility),
    /// and should have their <see cref="CategorizationRule.Category"/> loaded so the income/expense direction
    /// filter can apply.</summary>
    public static Guid? Match(
        string? counterpartyName,
        string? description,
        string? variableSymbol,
        string? counterpartyAccount,
        TransactionDirection direction,
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
            // An income-category rule must not fire on an outgoing payment (e.g. "úrok z úvěru" is an
            // expense), and vice versa. Skip when the category isn't loaded — the caller didn't opt in.
            if (rule.Category is { } category && !KindMatchesDirection(category.Kind, direction))
                continue;

            var haystack = rule.Field switch
            {
                RuleMatchField.CounterpartyName => name,
                RuleMatchField.Description => desc,
                RuleMatchField.VariableSymbol => vs,
                RuleMatchField.CounterpartyAccount => account,
                RuleMatchField.Any => any,
                _ => null,
            };

            if (!string.IsNullOrEmpty(haystack) && IsMatch(haystack, rule))
                return rule.CategoryId;
        }

        return null;
    }

    /// <summary>
    /// Filters and orders a family's rules for one account, applying scope: account-specific rules and the
    /// owner's personal rules join the always-applicable family rules. The result is ordered most-specific
    /// first (Account → Personal → Family), then by descending priority, then by creation — exactly the order
    /// <see cref="Match"/> expects. <paramref name="ownerMemberId"/> is the account's owner (may be null).
    /// </summary>
    public static IReadOnlyList<CategorizationRule> ForAccount(
        IEnumerable<CategorizationRule> rules, Guid accountId, Guid? ownerMemberId) =>
        rules
            .Where(r => r.Scope switch
            {
                RuleScope.Family => true,
                RuleScope.Account => r.BankAccountId == accountId,
                RuleScope.Personal => ownerMemberId is { } owner && r.OwnerMemberId == owner,
                _ => false,
            })
            .OrderBy(r => ScopeRank(r.Scope))
            .ThenByDescending(r => r.Priority)
            .ThenBy(r => r.CreatedAt)
            .ToList();

    /// <summary>Specificity order: an account rule beats a personal rule, which beats a family-wide rule.</summary>
    private static int ScopeRank(RuleScope scope) => scope switch
    {
        RuleScope.Account => 0,
        RuleScope.Personal => 1,
        _ => 2,
    };

    /// <summary>Tests one rule against an already-folded haystack, honouring its <see cref="RuleMatchMode"/>.</summary>
    private static bool IsMatch(string foldedHaystack, CategorizationRule rule)
    {
        var pattern = Fold(rule.Pattern);
        if (pattern.Length == 0)
            return false;

        switch (rule.MatchMode)
        {
            case RuleMatchMode.WholeWord:
            case RuleMatchMode.Regex:
                var regex = GetRegex(rule.MatchMode, pattern);
                if (regex is null)
                    return false; // invalid user-supplied regex never matches
                try
                {
                    return regex.IsMatch(foldedHaystack);
                }
                catch (RegexMatchTimeoutException)
                {
                    return false; // pathological pattern/input — treat as no match rather than hang
                }

            default:
                return foldedHaystack.Contains(pattern, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static Regex? GetRegex(RuleMatchMode mode, string foldedPattern)
    {
        var key = (mode == RuleMatchMode.WholeWord ? "w:" : "r:") + foldedPattern;
        return RegexCache.GetOrAdd(key, _ =>
        {
            var body = mode == RuleMatchMode.WholeWord
                ? $@"\b{Regex.Escape(foldedPattern)}\b"
                : foldedPattern;
            try
            {
                return new Regex(body, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout);
            }
            catch (ArgumentException)
            {
                return null;
            }
        });
    }

    /// <summary>Returns true when an income/expense category may be assigned to a transaction flowing in
    /// the given direction: income categories only to incoming money, expense categories only to outgoing.</summary>
    private static bool KindMatchesDirection(CategoryKind kind, TransactionDirection direction) =>
        kind == CategoryKind.Income
            ? direction == TransactionDirection.Incoming
            : direction == TransactionDirection.Outgoing;

    /// <summary>Validates a user-supplied regex pattern (used by the rules endpoint before persisting).</summary>
    public static bool IsValidRegex(string pattern)
    {
        try
        {
            _ = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
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
