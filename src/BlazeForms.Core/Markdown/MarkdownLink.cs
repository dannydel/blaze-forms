using System.Diagnostics.CodeAnalysis;

namespace BlazeForms.Markdown;

/// <summary>
/// A link found in an author's Markdown, surfaced so the linter can judge whether its text
/// describes its destination (A11Y-09, PRD §8). Carries the link as authored — no HTML, no
/// Markdig type — so it is safe on the public surface (AGENTS.md invariant #1).
/// </summary>
public sealed record MarkdownLink
{
    /// <summary>
    /// The link's visible text, formed by concatenating the literal text spans inside the link.
    /// </summary>
    public required string Text { get; init; }

    /// <summary>
    /// The destination, exactly as the author wrote it.
    /// </summary>
    [SuppressMessage(
        "Design",
        "CA1056:URI properties should not be strings",
        Justification = "The link is untrusted author input carried verbatim for the linter to judge — it may be relative, malformed, or carry a disallowed scheme, none of which System.Uri round-trips.")]
    public required string Url { get; init; }

    /// <summary>
    /// Whether the destination is an external <c>http</c> or <c>https</c> address, as opposed to a
    /// <c>mailto</c> or relative link.
    /// </summary>
    public required bool IsExternal { get; init; }
}
