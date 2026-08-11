using BlazeForms.Definitions;
using BlazeForms.Designer;
using BlazeForms.Internal;
using BlazeForms.Resources;
using Microsoft.AspNetCore.Components;
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
/// </remarks>
public partial class PageTabStrip : ComponentBase, IAsyncDisposable
{
    private DesignerEditContext? _subscribedContext;
    private bool _disposed;

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
}
