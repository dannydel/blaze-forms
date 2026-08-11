using System.Diagnostics.CodeAnalysis;
using BlazeForms.Definitions;
using BlazeForms.Designer;
using BlazeForms.Internal;
using BlazeForms.Linting;
using BlazeForms.Markdown;
using BlazeForms.Resources;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Localization;

namespace BlazeForms.Canvas;

/// <summary>
/// One node's row on the canvas (PRD §4.1): its label (or a localized "Untitled {type}"
/// fallback), a type chip, the required and half-width flags, its help text rendered through the
/// safe-Markdown pipeline, a logic-summary chip when it carries a visibility rule, and its own
/// inline lint findings via <see cref="InlineLintMarker"/> (PRD §8). <see cref="DesignerCanvas"/> is this row's only intended
/// host — it owns the roving <c>tabindex</c> and DOM focus for every row it renders
/// (<see cref="IsActive"/>, <see cref="RequestFocus"/>), and this row raises
/// <see cref="OnActivate"/> rather than selecting itself, so a click and the canvas's own
/// Enter-key handling land on exactly the same path.
/// </summary>
/// <remarks>
/// <para>
/// <b>Accessibility.</b> Renders <c>role="option"</c> with <c>aria-selected</c> reflecting
/// <see cref="IsSelected"/> — the <em>committed</em> <c>DesignerEditContext.Selection</c>, per
/// PRD §4.1/§11's split between focus and selection — and a roving <c>tabindex</c> (<c>0</c>
/// when active, <c>-1</c> otherwise) that instead reflects <see cref="IsActive"/>, the roving
/// cursor. The two are independent: ↑/↓/Home/End move <see cref="IsActive"/> (and real DOM
/// focus) without ever touching <see cref="IsSelected"/>, so a row the cursor is merely passing
/// over never announces itself as selected. This is the WAI-ARIA grouped-listbox pattern, with
/// <see cref="CanvasSection"/> as the <c>role="group"</c> and <see cref="DesignerCanvas"/>'s own
/// root as the <c>role="listbox"</c>. A click always moves both the roving cursor and commits
/// <c>DesignerEditContext.Selection</c> to this row, in one step; the native focus a mouse click
/// already gives a <c>tabindex</c>-bearing element means no extra
/// <see cref="ElementReferenceExtensions.FocusAsync(ElementReference)"/> call is needed for that
/// path — only keyboard-driven moves (<see cref="RequestFocus"/>) call it explicitly.
/// </para>
/// <para>
/// <b>Drag-and-drop (PRD §4.1's third reorder path).</b> This row is <c>draggable="true"</c>
/// and raises <see cref="OnDragStart"/>, <see cref="OnDropped"/>, and <see cref="OnDragEnd"/> for
/// <see cref="DesignerCanvas"/> to translate into the exact same
/// <see cref="DesignerEditContext.MoveNodeAcrossSections"/> call the keyboard paths use -- this
/// row carries no move logic of its own, purely the three native browser events. The
/// <c>dragover</c> default action is suppressed unconditionally (a drop target must call
/// <c>preventDefault</c> on it or the browser never fires <c>drop</c> at all), and
/// <see cref="OnDropped"/> stops the native <c>drop</c> event from also bubbling into
/// <see cref="CanvasSection"/>'s own drop handler -- a drop that already landed on a specific row
/// must never additionally trigger that section's "append to the end" fallback. Dragging is
/// pointer sugar only; the keyboard paths (Alt+arrows, the Ctrl+M dialog) remain the accessible
/// contract, so this row does not attempt an <c>aria-grabbed</c>-style announcement of drag state
/// -- a deprecated ARIA 1.1 attribute with no assistive-technology benefit here.
/// </para>
/// <para>
/// <b>Render discipline.</b> <see cref="ShouldRender"/> compares <see cref="Node"/> and
/// <see cref="LintFindings"/> by reference (every mutation rebuilds the definition tree
/// immutably, and <see cref="DesignerCanvas"/> itself only ever hands an unaffected node's row
/// back the exact same findings list instance it held before — AGENTS.md invariant #3) alongside
/// <see cref="IsActive"/>, <see cref="IsSelected"/>, and <see cref="RequestFocus"/>, so editing or
/// adding one row, or a lint pass that changes some other node's findings, never re-renders this
/// row's own siblings.
/// </para>
/// </remarks>
public partial class CanvasNodeRow : ComponentBase
{
    private ElementReference _element;
    private FormNode? _previousNode;
    private bool _previousIsActive;
    private bool _previousIsSelected;
    private string? _previousLogicSummary;
    private IReadOnlyList<LintResult> _previousLintFindings = [];
    private bool _focusOnNextRender;

    /// <summary>
    /// The node this row shows.
    /// </summary>
    [Parameter, EditorRequired]
    public FormNode Node { get; set; } = default!;

    /// <summary>
    /// Whether this row currently holds the canvas's roving cursor — <see langword="true"/> for
    /// exactly one row at a time, which is the only one carrying <c>tabindex="0"</c>; every other
    /// row carries <c>tabindex="-1"</c> (PRD §11). Independent of <see cref="IsSelected"/>: the
    /// cursor can sit on a row that is not the committed selection.
    /// </summary>
    [Parameter]
    public bool IsActive { get; set; }

    /// <summary>
    /// Whether this row is the <em>committed</em> <c>DesignerEditContext.Selection</c> — the one
    /// Enter or a click landed on, and the one a later phase's properties panel reads from. Drives
    /// <c>aria-selected</c> and this row's visible selected highlight; unlike <see cref="IsActive"/>,
    /// this never changes just because ↑/↓/Home/End moved the roving cursor across this row.
    /// </summary>
    [Parameter]
    public bool IsSelected { get; set; }

