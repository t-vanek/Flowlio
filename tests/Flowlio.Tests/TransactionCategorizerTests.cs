using Flowlio.Application.Statements;
using Flowlio.Domain;
using Xunit;

namespace Flowlio.Tests;

public class TransactionCategorizerTests
{
    private static CategorizationRule Rule(
        RuleMatchField field, string pattern, Guid categoryId, int priority = 0,
        RuleMatchMode mode = RuleMatchMode.Substring, CategoryKind? kind = null,
        RuleScope scope = RuleScope.Family, Guid? ownerMemberId = null, Guid? bankAccountId = null) =>
        new()
        {
            Field = field,
            MatchMode = mode,
            Scope = scope,
            OwnerMemberId = ownerMemberId,
            BankAccountId = bankAccountId,
            Pattern = pattern,
            CategoryId = categoryId,
            Priority = priority,
            IsActive = true,
            // Only set when a test exercises the income/expense direction filter; otherwise left null so
            // the filter is bypassed (matching how callers that don't Include the category behave).
            Category = kind is null ? null : new Category { Name = "test", Kind = kind.Value },
        };

    // Most tests don't care about amount/direction; default to a CZK outgoing payment.
    private static Guid? Match(
        string? counterpartyName, string? description, string? variableSymbol, string? counterpartyAccount,
        IReadOnlyList<CategorizationRule> rules, TransactionDirection direction = TransactionDirection.Outgoing,
        decimal amount = -100m, string currency = "CZK") =>
        TransactionCategorizer.Match(
            counterpartyName, description, variableSymbol, counterpartyAccount, amount, currency, direction, rules);

    [Fact]
    public void Any_matches_pattern_in_description_when_counterparty_is_empty()
    {
        var cat = Guid.NewGuid();
        var rules = new[] { Rule(RuleMatchField.Any, "Albert", cat) };

        var result = Match(null, "Platba kartou ALBERT 1234 PRAHA", null, null, rules);

        Assert.Equal(cat, result);
    }

    [Fact]
    public void Any_matches_pattern_in_counterparty()
    {
        var cat = Guid.NewGuid();
        var rules = new[] { Rule(RuleMatchField.Any, "Albert", cat) };

        Assert.Equal(cat, Match("ALBERT s.r.o.", null, null, null, rules));
    }

    [Fact]
    public void Specific_field_does_not_match_other_fields()
    {
        var rules = new[] { Rule(RuleMatchField.CounterpartyName, "Albert", Guid.NewGuid()) };

        // The pattern is only in the description, but the rule targets the counterparty -> no match.
        Assert.Null(Match(null, "ALBERT PRAHA", null, null, rules));
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

        Assert.Equal(groceries, Match("ALBERT", null, null, null, rules));
    }

    [Fact]
    public void Case_insensitive_match()
    {
        var cat = Guid.NewGuid();
        var rules = new[] { Rule(RuleMatchField.Any, "albert", cat) };

        Assert.Equal(cat, Match("ALBERT", null, null, null, rules));
    }

    [Fact]
    public void No_rules_returns_null()
    {
        Assert.Null(Match("ALBERT", "x", null, null, Array.Empty<CategorizationRule>()));
    }

    [Fact]
    public void No_match_returns_null()
    {
        var rules = new[] { Rule(RuleMatchField.Any, "Lidl", Guid.NewGuid()) };

        Assert.Null(Match("ALBERT", "Platba kartou", null, null, rules));
    }

    [Fact]
    public void Matches_when_statement_text_lacks_diacritics()
    {
        var cat = Guid.NewGuid();
        // Czech banks often strip diacritics and uppercase the merchant; the rule keeps its accents.
        var rules = new[] { Rule(RuleMatchField.Any, "Lékárna", cat) };

        Assert.Equal(cat, Match(null, "PLATBA KARTOU LEKARNA U ANDELA", null, null, rules));
    }

    [Fact]
    public void Matches_when_pattern_lacks_diacritics_but_text_has_them()
    {
        var cat = Guid.NewGuid();
        var rules = new[] { Rule(RuleMatchField.CounterpartyName, "Vyplata", cat) };

        Assert.Equal(cat, Match("Výplata mzdy", null, null, null, rules));
    }

    [Theory]
    [InlineData("Lékárna", "lekarna")]
    [InlineData("PŘÍJEM", "prijem")]
    [InlineData("Žabka", "zabka")]
    [InlineData(null, "")]
    [InlineData("   ", "")]
    public void Fold_strips_diacritics(string? input, string expected)
    {
        Assert.Equal(expected, TransactionCategorizer.Fold(input).ToLowerInvariant());
    }

