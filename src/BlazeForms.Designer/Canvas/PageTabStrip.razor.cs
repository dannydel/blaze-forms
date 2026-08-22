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
/// The page navigation strip (PRD §4.1): one plain, labelled page button per page, plus an
/// always-present add-page button, and -- when the active page has no sections yet -- an
/// empty-page affordance offering to add a blank section or (a stub this phase; see the remarks)
/// start from a template.
/// </summary>
/// <remarks>
/// <para>
/// <b>Not a tabs pattern.</b> This deliberately renders a plain <c>&lt;nav aria-label="Pages"&gt;</c>
/// of ordinary buttons rather than WAI-ARIA's <c>role="tablist"</c>/<c>role="tab"</c> pattern: that
/// pattern also requires each tab's <c>aria-controls</c> to name a <c>role="tabpanel"</c>, and
/// what a page tab would "control" here is <see cref="DesignerCanvas"/>'s own
/// <c>role="listbox"</c> -- a real ARIA role of its own, not a tabpanel, so pairing it with a
/// tablist would assert a relationship that does not exist and would fail an axe scan
/// (AGENTS.md invariant #4). The active page button instead carries <c>aria-current="page"</c>,
/// the correct ARIA attribute for "the page you are currently looking at" outside a tabs widget.
/// </para>
/// <para>
/// Which page is <em>active</em> is Designer view state, not a definition mutation, so switching
/// pages never touches <see cref="DesignerEditContext"/> at all -- it only raises
/// <see cref="ActivePageIdChanged"/> for whatever owns that state (<see cref="FormDesigner"/>
/// today). Adding a page or a section, by contrast, is a real mutation and goes straight to
/// <see cref="DesignerEditContext.AddPage"/> / <see cref="DesignerEditContext.AddSection"/> --
/// <see cref="FormDesigner"/> then follows the new page's own <see cref="DesignerSelection.PageId"/>
/// to keep the tab strip and the canvas showing the same page without this component doing
/// anything itself to make that happen.
/// </para>
/// <para>
/// <b>Start from template.</b> The task this button names -- scaffolding a page from a
/// prebuilt template -- has no template source to scaffold from yet, so this phase ships it
/// <c>disabled</c> with <c>aria-disabled="true"</c> (the same honestly-disabled affordance
/// <c>FieldPalette</c> uses for its own not-yet-addable entries) rather than a button that looks
/// live but silently does nothing when pressed. Wiring it up is future work.
/// </para>
/// <para>
/// Each page button and the add-page button are ordinary, individually-focusable elements (every
/// one carries its own place in the Tab sequence) rather than a roving-tabindex group -- unlike
/// <see cref="DesignerCanvas"/>, single-tab-stop roving focus was not asked of this phase's page
/// navigation, and a handful of pages is small enough that giving each its own Tab stop costs an
/// author nothing. The add-page button sits outside the <c>&lt;nav&gt;</c> entirely, rather than
/// as one more of its children, so it never reads as one more page to navigate to.
/// </para>
/// <para>
/// <b>Rename.</b> Double-clicking a page button, or pressing <c>F2</c> while it holds focus,
/// swaps that one button for a text input pre-filled with the page's current title (PRD §4.1,
/// §11). Pressing Enter, or the input losing focus, commits the edit through
/// <see cref="DesignerEditContext.RenamePage"/> -- a real, undoable mutation, so <c>Ctrl+Z</c>
/// restores the prior title the same as any other edit. Pressing <c>Escape</c> instead cancels,
/// discarding the in-progress text and leaving the page's title untouched. Committing an empty
/// or whitespace-only value clears the title back to its localized "Page N" fallback rather than
/// storing an empty string. Either way, focus returns to the tab button of the page that was
/// being edited once the editor closes -- not necessarily the active tab, since the button grid
/// carries no roving tabindex and an author can Tab to a non-active button and press <c>F2</c>
/// there -- a rename never moves focus onto the canvas.
/// </para>
/// <para>
/// <b>Why every button, not just the active one, captures a reference.</b> <see cref="TabButtonRefs"/>
/// holds one <see cref="ElementReference"/> per page, refreshed by every non-editing button's own
/// <c>@ref</c> every render, rather than a single field only the active button's markup binds.
/// Blazor's render-tree diff builder cannot swap an element that carries a reference capture for
/// one that does not under the same <c>@key</c> -- it throws
/// <see cref="NotImplementedException"/> ("Unexpected frame type during RemoveOldFrame:
/// ElementReferenceCapture") the instant a different page becomes active, since that swap is
/// exactly a with-capture/without-capture transition on the very element <c>@key="page.Id"</c>
/// asks it to diff in place. Giving every button an identically-shaped capture keeps the two
/// states' frames structurally identical, so only the <c>aria-current</c> attribute value and
/// CSS class actually differ between them -- an ordinary attribute diff, not a frame-shape one.
/// </para>
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed partial class PageTabStrip : ComponentBase, IAsyncDisposable
{
    private DesignerEditContext? _subscribedContext;
    private bool _disposed;
    private ElementReference _editorElement;
    private string? _editingPageId;
    private string _editingTitle = string.Empty;
    private bool _focusEditorOnNextRender;
    private string? _pendingFocusTabPageId;

    /// <summary>
    /// Every page's own tab button, keyed by <see cref="FormPage.Id"/> -- see this class's own
    /// remarks on why every button captures one rather than only the active button doing so.
    /// </summary>
    private Dictionary<string, ElementReference> TabButtonRefs { get; } = [];

    /// <summary>
    /// The mutation engine this strip adds pages and sections through.
    /// </summary>
    [Parameter, EditorRequired]
    public DesignerEditContext EditContext { get; set; } = default!;

    /// <summary>
    /// The page currently showing elsewhere in the designer (e.g. on <see cref="DesignerCanvas"/>).
    /// <see langword="null"/> when the draft has no pages yet.
    /// </summary>
    [Parameter]
    public string? ActivePageId { get; set; }

    /// <summary>
    /// Raised when the author clicks a different existing tab. Never raised for an add-page or
    /// add-section click -- <see cref="FormDesigner"/> picks up the new page from
    /// <see cref="DesignerEditContext.Selection"/> instead, the same way it does for every other
    /// mutation.
    /// </summary>
    [Parameter]
    public EventCallback<string> ActivePageIdChanged { get; set; }

    private static IStringLocalizer<DesignerStrings> Localizer => DesignerLocalization.Shared;

    private IReadOnlyList<FormPage> Pages => EditContext.Draft.Definition.Pages;

    private FormPage? ActivePage => ActivePageId is null
        ? null
        : Pages.FirstOrDefault(page => string.Equals(page.Id, ActivePageId, StringComparison.Ordinal));

    /// <inheritdoc/>
    protected override void OnParametersSet()
    {
        if (ReferenceEquals(_subscribedContext, EditContext))
        {
            return;
        }

        if (_subscribedContext is not null)
        {
            _subscribedContext.StateChanged -= OnEditContextStateChanged;
        }

        EditContext.StateChanged += OnEditContextStateChanged;
        _subscribedContext = EditContext;
    }

    /// <summary>
    /// Unsubscribes from <see cref="EditContext"/>. Safe to call more than once.
    /// </summary>
    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        _disposed = true;

        if (_subscribedContext is not null)
        {
            _subscribedContext.StateChanged -= OnEditContextStateChanged;
            _subscribedContext = null;
        }

        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }

    private bool IsActiveTab(string pageId) => string.Equals(ActivePageId, pageId, StringComparison.Ordinal);

    private string TabClass(string pageId) =>
        IsActiveTab(pageId) ? "bf-page-tabs__tab bf-page-tabs__tab--active" : "bf-page-tabs__tab";

    private static string PageTitle(FormPage page, int pageIndex) =>
        page.Title ?? Localizer["PageFallbackTitle", pageIndex + 1].Value;

    private Task SelectPage(string pageId) => ActivePageIdChanged.InvokeAsync(pageId);

    private void AddPage() => EditContext.AddPage();

    private void AddSection()
    {
        if (ActivePageId is not null)
        {
            EditContext.AddSection(ActivePageId);
        }
    }

    private void OnEditContextStateChanged() => InvokeAsync(StateHasChanged);

    private bool IsEditing(string pageId) => string.Equals(_editingPageId, pageId, StringComparison.Ordinal);

    /// <summary>
    /// Opens the inline editor for a page's title -- the double-click and <c>F2</c> paths.
    /// </summary>
    private void BeginRename(string pageId)
    {
        var page = Pages.FirstOrDefault(p => string.Equals(p.Id, pageId, StringComparison.Ordinal));
        if (page is null)
        {
            return;
        }

        _editingPageId = pageId;
        _editingTitle = page.Title ?? string.Empty; // empty when unset, so committing empty clears back to the fallback
        _focusEditorOnNextRender = true;
    }

    /// <summary>
    /// Commits the in-progress edit through <see cref="DesignerEditContext.RenamePage"/> -- the
    /// Enter and blur paths.
    /// </summary>
    private void CommitRename(string pageId)
    {
        // Guarded so the blur that fires when Enter/Escape has already torn the input down is a no-op
        // rather than a second commit.
        if (!IsEditing(pageId))
        {
            return;
        }

        _editingPageId = null;
        _pendingFocusTabPageId = pageId;
        EditContext.RenamePage(pageId, _editingTitle); // itself a no-op when the title is unchanged
    }

    /// <summary>
    /// Discards the in-progress edit without touching the draft -- the <c>Escape</c> path.
    /// </summary>
    private void CancelRename()
    {
        if (_editingPageId is null)
        {
            return;
        }

        _pendingFocusTabPageId = _editingPageId;
        _editingPageId = null;
    }

    private void OnEditorKeyDown(KeyboardEventArgs e, string pageId)
    {
        if (string.Equals(e.Key, "Enter", StringComparison.Ordinal))
        {
            CommitRename(pageId);
        }
        else if (string.Equals(e.Key, "Escape", StringComparison.Ordinal))
        {
            CancelRename();
        }
    }

    // F2 is the platform-standard rename key -- the keyboard equivalent of the double-click, so the
    // rename affordance is not mouse-only (the designer gates on axe/WCAG).
    private void OnTabKeyDown(KeyboardEventArgs e, string pageId)
    {
        if (string.Equals(e.Key, "F2", StringComparison.Ordinal))
        {
            BeginRename(pageId);
        }
    }

    /// <summary>
    /// Prunes <see cref="TabButtonRefs"/> of any page id no longer present in <see cref="Pages"/>
    /// -- page ids are immutable and never reused, so a stale entry is harmless (its
    /// <see cref="ElementReference"/> roots no DOM once the button is gone), but nothing else ever
    /// removes one, so this keeps the dictionary from growing forever across deletes.
    /// </summary>
    protected override void OnAfterRender(bool firstRender)
    {
        if (TabButtonRefs.Count > Pages.Count)
        {
            var live = Pages.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
            foreach (var staleId in TabButtonRefs.Keys.Where(id => !live.Contains(id)).ToList())
            {
                TabButtonRefs.Remove(staleId);
            }
        }
    }

    /// <inheritdoc/>
    [SuppressMessage(
        "Reliability",
        "CA2007:Consider calling ConfigureAwait on the awaited task",
        Justification = "A Blazor lifecycle method must resume on the renderer's synchronization context, not a captured-context-free one, so it can safely schedule the next render.")]
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_focusEditorOnNextRender)
        {
            _focusEditorOnNextRender = false;
            await _editorElement.FocusAsync();
        }
        else if (_pendingFocusTabPageId is { } focusPageId)
        {
            _pendingFocusTabPageId = null;

            // Only when no editor is open, and only if that page still has a rendered button --
            // guards against a rename that immediately re-opens another edit (unlikely, but not
            // impossible if a future caller chains one) racing focus back onto a tab button whose
            // row just became an input again, and against the edited page having been deleted out
            // from under a still-pending focus request.
            if (_editingPageId is null && TabButtonRefs.TryGetValue(focusPageId, out var button))
            {
                await button.FocusAsync();
            }
        }
    }
}
