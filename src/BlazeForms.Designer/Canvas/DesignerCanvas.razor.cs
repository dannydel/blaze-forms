using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using BlazeForms.Definitions;
using BlazeForms.Delete;
using BlazeForms.Designer;
using BlazeForms.Designer.Internal;
using BlazeForms.Expressions;
using BlazeForms.Internal;
using BlazeForms.Linting;
using BlazeForms.Resources;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Localization;
using Microsoft.JSInterop;

namespace BlazeForms.Canvas;

/// <summary>
/// The single-tab-stop, roving-focus canvas (PRD §4.1, §11): renders <see cref="ActivePageId"/>'s
/// sections as <see cref="CanvasSection"/> groups and their nodes as <see cref="CanvasNodeRow"/>
/// options, and owns the one roving cursor among them -- exactly one row carries
/// <c>tabindex="0"</c> at a time, every other row carries <c>tabindex="-1"</c>, and the canvas
/// itself is never a Tab stop of its own. <c>↑</c>/<c>↓</c> move the cursor by one row;
/// <c>Home</c>/<c>End</c> jump to the first or last row on the current page; <c>Enter</c> commits
/// the cursor's row as <see cref="DesignerEditContext.Selection"/> via
/// <see cref="DesignerEditContext.Select"/> -- the hook Phase 4's properties panel will read from.
/// A click does the same commit as Enter, in one step.
/// </summary>
/// <remarks>
/// <para>
/// <b>Focus after a mutation.</b> Every mutation method on <see cref="EditContext"/> moves
/// <see cref="DesignerEditContext.Selection"/> to the node, section, or page it affected, tagged
/// with a <see cref="DesignerFocusIntent"/> other than <see cref="DesignerFocusIntent.None"/> when
/// that move should carry real DOM focus with it (PRD §11) -- a palette add landing on its new
/// row is this phase's example, even though the delete/reorder mutations that also tag a
/// non-<see cref="DesignerFocusIntent.None"/> intent do not have a keyboard path of their own
/// until a later phase. <see cref="OnEditContextStateChanged"/> reads exactly that signal: when
/// the mutation's selection lands on a node on the page this canvas is currently showing, and its
/// intent is not <see cref="DesignerFocusIntent.None"/>, the roving cursor moves there and
/// <see cref="CanvasNodeRow.RequestFocus"/> asks that one row to call
/// <see cref="ElementReferenceExtensions.FocusAsync(ElementReference)"/> in its own <c>OnAfterRenderAsync</c> -- mirroring the
/// <c>_focusXOnNextRender</c> flag pattern <c>FormRenderer.razor.cs</c> uses for its own
/// post-mutation focus moves, just distributed one flag per row instead of a handful of fields on
/// one component. That signal is one-shot: <see cref="OnAfterRender"/> clears it the moment this
/// canvas's own render has gone out, so an unrelated later render never re-steals focus.
/// </para>
/// <para>
/// <b>Keyboard.</b> <see cref="OnKeyDown"/> is bound once, on this canvas's own root element, and
/// relies on <c>keydown</c> bubbling up from whichever row currently holds focus -- the standard
/// roving-tabindex shape. It deliberately never calls <c>@onkeydown:preventDefault</c>: that
/// modifier applies to every <c>keydown</c> this element receives, including <c>Tab</c>, and
/// Blazor has no way to decide it conditionally per key value once the event has already fired --
/// so binding it at all here would risk trapping <c>Tab</c> the moment it bubbled through
/// alongside ↑/↓/Home/End/Enter. That still leaves <c>ArrowUp</c>/<c>ArrowDown</c>/<c>Home</c>/
/// <c>End</c> free to also trigger the browser's own default scroll action on top of
/// <see cref="ElementReferenceExtensions.FocusAsync(ElementReference)"/>'s own scroll-into-view --
/// a visible double-jump -- and <c>Ctrl+D</c>/<c>Ctrl+Z</c>/<c>Ctrl+Shift+Z</c> free to trigger a
/// browser's own bookmark-this-page or document-level undo/redo shortcut instead of this canvas's
/// own duplicate/undo/redo -- so the collocated <c>DesignerCanvas.razor.js</c> module attaches a
/// second, genuinely JS-side <c>keydown</c> listener to this same root element that calls only
/// <c>preventDefault()</c> for those seven keys/combinations (never <c>Tab</c>, never anything
/// else, and never <c>stopPropagation()</c>) -- the one platform gap Blazor's own event model
/// cannot close, since <c>preventDefault</c> must run before <c>OnKeyDown</c>'s dispatch, not
/// after. The Blazor <c>@onkeydown</c> handler on this element still receives, and still handles,
/// every key exactly as before; the module only ever suppresses the browser's own default action
/// underneath it, and only ever on this canvas's own root element -- a text input elsewhere in the
/// shell (e.g. the properties panel) never has this module attached to it at all, so
/// <c>Ctrl+Z</c> there keeps its ordinary text-editing meaning. <c>Delete</c> has no browser
/// default worth suppressing, so it needs no entry in the module at all.
/// </para>
/// <para>
/// <b>The three reorder paths (PRD §4.1).</b> <c>Alt+↑/↓</c> (within a section),
/// <c>Alt+←/→</c> (across sections), the <c>Ctrl+M</c> <see cref="MoveToPositionDialog"/>, and
/// native drag-and-drop between <see cref="CanvasNodeRow"/>s all funnel into the exact same
/// <see cref="DesignerEditContext.MoveNodeWithinSection"/> and
/// <see cref="DesignerEditContext.MoveNodeAcrossSections"/> calls -- none of this class's own
/// input handling ever computes a reordered node list itself, only the identifiers and indices
/// those two methods (and the <see cref="MoveToPositionDialog"/> they hand off to) already need,
/// which is what keeps every path's resulting <see cref="Definitions.FormDefinition"/> identical
/// for the same logical move. <c>Alt+←/→</c>'s target index is deliberately always the end of the
/// adjacent section (see <see cref="MoveActiveNodeAcrossAdjacentSection"/>) -- a keyboard move has
/// no "drop position" of its own the way drag-and-drop or the dialog do, and appending is the
/// simplest, most predictable choice among the alternatives. <c>Alt+↑/↓</c> at either end of a
/// section, and <c>Alt+←/→</c> at either end of the page's own section list, are no-ops:
/// <see cref="DesignerEditContext.MoveNodeWithinSection"/> already guards the former, and
/// <see cref="MoveActiveNodeAcrossAdjacentSection"/> itself guards the latter before ever calling
/// <see cref="DesignerEditContext.MoveNodeAcrossSections"/>, so neither ever raises a spurious
/// announcement.
/// </para>
/// <para>
/// <b>Drag cancellation.</b> <see cref="_draggedNodeId"/> is set on <c>dragstart</c> and would
/// otherwise only be cleared by <see cref="DropOnRow"/>/<see cref="DropOnSection"/> -- but a drag
/// an author cancels (<c>Esc</c>, or releases outside any drop target) never reaches either of
/// those, so every <see cref="CanvasNodeRow"/> also raises <c>dragend</c>, which always fires
/// (after any drop that did land) and resets <see cref="_draggedNodeId"/> unconditionally via
/// <see cref="EndDrag"/>. Without it, a cancelled drag would leave that field pointing at a stale
/// node, and a later, wholly unrelated drop (an external file, selected text) landing on a row or
/// section would silently move it -- the exact "drop with nothing actually dragged is a silent
/// no-op" contract <see cref="DropOnRow"/> and <see cref="DropOnSection"/> otherwise guarantee.
/// </para>
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed partial class DesignerCanvas : ComponentBase, IAsyncDisposable
{
    /// <summary>
    /// The static web asset path this component imports its scroll-suppression JS module from,
    /// following the same <c>_content/{assembly}/{path}</c> convention every collocated Razor
    /// Class Library JS file resolves to. <c>internal</c> so <c>DesignerCanvasTests</c> can set
    /// up the module mock against the exact path this component requests.
    /// </summary>
    internal const string ModulePath = "./_content/BlazeForms.Designer/Canvas/DesignerCanvas.razor.js";

    private readonly Dictionary<string, EventCallback> _activateCallbacks = new(StringComparer.Ordinal);
    private readonly Dictionary<string, EventCallback<DragEventArgs>> _dragStartCallbacks = new(StringComparer.Ordinal);
    private readonly Dictionary<string, EventCallback> _dragEndCallbacks = new(StringComparer.Ordinal);
    private readonly Dictionary<string, EventCallback<DragEventArgs>> _rowDropCallbacks = new(StringComparer.Ordinal);
    private readonly Dictionary<string, EventCallback<DragEventArgs>> _sectionDropCallbacks = new(StringComparer.Ordinal);
    private DesignerEditContext? _subscribedContext;
    private ElementReference _canvasElement;
    private IJSObjectReference? _module;
    private IJSObjectReference? _scrollSuppressionHandle;
    private string? _scrollSuppressionElementKey;
    private string? _activeNodeId;
    private string? _pendingFocusNodeId;
    private string? _pendingFocusSectionId;
    private bool _pendingFocusCanvasRoot;
    private string? _lastSyncedActivePageId;
    private string? _draggedNodeId;
    private bool _showMoveDialog;
    private string? _moveDialogNodeId;
    private bool _showDeleteDialog;
    private string? _deleteDialogNodeId;
    private IReadOnlyList<LintResult>? _lastSyncedLintResults;
    private Dictionary<string, IReadOnlyList<LintResult>> _findingsByNode = new(StringComparer.Ordinal);
    private bool _disposed;

    /// <summary>
    /// The mutation engine this canvas reads its content, selection, and focus intent from.
    /// </summary>
    [Parameter, EditorRequired]
    public DesignerEditContext EditContext { get; set; } = default!;

    /// <summary>
    /// The page currently showing. <see langword="null"/> when the draft has no pages yet --
    /// <see cref="FormDesigner"/> (or, in a host embedding this directly, whatever owns the
    /// "which page" view state) is responsible for giving an author a way to add one; this canvas
    /// only ever reads the identifier, it never creates a page of its own.
    /// </summary>
    [Parameter]
    public string? ActivePageId { get; set; }

    /// <summary>
    /// The most recent lint pass's results (PRD §8), computed by whichever <c>LinterDock</c> a
    /// host mounts alongside this canvas and handed down through it -- this canvas never runs the
    /// linter itself, only groups these by <see cref="LintResult.NodeId"/> to give each row its
    /// own findings via <see cref="GetFindingsForNode"/>. Defaults to empty so a designer with no
    /// dock mounted (or a test with no reason to exercise lint findings) needs no explicit value.
    /// </summary>
    [Parameter]
    public IReadOnlyList<LintResult> LintResults { get; set; } = [];

    /// <summary>
    /// Used only to import <see cref="ModulePath"/>'s scroll-suppression module -- see this
    /// type's own remarks on why closing that one platform gap needs genuine JS, and
    /// <see cref="OnAfterRenderAsync"/> for where the import happens.
    /// </summary>
    [Inject]
    private IJSRuntime JSRuntime { get; set; } = default!;

    private static IStringLocalizer<DesignerStrings> Localizer => DesignerLocalization.Shared;

    private FormPage? ActivePage => ActivePageId is null
        ? null
        : EditContext.Draft.Definition.Pages.FirstOrDefault(page => string.Equals(page.Id, ActivePageId, StringComparison.Ordinal));

    /// <summary>
    /// The repeating group this canvas is currently drilled into (repeating-groups-plan.md,
    /// Increment C), or <see langword="null"/> at the top level. Derived straight from
    /// <see cref="DesignerEditContext.Selection"/>'s own <see cref="DesignerSelection.GroupId"/>
    /// rather than a field this component owns -- the single source of truth every
    /// scope-restoring undo/redo (a whole-definition memento carries the selection, GroupId
    /// included) and jump-to-node action already updates for free, with no extra bookkeeping here.
    /// Also requires <see cref="DesignerSelection.PageId"/> to still match <see cref="ActivePageId"/>
    /// -- an author switching page tabs while scoped lands back at that new page's own top level,
    /// never showing a stale scope that belongs to the page just left.
    /// </summary>
    private string? ScopeGroupId => EditContext.Selection.GroupId is { } groupId
        && string.Equals(EditContext.Selection.PageId, ActivePageId, StringComparison.Ordinal)
        ? groupId
        : null;

    /// <summary>
    /// <see cref="ScopeGroupId"/>'s own node, or <see langword="null"/> when nothing is scoped
    /// (or, defensively, a scoped group id that no longer resolves -- unreachable through this
    /// component's own affordances, since nothing ever deletes a group while its own scope is
    /// showing, but never trusted blindly).
    /// </summary>
    private FormNode? ScopeGroup => ScopeGroupId is { } groupId ? EditContext.Draft.Definition.FindNode(groupId) : null;

    private bool IsScoped => ScopeGroup is not null;

    /// <summary>
    /// The roving cursor's own current node -- a top-level row, or, while scoped, one of
    /// <see cref="ScopeGroup"/>'s own children. Resolved through <see cref="FormDefinitionExtensions.FindNode"/>
    /// directly (which already descends into every node's own <see cref="FormNode.Children"/>)
    /// rather than re-deriving which list <see cref="_activeNodeId"/> lives in here -- node ids
    /// are globally unique (AGENTS.md invariant #5), so this is correct regardless of scope.
    /// </summary>
    private FormNode? ActiveNode => _activeNodeId is null ? null : EditContext.Draft.Definition.FindNode(_activeNodeId);

    /// <inheritdoc/>
    protected override void OnParametersSet()
    {
        if (!ReferenceEquals(_subscribedContext, EditContext))
        {
            if (_subscribedContext is not null)
            {
                _subscribedContext.StateChanged -= OnEditContextStateChanged;
            }

            EditContext.StateChanged += OnEditContextStateChanged;
            _subscribedContext = EditContext;
        }

        var pageChanged = !string.Equals(_lastSyncedActivePageId, ActivePageId, StringComparison.Ordinal);

        if (pageChanged)
        {
            _lastSyncedActivePageId = ActivePageId;
            SyncActiveNodeForPageChange();
        }

        var lintResultsChanged = !ReferenceEquals(_lastSyncedLintResults, LintResults);

        if (lintResultsChanged)
        {
            _lastSyncedLintResults = LintResults;
        }

        // A page switch needs the same regroup as a fresh lint pass: GetFindingsForNode's cache
        // is keyed by the flat node list BuildFlatNodeIds returns for whichever page is active,
        // so switching pages without also rebuilding it would leave the new page's own rows
        // reading stale (or entirely missing) findings until the next lint pass happens to tick.
        if (pageChanged || lintResultsChanged)
        {
            RebuildFindingsByNode();
        }
    }

    /// <inheritdoc/>
    protected override void OnAfterRender(bool firstRender)
    {
        _pendingFocusNodeId = null;
        _pendingFocusSectionId = null;
    }

    /// <summary>
    /// Imports <see cref="ModulePath"/>'s scroll-suppression module on this canvas's first render
    /// (prerender-safe: importing costs nothing more than an unused module reference under a
    /// non-interactive prerendered pass, and every render after the circuit resumes calls this
    /// again with <paramref name="firstRender"/> still <see langword="true"/> exactly once), then
    /// attaches its listener to <see cref="_canvasElement"/> whenever <see cref="ActivePage"/> is
    /// showing a real row list and nothing is attached yet -- and detaches it the moment
    /// <see cref="ActivePage"/> goes back to <see langword="null"/> (the empty state has no
    /// <c>bf-canvas</c> element for a stale reference to keep pointing at). A page switching back
    /// and forth between "has content" and "no page selected", or -- Increment C -- into and out
    /// of a repeating group's own drill-in scope, re-attaches against whichever new DOM element
    /// <c>@ref</c> most recently captured: <c>DesignerCanvas.razor</c>'s own <c>@if</c>/
    /// <c>else if</c>/<c>else</c> tears the element down and recreates it on every one of those
    /// switches, since the scoped and unscoped branches are two different render fragments even
    /// though both happen to carry the exact same <c>@ref</c>. <see cref="_scrollSuppressionElementKey"/>
    /// is what notices that: it changes whenever <see cref="ActivePageId"/> or
    /// <see cref="ScopeGroupId"/> does, which is exactly when the rendered element is a fresh one.
    /// </summary>
    [SuppressMessage(
        "Reliability",
        "CA2007:Consider calling ConfigureAwait on the awaited task",
        Justification = "A Blazor lifecycle method must resume on the renderer's synchronization context, not a captured-context-free one, so it can safely schedule the next render.")]
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            _module = await JSRuntime.InvokeAsync<IJSObjectReference>("import", ModulePath);
        }

        // The last-resort fallback for a node-less restored selection that names no section
        // either (DesignerSelection.None, or a page-only selection) -- see
        // OnEditContextStateChanged's own remarks. This is a plain ElementReference.FocusAsync
        // call, not JS interop through _module, so it runs (and clears itself) independently of
        // whether that module ever finished importing.
        if (_pendingFocusCanvasRoot)
        {
            _pendingFocusCanvasRoot = false;

            if (ActivePage is not null)
            {
                await _canvasElement.FocusAsync();
            }
        }

        if (_module is null)
        {
            return;
        }

        var elementKey = ActivePage is null ? null : $"{ActivePageId}|{ScopeGroupId}";

        if (_scrollSuppressionHandle is not null && !string.Equals(_scrollSuppressionElementKey, elementKey, StringComparison.Ordinal))
        {
            var staleHandle = _scrollSuppressionHandle;
            _scrollSuppressionHandle = null;
            await staleHandle.InvokeVoidAsync("dispose");
            await staleHandle.DisposeAsync();
        }

        _scrollSuppressionElementKey = elementKey;

        if (elementKey is not null && _scrollSuppressionHandle is null)
        {
            _scrollSuppressionHandle = await _module.InvokeAsync<IJSObjectReference>("attachScrollSuppression", _canvasElement);
        }
    }

    /// <summary>
    /// Unsubscribes from <see cref="EditContext"/> and disposes the scroll-suppression module and
    /// its listener handle, when either was ever imported or attached. Safe to call more than
    /// once.
    /// </summary>
    [SuppressMessage(
        "Reliability",
        "CA2007:Consider calling ConfigureAwait on the awaited task",
        Justification = "Blazor disposes a component on its own renderer's synchronization context, same as every other lifecycle method in this file.")]
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_subscribedContext is not null)
        {
            _subscribedContext.StateChanged -= OnEditContextStateChanged;
            _subscribedContext = null;
        }

        if (_scrollSuppressionHandle is not null)
        {
            await _scrollSuppressionHandle.InvokeVoidAsync("dispose");
            await _scrollSuppressionHandle.DisposeAsync();
            _scrollSuppressionHandle = null;
        }

        if (_module is not null)
        {
            await _module.DisposeAsync();
            _module = null;
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Whether the scroll-suppression JS module has been imported. <c>internal</c>, not
    /// <c>private</c>, solely so <c>DesignerCanvasTests</c> can prove <see cref="DisposeAsync"/>
    /// actually disposes it -- bUnit's JS-interop mock does not itself simulate a module reference
    /// becoming unusable once disposed, so the only deterministic way to prove this component
    /// disposes it is to observe the field going back to <see langword="null"/>, the same
    /// rationale <c>BlazeForms.FormSubmissionView</c>'s own <c>HasImportedModule</c> test seam
    /// gives for itself.
    /// </summary>
    internal bool HasImportedScrollSuppressionModule => _module is not null;

    private bool IsRowActive(string nodeId) => string.Equals(_activeNodeId, nodeId, StringComparison.Ordinal);

    /// <summary>
    /// Whether <paramref name="nodeId"/> is the <em>committed</em>
    /// <see cref="DesignerEditContext.Selection"/> — read straight off <see cref="EditContext"/>
    /// on every render rather than cached, so a bare <see cref="DesignerEditContext.Select"/>
    /// (with no focus-carrying intent, and so no roving-cursor move) still updates
    /// <c>aria-selected</c>. Never <see langword="true"/> for any row while
    /// <see cref="DesignerEditContext.Selection"/> is <see cref="DesignerSelection.None"/> (PRD
    /// §4.1, §11) — unlike <see cref="IsRowActive"/>, this has no "default to the first row"
    /// fallback.
    /// </summary>
    private bool IsRowSelected(string nodeId) => string.Equals(EditContext.Selection.NodeId, nodeId, StringComparison.Ordinal);

    private bool IsFocusPending(string nodeId) => string.Equals(_pendingFocusNodeId, nodeId, StringComparison.Ordinal);

    /// <summary>
    /// Whether <paramref name="sectionId"/> is the one-shot target
    /// <see cref="OnEditContextStateChanged"/>'s node-less-restore fallback most recently named --
    /// see <see cref="CanvasSection.RequestFocus"/>.
    /// </summary>
    private bool IsSectionFocusPending(string sectionId) => string.Equals(_pendingFocusSectionId, sectionId, StringComparison.Ordinal);

    /// <summary>
    /// <see cref="ScopeGroup"/>'s own real, top-level enclosing section -- the section its own
    /// row actually lives in, as distinct from <see cref="BuildScopeSection"/>'s synthetic
    /// stand-in the scoped render feeds <see cref="CanvasSection"/>. <see langword="null"/> only
    /// when <see cref="ScopeGroup"/> itself is (defensively; see that property's own remarks).
    /// </summary>
    private FormSection? ScopeRealSection
    {
        get
        {
            if (ScopeGroup is not { } group)
            {
                return null;
            }

            var located = DefinitionMutations.FindNodeLocation(EditContext.Draft.Definition, group.Id);
            return located is { } location ? EditContext.Draft.Definition.Pages[location.PageIndex].Sections[location.SectionIndex] : null;
        }
    }

    /// <summary>
    /// Builds the synthetic <see cref="FormSection"/> the scoped render feeds
    /// <see cref="CanvasSection"/> in place of a real one (repeating-groups-plan.md, Increment C):
    /// <paramref name="realSection"/> itself, but with its own <see cref="FormSection.Title"/>
    /// replaced by the group-scope heading -- reusing <paramref name="realSection"/>'s own
    /// <see cref="FormSection.Id"/> via <c>with</c> is exactly what keeps
    /// <see cref="IsSectionFocusPending"/> matching it for free, with no scope-specific focus
    /// field of this component's own to keep in sync. Never rendered on screen itself (only
    /// <see cref="CanvasSection"/>'s own <c>aria-hidden</c> heading reads it), so
    /// <see cref="FormSection.Description"/> is cleared rather than carried over -- the real
    /// section's own description describes that section's top-level content, not this group's.
    /// </summary>
    private static FormSection BuildScopeSection(FormNode group, FormSection realSection) => realSection with
    {
        Title = Localizer["CanvasScopeHeading", NodeDisplayLabel(group)].Value,
        Description = null,
    };

    /// <summary>
    /// Leaves <see cref="ScopeGroup"/>'s own scope -- the breadcrumb back button's path, the
    /// pointer-and-keyboard-both counterpart to <c>Esc</c>'s own handling in
    /// <see cref="OnKeyDown"/>.
    /// </summary>
    private void ExitScope()
    {
        if (ScopeGroupId is { } groupId)
        {
            GroupScopeNavigation.Exit(EditContext, groupId);
        }
    }

    private static string NodeDisplayLabel(FormNode node) =>
        node.Label ?? Localizer["UntitledNodeLabel", Localizer[$"NodeType{node.Type}"].Value].Value;

    private static string SectionTitleFor(FormSection section) => section.Title ?? Localizer["UntitledSectionName"].Value;

    /// <summary>
    /// The plain-language description of <paramref name="node"/>'s own
    /// <see cref="FormNode.VisibleWhen"/> for its own row's logic-summary chip -- resolved here,
    /// from the full <see cref="EditContext"/>'s definition, rather than inside
    /// <see cref="CanvasNodeRow"/> itself, so that leaf row never needs more than its own node to
    /// render (PRD §4.1). <see langword="null"/> when the node carries no rule at all, matching
    /// <see cref="CanvasNodeRow"/>'s own chip gate.
    /// </summary>
    private string? GetLogicSummary(FormNode node) =>
        node.VisibleWhen is null ? null : VisibilitySummaryFormatter.Format(node, EditContext.Draft.Definition);

    /// <summary>
    /// This node's own current lint findings, from the last <see cref="RebuildFindingsByNode"/>
    /// pass -- always the exact same list instance as the previous render's for a node whose own
    /// findings did not change, which is what lets <see cref="CanvasNodeRow.ShouldRender"/> skip
    /// re-rendering every row a lint pass did not actually touch (PRD §8's render-discipline
    /// requirement).
    /// </summary>
    private IReadOnlyList<LintResult> GetFindingsForNode(string nodeId) =>
        _findingsByNode.TryGetValue(nodeId, out var findings) ? findings : [];

    /// <summary>
    /// Regroups <see cref="LintResults"/> by <see cref="LintResult.NodeId"/> whenever that
    /// parameter's own reference changes, reusing a node's previous findings list instance when
    /// its content is unchanged (a <see cref="LintResult"/> is a record, so a fresh lint pass over
    /// an unrelated part of the form still compares equal here) -- so <see cref="GetFindingsForNode"/>
    /// only ever hands a row a new list when that row's own findings actually differ from what it
    /// already had.
    /// </summary>
    private void RebuildFindingsByNode()
    {
        var grouped = LintResults
            .Where(result => result.NodeId is not null)
            .GroupBy(result => result.NodeId!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<LintResult>)[.. group], StringComparer.Ordinal);

        var next = new Dictionary<string, IReadOnlyList<LintResult>>(StringComparer.Ordinal);

        foreach (var nodeId in BuildFlatNodeIds())
        {
            var findings = grouped.TryGetValue(nodeId, out var nodeFindings) ? nodeFindings : [];

            next[nodeId] = _findingsByNode.TryGetValue(nodeId, out var previous) && previous.SequenceEqual(findings)
                ? previous
                : findings;
        }

        _findingsByNode = next;
    }

    /// <summary>
    /// Returns the same cached <see cref="EventCallback"/> for a given node on every call, the
    /// same reason <c>FormRenderer.GetValueChangedCallback</c> caches its own per-node callbacks.
    /// </summary>
    private EventCallback GetActivateCallback(string nodeId)
    {
        if (!_activateCallbacks.TryGetValue(nodeId, out var callback))
        {
            callback = EventCallback.Factory.Create(this, () => Activate(nodeId));
            _activateCallbacks[nodeId] = callback;
        }

        return callback;
    }

    /// <summary>
    /// Returns the same cached <see cref="EventCallback{DragEventArgs}"/> for a given node's
    /// <c>dragstart</c> on every call, the same reason <see cref="GetActivateCallback"/> caches
    /// its own per-node callbacks.
    /// </summary>
    private EventCallback<DragEventArgs> GetDragStartCallback(string nodeId)
    {
        if (!_dragStartCallbacks.TryGetValue(nodeId, out var callback))
        {
            callback = EventCallback.Factory.Create<DragEventArgs>(this, () => _draggedNodeId = nodeId);
            _dragStartCallbacks[nodeId] = callback;
        }

        return callback;
    }

    /// <summary>
    /// Returns the same cached <see cref="EventCallback"/> for a given node's own <c>dragend</c>
    /// on every call, the same reason <see cref="GetActivateCallback"/> caches its own per-node
    /// callbacks.
    /// </summary>
    private EventCallback GetDragEndCallback(string nodeId)
    {
        if (!_dragEndCallbacks.TryGetValue(nodeId, out var callback))
        {
            callback = EventCallback.Factory.Create(this, EndDrag);
            _dragEndCallbacks[nodeId] = callback;
        }

        return callback;
    }

    /// <summary>
    /// Resets <see cref="_draggedNodeId"/> whenever a drag ends -- whether it ended in a
    /// <see cref="DropOnRow"/>/<see cref="DropOnSection"/> drop (already <see langword="null"/> by
    /// then, so this is a harmless no-op) or was cancelled (<c>Esc</c>, or released outside any
    /// drop target, which never calls either drop handler at all). Without this, a cancelled drag
    /// would leave <see cref="_draggedNodeId"/> pointing at whichever node started it, so a later,
    /// wholly unrelated drop (an external file, selected text) landing on a row or section would
    /// silently move that stale node -- exactly the "drop with nothing actually dragged is a
    /// silent no-op" contract <see cref="DropOnRow"/> and <see cref="DropOnSection"/> otherwise
    /// guarantee.
    /// </summary>
    private void EndDrag() => _draggedNodeId = null;

    /// <summary>
    /// Returns the same cached <see cref="EventCallback{DragEventArgs}"/> for a given node's own
    /// <c>drop</c> on every call, the same reason <see cref="GetActivateCallback"/> caches its own
    /// per-node callbacks.
    /// </summary>
    private EventCallback<DragEventArgs> GetRowDropCallback(string nodeId)
    {
        if (!_rowDropCallbacks.TryGetValue(nodeId, out var callback))
        {
            callback = EventCallback.Factory.Create<DragEventArgs>(this, () => DropOnRow(nodeId));
            _rowDropCallbacks[nodeId] = callback;
        }

        return callback;
    }

    /// <summary>
    /// Returns the same cached <see cref="EventCallback{DragEventArgs}"/> for a given section's
    /// own drop fallback on every call, the same reason <see cref="GetActivateCallback"/> caches
    /// its own per-node callbacks.
    /// </summary>
    private EventCallback<DragEventArgs> GetSectionDropCallback(string sectionId)
    {
        if (!_sectionDropCallbacks.TryGetValue(sectionId, out var callback))
        {
            callback = EventCallback.Factory.Create<DragEventArgs>(this, () => DropOnSection(sectionId));
            _sectionDropCallbacks[sectionId] = callback;
        }

        return callback;
    }

    /// <summary>
    /// The drag-and-drop reorder path's row-drop destination (PRD §4.1): moves whichever node
    /// <see cref="GetDragStartCallback"/> most recently recorded to immediately before
    /// <paramref name="targetNodeId"/>'s row, via the exact same
    /// <see cref="DesignerEditContext.MoveNodeAcrossSections"/> the keyboard paths call. Dropping
    /// a node onto itself, or a drop with nothing actually dragged (dev tools, a stray browser
    /// drag), is a silent no-op -- no move, no announcement. When the dragged node is already in
    /// <paramref name="targetNodeId"/>'s own section and sits earlier in it, the target's index
    /// shifts down by one the moment the dragged node leaves that section, which this method
    /// accounts for so the dragged node actually lands immediately before the target rather than
    /// immediately after it.
    /// </summary>
    private void DropOnRow(string targetNodeId)
    {
        var draggedNodeId = _draggedNodeId;
        _draggedNodeId = null;

        if (draggedNodeId is null || string.Equals(draggedNodeId, targetNodeId, StringComparison.Ordinal))
        {
            return;
        }

        var sourceLocated = DefinitionMutations.FindNodeLocation(EditContext.Draft.Definition, draggedNodeId);
        var targetLocated = DefinitionMutations.FindNodeLocation(EditContext.Draft.Definition, targetNodeId);

        if (sourceLocated is null || targetLocated is null)
        {
            return;
        }

        var targetSection = EditContext.Draft.Definition.Pages[targetLocated.Value.PageIndex].Sections[targetLocated.Value.SectionIndex];
        var sameSection = sourceLocated.Value.PageIndex == targetLocated.Value.PageIndex
            && sourceLocated.Value.SectionIndex == targetLocated.Value.SectionIndex;
        var targetIndex = sameSection && sourceLocated.Value.NodeIndex < targetLocated.Value.NodeIndex
            ? targetLocated.Value.NodeIndex - 1
            : targetLocated.Value.NodeIndex;

        EditContext.MoveNodeAcrossSections(draggedNodeId, targetSection.Id, targetIndex);
    }

    /// <summary>
    /// The drag-and-drop reorder path's section-level drop fallback (PRD §4.1): appends whichever
    /// node <see cref="GetDragStartCallback"/> most recently recorded to the end of
    /// <paramref name="sectionId"/> -- reached only when the drop lands somewhere in that
    /// section's rows wrapper that is not one of its own rows (an empty section, or the space
    /// below its last row), since <see cref="CanvasNodeRow.OnDropped"/> stops its own row-level
    /// drop from bubbling here. A no-op with nothing actually dragged, the same as
    /// <see cref="DropOnRow"/>.
    /// </summary>
    private void DropOnSection(string sectionId)
    {
        var draggedNodeId = _draggedNodeId;
        _draggedNodeId = null;

        if (draggedNodeId is null)
        {
            return;
        }

        var section = EditContext.Draft.Definition.Pages
            .SelectMany(page => page.Sections)
            .First(candidate => string.Equals(candidate.Id, sectionId, StringComparison.Ordinal));

        EditContext.MoveNodeAcrossSections(draggedNodeId, sectionId, section.Nodes.Count);
    }

    /// <summary>
    /// Dispatches every keyboard command this canvas recognizes -- <c>Esc</c>'s scope exit (only
    /// while scoped, checked first so it fires even for an empty group's own zero-row scope),
    /// <c>Ctrl+M</c>'s move-to-position dialog (a no-op while scoped -- see this method's own
    /// remarks below), <c>Ctrl+Shift+Z</c>/<c>Ctrl+Z</c> redo/undo (checked in that order, since
    /// <c>Ctrl+Z</c> alone is a strict subset of <c>Ctrl+Shift+Z</c>'s own key combination),
    /// <c>Ctrl+D</c> duplicate, the two <c>Alt+</c>-modified reorder paths
    /// (<see cref="HandleAltArrow"/>), and finally plain roving-cursor movement, <c>→</c>'s scope
    /// entry, and <c>Delete</c> -- in that priority order, so an author holding <c>Alt</c> or
    /// <c>Ctrl</c> for one of those never also falls through to the unmodified handling below
    /// (PRD §4.1, §11).
    /// </summary>
    /// <remarks>
    /// <b>The drill-in scope's own reorder path (repeating-groups-plan.md, Increment C).</b> Only
    /// <c>Alt+↑/↓</c> (<see cref="HandleAltArrow"/>, unchanged -- it already only ever needs
    /// <see cref="_activeNodeId"/>, and <see cref="DesignerEditContext.MoveNodeWithinSection"/>
    /// itself now reorders within whichever container a node actually sits in) works while
    /// scoped: a group's own scope has exactly one container, so <c>Alt+←/→</c> (which moves to an
    /// <em>adjacent section</em>), the <c>Ctrl+M</c> dialog (which <em>picks</em> a section to move
    /// into), and drag-and-drop (this canvas never wires a scoped row's own drag callbacks at all
    /// -- see <c>DesignerCanvas.razor</c>'s own remarks) have nothing left to offer there that
    /// <c>Alt+↑/↓</c> does not already cover, so this method disables <c>Ctrl+M</c> outright while
    /// scoped and leaves <c>Alt+←/→</c> to its own existing no-op guard (it already bails the
    /// moment <see cref="DefinitionMutations.FindNodeLocation"/> -- top-level only -- cannot find
    /// a scoped child).
    /// </remarks>
    private void OnKeyDown(KeyboardEventArgs e)
    {
        if (IsScoped && string.Equals(e.Key, "Escape", StringComparison.Ordinal))
        {
            GroupScopeNavigation.Exit(EditContext, ScopeGroupId!);
            return;
        }

        if (e.CtrlKey && string.Equals(e.Key, "m", StringComparison.OrdinalIgnoreCase))
        {
            if (!IsScoped)
            {
                OpenMoveDialog();
            }

            return;
        }

        if (e.CtrlKey && e.ShiftKey && string.Equals(e.Key, "z", StringComparison.OrdinalIgnoreCase))
        {
            if (EditContext.CanRedo)
            {
                EditContext.Redo();
            }

            return;
        }

        if (e.CtrlKey && string.Equals(e.Key, "z", StringComparison.OrdinalIgnoreCase))
        {
            if (EditContext.CanUndo)
            {
                EditContext.Undo();
            }

            return;
        }

        if (e.CtrlKey && string.Equals(e.Key, "d", StringComparison.OrdinalIgnoreCase))
        {
            if (_activeNodeId is not null)
            {
                EditContext.DuplicateNode(_activeNodeId);
            }

            return;
        }

        if (e.AltKey)
        {
            HandleAltArrow(e.Key);
            return;
        }

        var flatNodeIds = BuildFlatNodeIds();

        if (flatNodeIds.Count == 0)
        {
            return;
        }

        switch (e.Key)
        {
            case "ArrowDown":
                MoveActive(flatNodeIds, +1);
                break;
            case "ArrowUp":
                MoveActive(flatNodeIds, -1);
                break;
            case "ArrowRight":
                if (!IsScoped && ActiveNode is { Type: NodeType.Repeating } activeGroup)
                {
                    GroupScopeNavigation.Enter(EditContext, activeGroup.Id);
                }

                break;
            case "Home":
                SetActive(flatNodeIds[0]);
                break;
            case "End":
                SetActive(flatNodeIds[^1]);
                break;
            case "Enter":
                if (_activeNodeId is not null)
                {
                    Activate(_activeNodeId);
                }

                break;
            case "Delete":
                if (_activeNodeId is not null)
                {
                    RequestDelete(_activeNodeId);
                }

                break;
        }
    }

    /// <summary>
    /// The <c>Alt+↑/↓/←/→</c> reorder paths (PRD §4.1): <c>Alt+↑/↓</c> moves the active node
    /// earlier or later within its own section via
    /// <see cref="DesignerEditContext.MoveNodeWithinSection"/>; <c>Alt+←/→</c> moves it to the
    /// previous or next section on this page via <see cref="MoveActiveNodeAcrossAdjacentSection"/>.
    /// A no-op, silently, when nothing is active yet (an empty page).
    /// </summary>
    private void HandleAltArrow(string key)
    {
        if (_activeNodeId is null)
        {
            return;
        }

        switch (key)
        {
            case "ArrowUp":
                EditContext.MoveNodeWithinSection(_activeNodeId, -1);
                break;
            case "ArrowDown":
                EditContext.MoveNodeWithinSection(_activeNodeId, +1);
                break;
            case "ArrowLeft":
                MoveActiveNodeAcrossAdjacentSection(-1);
                break;
            case "ArrowRight":
                MoveActiveNodeAcrossAdjacentSection(+1);
                break;
        }
    }

    /// <summary>
    /// Moves the active node to the previous (<paramref name="delta"/> <c>-1</c>) or next
    /// (<c>+1</c>) section on this page, appending it to that section's end -- the insertion
    /// index <c>Alt+←/→</c> always uses, since a keyboard move has no drag position or dialog
    /// selection of its own to pick a more specific one from (PRD §4.1). A no-op when the active
    /// node's own section is already the first (<c>Alt+←</c>) or last (<c>Alt+→</c>) on the page,
    /// which this method itself guards -- never handing <see cref="DesignerEditContext"/> an
    /// out-of-range section to fall back to.
    /// </summary>
    private void MoveActiveNodeAcrossAdjacentSection(int delta)
    {
        if (_activeNodeId is null || ActivePage is null)
        {
            return;
        }

        var located = DefinitionMutations.FindNodeLocation(EditContext.Draft.Definition, _activeNodeId);

        if (located is null)
        {
            return;
        }

        var sections = ActivePage.Sections;
        var targetSectionIndex = located.Value.SectionIndex + delta;

        if (targetSectionIndex < 0 || targetSectionIndex >= sections.Count)
        {
            return;
        }

        var targetSection = sections[targetSectionIndex];
        EditContext.MoveNodeAcrossSections(_activeNodeId, targetSection.Id, targetSection.Nodes.Count);
    }

    /// <summary>
    /// Opens the <c>Ctrl+M</c> move-to-position dialog for the active node (PRD §4.1) -- a no-op
    /// when nothing is active yet.
    /// </summary>
    private void OpenMoveDialog()
    {
        if (_activeNodeId is null)
        {
            return;
        }

        _moveDialogNodeId = _activeNodeId;
        _showMoveDialog = true;
    }

    /// <summary>
    /// Hides the move-to-position dialog and re-requests focus for the active node's row -- the
    /// moved row itself after a confirmed move (a move never changes <see cref="FormNode.Id"/>,
    /// so <see cref="_activeNodeId"/> already names it, per
    /// <see cref="OnEditContextStateChanged"/>'s own handling of the mutation's
    /// <see cref="DesignerFocusIntent.Moved"/> selection), or the unchanged origin row after
    /// <see cref="MoveToPositionDialog"/>'s own Esc-cancel path, where nothing moved at all.
    /// </summary>
    private void CloseMoveDialog()
    {
        _showMoveDialog = false;

        if (_activeNodeId is not null)
        {
            _pendingFocusNodeId = _activeNodeId;
        }
    }

    /// <summary>
    /// The <c>Delete</c> key's delete-protection flow (PRD §4.1): a node with no live reference to
    /// it (<see cref="ExpressionDependencyAnalysis.ReferencesTo"/> comes back empty) deletes
    /// directly via <see cref="DesignerEditContext.DeleteNode"/> -- focus already falls out to the
    /// engine's own neighbour intent, the same as every other delete path, so there is nothing
    /// more for this method to do. A referenced node instead opens <see cref="DeleteProtectionDialog"/>,
    /// which names every reference and only ever deletes if the author confirms "delete anyway".
    /// </summary>
    private void RequestDelete(string nodeId)
    {
        var definition = EditContext.Draft.Definition;
        var node = definition.FindNode(nodeId);

        // A node this canvas is actively showing always resolves, but this stays defensive
        // rather than asserting it, the same "never trust a stale click blindly" caution
        // RequestDelete's own callers already carry -- ExpressionDependencyAnalysis.ReferencesTo
        // is exactly as safe to call by bare id if it somehow does not.
        var references = node is null
            ? ExpressionDependencyAnalysis.ReferencesTo(definition, nodeId)
            : GroupDeleteReferences.ReferencesTo(definition, node);

        if (references.Count == 0)
        {
            EditContext.DeleteNode(nodeId);
            return;
        }

        _deleteDialogNodeId = nodeId;
        _showDeleteDialog = true;
    }

    /// <summary>
    /// Hides the delete-protection dialog and re-requests focus for the active node's row --
    /// exactly the same "focus follows whatever the roving cursor now names" tail
    /// <see cref="CloseMoveDialog"/> gives its own dialog: a confirmed "delete anyway" has already
    /// moved <see cref="_activeNodeId"/> onto the engine's own neighbour selection by the time this
    /// runs (<see cref="OnEditContextStateChanged"/> reacted to <see cref="DesignerEditContext.DeleteNode"/>
    /// before <see cref="DeleteProtectionDialog"/> ever raises <c>OnClosed</c>), and a cancel never
    /// touched <see cref="_activeNodeId"/> at all, so either way this re-request lands focus back
    /// on the row an author now expects it on, once the dialog itself has actually left the DOM.
    /// </summary>
    private void CloseDeleteDialog()
    {
        _showDeleteDialog = false;
        _deleteDialogNodeId = null;

        if (_activeNodeId is not null)
        {
            _pendingFocusNodeId = _activeNodeId;
        }
    }

    private void MoveActive(List<string> flatNodeIds, int delta)
    {
        var currentIndex = _activeNodeId is null ? -1 : flatNodeIds.IndexOf(_activeNodeId);
        var newIndex = Math.Clamp((currentIndex < 0 ? 0 : currentIndex) + delta, 0, flatNodeIds.Count - 1);
        SetActive(flatNodeIds[newIndex]);
    }

    /// <summary>
    /// Moves the roving cursor to <paramref name="nodeId"/> and asks its row to move real DOM
    /// focus there -- the ↑/↓/Home/End paths, which move the cursor without committing a
    /// selection.
    /// </summary>
    private void SetActive(string nodeId)
    {
        _activeNodeId = nodeId;
        _pendingFocusNodeId = nodeId;
    }

    /// <summary>
    /// Moves the roving cursor to <paramref name="nodeId"/> and commits it as
    /// <see cref="DesignerEditContext.Selection"/> -- a click, or Enter on the row the cursor is
    /// already sitting on. Never itself requests a focus move: a click already carries native
    /// focus, and Enter's cursor is, by definition, already where focus is.
    /// </summary>
    private void Activate(string nodeId)
    {
        if (ActivePageId is null)
        {
            return;
        }

        // A scoped row's own top-level container is its own group's -- FindNodeLocation only
        // ever finds a section's own top-level nodes, so the anchor to search by is the group's
        // id, not the child's own, while scoped.
        var anchorId = ScopeGroupId ?? nodeId;
        var located = DefinitionMutations.FindNodeLocation(EditContext.Draft.Definition, anchorId);

        if (located is null)
        {
            return;
        }

        var section = EditContext.Draft.Definition.Pages[located.Value.PageIndex].Sections[located.Value.SectionIndex];
        _activeNodeId = nodeId;
        var selection = DesignerSelection.ForNode(nodeId, ActivePageId, section.Id, DesignerFocusIntent.None);

        if (ScopeGroupId is { } groupId)
        {
            selection = selection with { GroupId = groupId };
        }

        EditContext.Select(selection);
    }

    /// <summary>
    /// The roving cursor's own flat id list: <see cref="ScopeGroup"/>'s own children while
    /// scoped, or every node on <see cref="ActivePage"/> otherwise -- the one list ↑/↓/Home/End
    /// and the roving cursor itself all work over, regardless of which one it is (the scope is
    /// just a different node list, per D-4's own "every existing affordance applies to the
    /// children unchanged").
    /// </summary>
    private List<string> BuildFlatNodeIds() => ScopeGroup is { } group
        ? [.. group.Children.Select(child => child.Id)]
        : ActivePage is null
            ? []
            : [.. ActivePage.Sections.SelectMany(section => section.Nodes).Select(node => node.Id)];

    /// <summary>
    /// The tail of <see cref="OnParametersSet"/>'s page-changed branch: picks which node the
    /// roving cursor lands on for the page this canvas has just switched to, and whether that move
    /// should also carry real DOM focus with it.
    /// </summary>
    /// <remarks>
    /// Prefers <see cref="DesignerEditContext.Selection"/>'s own node over the page's first one
    /// when that selection already names a node on this exact page -- the linter dock's
    /// jump-to-node action (PRD §8) is exactly this case: it names a page, section, and node all
    /// at once via <see cref="DesignerEditContext.Select"/>, and <see cref="FormDesigner"/>'s own
    /// <c>SyncActivePageFromSelection</c> is what flows the resulting new <see cref="ActivePageId"/>
    /// down to this very method, in the SAME synchronous render pass <see cref="DesignerEditContext.Select"/>
    /// triggered -- <see cref="EditContext"/>'s <see cref="DesignerEditContext.Selection"/> property
    /// itself was already updated before that render even started, so reading it here needs no
    /// waiting on <see cref="OnEditContextStateChanged"/>'s own, separately-dispatched reaction to
    /// the same event ever to "catch up" -- a race that reaction alone cannot win when both it and
    /// this page switch are triggered by the exact same <see cref="DesignerEditContext.StateChanged"/>
    /// event, since which of the two sees <see cref="ActivePageId"/> already updated depends on
    /// dispatch order this canvas has no control over. A page switch an author drives directly
    /// (<see cref="Canvas.PageTabStrip"/>'s own tab click) carries no such selection, so this falls
    /// back to the page's first node exactly as before.
    /// </remarks>
    private void SyncActiveNodeForPageChange()
    {
        var flatNodeIds = BuildFlatNodeIds();
        var selection = EditContext.Selection;

        if (string.Equals(selection.PageId, ActivePageId, StringComparison.Ordinal)
            && selection.NodeId is { } selectedNodeId
            && flatNodeIds.Contains(selectedNodeId))
        {
            _activeNodeId = selectedNodeId;

            if (selection.Intent != DesignerFocusIntent.None)
            {
                _pendingFocusNodeId = selectedNodeId;
            }

            return;
        }

        _activeNodeId = flatNodeIds.Count > 0 ? flatNodeIds[0] : null;
    }

    /// <summary>
    /// Reacts to a mutation or a bare <see cref="DesignerEditContext.Select"/> call: when the new
    /// selection lands on a node on the page this canvas is showing, the roving cursor follows it,
    /// and -- only when the mutation tagged a real focus move (PRD §11) -- that row is asked to
    /// take DOM focus too. A selection that does not concern this page (a different page's node,
    /// or a page/section-only selection) never moves the cursor away from wherever the author
    /// last left it here -- the cross-page case a selection like the linter dock's jump-to-node
    /// action produces is <see cref="SyncActiveNodeForPageChange"/>'s own job instead, precisely
    /// because <see cref="ActivePageId"/> read from inside this dispatched reaction is not
    /// guaranteed to already reflect a page switch that same event is triggering elsewhere (see
    /// that method's own remarks).
    /// </summary>
    /// <remarks>
    /// <b>Node-less fallback (WCAG 2.4.3).</b> Two mutations can tag a selection that names no
    /// node at all, tagged with an intent other than <see cref="DesignerFocusIntent.None"/> so
    /// focus still moves somewhere sensible: <see cref="DesignerEditContext.Undo"/>/
    /// <see cref="DesignerEditContext.Redo"/> tag <see cref="DesignerFocusIntent.Restored"/>
    /// regardless of what they restore -- <see cref="DesignerSelection.None"/> itself (undoing the
    /// only add a still-empty section or page ever had), or a section that (still, or once again)
    /// has no rows of its own -- and <c>GroupScopeNavigation.Enter</c> tags
    /// <see cref="DesignerFocusIntent.JumpedTo"/> when the group being drilled into has no fields
    /// yet (repeating-groups-plan.md, Increment C). With nothing to hand a
    /// <see cref="CanvasNodeRow"/>, real DOM focus would otherwise stay exactly where it was --
    /// typically nowhere, once whatever row or dialog last held it has left the DOM -- stranding
    /// it on <c>&lt;body&gt;</c> even though <see cref="DesignerEditContext.Announced"/> still
    /// speaks the change. This falls back to the named section's own group element
    /// (<see cref="CanvasSection.RequestFocus"/>) when the selection still anchors one -- the
    /// scoped-empty case's own synthetic scope section reuses its real enclosing section's own
    /// id for exactly this reason, so <see cref="IsSectionFocusPending"/> matches it with no
    /// scope-specific field of its own -- or this canvas's own listbox root when it does not.
    /// <see cref="DesignerSelection.PageId"/> is <see langword="null"/> for
    /// <see cref="DesignerSelection.None"/> itself, so the same page-match this method's node
    /// branch requires would otherwise reject it outright even though undo/redo only ever
    /// concerns whichever page this canvas is already showing.
    /// </remarks>
    private void OnEditContextStateChanged() => InvokeAsync(() =>
    {
        var selection = EditContext.Selection;
        var isThisPage = selection.PageId is null || string.Equals(selection.PageId, ActivePageId, StringComparison.Ordinal);

        if (isThisPage && selection.NodeId is { } nodeId)
        {
            _activeNodeId = nodeId;

            if (selection.Intent != DesignerFocusIntent.None)
            {
                _pendingFocusNodeId = nodeId;
            }
        }
        else if (isThisPage && selection.Intent is DesignerFocusIntent.Restored or DesignerFocusIntent.JumpedTo)
        {
            // Every other node-less intent (NewNode's own AddSection/AddPage, for instance) is a
            // live commit whose focus destination is a later phase's own concern, not this
            // fallback's -- only Restored (undo/redo) and JumpedTo (this scope's own empty-group
            // entry) ever need it here.
            _pendingFocusSectionId = selection.SectionId;
            _pendingFocusCanvasRoot = selection.SectionId is null;
        }

        // Scoping in or out is a view change, not a mutation LintResults itself reacts to
        // (Increment C) -- OnParametersSet's own pageChanged/lintResultsChanged gate never fires
        // for it, so a mutation-free scope exit needs its own rebuild here, or the page's
        // top-level rows would keep showing whichever findings _findingsByNode last held while
        // scoped (a stale group's own children's) until the next real lint pass happened to tick.
        RebuildFindingsByNode();

        StateHasChanged();
    });
}
