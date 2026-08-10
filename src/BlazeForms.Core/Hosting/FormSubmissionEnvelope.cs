using System.Collections.ObjectModel;
using System.Text.Json;

namespace BlazeForms.Hosting;

/// <summary>
/// What a completed fill hands to <see cref="IFormSubmissionSink"/> (PRD §9). The host owns
/// everything after this point.
/// </summary>
public sealed record FormSubmissionEnvelope
{
    private readonly IReadOnlyDictionary<string, JsonElement>? _values;

    /// <summary>
    /// The machine-generated identifier of this submission. Generate one with
    /// <see cref="Definitions.FormIds.NewSubmissionId"/>.
    /// </summary>
    public required string SubmissionId { get; init; }

    /// <summary>
    /// The identifier of the form that was filled.
    /// </summary>
    public required string FormId { get; init; }

    /// <summary>
    /// The definition version this submission was captured against. The submission renders
    /// against this exact version forever (PRD §7).
    /// </summary>
    public required int DefinitionVersion { get; init; }

    /// <summary>
    /// When the respondent started filling.
    /// </summary>
    public required DateTimeOffset StartedAt { get; init; }

    /// <summary>
    /// When the respondent submitted.
    /// </summary>
    public required DateTimeOffset SubmittedAt { get; init; }

    /// <summary>
    /// The answers, keyed by node ID. Answers to fields hidden by logic are <em>absent</em>, not
    /// null (PRD §9) — see
    /// <see cref="Expressions.VisibilityEvaluator.FilterToVisible"/> and
    /// <see cref="Serialization.FormValues.ToJsonValues"/>.
    /// </summary>
    /// <value>
    /// A read-only copy of whatever was assigned. Note that a <see cref="JsonElement"/> is only
    /// valid while the <see cref="JsonDocument"/> that produced it is alive, so hosts assigning
    /// their own elements must either keep that document undisposed or hand over detached clones;
    /// <see cref="Serialization.FormValues.ToJsonValues"/> is the safe path and always clones.
    /// </value>
    public IReadOnlyDictionary<string, JsonElement> Values
    {
        get => _values ?? ReadOnlyDictionary<string, JsonElement>.Empty;
        init => _values = value is null
            ? null
            : new ReadOnlyDictionary<string, JsonElement>(new Dictionary<string, JsonElement>(value, StringComparer.Ordinal));
    }

    /// <summary>
    /// An opaque host-supplied key identifying the respondent, or <see langword="null"/> for an
    /// anonymous fill. BlazeForms never interprets it.
    /// </summary>
    public string? RespondentKey { get; init; }
}
