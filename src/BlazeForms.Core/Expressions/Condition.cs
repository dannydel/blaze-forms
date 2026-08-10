using System.Text.Json.Serialization;

namespace BlazeForms.Expressions;

/// <summary>
/// One clause of an expression tree: <c>{ field, op, value }</c> (PRD §6).
/// </summary>
public sealed record Condition
{
    /// <summary>
    /// The identifier of the node whose answer is examined. A reference to a node that no longer
    /// exists is a blocking lint (FR-03, PRD §8), not a runtime error: the evaluator treats the
    /// missing answer as blank.
    /// </summary>
    public required string Field { get; init; }

    /// <summary>
    /// The comparison to apply.
    /// </summary>
    [JsonPropertyName("op")]
    public required ConditionOperator Operator { get; init; }

    /// <summary>
    /// The value compared against, stored as text so the serialized shape stays stable. The
    /// evaluator coerces it to the answer's shape — for choice nodes this is the option's stored
    /// value, never its label. Ignored by the operators that take no operand.
    /// </summary>
    public string? Value { get; init; }
}
