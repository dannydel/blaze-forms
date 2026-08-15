using System.Text.Json.Serialization;

namespace BlazeForms.Expressions;

/// <summary>
/// The functions a <see cref="CalcOperand"/> can stand for instead of a field reference or a
/// literal (PRD §13). The JSON name of each member is part of the schema contract.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<CalcFunction>))]
public enum CalcFunction
{
    /// <summary>
    /// The date the form is being filled, supplied to the evaluator by the caller — never read
    /// from a clock inside Core, so evaluation stays pure and deterministic.
    /// </summary>
    [JsonStringEnumMemberName("today")]
    Today,
}