    // ---- Match modes --------------------------------------------------------

    [Fact]
    public void WholeWord_does_not_match_inside_another_word()
    {
        var cat = Guid.NewGuid();
        var rules = new[] { Rule(RuleMatchField.Any, "Plat", cat, mode: RuleMatchMode.WholeWord) };

        // "Plat" must not fire on "platba kartou".
        Assert.Null(Match(null, "PLATBA KARTOU LIDL", null, null, rules));
    }

    [Fact]
    public void WholeWord_matches_standalone_token()
    {
        var cat = Guid.NewGuid();
        var rules = new[] { Rule(RuleMatchField.Any, "Plat", cat, mode: RuleMatchMode.WholeWord) };

        Assert.Equal(cat, Match(null, "Plat za 05/2026", null, null, rules, TransactionDirection.Incoming));
    }

    [Fact]
    public void Regex_matches_alternation_across_diacritics()
    {
        var cat = Guid.NewGuid();
        var rules = new[] { Rule(RuleMatchField.Any, "albert|billa|lídl", cat, mode: RuleMatchMode.Regex) };

        Assert.Equal(cat, Match(null, "NAKUP BILLA PRAHA", null, null, rules));
    }

    [Fact]
    public void Invalid_regex_never_matches_and_does_not_throw()
    {
        var rules = new[] { Rule(RuleMatchField.Any, "(unterminated", Guid.NewGuid(), mode: RuleMatchMode.Regex) };

        Assert.Null(Match(null, "(unterminated text", null, null, rules));
    }

    // ---- Income / expense direction filter ----------------------------------

    [Fact]
    public void Income_rule_does_not_apply_to_outgoing_payment()
    {
        var income = Guid.NewGuid();
        // "úrok z úvěru" is an outgoing expense; an income "Úrok" rule must not claim it.
        var rules = new[] { Rule(RuleMatchField.Any, "Úrok", income, kind: CategoryKind.Income) };

        Assert.Null(Match(null, "Úrok z úvěru", null, null, rules, TransactionDirection.Outgoing));
    }

    [Fact]
    public void Income_rule_applies_to_incoming_payment()
    {
        var income = Guid.NewGuid();
        var rules = new[] { Rule(RuleMatchField.Any, "Úrok", income, kind: CategoryKind.Income) };

        Assert.Equal(income, Match(null, "Připsání úroku", null, null, rules, TransactionDirection.Incoming));
    }

    [Fact]
    public void Expense_rule_does_not_apply_to_incoming_payment()
    {
        var expense = Guid.NewGuid();
        var rules = new[] { Rule(RuleMatchField.Any, "Albert", expense, kind: CategoryKind.Expense) };

        Assert.Null(Match("ALBERT", null, null, null, rules, TransactionDirection.Incoming));
    }

    [Fact]
    public void MatchRule_returns_the_winning_rule_so_callers_can_record_attribution()
    {
        var groceries = Guid.NewGuid();
        var winner = Rule(RuleMatchField.Any, "Albert", groceries, priority: 10);
        var rules = new[] { winner, Rule(RuleMatchField.Any, "Albert", Guid.NewGuid(), priority: 1) };

        var matched = TransactionCategorizer.MatchRule(
            "ALBERT", null, null, null, -100m, "CZK", TransactionDirection.Outgoing, rules);

        Assert.Same(winner, matched);
    }

    // ---- Scope (personal / account / family) --------------------------------

    [Fact]
    public void Account_rule_wins_over_personal_and_family_for_that_account()
    {
        var account = Guid.NewGuid();
        var owner = Guid.NewGuid();
        Guid family = Guid.NewGuid(), personal = Guid.NewGuid(), accountCat = Guid.NewGuid();
        var rules = new[]
        {
            Rule(RuleMatchField.Any, "Albert", family, scope: RuleScope.Family),
            Rule(RuleMatchField.Any, "Albert", personal, scope: RuleScope.Personal, ownerMemberId: owner),
            Rule(RuleMatchField.Any, "Albert", accountCat, scope: RuleScope.Account, bankAccountId: account),
        };

        var ordered = TransactionCategorizer.ForAccount(rules, account, owner);

        Assert.Equal(accountCat, Match("ALBERT", null, null, null, ordered));
    }

