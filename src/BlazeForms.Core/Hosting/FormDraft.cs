using System.Collections.ObjectModel;
using System.Text.Json;

namespace BlazeForms.Hosting;

/// <summary>
/// Identifies one respondent's in-progress fill of one definition version (PRD §9).
/// </summary>
/// <param name="FormId">
/// The identifier of the form being filled.
/// </param>
/// <param name="DefinitionVersion">
/// The definition version the fill started on. A draft is pinned to it and completes against it
/// even if a newer version publishes mid-fill (PRD §4.2, D13).
/// </param>
/// <param name="RespondentKey">
/// An opaque host-supplied key identifying the respondent. BlazeForms never interprets it.
/// </param>
public sealed record FormDraftKey(string FormId, int DefinitionVersion, string RespondentKey);

/// <summary>
/// A saved in-progress fill, so a returning respondent resumes where they left off (PRD §4.2).
/// </summary>
public sealed record FormDraft
{
    private readonly IReadOnlyDictionary<string, JsonElement>? _values;

    /// <summary>
    /// What this draft belongs to.
    /// </summary>
    public required FormDraftKey Key { get; init; }

    /// <summary>
    /// When the respondent first opened the form.
    /// </summary>
    public required DateTimeOffset StartedAt { get; init; }

    /// <summary>
    /// When the draft was last autosaved.
    /// </summary>
    public required DateTimeOffset UpdatedAt { get; init; }

    /// <summary>
    /// The answers captured so far, keyed by node ID.
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
    /// The zero-based index of the page the respondent was last on, so the renderer can resume
    /// there.
    /// </summary>
    public int CurrentPageIndex { get; init; }
}
