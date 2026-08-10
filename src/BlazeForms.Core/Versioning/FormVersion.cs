using BlazeForms.Definitions;

namespace BlazeForms.Versioning;

/// <summary>
/// One version of a form: its definition plus where it sits in the lifecycle. A published
/// version is immutable forever — edits accumulate on a new draft, and "restoring" an old
/// version means publishing its content again as the next version (PRD §7, AGENTS.md invariant
/// #3).
/// </summary>
public sealed record FormVersion
{
    /// <summary>
    /// The identifier of the form this version belongs to. Matches
    /// <see cref="FormDefinition.Id"/>.
    /// </summary>
    public required string FormId { get; init; }

    /// <summary>
    /// The published version number, from 1 upward, or
    /// <see cref="FormLifecycle.UnpublishedVersion"/> while this is still a draft.
    /// </summary>
    public required int Version { get; init; }

    /// <summary>
    /// Where this version sits in the lifecycle.
    /// </summary>
    public required FormLifecycleState State { get; init; }

    /// <summary>
    /// The definition content of this version.
    /// </summary>
    public required FormDefinition Definition { get; init; }

    /// <summary>
    /// The note the author supplied at publish time, kept in version history (PRD §7).
    /// <see langword="null"/> for a draft.
    /// </summary>
    public string? ChangeNote { get; init; }

    /// <summary>
    /// An opaque host-supplied key for the author who published this version.
    /// <see langword="null"/> for a draft.
    /// </summary>
    public string? Author { get; init; }

    /// <summary>
    /// When this version was published, or <see langword="null"/> for a draft.
    /// </summary>
    public DateTimeOffset? PublishedAt { get; init; }

    /// <summary>
    /// When this version was retired, or <see langword="null"/> while it still accepts fills.
    /// </summary>
    public DateTimeOffset? RetiredAt { get; init; }
}