    [Fact]
    public void Personal_rule_wins_over_family_even_with_lower_priority()
    {
        var owner = Guid.NewGuid();
        var family = Guid.NewGuid();
        var personal = Guid.NewGuid();
        var rules = new[]
        {
            Rule(RuleMatchField.Any, "Albert", family, priority: 99, scope: RuleScope.Family),
            Rule(RuleMatchField.Any, "Albert", personal, priority: 0, scope: RuleScope.Personal, ownerMemberId: owner),
        };

        var ordered = TransactionCategorizer.ForAccount(rules, Guid.NewGuid(), owner);

        Assert.Equal(personal, Match("ALBERT", null, null, null, ordered));
    }

    [Fact]
    public void Another_members_personal_rule_does_not_apply()
    {
        var rules = new[]
        {
            Rule(RuleMatchField.Any, "Albert", Guid.NewGuid(), scope: RuleScope.Personal, ownerMemberId: Guid.NewGuid()),
        };

        // Account owned by a different member than the rule's owner.
        var ordered = TransactionCategorizer.ForAccount(rules, Guid.NewGuid(), ownerMemberId: Guid.NewGuid());

        Assert.Empty(ordered);
        Assert.Null(Match("ALBERT", null, null, null, ordered));
    }

    [Fact]
    public void Account_rule_for_a_different_account_does_not_apply()
    {
        var rules = new[]
        {
            Rule(RuleMatchField.Any, "Albert", Guid.NewGuid(), scope: RuleScope.Account, bankAccountId: Guid.NewGuid()),
        };

        var ordered = TransactionCategorizer.ForAccount(rules, accountId: Guid.NewGuid(), ownerMemberId: Guid.NewGuid());

        Assert.Empty(ordered);
    }

    [Fact]
    public void Family_rule_applies_to_any_account()
    {
        var family = Guid.NewGuid();
        var rules = new[] { Rule(RuleMatchField.Any, "Albert", family, scope: RuleScope.Family) };

        var ordered = TransactionCategorizer.ForAccount(rules, Guid.NewGuid(), ownerMemberId: null);

        Assert.Equal(family, Match("ALBERT", null, null, null, ordered));
    }

    // ---- Amount conditions --------------------------------------------------

    private static CategorizationRule AmountRule(
        Guid categoryId, decimal? min, decimal? max, string currency = "CZK", string? pattern = null) =>
        new()
        {
            Field = RuleMatchField.Any,
            MatchMode = RuleMatchMode.Substring,
            Pattern = pattern,
            MinAmount = min,
            MaxAmount = max,
            AmountCurrency = currency,
            CategoryId = categoryId,
            IsActive = true,
        };

    [Fact]
    public void Amount_only_rule_matches_within_range_on_absolute_value()
    {
        var cat = Guid.NewGuid();
        var rules = new[] { AmountRule(cat, min: 1000m, max: 5000m) };

        // Outgoing -2500 → magnitude 2500, within [1000, 5000].
        Assert.Equal(cat, Match(null, "cokoliv", null, null, rules, amount: -2500m));
    }

    [Fact]
    public void Amount_rule_does_not_match_outside_range()
    {
        var rules = new[] { AmountRule(Guid.NewGuid(), min: 1000m, max: 5000m) };

        Assert.Null(Match(null, "cokoliv", null, null, rules, amount: -200m));
        Assert.Null(Match(null, "cokoliv", null, null, rules, amount: -9000m));
    }

    [Fact]
    public void Amount_rule_with_min_only_is_a_lower_bound()
    {
        var cat = Guid.NewGuid();
        var rules = new[] { AmountRule(cat, min: 20000m, max: null) };

        Assert.Equal(cat, Match(null, null, null, null, rules, direction: TransactionDirection.Incoming, amount: 31000m));
        Assert.Null(Match(null, null, null, null, rules, direction: TransactionDirection.Incoming, amount: 15000m));
    }

    [Fact]
    public void Amount_rule_does_not_match_a_different_currency()
    {
        var rules = new[] { AmountRule(Guid.NewGuid(), min: 100m, max: 1000m, currency: "CZK") };

        Assert.Null(Match(null, "x", null, null, rules, amount: -500m, currency: "EUR"));
    }

    [Fact]
    public void Text_and_amount_must_both_hold()
    {
        var cat = Guid.NewGuid();
        var rules = new[] { AmountRule(cat, min: 10000m, max: null, pattern: "CSOB") };

        // Text matches and amount qualifies.
        Assert.Equal(cat, Match("CSOB", null, null, null, rules, amount: -15000m));
        // Text matches but amount too small → no match.
        Assert.Null(Match("CSOB", null, null, null, rules, amount: -300m));
        // Amount qualifies but text doesn't → no match.
        Assert.Null(Match("Albert", null, null, null, rules, amount: -15000m));
    }
}
