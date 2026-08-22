using System.ComponentModel;

namespace BlazeForms.Designer;

/// <summary>
/// One plain-language announcement a <see cref="DesignerEditContext"/> mutation raises for the
/// aria-live region to speak (PRD §4.1, §11 -- e.g. "Moved to position 3 of 5 in
/// 'Transportation'."). Exactly one of these is raised per mutation, including undo and redo.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed record DesignerAnnouncement
{
    /// <summary>
    /// The localized, plain-language text to announce. Never the raw mutation name or a node's
    /// machine-generated identifier -- always something a screen reader user would understand on
    /// its own.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// How urgently the live region should interrupt to speak this. Every mutation in this phase
    /// announces politely (PRD §11); the default is what a later phase would have to opt out of
    /// to raise something more urgent.
    /// </summary>
    public AriaLivePoliteness Politeness { get; init; } = AriaLivePoliteness.Polite;
}

/// <summary>
/// The <c>aria-live</c> politeness levels an <see cref="AriaLiveRegion"/> can render.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public enum AriaLivePoliteness
{
    /// <summary>
    /// Announced once the screen reader is idle -- <c>aria-live="polite"</c>. Every mutation
    /// announcement in this phase uses this level.
    /// </summary>
    Polite,

    /// <summary>
    /// Announced immediately, interrupting whatever the screen reader is currently saying --
    /// <c>aria-live="assertive"</c>. Reserved for a later phase's more urgent announcements (e.g.
    /// a blocking delete-reference warning); nothing in this phase raises one.
    /// </summary>
    Assertive,
}
