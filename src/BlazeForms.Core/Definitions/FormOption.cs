namespace BlazeForms.Definitions;

/// <summary>
/// One choice offered by a choice node. <see cref="Value"/> is what submissions store and what
/// visibility rules compare against, so it stays stable when <see cref="Label"/> is edited.
/// </summary>
public sealed record FormOption
{
    /// <summary>
    /// The stable stored value. Never re-derived from <see cref="Label"/>.
    /// </summary>
    public required string Value { get; init; }

    /// <summary>
    /// The text shown to the respondent. Plain text always (PRD §5.1).
    /// </summary>
    public required string Label { get; init; }
}
