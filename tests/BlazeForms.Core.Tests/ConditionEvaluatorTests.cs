using BlazeForms.Expressions;

namespace BlazeForms.Core.Tests;

/// <summary>
/// Covers every operator in PRD §6, both joins, and the coercion edge cases that decide
/// whether a respondent sees a field.
/// </summary>
public sealed class ConditionEvaluatorTests
{
    private static readonly string[] OneSelection = ["bus"];
    private static readonly string[] TwoSelections = ["bus", "walk"];

    private static readonly IReadOnlyDictionary<string, object?> Values =
        new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["consent"] = true,
            ["declined"] = false,
            ["age"] = 21m,
            ["ageText"] = "21",
            ["paddedCode"] = "01",
            ["dateText"] = "2026-01-31",
            ["name"] = "Ada Lovelace",
            ["whitespace"] = "   ",
            ["explicitNull"] = null,
            ["startDate"] = new DateOnly(2026, 1, 31),
            ["interests"] = new[] { "bus", "walk" },
            ["singleSelection"] = new[] { "bus" },
            ["noSelections"] = Array.Empty<string>(),
            ["needsTransport"] = "yes",
        };

    private static bool Eval(ConditionOperator conditionOperator, string field, string? value = null) =>
        ConditionEvaluator.Evaluate(
            new Condition { Field = field, Operator = conditionOperator, Value = value },
            Values);

    [Fact]
    public void IsComparesStoredOptionValuesExactly()
    {
        Assert.True(Eval(ConditionOperator.Is, "needsTransport", "yes"));
        Assert.False(Eval(ConditionOperator.Is, "needsTransport", "YES"));
        Assert.False(Eval(ConditionOperator.Is, "needsTransport", "no"));
    }

    [Fact]
    public void IsCoercesAnAnswerThatIsGenuinelyANumberOrADate()
    {
        Assert.True(Eval(ConditionOperator.Is, "age", "21"));
        Assert.True(Eval(ConditionOperator.Is, "age", "21.0"));
        Assert.True(Eval(ConditionOperator.Is, "startDate", "2026-01-31"));
        Assert.False(Eval(ConditionOperator.Is, "startDate", "2026-02-01"));
    }

    [Fact]
    public void IsComparesTextAnswersOrdinallySoStoredValuesStayStable()
    {
        // A stored option value is text. Coercing it to a number would make these pass, and two
        // distinct option values would start colliding.
        Assert.True(Eval(ConditionOperator.Is, "ageText", "21"));
        Assert.False(Eval(ConditionOperator.Is, "ageText", "21.0"));
        Assert.False(Eval(ConditionOperator.Is, "paddedCode", "1"));
        Assert.True(Eval(ConditionOperator.Is, "paddedCode", "01"));
        Assert.False(Eval(ConditionOperator.Is, "dateText", "31 Jan 2026"));
        Assert.True(Eval(ConditionOperator.Is, "dateText", "2026-01-31"));
    }

    [Fact]
    public void IsAndIsNotBothFailWhenTheRuleCarriesNoValueToCompareAgainst()
    {
        Assert.False(Eval(ConditionOperator.Is, "needsTransport"));
        Assert.False(Eval(ConditionOperator.IsNot, "needsTransport"));
    }

    [Fact]
    public void ALazilyEnumeratedSelectionBehavesLikeAMaterializedOne()
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["lazyOne"] = OneSelection.Select(selection => selection),
            ["lazyMany"] = TwoSelections.Select(selection => selection),
            ["lazyNone"] = Array.Empty<string>().Select(selection => selection),
        };

        bool Lazy(ConditionOperator conditionOperator, string field, string? value = null) =>
            ConditionEvaluator.Evaluate(
                new Condition { Field = field, Operator = conditionOperator, Value = value },
                values);

        Assert.True(Lazy(ConditionOperator.Is, "lazyOne", "bus"));
        Assert.False(Lazy(ConditionOperator.Is, "lazyMany", "bus"));
        Assert.True(Lazy(ConditionOperator.Contains, "lazyMany", "bus"));
        Assert.True(Lazy(ConditionOperator.IsBlank, "lazyNone"));
        Assert.True(Lazy(ConditionOperator.IsNotBlank, "lazyOne"));
    }

    [Fact]
    public void IsTreatsABooleanFieldAsTrueOrFalseText()
    {
        Assert.True(Eval(ConditionOperator.Is, "consent", "true"));
        Assert.True(Eval(ConditionOperator.Is, "consent", "yes"));
        Assert.False(Eval(ConditionOperator.Is, "consent", "no"));
    }

    [Fact]
    public void IsMatchesASingleSelectionCollectionButNotAMultiSelection()
    {
        Assert.True(Eval(ConditionOperator.Is, "singleSelection", "bus"));
        Assert.False(Eval(ConditionOperator.Is, "interests", "bus"));
    }

    [Fact]
    public void IsIsFalseWhenTheFieldIsMissingOrNull()
    {
        Assert.False(Eval(ConditionOperator.Is, "nonexistent", "yes"));
        Assert.False(Eval(ConditionOperator.Is, "explicitNull", "yes"));
    }

    [Fact]
    public void IsNotNegatesIsIncludingForMissingFields()
    {
        Assert.True(Eval(ConditionOperator.IsNot, "needsTransport", "no"));
        Assert.False(Eval(ConditionOperator.IsNot, "needsTransport", "yes"));
        Assert.True(Eval(ConditionOperator.IsNot, "nonexistent", "yes"));
    }

    [Fact]
    public void IsTrueAcceptsBooleansAndAffirmativeText()
    {
        Assert.True(Eval(ConditionOperator.IsTrue, "consent"));
        Assert.True(Eval(ConditionOperator.IsTrue, "needsTransport"));
        Assert.False(Eval(ConditionOperator.IsTrue, "declined"));
    }

    [Fact]
    public void IsTrueIsFalseWhenThereIsNoValueToBeTrue()
    {
        Assert.False(Eval(ConditionOperator.IsTrue, "nonexistent"));
        Assert.False(Eval(ConditionOperator.IsTrue, "explicitNull"));
        Assert.False(Eval(ConditionOperator.IsTrue, "name"));
    }

    [Fact]
    public void IsFalseRequiresAValueThatIsActuallyFalse()
    {
        Assert.True(Eval(ConditionOperator.IsFalse, "declined"));
        Assert.False(Eval(ConditionOperator.IsFalse, "consent"));
        Assert.False(Eval(ConditionOperator.IsFalse, "nonexistent"));
        Assert.False(Eval(ConditionOperator.IsFalse, "explicitNull"));
    }

    [Fact]
    public void IsBlankCoversMissingNullWhitespaceAndEmptySelections()
    {
        Assert.True(Eval(ConditionOperator.IsBlank, "nonexistent"));
        Assert.True(Eval(ConditionOperator.IsBlank, "explicitNull"));
        Assert.True(Eval(ConditionOperator.IsBlank, "whitespace"));
        Assert.True(Eval(ConditionOperator.IsBlank, "noSelections"));
        Assert.False(Eval(ConditionOperator.IsBlank, "name"));
        Assert.False(Eval(ConditionOperator.IsBlank, "declined"));
    }

    [Fact]
    public void IsNotBlankNegatesIsBlank()
    {
        Assert.True(Eval(ConditionOperator.IsNotBlank, "name"));
        Assert.True(Eval(ConditionOperator.IsNotBlank, "interests"));
        Assert.False(Eval(ConditionOperator.IsNotBlank, "whitespace"));
        Assert.False(Eval(ConditionOperator.IsNotBlank, "nonexistent"));
    }

    [Fact]
    public void GreaterThanCoercesBothOperandsToNumbers()
    {
        Assert.True(Eval(ConditionOperator.GreaterThan, "age", "18"));
        Assert.True(Eval(ConditionOperator.GreaterThan, "ageText", "18"));
        Assert.False(Eval(ConditionOperator.GreaterThan, "age", "21"));
        Assert.False(Eval(ConditionOperator.GreaterThan, "age", "40"));
    }

    [Fact]
    public void LessThanCoercesBothOperandsToNumbers()
    {
        Assert.True(Eval(ConditionOperator.LessThan, "age", "40"));
        Assert.False(Eval(ConditionOperator.LessThan, "age", "21"));
        Assert.False(Eval(ConditionOperator.LessThan, "age", "18"));
    }

    [Fact]
    public void ComparisonOperatorsUnderstandDates()
    {
        Assert.True(Eval(ConditionOperator.GreaterThan, "startDate", "2026-01-01"));
        Assert.True(Eval(ConditionOperator.LessThan, "startDate", "2026-02-01"));
        Assert.False(Eval(ConditionOperator.GreaterThan, "startDate", "2026-03-01"));
    }

    [Fact]
    public void ComparisonOperatorsAreFalseWhenAnOperandCannotBeCoerced()
    {
        Assert.False(Eval(ConditionOperator.GreaterThan, "name", "18"));
        Assert.False(Eval(ConditionOperator.LessThan, "name", "18"));
        Assert.False(Eval(ConditionOperator.GreaterThan, "age", "eighteen"));
        Assert.False(Eval(ConditionOperator.GreaterThan, "nonexistent", "18"));
        Assert.False(Eval(ConditionOperator.GreaterThan, "age", null));
    }

    [Fact]
    public void ContainsMatchesMembershipInAMultiSelection()
    {
        Assert.True(Eval(ConditionOperator.Contains, "interests", "bus"));
        Assert.True(Eval(ConditionOperator.Contains, "interests", "walk"));
        Assert.False(Eval(ConditionOperator.Contains, "interests", "car"));
        Assert.False(Eval(ConditionOperator.Contains, "noSelections", "bus"));
    }

    [Fact]
    public void ContainsMatchesSubstringsInTextCaseInsensitively()
    {
        Assert.True(Eval(ConditionOperator.Contains, "name", "lovelace"));
        Assert.True(Eval(ConditionOperator.Contains, "name", "Ada"));
        Assert.False(Eval(ConditionOperator.Contains, "name", "Babbage"));
    }

    [Fact]
    public void ContainsIsFalseWithoutAValueToLookFor()
    {
        Assert.False(Eval(ConditionOperator.Contains, "interests", null));
        Assert.False(Eval(ConditionOperator.Contains, "nonexistent", "bus"));
    }

    [Fact]
    public void AllJoinRequiresEveryCondition()
    {
        var group = new ConditionGroup
        {
            Join = ConditionJoin.All,
            Conditions =
            [
                new Condition { Field = "needsTransport", Operator = ConditionOperator.Is, Value = "yes" },
                new Condition { Field = "age", Operator = ConditionOperator.GreaterThan, Value = "18" },
            ],
        };

        Assert.True(ConditionEvaluator.Evaluate(group, Values));

        var failing = group with
        {
            Conditions =
            [
                .. group.Conditions,
                new Condition { Field = "name", Operator = ConditionOperator.IsBlank },
            ],
        };

        Assert.False(ConditionEvaluator.Evaluate(failing, Values));
    }

    [Fact]
    public void AnyJoinNeedsOnlyOneCondition()
    {
        var group = new ConditionGroup
        {
            Join = ConditionJoin.Any,
            Conditions =
            [
                new Condition { Field = "name", Operator = ConditionOperator.IsBlank },
                new Condition { Field = "age", Operator = ConditionOperator.GreaterThan, Value = "18" },
            ],
        };

        Assert.True(ConditionEvaluator.Evaluate(group, Values));

        var failing = group with
        {
            Conditions =
            [
                new Condition { Field = "name", Operator = ConditionOperator.IsBlank },
                new Condition { Field = "age", Operator = ConditionOperator.GreaterThan, Value = "99" },
            ],
        };

        Assert.False(ConditionEvaluator.Evaluate(failing, Values));
    }

    [Fact]
    public void AnEmptyGroupFollowsOrdinaryLogic()
    {
        Assert.True(ConditionEvaluator.Evaluate(new ConditionGroup { Join = ConditionJoin.All }, Values));
        Assert.False(ConditionEvaluator.Evaluate(new ConditionGroup { Join = ConditionJoin.Any }, Values));
    }

    [Fact]
    public void JoinDefaultsToAll()
    {
        Assert.Equal(ConditionJoin.All, new ConditionGroup().Join);
    }

    [Fact]
    public void EvaluateRejectsNullArguments()
    {
        var group = new ConditionGroup();
        var condition = new Condition { Field = "name", Operator = ConditionOperator.IsNotBlank };

        Assert.Throws<ArgumentNullException>(() => ConditionEvaluator.Evaluate((ConditionGroup)null!, Values));
        Assert.Throws<ArgumentNullException>(() => ConditionEvaluator.Evaluate(group, null!));
        Assert.Throws<ArgumentNullException>(() => ConditionEvaluator.Evaluate((Condition)null!, Values));
        Assert.Throws<ArgumentNullException>(() => ConditionEvaluator.Evaluate(condition, null!));
    }
}
