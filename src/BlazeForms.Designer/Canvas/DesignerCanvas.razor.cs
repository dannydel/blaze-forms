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
    private DesignerEditContext? _subscribedContext;
    private ElementReference _canvasElement;
    private IJSObjectReference? _module;
    private IJSObjectReference? _scrollSuppressionHandle;
    private string? _activeNodeId;
    private string? _pendingFocusNodeId;
    private string? _lastSyncedActivePageId;
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

    private void OnKeyDown(KeyboardEventArgs e)
    {
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
