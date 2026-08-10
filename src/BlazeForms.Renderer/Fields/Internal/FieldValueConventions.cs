using BlazeForms.Definitions;

namespace BlazeForms.Fields.Internal;

/// <summary>
/// The fixed, per-<see cref="NodeType"/> convention every shipped field component follows for
/// the CLR type it stores an answer as and the value that answer takes when the field is empty.
/// Every field component — and any host replacement registered through
/// <c>IFieldComponentRegistry</c> — writes only the CLR types
/// <see cref="Serialization.FormValues.ToJsonValues"/> understands: <see cref="string"/>,
/// <see cref="bool"/>, <see cref="decimal"/>, <see cref="DateOnly"/>, or
/// <see cref="IEnumerable{T}"/> of <see cref="string"/>; anything else throws
/// <see cref="NotSupportedException"/> when a submission or draft tries to serialize it.
/// </summary>
internal static class FieldValueConventions
{
    /// <summary>
    /// The CLR type a field of this node type stores its answer as.
    /// </summary>
    /// <param name="nodeType">
    /// The field's node type.
    /// </param>
    /// <returns>
    /// The stored CLR type, or <see langword="null"/> for a static-content node type (and for
    /// <see cref="NodeType.Calc"/>, which writes no value in P1) and for the P2-reserved types.
    /// </returns>
    public static Type? GetStoredClrType(NodeType nodeType) => nodeType switch
    {
        NodeType.Text or NodeType.TextArea or NodeType.Email or NodeType.Phone => typeof(string),
        NodeType.Select or NodeType.Radio or NodeType.YesNo => typeof(string),
        NodeType.Number or NodeType.Currency => typeof(decimal),
        NodeType.Date => typeof(DateOnly),
        NodeType.DateRange => typeof(string[]),
        NodeType.CheckboxGroup => typeof(List<string>),
        NodeType.Boolean => typeof(bool),
        _ => null,
    };

    /// <summary>
    /// The value a field of this node type reports when the respondent has given it no answer.
    /// </summary>
    /// <param name="nodeType">
    /// The field's node type.
    /// </param>
    /// <returns>
    /// An empty <see cref="List{T}"/> of <see cref="string"/> for <see cref="NodeType.CheckboxGroup"/>
    /// (an unanswered multi-select is a selection of nothing, not an absent selection);
    /// <see langword="false"/> for <see cref="NodeType.Boolean"/> (an unchecked box always has a
    /// value); and <see langword="null"/> for every other type, including the static-content and
    /// P2-reserved types.
    /// </returns>
    public static object? GetEmptyValue(NodeType nodeType) => nodeType switch
    {
        NodeType.CheckboxGroup => new List<string>(),
        NodeType.Boolean => false,
        _ => null,
    };
}
