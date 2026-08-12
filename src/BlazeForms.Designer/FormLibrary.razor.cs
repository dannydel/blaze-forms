using System.Diagnostics.CodeAnalysis;
using BlazeForms.Hosting;
using BlazeForms.Internal;
using BlazeForms.Resources;
using BlazeForms.Versioning;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Localization;

namespace BlazeForms;

/// <summary>
/// The library surface (PRD §4.4): a thin management view over <see cref="IFormDefinitionStore.ListFormsAsync"/> —
/// search, filter, sort, and a cards⇄table toggle over every form the store knows about, each row
/// carrying its status badge, version, and submission count, with an open-in-designer action that
/// raises <see cref="OnOpenInDesigner"/> rather than navigating anywhere itself. Hosts with their
/// own list UX (PRD §4.4) skip this component entirely and drive <see cref="FormDesigner"/> from
/// their own surface instead.
/// </summary>
/// <remarks>
/// <para>
/// <b>Facets this phase implements, and the two it deliberately does not.</b>
/// <see cref="FormVersionSummary"/> — the only shape <see cref="IFormDefinitionStore.ListFormsAsync"/>
/// hands back — carries a form's <see cref="FormVersionSummary.Name"/> and
/// <see cref="FormVersionSummary.Author"/> but no "program" facet at all, so search here covers
/// name and author (PRD §4.4's "owner" maps onto <c>Author</c>) and the program facet is simply
/// absent — it is not implemented as a no-op control, because there is no field to back it.
/// Likewise, PRD §4.4's "blocking-issues-only" filter is omitted rather than implemented: a
/// summary carries no <see cref="BlazeForms.Definitions.FormDefinition"/> at all, so answering it
/// would mean loading every listed form's full content and re-running <see cref="BlazeForms.Linting.FormLinter"/>
/// over each one just to decide whether to show a checkbox's worth of filtering — an O(n) store
/// round-trip plus a lint pass per row, paid on every open and again on every keystroke that
/// affects this filter, for a component PRD §4.4 itself calls "thin". OQ-3's "in-memory filtering
/// is acceptable at launch scale" is about filtering the summaries this component already holds,
/// not about paying that per-row cost, so this phase ships the filter and sort facets over fields
/// <see cref="FormVersionSummary"/> can actually back and leaves blocking-issues-only for whenever
/// the store contract grows a cheaper way to ask it (e.g. a precomputed flag alongside the
/// summary).
/// </para>
/// <para>
/// <b>Loading is prerender-safe by construction, not by a guard.</b> Unlike <see cref="FormDesigner"/>'s
/// draft load, <see cref="LoadFormsAsync"/> is a pure read with no create-on-miss side effect, so
/// running it twice under a prerender-then-resume host (once on the server-rendered pass, again
/// once the circuit reconnects) is harmless — it just re-fetches the same list. That is why this
/// component loads from the ordinary <see cref="OnInitializedAsync"/>, the same shape
/// <see cref="Versioning.VersionHistory"/> uses for its own read-only <c>ListVersionsAsync</c>
/// call, rather than <see cref="FormDesigner"/>'s <c>OnAfterRenderAsync</c>-plus-flag dance.
/// </para>
/// </remarks>
public partial class FormLibrary : ComponentBase
{
    private readonly string _searchInputId = "bf-library-search-" + Guid.NewGuid().ToString("n");
    private readonly string _statusFilterId = "bf-library-status-" + Guid.NewGuid().ToString("n");
    private readonly string _sortSelectId = "bf-library-sort-" + Guid.NewGuid().ToString("n");
    private IFormDefinitionStore _store = default!;
    private IReadOnlyList<FormVersionSummary> _forms = [];
    private bool _isLoading = true;
    private string _searchTerm = string.Empty;
    private FormLifecycleState? _statusFilter;
    private SortMode _sortMode = SortMode.Name;
    private ViewMode _viewMode = ViewMode.Cards;

    private enum SortMode
    {
        Name,
        SubmissionCount,
        LastPublished,
    }

    private enum ViewMode
    {
        Cards,
        Table,
    }

    /// <summary>
    /// Raised with a form's <see cref="FormVersionSummary.FormId"/> when the author activates that
    /// form's open control, whether it is showing as a card or a table row. This component never
    /// navigates on its own — the host decides what "open in designer" means (mounting
    /// <see cref="FormDesigner"/> in a panel, a full-page route, or anything else, PRD §4.4).
    /// </summary>
    [Parameter]
    public EventCallback<string> OnOpenInDesigner { get; set; }

