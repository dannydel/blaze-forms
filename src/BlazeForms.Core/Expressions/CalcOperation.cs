using System.Text.Json.Serialization;

namespace BlazeForms.Expressions;

/// <summary>
/// The operations a <see cref="CalcExpression"/> can apply to its operands (PRD §5, §13). Like
/// <see cref="ConditionOperator"/>, this enum is the whole vocabulary — there is no string DSL —
/// and the JSON name of each member is part of the schema contract, pinned by the golden-file and
/// serialization tests, so members may be added but never renamed without a
/// <c>schemaVersion</c> bump.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<CalcOperation>))]
public enum CalcOperation
{
    /// <summary>
    /// The sum of every numeric operand. Blank operands are skipped; an expression whose operands
    /// are all blank evaluates to no value rather than to zero.
    /// </summary>
    [JsonStringEnumMemberName("sum")]
    Sum,

    /// <summary>
    /// The first numeric operand less each operand after it, folded left in operand order. Any
    /// blank or non-numeric operand makes the whole expression evaluate to no value.
    /// </summary>
    [JsonStringEnumMemberName("subtract")]
    Subtract,

    /// <summary>
    /// The product of every numeric operand, folded left in operand order. Any blank or
    /// non-numeric operand makes the whole expression evaluate to no value.
    /// </summary>
    [JsonStringEnumMemberName("multiply")]
    Multiply,

    /// <summary>
    /// The first numeric operand divided by each operand after it, folded left in operand order.
    /// Any blank or non-numeric operand — or a division by zero — makes the whole expression
    /// evaluate to no value.
    /// </summary>
    [JsonStringEnumMemberName("divide")]
    Divide,

    /// <summary>
    /// A date operand advanced by a numeric day-count operand, yielding a date. Expects exactly two
    /// operands, a date then a number; any other shape, or a blank operand, evaluates to no value.
    /// </summary>
    [JsonStringEnumMemberName("dateAddDays")]
    DateAddDays,

    /// <summary>
    /// The whole number of days from the first date operand to the second, yielding a number.
    /// Expects exactly two date operands; any other shape, or a blank operand, evaluates to no
    /// value.
    /// </summary>
    [JsonStringEnumMemberName("dateDiffDays")]
    DateDiffDays,
}
