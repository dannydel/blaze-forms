namespace BlazeForms.Versioning;

/// <summary>
/// A row of version history: note, author, date, and submission count per version (PRD §7), and
/// the status badge the library surface shows (PRD §4.4). Carries no definition content, so a
/// store can list versions without loading them.
/// </summary>
public sealed record FormVersionSummary
{
    /// <summary>
    /// The identifier of the form this version belongs to.
    /// </summary>
    public required string FormId { get; init; }

    /// <summary>
    /// The form's display name as of this version.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// The published version number, or <see cref="FormLifecycle.UnpublishedVersion"/> for a
    /// draft.
    /// </summary>
    public required int Version { get; init; }

    /// <summary>
    /// Where this version sits in the lifecycle.
    /// </summary>
    public required FormLifecycleState State { get; init; }

    /// <summary>
    /// The note the author supplied at publish time.
    /// </summary>
    public string? ChangeNote { get; init; }

    /// <summary>
    /// An opaque host-supplied key for the author who published this version.
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

    /// <summary>
    /// How many submissions were captured against this version. Hosts that do not track
    /// submissions report zero.
    /// </summary>
    public int SubmissionCount { get; init; }
}
