using Flowlio.Application.Statements;
using Flowlio.Domain;
using Xunit;

namespace Flowlio.Tests;

public class TransactionCategorizerTests
{
    private static CategorizationRule Rule(RuleMatchField field, string pattern, Guid categoryId, int priority = 0) =>
        new() { Field = field, Pattern = pattern, CategoryId = categoryId, Priority = priority, IsActive = true };

    [Fact]
    public void Any_matches_pattern_in_description_when_counterparty_is_empty()
    {
        var cat = Guid.NewGuid();
        var rules = new[] { Rule(RuleMatchField.Any, "Albert", cat) };

        var result = TransactionCategorizer.Match(
            counterpartyName: null, description: "Platba kartou ALBERT 1234 PRAHA",
            variableSymbol: null, counterpartyAccount: null, rules);

        Assert.Equal(cat, result);
    }

    [Fact]
    public void Any_matches_pattern_in_counterparty()
    {
        var cat = Guid.NewGuid();
        var rules = new[] { Rule(RuleMatchField.Any, "Albert", cat) };

        Assert.Equal(cat, TransactionCategorizer.Match("ALBERT s.r.o.", null, null, null, rules));
    }

    [Fact]
    public void Specific_field_does_not_match_other_fields()
    {
        var rules = new[] { Rule(RuleMatchField.CounterpartyName, "Albert", Guid.NewGuid()) };

        // The pattern is only in the description, but the rule targets the counterparty -> no match.
        Assert.Null(TransactionCategorizer.Match(null, "ALBERT PRAHA", null, null, rules));
    }

    [Fact]
    public void First_matching_rule_wins_in_given_order()
    {
        var groceries = Guid.NewGuid();
        var other = Guid.NewGuid();
        // Callers pass rules already ordered by descending priority; Match returns the first hit.
        var rules = new[]
        {
            Rule(RuleMatchField.Any, "Albert", groceries, priority: 10),
            Rule(RuleMatchField.Any, "Albert", other, priority: 1),
        };

        Assert.Equal(groceries, TransactionCategorizer.Match("ALBERT", null, null, null, rules));
    }

    [Fact]
    public void Case_insensitive_match()
    {
        var cat = Guid.NewGuid();
        var rules = new[] { Rule(RuleMatchField.Any, "albert", cat) };

        Assert.Equal(cat, TransactionCategorizer.Match("ALBERT", null, null, null, rules));
    }

    [Fact]
    public void No_rules_returns_null()
    {
        Assert.Null(TransactionCategorizer.Match("ALBERT", "x", null, null, Array.Empty<CategorizationRule>()));
    }

    [Fact]
    public void No_match_returns_null()
    {
        var rules = new[] { Rule(RuleMatchField.Any, "Lidl", Guid.NewGuid()) };

        Assert.Null(TransactionCategorizer.Match("ALBERT", "Platba kartou", null, null, rules));
    }
}
