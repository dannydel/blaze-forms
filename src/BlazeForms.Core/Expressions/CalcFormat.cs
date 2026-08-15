using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace BlazeForms.Expressions;

/// <summary>
/// How a <see cref="CalcExpression"/>'s computed value is presented to the respondent (PRD §5).
/// A display hint only: the value the renderer captures and the envelope stores is never rounded
/// to the format, so a submission keeps full precision regardless of how it was shown. The JSON
/// name of each member is part of the schema contract.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<CalcFormat>))]
public enum CalcFormat
{
    /// <summary>
    /// A plain number, formatted in the respondent's culture.
    /// </summary>
    [JsonStringEnumMemberName("number")]
    Number,

    /// <summary>
    /// A whole number, rounded to the nearest integer for display only.
    /// </summary>
    [SuppressMessage(
        "Naming",
        "CA1720:Identifier contains type name",
        Justification = "The member name mirrors the 'integer' calc format named in PRD §5; the schema name is the contract.")]
    [JsonStringEnumMemberName("integer")]
    Integer,

    /// <summary>
    /// A monetary amount: two decimal places in the respondent's culture, with no currency symbol,
    /// matching how a <see cref="Definitions.NodeType.Currency"/> input reads.
    /// </summary>
    [JsonStringEnumMemberName("currency")]
    Currency,

    /// <summary>
    /// A calendar date, formatted in the respondent's culture.
    /// </summary>
    [JsonStringEnumMemberName("date")]
    Date,
}
