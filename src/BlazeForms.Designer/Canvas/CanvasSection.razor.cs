using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using BlazeForms.Definitions;
using BlazeForms.Designer;
using BlazeForms.Internal;
using BlazeForms.Resources;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Localization;

namespace BlazeForms.Canvas;

/// <summary>
/// The WAI-ARIA <c>role="group"</c> wrapper <see cref="DesignerCanvas"/> renders around one
/// <see cref="Section"/>'s node rows -- see <see cref="DesignerCanvas"/>'s own remarks for the
/// grouped-listbox pattern this and <see cref="CanvasNodeRow"/> together form (PRD §4.1, §11).
/// </summary>
/// <remarks>
/// <b>Focus fallback (WCAG 2.4.3).</b> <see cref="RequestFocus"/> is a one-shot signal from
/// <see cref="DesignerCanvas"/>, the exact same contract <see cref="CanvasNodeRow.RequestFocus"/>
/// gives its own row: set for exactly the render where this section's own group element -- not
/// one of its rows -- is where real DOM focus should land. That happens only for an undo or redo
/// (<see cref="DesignerFocusIntent.Restored"/>) whose restored selection anchors this section but
/// names no node, because the section it restores to has no rows of its own to focus instead
/// (<see cref="DesignerCanvas"/>'s own <c>OnEditContextStateChanged</c> is what decides this).
/// This group carries its own <c>tabindex="-1"</c> so it is a genuine focus target despite never
/// being a Tab stop of its own -- the same "programmatically focusable, never Tab-reachable"
/// contract <see cref="Preview.PreviewPane"/>'s own heading, and <see cref="DesignerCanvas"/>'s
/// own root, give themselves for the same reason.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed partial class CanvasSection : ComponentBase
{
    private readonly string _headingId = "bf-canvas-section-" + Guid.NewGuid().ToString("n");
    private ElementReference _element;
    private bool _focusOnNextRender;

    /// <summary>
    /// The section this wrapper groups.
    /// </summary>
    [Parameter, EditorRequired]
    public FormSection Section { get; set; } = default!;

    /// <summary>
    /// The section's node rows, rendered by <see cref="DesignerCanvas"/>. <see langword="null"/>
    /// renders an empty (but still grouped and labelled) section. The wrapping
    /// <c>div.bf-canvas-section__rows</c> carries <c>role="presentation"</c> so it drops out of
    /// the accessibility tree entirely -- without it, each row's <c>role="option"</c> would sit
    /// inside a generic, unnamed <c>div</c> rather than being owned directly by this section's own
    /// <c>role="group"</c>, breaking the listbox → group → option ownership chain WAI-ARIA's
    /// grouped-listbox pattern requires (an axe <c>aria-required-parent</c> violation).
    /// </summary>
    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    /// <summary>
    /// A one-shot signal from <see cref="DesignerCanvas"/> that this render is the reason real DOM
    /// focus should land on this section's own group element rather than one of its rows -- see
    /// this type's own remarks.
    /// </summary>
    [Parameter]
    public bool RequestFocus { get; set; }

    /// <summary>
    /// Raised on a native <c>drop</c> landing anywhere in this section's rows wrapper that a row's
    /// own <see cref="CanvasNodeRow.OnDropped"/> did not already claim and stop from propagating
    /// -- an empty section, or the space below its last row. <see cref="DesignerCanvas"/> is the
    /// only intended subscriber; it appends the dragged node to the end of <see cref="Section"/>,
    /// the drag-and-drop path's fallback target when there is no specific row to drop before
    /// (PRD §4.1).
    /// </summary>
    [Parameter]
    public EventCallback<DragEventArgs> OnDropped { get; set; }

    private string SectionTitle => Section.Title ?? Localizer["UntitledSectionName"].Value;

    private static IStringLocalizer<DesignerStrings> Localizer => DesignerLocalization.Shared;

    /// <inheritdoc/>
    protected override void OnParametersSet()
    {
        if (RequestFocus)
        {
            _focusOnNextRender = true;
        }
    }

    /// <inheritdoc/>
    [SuppressMessage(
        "Reliability",
        "CA2007:Consider calling ConfigureAwait on the awaited task",
        Justification = "A Blazor lifecycle method must resume on the renderer's synchronization context, not a captured-context-free one, so it can safely schedule the next render.")]
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_focusOnNextRender)
        {
            _focusOnNextRender = false;
            await _element.FocusAsync();
        }
    }
}
