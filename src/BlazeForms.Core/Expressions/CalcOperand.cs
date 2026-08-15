using System.Text.Json.Serialization;

namespace BlazeForms.Expressions;

/// <summary>
/// One operand of a <see cref="CalcExpression"/>: exactly one of a field reference, a numeric
/// literal, or a function (PRD §5, §13).
/// </summary>
/// <remarks>
/// The "exactly one" rule is a validity constraint, not a construction constraint: a definition is
/// untrusted input, so a malformed operand that sets none or several of these must deserialize
/// without throwing and then be rejected downstream — the evaluator treats it as no value, and the
/// linter reports it — exactly as a dangling <see cref="Condition.Field"/> is a lint (FR-03) rather
/// than a deserialization error. Only nulls are omitted from the wire, so a well-formed operand
/// serializes to a single property.
/// </remarks>
public sealed record CalcOperand
{
    /// <summary>
    /// The identifier of the node whose answer this operand reads. A reference to a node that no
    /// longer exists is a blocking lint (FR-03, PRD §8), not a runtime error: the evaluator treats
    /// the missing answer as blank.
    /// </summary>
    public string? Field { get; init; }

    /// <summary>
    /// A literal numeric value.
    /// </summary>
    public decimal? Number { get; init; }

    /// <summary>
    /// A function standing in for a value the caller supplies, such as
    /// <see cref="CalcFunction.Today"/>.
    /// </summary>
    public CalcFunction? Function { get; init; }
}
