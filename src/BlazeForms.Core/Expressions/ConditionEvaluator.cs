using System.Globalization;

namespace BlazeForms.Expressions;

/// <summary>
/// Evaluates the expression tree against a respondent's answers.
/// </summary>
/// <remarks>
/// <para>
/// Answers arrive as a dictionary keyed by node ID, holding whatever the host or renderer put
/// there: text, <see cref="bool"/>, a number, a <see cref="DateOnly"/>, or a collection of
/// stored option values for a checkbox group. A key that is absent is treated exactly like a
/// <see langword="null"/> answer.
/// </para>
/// <para>
/// Coercion rules, so authors get predictable results. Equality is decided by the answer's own
/// type: a text answer — which is what a choice node's stored option value is — compares
/// ordinally, so <c>"01"</c> never equals <c>"1"</c> and <c>"2026-01-31"</c> never equals
/// <c>"31 Jan 2026"</c>; only an answer that is genuinely a number or a date is compared
/// numerically or chronologically, which is what makes <c>21</c> agree with <c>"21.0"</c>.
/// </para>
/// <para>
/// <c>gt</c>/<c>lt</c> are the one place text is coerced, since a bound is always authored as
/// text: both operands must reach a number or a date, and the clause is
/// <see langword="false"/> otherwise. <c>contains</c> is membership for a multi-selection and a
/// case-insensitive substring for text. A truthiness test reads booleans directly, accepts the
/// affirmative and negative words a yes/no node stores, and treats a number as true when it is
/// non-zero. A clause whose <see cref="Condition.Value"/> is absent never holds — including
/// <c>isNot</c>, which would otherwise make a half-authored rule always match.
/// </para>
/// </remarks>
public static class ConditionEvaluator
{
    private static readonly string[] TruthyText = ["true", "yes", "y", "1", "on"];
    private static readonly string[] FalsyText = ["false", "no", "n", "0", "off"];

    /// <summary>
    /// Evaluates a whole expression tree.
    /// </summary>
    /// <param name="group">
    /// The expression to evaluate.
    /// </param>
    /// <param name="values">
    /// The respondent's answers, keyed by node ID.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the expression is satisfied. An empty
    /// <see cref="ConditionJoin.All"/> group is vacuously satisfied; an empty
    /// <see cref="ConditionJoin.Any"/> group never is.
    /// </returns>
    public static bool Evaluate(ConditionGroup group, IReadOnlyDictionary<string, object?> values)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(values);

        if (group.Conditions.Count == 0)
        {
            return group.Join == ConditionJoin.All;
        }

