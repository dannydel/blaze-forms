namespace BlazeForms.Definitions;

/// <summary>
/// One page of a form. The renderer presents pages as steps with a progress header, and
/// validates a whole page on advance (PRD §4.2).
/// </summary>
public sealed record FormPage
{
    private readonly IReadOnlyList<FormSection>? _sections;

    /// <summary>
    /// The machine-generated, immutable identifier for this page.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// The page title, shown in the designer tab strip and the renderer's progress header.
    /// Plain text always.
    /// </summary>
    public string? Title { get; init; }

    /// <summary>
    /// The page's sections, in order. Reads as empty when a document omits it.
    /// </summary>
    public IReadOnlyList<FormSection> Sections
    {
        get => _sections ?? [];
        init => _sections = value is null ? null : Array.AsReadOnly<FormSection>([.. value]);
    }
}
