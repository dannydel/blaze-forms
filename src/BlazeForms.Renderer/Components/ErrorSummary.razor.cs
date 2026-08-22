using System.ComponentModel;
using Microsoft.AspNetCore.Components;

namespace BlazeForms.Components;

/// <summary>
/// The focusable, <c>role="alert"</c> validation summary <see cref="FormRenderer"/> shows after a
/// failed page-advance or submit (PRD §4.2, §11): a heading plus one anchor per offending field,
/// in document order, each pointing at the field's namespaced DOM id. Renders nothing when
/// <see cref="Entries"/> is empty, so a caller can include it unconditionally in its markup.
/// </summary>
/// <remarks>
/// <para>
/// Renderer chrome, not a documented host extension point — a host never places this component
/// itself. It is a <c>public partial class</c> only because every Razor-file-backed type the SDK
/// generates is public; there is no directive to make the generated half of a component's partial
/// class <c>internal</c> while the code-behind half stays consistent (unlike
/// <c>Fields/DefaultFieldComponents.cs</c>'s resolver, a plain C# class with no such constraint).
/// </para>
/// <para>
/// Clicking an entry's anchor moves focus to the offending field through the browser's native
/// fragment-navigation focus behavior (the indicated part of the document receives focus when it
/// is focusable, per the HTML specification) — no JS and no explicit focus call are needed for
/// that half of the a11y model. <see cref="FocusAsync"/> covers the other half: moving focus to
/// the summary itself the moment it appears, since nothing about a validation failure otherwise
/// changes where keyboard focus sits.
/// </para>
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed partial class ErrorSummary : ComponentBase
{
    private ElementReference _rootElement;

    /// <summary>
    /// The entries to list, in the order they should appear — document order, by convention of
    /// every caller in this assembly.
    /// </summary>
    [Parameter]
    public IReadOnlyList<ErrorSummaryEntry> Entries { get; set; } = [];

    /// <summary>
    /// The localized heading shown above the list.
    /// </summary>
    [Parameter]
    public string HeadingText { get; set; } = "";

    /// <summary>
    /// Moves focus to the summary's root element. A no-op if the summary is not currently
    /// rendered (an empty <see cref="Entries"/>), since there is then no element to focus.
    /// </summary>
    internal ValueTask FocusAsync() => Entries.Count > 0 ? _rootElement.FocusAsync() : ValueTask.CompletedTask;
}
