using System.Text.Json.Serialization;

namespace BlazeForms.Expressions;

/// <summary>
/// How the conditions of a <see cref="ConditionGroup"/> combine (PRD §6). The JSON name of each
/// member is part of the schema contract.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ConditionJoin>))]
public enum ConditionJoin
{
    /// <summary>
    /// Every condition must hold. An empty group is vacuously satisfied.
    /// </summary>
    [JsonStringEnumMemberName("all")]
    All,

    /// <summary>
    /// At least one condition must hold. An empty group is never satisfied.
    /// </summary>
    [JsonStringEnumMemberName("any")]
    Any,
}
