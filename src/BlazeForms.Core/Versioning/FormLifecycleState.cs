using System.Text.Json.Serialization;

namespace BlazeForms.Versioning;

/// <summary>
/// Where a form version sits in the lifecycle: Draft → Published v1..vN → Retired (PRD §7).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<FormLifecycleState>))]
public enum FormLifecycleState
{
    /// <summary>
    /// Editable and not yet published. Accumulates the edits that will become the next version.
    /// </summary>
    [JsonStringEnumMemberName("draft")]
    Draft,

    /// <summary>
    /// Published and immutable forever. Accepts new fills and renders existing submissions.
    /// </summary>
    [JsonStringEnumMemberName("published")]
    Published,

    /// <summary>
    /// Withdrawn from new fills. Existing submissions stay renderable; there is no unpublish and
    /// no rollback in place.
    /// </summary>
    [JsonStringEnumMemberName("retired")]
    Retired,
}
