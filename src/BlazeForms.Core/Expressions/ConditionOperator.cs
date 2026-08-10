using System.Text.Json.Serialization;

namespace BlazeForms.Expressions;

/// <summary>
/// The nine operators of the expression tree (PRD §6). There is no string DSL anywhere in P1 —
/// this enum is the whole vocabulary. The JSON name of each member is part of the schema
/// contract.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ConditionOperator>))]
public enum ConditionOperator
{
    /// <summary>
    /// The field's answer equals the condition value.
    /// </summary>
    [JsonStringEnumMemberName("is")]
    Is,

    /// <summary>
    /// The field's answer does not equal the condition value. A field with no answer satisfies
    /// this operator.
    /// </summary>
    [JsonStringEnumMemberName("isNot")]
    IsNot,

    /// <summary>
    /// The field's answer reads as true. Ignores the condition value.
    /// </summary>
    [JsonStringEnumMemberName("isTrue")]
    IsTrue,

    /// <summary>
    /// The field's answer reads as false. Ignores the condition value; a field with no answer
    /// is neither true nor false.
    /// </summary>
    [JsonStringEnumMemberName("isFalse")]
    IsFalse,

    /// <summary>
    /// The field has no answer: missing, null, whitespace, or an empty set of selections.
    /// Ignores the condition value.
    /// </summary>
    [JsonStringEnumMemberName("isBlank")]
    IsBlank,

    /// <summary>
    /// The field has an answer. Ignores the condition value.
    /// </summary>
    [JsonStringEnumMemberName("isNotBlank")]
    IsNotBlank,

    /// <summary>
    /// The field's answer sorts after the condition value once both coerce to a number or a
    /// date.
    /// </summary>
    [JsonStringEnumMemberName("gt")]
    GreaterThan,

    /// <summary>
    /// The field's answer sorts before the condition value once both coerce to a number or a
    /// date.
    /// </summary>
    [JsonStringEnumMemberName("lt")]
    LessThan,

    /// <summary>
    /// The condition value is one of the field's selections, or appears within its text.
    /// </summary>
    [JsonStringEnumMemberName("contains")]
    Contains,
}
