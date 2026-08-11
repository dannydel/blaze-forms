using System.Diagnostics.CodeAnalysis;
using BlazeForms.Definitions;
using BlazeForms.Designer;
using BlazeForms.Designer.Internal;
using BlazeForms.Internal;
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
/// a visible double-jump -- so the collocated <c>DesignerCanvas.razor.js</c> module attaches a
/// second, genuinely JS-side <c>keydown</c> listener to this same root element that calls only
/// <c>preventDefault()</c> for those four keys (never <c>Tab</c>, never anything else, and never
/// <c>stopPropagation()</c>) -- the one platform gap Blazor's own event model cannot close, since
/// <c>preventDefault</c> must run before <c>OnKeyDown</c>'s dispatch, not after. The Blazor
/// <c>@onkeydown</c> handler on this element still receives, and still handles, every key exactly
/// as before; the module only ever suppresses the browser's own default action underneath it.
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
public partial class DesignerCanvas : ComponentBase, IAsyncDisposable
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
    private string? _activeNodeId;
    private string? _pendingFocusNodeId;
    private string? _lastSyncedActivePageId;
    private string? _draggedNodeId;
    private bool _showMoveDialog;
    private string? _moveDialogNodeId;
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

        if (!string.Equals(_lastSyncedActivePageId, ActivePageId, StringComparison.Ordinal))
        {
            _lastSyncedActivePageId = ActivePageId;
            var flatNodeIds = BuildFlatNodeIds();
            _activeNodeId = flatNodeIds.Count > 0 ? flatNodeIds[0] : null;
        }
    }

    /// <inheritdoc/>
    protected override void OnAfterRender(bool firstRender) => _pendingFocusNodeId = null;

    /// <summary>
    /// Imports <see cref="ModulePath"/>'s scroll-suppression module on this canvas's first render
    /// (prerender-safe: importing costs nothing more than an unused module reference under a
    /// non-interactive prerendered pass, and every render after the circuit resumes calls this
    /// again with <paramref name="firstRender"/> still <see langword="true"/> exactly once), then
    /// attaches its listener to <see cref="_canvasElement"/> whenever <see cref="ActivePage"/> is
    /// showing a real row list and nothing is attached yet -- and detaches it the moment
    /// <see cref="ActivePage"/> goes back to <see langword="null"/> (the empty state has no
    /// <c>bf-canvas</c> element for a stale reference to keep pointing at). A page switching back
    /// and forth between "has content" and "no page selected" re-attaches against whichever new
    /// DOM element <c>@ref</c> most recently captured, since the <c>@if</c> in
    /// <c>DesignerCanvas.razor</c> tears the element down and recreates it on each switch.
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

        if (_module is null)
        {
            return;
        }

        if (ActivePage is not null && _scrollSuppressionHandle is null)
        {
            _scrollSuppressionHandle = await _module.InvokeAsync<IJSObjectReference>("attachScrollSuppression", _canvasElement);
        }
        else if (ActivePage is null && _scrollSuppressionHandle is not null)
        {
            var handle = _scrollSuppressionHandle;
            _scrollSuppressionHandle = null;
            await handle.InvokeVoidAsync("dispose");
            await handle.DisposeAsync();
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
    /// Dispatches every keyboard command this canvas recognizes -- roving-cursor movement (no
    /// modifier), the two <c>Alt+</c>-modified reorder paths (<see cref="HandleAltArrow"/>), and
    /// <c>Ctrl+M</c>'s move-to-position dialog -- in that priority order, so an author holding
    /// <c>Alt</c> or <c>Ctrl</c> for one of those never also falls through to the plain
    /// roving-cursor handling below (PRD §4.1, §11).
    /// </summary>
    private void OnKeyDown(KeyboardEventArgs e)
    {
        if (e.CtrlKey && string.Equals(e.Key, "m", StringComparison.OrdinalIgnoreCase))
        {
            OpenMoveDialog();
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
        var located = DefinitionMutations.FindNodeLocation(EditContext.Draft.Definition, nodeId);

        if (located is null || ActivePageId is null)
        {
            return;
        }

        var section = EditContext.Draft.Definition.Pages[located.Value.PageIndex].Sections[located.Value.SectionIndex];
        _activeNodeId = nodeId;
        EditContext.Select(DesignerSelection.ForNode(nodeId, ActivePageId, section.Id, DesignerFocusIntent.None));
    }

    private List<string> BuildFlatNodeIds() => ActivePage is null
        ? []
        : [.. ActivePage.Sections.SelectMany(section => section.Nodes).Select(node => node.Id)];

    /// <summary>
    /// Reacts to a mutation or a bare <see cref="DesignerEditContext.Select"/> call: when the new
    /// selection lands on a node on the page this canvas is showing, the roving cursor follows it,
    /// and -- only when the mutation tagged a real focus move (PRD §11) -- that row is asked to
    /// take DOM focus too. A selection that does not concern this page (a different page's node,
    /// or a page/section-only selection) never moves the cursor away from wherever the author
    /// last left it here.
    /// </summary>
    private void OnEditContextStateChanged() => InvokeAsync(() =>
    {
        var selection = EditContext.Selection;

        if (string.Equals(selection.PageId, ActivePageId, StringComparison.Ordinal) && selection.NodeId is { } nodeId)
        {
            _activeNodeId = nodeId;

            if (selection.Intent != DesignerFocusIntent.None)
            {
                _pendingFocusNodeId = nodeId;
            }
        }

        StateHasChanged();
    });
}