        return group.Join == ConditionJoin.All
            ? group.Conditions.All(condition => Evaluate(condition, values))
            : group.Conditions.Any(condition => Evaluate(condition, values));
    }

    /// <summary>
    /// Evaluates a single clause.
    /// </summary>
    /// <param name="condition">
    /// The clause to evaluate.
    /// </param>
    /// <param name="values">
    /// The respondent's answers, keyed by node ID.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the clause holds.
    /// </returns>
    public static bool Evaluate(Condition condition, IReadOnlyDictionary<string, object?> values)
    {
        ArgumentNullException.ThrowIfNull(condition);
        ArgumentNullException.ThrowIfNull(values);

        values.TryGetValue(condition.Field, out var answer);

        return condition.Operator switch
        {
            ConditionOperator.Is => condition.Value is not null && AreEqual(answer, condition.Value),
            ConditionOperator.IsNot => condition.Value is not null && !AreEqual(answer, condition.Value),
            ConditionOperator.IsTrue => AsBoolean(answer) == true,
            ConditionOperator.IsFalse => AsBoolean(answer) == false,
            ConditionOperator.IsBlank => IsBlank(answer),
            ConditionOperator.IsNotBlank => !IsBlank(answer),
            ConditionOperator.GreaterThan => Compare(answer, condition.Value) is int ordering && ordering > 0,
            ConditionOperator.LessThan => Compare(answer, condition.Value) is int ordering && ordering < 0,
            ConditionOperator.Contains => ContainsValue(answer, condition.Value),
            _ => false,
        };
    }

    private static bool AreEqual(object? answer, string? expected)
    {
        if (answer is null || expected is null)
        {
            return false;
        }

        if (answer is IEnumerable<string> selections)
        {
            var chosen = Materialize(selections);

            return chosen.Count == 1 && string.Equals(chosen[0], expected, StringComparison.Ordinal);
        }

        if (answer is bool flag)
        {
            return AsBoolean(expected) == flag;
        }

        // Numeric and chronological comparison is gated on the answer's own type. Coercing a text
        // answer here would quietly make "01" equal "1", which breaks the stable-stored-value
        // guarantee choice nodes rely on (PRD §5).
        if (IsNumber(answer))
        {
            return ValueCoercion.TryAsDecimal(answer, out var answerNumber)
                && ValueCoercion.TryAsDecimal(expected, out var expectedNumber)
                && answerNumber == expectedNumber;
        }

        if (IsDate(answer))
        {
            return ValueCoercion.TryAsDate(answer, out var answerDate)
                && ValueCoercion.TryAsDate(expected, out var expectedDate)
                && answerDate == expectedDate;
        }

        return string.Equals(AsText(answer), expected, StringComparison.Ordinal);
    }

    private static bool IsNumber(object answer) =>
        answer is byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal;

    private static bool IsDate(object answer) => answer is DateOnly or DateTime or DateTimeOffset;

    private static IReadOnlyList<string> Materialize(IEnumerable<string> selections) =>
        selections as IReadOnlyList<string> ?? [.. selections];

    private static bool? AsBoolean(object? answer)
    {
        switch (answer)
        {
            case null:
                return null;
            case bool flag:
                return flag;
            case string text when TruthyText.Contains(text.Trim(), StringComparer.OrdinalIgnoreCase):
                return true;
            case string text when FalsyText.Contains(text.Trim(), StringComparer.OrdinalIgnoreCase):
                return false;
            case string:
                return null;
            default:
                // A numeric answer is true when it is non-zero, matching how a host that stores a
                // checkbox as 0/1 would expect it to read.
                return ValueCoercion.TryAsDecimal(answer, out var number) ? number != 0m : null;
        }
    }

    private static bool IsBlank(object? answer) => answer switch
    {
        null => true,
        string text => string.IsNullOrWhiteSpace(text),
        IEnumerable<string> selections => !selections.Any(),
        System.Collections.IEnumerable sequence => !sequence.Cast<object?>().Any(),
        _ => false,
    };

    private static bool ContainsValue(object? answer, string? expected)
    {
        if (answer is null || expected is null)
        {
            return false;
        }

        if (answer is IEnumerable<string> selections)
        {
            return selections.Contains(expected, StringComparer.Ordinal);
        }

        var text = AsText(answer);

        return text is not null && text.Contains(expected, StringComparison.OrdinalIgnoreCase);
    }

    private static int? Compare(object? answer, string? expected)
    {
        if (answer is null || expected is null)
        {
            return null;
        }

        if (ValueCoercion.TryAsDecimal(answer, out var answerNumber) && ValueCoercion.TryAsDecimal(expected, out var expectedNumber))
        {
            return answerNumber.CompareTo(expectedNumber);
        }

        if (ValueCoercion.TryAsDate(answer, out var answerDate) && ValueCoercion.TryAsDate(expected, out var expectedDate))
        {
            return answerDate.CompareTo(expectedDate);
        }

        return null;
    }

    private static string? AsText(object? value) => value switch
    {
        null => null,
        string text => text,
        bool flag => flag ? "true" : "false",
        DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        DateTimeOffset instant => instant.ToString("O", CultureInfo.InvariantCulture),
        DateTime instant => instant.ToString("O", CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString(),
    };
}