    /// <summary>
    /// A one-shot signal from <see cref="DesignerCanvas"/> that this render is the reason real
    /// DOM focus should land here — a keyboard move (↑/↓/Home/End) or a mutation that selects a
    /// new row (e.g. a palette add). <see cref="DesignerCanvas"/> only ever sets this
    /// <see langword="true"/> for the one render that should move focus, never a mouse click
    /// (which already carries native focus of its own).
    /// </summary>
    [Parameter]
    public bool RequestFocus { get; set; }

    /// <summary>
    /// The plain-language description of <see cref="Node"/>'s own <see cref="FormNode.VisibleWhen"/>
    /// (e.g. "Shown when Status is 'Active'."), resolved by <see cref="DesignerCanvas"/> from the
    /// whole definition it already has in scope -- this row shows whatever it is given verbatim,
    /// never resolving another node's label itself. <see langword="null"/> falls back to the
    /// generic <c>RowVisibilityRuleChip</c> text, which a test that renders this row in isolation
    /// (with no reason to exercise the real summary) relies on.
    /// </summary>
    [Parameter]
    public string? LogicSummary { get; set; }

    /// <summary>
    /// This node's own current lint findings (PRD §8), resolved by <see cref="DesignerCanvas"/>
    /// from the same debounced lint pass the linter dock runs -- this row renders whatever it is
    /// given verbatim via <see cref="InlineLintMarker"/>, never running the linter itself. Defaults
    /// to empty so a test that renders this row in isolation (with no reason to exercise lint
    /// findings) needs no explicit value.
    /// </summary>
    [Parameter]
    public IReadOnlyList<LintResult> LintFindings { get; set; } = [];

    /// <summary>
    /// Raised when this row is clicked. <see cref="DesignerCanvas"/> is the only intended
    /// subscriber; it both moves the roving cursor and commits the selection.
    /// </summary>
    [Parameter, EditorRequired]
    public EventCallback OnActivate { get; set; }

    /// <summary>
    /// Raised on the native <c>dragend</c> event -- fires whether the drag ended in an actual drop
    /// or was cancelled (<c>Esc</c>, or released outside any drop target), and always after
    /// whichever <see cref="OnDropped"/>/<see cref="CanvasSection.OnDropped"/> call a successful
    /// drop already made, if any. <see cref="DesignerCanvas"/> is the only intended subscriber; it
    /// resets whichever node <see cref="OnDragStart"/> most recently recorded, so a cancelled drag
    /// can never leak into a later, unrelated drop -- see <see cref="DesignerCanvas"/>'s own
    /// remarks on why that reset cannot instead live inside <see cref="OnDropped"/> alone.
    /// </summary>
    [Parameter]
    public EventCallback OnDragEnd { get; set; }

    /// <summary>
    /// Raised on the native <c>dragstart</c> event -- the drag-and-drop reorder path's origin
    /// (PRD §4.1). <see cref="DesignerCanvas"/> is the only intended subscriber; it records which
    /// node is being dragged so a later <see cref="OnDropped"/> elsewhere on the canvas knows what
    /// to move.
    /// </summary>
    [Parameter]
    public EventCallback<DragEventArgs> OnDragStart { get; set; }

    /// <summary>
    /// Raised on the native <c>drop</c> event landing on this row -- the drag-and-drop reorder
    /// path's destination (PRD §4.1). <see cref="DesignerCanvas"/> is the only intended
    /// subscriber; it translates this into the same
    /// <see cref="DesignerEditContext.MoveNodeAcrossSections"/> call the keyboard paths use,
    /// inserting the dragged node immediately before this row.
    /// </summary>
    [Parameter]
    public EventCallback<DragEventArgs> OnDropped { get; set; }

    private static IStringLocalizer<DesignerStrings> Localizer => DesignerLocalization.Shared;

    private string TypeLabel => Localizer[$"NodeType{Node.Type}"].Value;

    private string RowLabel => Node.Label ?? Localizer["UntitledNodeLabel", TypeLabel].Value;

    private bool HasHelp => !string.IsNullOrWhiteSpace(Node.Help);

    private MarkupString HelpMarkup => new(SafeMarkdown.ToHtml(Node.Help).Value);

    /// <inheritdoc/>
    protected override void OnInitialized()
    {
        // Seeds the baseline ShouldRender compares against from the very first render's own
        // parameters, before Blazor has ever actually invoked ShouldRender at all (it never does
        // for a component's inaugural render). Without this, the baseline fields default to
        // null/false, so whatever render happens to be the first one ShouldRender is actually
        // asked about -- not necessarily one where anything about this row changed -- would read
        // as "changed" purely from comparing against that default rather than this row's own
        // last-rendered state. One such spurious ask is entirely ordinary here: the row Blazor
        // just dispatched a click to is asked to re-render on its own account once that handler
        // returns, independent of whatever DesignerCanvas itself goes on to do next.
        _previousNode = Node;
        _previousIsActive = IsActive;
        _previousIsSelected = IsSelected;
        _previousLogicSummary = LogicSummary;
        _previousLintFindings = LintFindings;
    }

    /// <inheritdoc/>
    protected override bool ShouldRender()
    {
        var changed = !ReferenceEquals(_previousNode, Node)
            || _previousIsActive != IsActive
            || _previousIsSelected != IsSelected
            || !string.Equals(_previousLogicSummary, LogicSummary, StringComparison.Ordinal)
            || !ReferenceEquals(_previousLintFindings, LintFindings)
            || RequestFocus;
        _previousNode = Node;
        _previousIsActive = IsActive;
        _previousIsSelected = IsSelected;
        _previousLogicSummary = LogicSummary;
        _previousLintFindings = LintFindings;
        return changed;
    }

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

    private Task Activate() => OnActivate.InvokeAsync();
}