    /// <summary>
    /// Used once, in <see cref="OnInitialized"/>, to resolve <see cref="_store"/>. Kept as the raw
    /// service provider rather than an <c>[Inject]</c> property typed to
    /// <see cref="IFormDefinitionStore"/> directly, for the same clear-failure reason
    /// <see cref="FormDesigner.ServiceProvider"/> documents for itself.
    /// </summary>
    [Inject]
    private IServiceProvider ServiceProvider { get; set; } = default!;

    private static IStringLocalizer<DesignerStrings> Localizer => DesignerLocalization.Shared;

    /// <summary>
    /// Every form this component last loaded, unfiltered. <c>internal</c>, not <c>private</c>,
    /// purely so a test can assert the loaded picture directly.
    /// </summary>
    internal IReadOnlyList<FormVersionSummary> Forms => _forms;

    /// <summary>
    /// <see cref="Forms"/> narrowed by <see cref="_searchTerm"/> (name or author) and
    /// <see cref="_statusFilter"/>, then ordered by <see cref="_sortMode"/> — recomputed on every
    /// access rather than cached, since <see cref="Forms"/> is small enough at launch scale for
    /// that to be free (OQ-3) and a cached copy would be one more place to keep in sync with three
    /// different inputs. <c>internal</c>, not <c>private</c>, so a test can assert the filtered
    /// picture directly instead of parsing rendered markup.
    /// </summary>
    internal IReadOnlyList<FormVersionSummary> FilteredForms
    {
        get
        {
            IEnumerable<FormVersionSummary> query = _forms;

            if (_statusFilter is { } status)
            {
                query = query.Where(form => form.State == status);
            }

            if (!string.IsNullOrWhiteSpace(_searchTerm))
            {
                query = query.Where(form =>
                    form.Name.Contains(_searchTerm, StringComparison.OrdinalIgnoreCase)
                    || (form.Author is not null && form.Author.Contains(_searchTerm, StringComparison.OrdinalIgnoreCase)));
            }

            query = _sortMode switch
            {
                SortMode.SubmissionCount => query
                    .OrderByDescending(form => form.SubmissionCount)
                    .ThenBy(form => form.Name, StringComparer.OrdinalIgnoreCase),
                SortMode.LastPublished => query
                    .OrderByDescending(form => form.PublishedAt ?? DateTimeOffset.MinValue)
                    .ThenBy(form => form.Name, StringComparer.OrdinalIgnoreCase),
                _ => query.OrderBy(form => form.Name, StringComparer.OrdinalIgnoreCase),
            };

            return [.. query];
        }
    }

    private string ResultCountAnnouncement =>
        Localizer["FormLibraryResultCount", FilteredForms.Count, _forms.Count].Value;

    /// <inheritdoc/>
    protected override void OnInitialized()
    {
        _store = ServiceProvider.GetService(typeof(IFormDefinitionStore)) as IFormDefinitionStore
            ?? throw new InvalidOperationException(
                "No IFormDefinitionStore is registered. FormLibrary requires one to list the forms it manages -- register an implementation (InMemoryFormDefinitionStore for demos and tests) with the host's DI container.");
    }

    /// <inheritdoc/>
    [SuppressMessage(
        "Reliability",
        "CA2007:Consider calling ConfigureAwait on the awaited task",
        Justification = "A Blazor lifecycle method must resume on the renderer's synchronization context, not a captured-context-free one, so it can safely schedule the next render.")]
    protected override async Task OnInitializedAsync() => await LoadFormsAsync();

    /// <summary>
    /// Reloads <see cref="Forms"/> from <see cref="_store"/> — see this type's own remarks for why
    /// this is safe to call from the ordinary <see cref="OnInitializedAsync"/> even under a
    /// prerender-then-resume host.
    /// </summary>
    [SuppressMessage(
        "Reliability",
        "CA2007:Consider calling ConfigureAwait on the awaited task",
        Justification = "Called only from OnInitializedAsync, which must itself resume on the renderer's synchronization context.")]
    private async Task LoadFormsAsync()
    {
        _isLoading = true;
        _forms = await _store.ListFormsAsync();
        _isLoading = false;
    }

    private void OnSearchInput(ChangeEventArgs args) => _searchTerm = args.Value?.ToString() ?? string.Empty;

    private void OnStatusFilterChanged(ChangeEventArgs args)
    {
        var value = args.Value?.ToString();
        _statusFilter = string.IsNullOrEmpty(value) ? null : Enum.Parse<FormLifecycleState>(value);
    }

    private void OnSortChanged(ChangeEventArgs args)
    {
        var value = args.Value?.ToString();
        _sortMode = value is not null && Enum.TryParse<SortMode>(value, out var parsed) ? parsed : SortMode.Name;
    }

    private void SetViewMode(ViewMode mode) => _viewMode = mode;

    private Task OpenAsync(string formId) => OnOpenInDesigner.InvokeAsync(formId);
}
