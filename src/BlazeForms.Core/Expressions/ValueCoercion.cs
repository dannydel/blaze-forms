using System.Globalization;

namespace BlazeForms.Expressions;

/// <summary>
/// The shared answer-coercion vocabulary used by both <see cref="ConditionEvaluator"/> and
/// <see cref="CalcEvaluator"/>. Answers arrive as loosely typed <see cref="object"/> — whatever the
/// host or renderer stored (text, a number, a <see cref="bool"/>, a <see cref="DateOnly"/>, or a
/// draft-round-tripped ISO string) — so both evaluators reach a number or a date the same way,
/// which is what keeps their results consistent for authors (PRD §6).
/// </summary>
internal static class ValueCoercion
{
    /// <summary>
    /// Whether an answer reads as no value: missing, null, whitespace, or an empty set of
    /// selections.
    /// </summary>
    internal static bool IsBlank(object? value) => value switch
    {
        null => true,
        string text => string.IsNullOrWhiteSpace(text),
        IEnumerable<string> selections => !selections.Any(),
        System.Collections.IEnumerable sequence => !sequence.Cast<object?>().Any(),
        _ => false,
    };

    /// <summary>
    /// Coerces an answer to a <see cref="decimal"/>. Numbers pass through; a string parses under the
    /// invariant culture so <c>"21.0"</c> reads the same wherever the form runs.
    /// </summary>
    internal static bool TryAsDecimal(object? value, out decimal number)
    {
        switch (value)
        {
            case decimal already:
                number = already;
                return true;
            case int integer:
                number = integer;
                return true;
            case long integer:
                number = integer;
                return true;
            case double real:
                // NaN, the infinities, and magnitudes beyond decimal's range have no decimal value;
                // read them as not-a-number rather than letting the cast throw.
                if (!double.IsFinite(real))
                {
                    number = 0m;
                    return false;
                }

                try
                {
                    number = (decimal)real;
                    return true;
                }
                catch (OverflowException)
                {
                    number = 0m;
                    return false;
                }
            case float real:
                if (!float.IsFinite(real))
                {
                    number = 0m;
                    return false;
                }

                try
                {
                    number = (decimal)real;
                    return true;
                }
                catch (OverflowException)
                {
                    number = 0m;
                    return false;
                }
            case string text:
                return decimal.TryParse(
                    text,
                    NumberStyles.Number,
                    CultureInfo.InvariantCulture,
                    out number);
            default:
                number = 0m;
                return false;
        }
    }

    /// <summary>
    /// Coerces an answer to a <see cref="DateTimeOffset"/>, for the chronological comparisons the
    /// expression tree makes. A string parses under the invariant culture, assumed UTC.
    /// </summary>
    internal static bool TryAsDate(object? value, out DateTimeOffset date)
    {
        switch (value)
        {
            case DateTimeOffset already:
                date = already;
                return true;
            case DateOnly dateOnly:
                date = new DateTimeOffset(dateOnly.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
                return true;
            case DateTime dateTime:
                date = new DateTimeOffset(DateTime.SpecifyKind(dateTime, DateTimeKind.Utc));
                return true;
            case string text:
                return DateTimeOffset.TryParse(
                    text,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out date);
            default:
                date = default;
                return false;
        }
    }

    /// <summary>
    /// Coerces an answer to a <see cref="DateOnly"/>, the shape a calculation's date operations work
    /// in. A <see cref="DateOnly"/> input — what a <see cref="Definitions.NodeType.Date"/> node
    /// stores — passes straight through; a string parses as an ISO date first, then as a full
    /// instant whose date component is taken. Date arithmetic in <see cref="DateOnly"/> is immune to
    /// time zones and daylight saving, which is what makes day counts predictable for authors.
    /// </summary>
    internal static bool TryAsDateOnly(object? value, out DateOnly date)
    {
        switch (value)
        {
            case DateOnly already:
                date = already;
                return true;
            case DateTime dateTime:
                date = DateOnly.FromDateTime(dateTime);
                return true;
            case DateTimeOffset instant:
                date = DateOnly.FromDateTime(instant.UtcDateTime);
                return true;
            case string text when DateOnly.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed):
                date = parsed;
                return true;
            case string text when DateTimeOffset.TryParse(
                text,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var instant):
                date = DateOnly.FromDateTime(instant.UtcDateTime);
                return true;
            default:
                date = default;
                return false;
        }
    }
}
