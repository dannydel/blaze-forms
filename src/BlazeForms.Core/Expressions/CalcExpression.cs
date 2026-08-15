using System.Text.Json.Serialization;

namespace BlazeForms.Expressions;

/// <summary>
/// A serializable value-producing expression: <c>{ op, operands[], format }</c> (PRD §5, §13). The
/// value-valued sibling of <see cref="ConditionGroup"/> — the same one-tree principle (decision log
/// D12), grown to compute a number or a date for a <see cref="Definitions.NodeType.Calc"/> node
/// rather than to decide a boolean. Flat by design: one operation over a list of operands. Nesting
/// (an operand carrying its own sub-expression) is a deliberately deferred, additive growth path.
/// </summary>
public sealed record CalcExpression
{
    private readonly IReadOnlyList<CalcOperand>? _operands;

    /// <summary>
    /// The operation applied to <see cref="Operands"/>.
    /// </summary>
    [JsonPropertyName("op")]
    public required CalcOperation Operation { get; init; }

    /// <summary>
    /// The operands, in the order the calculation editor shows them — significant for the
    /// order-sensitive operations (<see cref="CalcOperation.Subtract"/>,
    /// <see cref="CalcOperation.Divide"/>, and the date operations). Reads as empty when a document
    /// omits it.
    /// </summary>
    public IReadOnlyList<CalcOperand> Operands
    {
        get => _operands ?? [];
        init => _operands = value is null ? null : Array.AsReadOnly<CalcOperand>([.. value]);
    }

    /// <summary>
    /// How the computed value is presented. Display-only; the captured value keeps full precision.
    /// </summary>
    public CalcFormat Format { get; init; }
}
