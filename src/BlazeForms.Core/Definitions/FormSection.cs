namespace BlazeForms.Definitions;

/// <summary>
/// A group of nodes within a page. The renderer projects a section onto a
/// <c>fieldset</c>/<c>legend</c> pair (PRD §4.2).
/// </summary>
public sealed record FormSection
{
    private readonly IReadOnlyList<FormNode>? _nodes;

    /// <summary>
    /// The machine-generated, immutable identifier for this section.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// The section title, rendered as the group's legend. Plain text always.
    /// </summary>
    public string? Title { get; init; }

    /// <summary>
    /// Supporting text for the section. Plain text always.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// The section's nodes, in the order the respondent meets them. Reads as empty when a
    /// document omits it.
    /// </summary>
    public IReadOnlyList<FormNode> Nodes
    {
        get => _nodes ?? [];
        init => _nodes = value is null ? null : Array.AsReadOnly<FormNode>([.. value]);
    }
}
