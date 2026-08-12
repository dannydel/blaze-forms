using System.Diagnostics.CodeAnalysis;
using BlazeForms.Definitions;
using BlazeForms.Designer;
using BlazeForms.Hosting;
using BlazeForms.Internal;
using BlazeForms.Linting;
using BlazeForms.Resources;
using BlazeForms.Versioning;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;

namespace BlazeForms;

/// <summary>
/// The designer shell: a three-pane docked layout — field palette, canvas, and properties panel
/// (PRD §4.1, D9 — docked is the only P1 layout). This phase wires up the mutation engine
/// (<see cref="DesignerEditContext"/>) and its aria-live announcer, hosts the page tab strip and
/// the roving-focus canvas (<see cref="Canvas.PageTabStrip"/>, <see cref="Canvas.DesignerCanvas"/>),
/// and turns the palette's <see cref="Palette.FieldPalette.OnAddRequested"/> into a real
/// <see cref="DesignerEditContext.AddNode"/> call; the properties panel's field-specific controls,
/// the linter dock, and the publish dialog all land in later phases.
/// </summary>
/// <remarks>
/// <see cref="OnInitialized"/> resolves the host's <see cref="IFormDefinitionStore"/> only — no
/// store I/O happens there. Loading (or, on a miss, in-memory creating) <see cref="FormId"/>'s
/// working draft happens in <see cref="OnAfterRenderAsync"/> instead, for the same prerender-safety
/// reason <see cref="FormRenderer"/> defers its own draft load there (see its remarks): under a
/// prerender-then-resume host, <c>OnInitializedAsync</c> runs twice, which would double-load (or,
/// worse, double-create) the draft before there is any interactive circuit to eventually persist
/// back to. A miss creates the draft purely in memory — revised from the form's latest published
/// version when one exists, or from scratch otherwise — and never saves it: PRD §7's "edits
/// accumulate on a new draft" means a draft is persisted on the first edit, not on mere open, so
/// opening the designer on an already-published form must never flip that form's library status to
/// "draft in progress" as a side effect of viewing it. That loaded (or created) draft becomes the
/// one <see cref="DesignerEditContext"/> this designer instance owns for its whole lifetime;
/// <see cref="_editContext"/>'s own autosave scheduler is what actually persists it, on the first
/// mutation that context ever makes, not on this load.
/// <para>
/// The store itself is required, unlike <see cref="FormRenderer"/>'s optional submission sink and
/// draft store (PRD §9) — a designer with nowhere to persist its edits is not a usable designer, so
/// a host that forgot to register one gets a clear failure now rather than a designer that silently
/// discards every edit later.
/// </para>
/// </remarks>
public partial class FormDesigner : ComponentBase, IAsyncDisposable
{
    private IFormDefinitionStore _store = default!;
    private DesignerEditContext? _editContext;
    private string? _activePageId;
    private IReadOnlyList<LintResult> _lintResults = [];
    private bool _showKeyboardHelp;
    private bool _restoreHelpFocusOnNextRender;
    private ElementReference _helpButtonElement;
    private bool _showPublishDialog;
    private bool _restorePublishFocusOnNextRender;
    private ElementReference _publishButtonElement;
    private bool _showVersionHistory;
    private bool _restoreVersionHistoryFocusOnNextRender;
    private ElementReference _versionHistoryButtonElement;
    private bool _isPreviewing;
    private bool _restorePreviewFocusOnNextRender;
    private ElementReference _previewButtonElement;
    private bool _draftLoadAttempted;
    private bool _disposed;
    private readonly CancellationTokenSource _disposalCts = new();

    /// <summary>
    /// The identifier of the form to design. Required — the designer always opens onto a
    /// specific form's working draft, never a blank slate with no identity of its own (PRD §4.1,
    /// §4.4).
    /// </summary>
    [Parameter, EditorRequired]
    public string FormId { get; set; } = default!;

    /// <summary>
    /// Raised when the host's chrome around the designer should close it — the mirror image of
    /// the open-in-designer callback the library surface (PRD §4.4) uses to launch this
    /// component. Unused until a later phase wires a close affordance into the shell.
    /// </summary>
    [Parameter]
    public EventCallback OnClosed { get; set; }

    /// <summary>
    /// Raised once a draft publishes (PRD §7). Declared now so the publish dialog, landing in a
    /// later phase, has a parameter contract to raise against; nothing in this phase ever
    /// publishes a draft.
    /// </summary>
    [Parameter]
    public EventCallback<FormVersion> OnPublished { get; set; }

    /// <summary>
    /// The host-supplied display name of the person currently editing this draft — forwarded
    /// verbatim to <see cref="PublishDialog"/>, which is the only place it is actually read (PRD
    /// §7 requires an author on every publish). <see langword="null"/> is a valid value all the
    /// way through: <see cref="PublishDialog"/> falls back to a localized "Unknown" rather than
    /// this shell blocking publish on a host that has not wired an identity up yet.
    /// </summary>
    [Parameter]
    public string? Author { get; set; }

    /// <summary>
    /// Used once, in <see cref="OnInitialized"/>, to resolve <see cref="_store"/>. Kept as
    /// the raw service provider rather than an <c>[Inject]</c> property typed to
    /// <see cref="IFormDefinitionStore"/> directly, purely so the missing-registration failure
    /// mode below is a clear <see cref="InvalidOperationException"/> naming the contract a host
    /// forgot, rather than Blazor's own less specific property-injection failure.
    /// </summary>
    [Inject]
    private IServiceProvider ServiceProvider { get; set; } = default!;

    /// <summary>
    /// The designer chrome's localizer — the internal, host-immune
    /// <see cref="DesignerLocalization.Shared"/> instance (PRD §12), never a DI-injected one, for
    /// the same host-immunity reason <see cref="RendererLocalization"/> documents for the
    /// renderer's own chrome.
    /// </summary>
    private static IStringLocalizer<DesignerStrings> Localizer => DesignerLocalization.Shared;

    /// <inheritdoc/>
    protected override void OnInitialized()
    {
        _store = ServiceProvider.GetService(typeof(IFormDefinitionStore)) as IFormDefinitionStore
            ?? throw new InvalidOperationException(
                "No IFormDefinitionStore is registered. FormDesigner requires one to load and save the form it edits -- register an implementation (InMemoryFormDefinitionStore for demos and tests) with the host's DI container.");
    }

    /// <inheritdoc/>
    [SuppressMessage(
        "Reliability",
        "CA2007:Consider calling ConfigureAwait on the awaited task",
        Justification = "A Blazor lifecycle method must resume on the renderer's synchronization context, not a captured-context-free one, so it can safely schedule the next render.")]
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender && !_draftLoadAttempted)
        {
            // Loading here rather than in OnInitializedAsync is what keeps this prerender-safe:
            // OnInitializedAsync runs twice under a prerender-then-resume host (once on the
            // server-rendered pass, again once the circuit reconnects), which would load or
            // in-memory-create the draft twice and would run before there is any interactive
            // circuit to eventually persist back to. The explicit flag is a second,
            // belt-and-suspenders guard against ever awaiting LoadDraftAsync more than once, on
            // top of firstRender itself only ever being true for the very first call -- mirrors
            // FormRenderer.OnAfterRenderAsync (PRD §4.1).
            _draftLoadAttempted = true;
            await LoadDraftAsync();
        }

        if (_restoreHelpFocusOnNextRender)
        {
            // One-shot, the same as DesignerCanvas's own _pendingFocusNodeId: CloseKeyboardHelp
            // sets this flag and StateHasChanged's next render is the one that actually removes
            // KeyboardHelpDialog from the DOM (the @if in FormDesigner.razor), so only THIS
            // render -- not the one CloseKeyboardHelp itself ran on -- is safe to move focus
            // back to the Help button on. Left unset, the dialog closing would drop focus to
            // <body> (PRD §11).
            _restoreHelpFocusOnNextRender = false;
            await _helpButtonElement.FocusAsync();
        }

        if (_restorePublishFocusOnNextRender)
        {
            // Same one-shot shape as _restoreHelpFocusOnNextRender above: PublishDialog has
            // already left the DOM by the time this render runs, whether it closed on a cancel
            // or a successful publish, so this is always safe to run.
            _restorePublishFocusOnNextRender = false;
            await _publishButtonElement.FocusAsync();
        }

        if (_restoreVersionHistoryFocusOnNextRender)
        {
            _restoreVersionHistoryFocusOnNextRender = false;
            await _versionHistoryButtonElement.FocusAsync();
        }

        if (_restorePreviewFocusOnNextRender)
        {
            // Same one-shot shape as the three flags above: ExitPreview sets this and
            // StateHasChanged's next render is the one that actually removes PreviewPane from
            // the DOM (the @if in FormDesigner.razor), so only THIS render is safe to move focus
            // back to the toggle button on. PreviewPane's own OnAfterRenderAsync is the mirror
            // image of this on the way in -- it focuses its own heading the moment it mounts,
            // without FormDesigner's help.
            _restorePreviewFocusOnNextRender = false;
            await _previewButtonElement.FocusAsync();
        }
    }

    /// <summary>
    /// Turns the palette's <see cref="Palette.FieldPalette.OnAddRequested"/> into a real
    /// <see cref="DesignerEditContext.AddNode"/> call, targeting <see cref="_activePageId"/>'s
    /// currently-selected section when there is one and it actually belongs to that page, or
    /// otherwise the page's last section — "add near what the author is already looking at",
    /// absent any stronger signal (PRD §4.1). A page with no section yet gets one first
    /// (<see cref="DesignerEditContext.AddSection"/>) so the palette always has somewhere to add
    /// into; a designer with no page at all, or before the draft has loaded, is a no-op, since
    /// <see cref="Palette.FieldPalette"/> has nothing to target either way.
    /// </summary>
    /// <param name="nodeType">
    /// The node type the author asked to add.
    /// </param>
    private void OnPaletteAddRequested(NodeType nodeType)
    {
        if (_editContext is null || _activePageId is null)
        {
            return;
        }

        var page = _editContext.Draft.Definition.Pages
            .FirstOrDefault(candidate => string.Equals(candidate.Id, _activePageId, StringComparison.Ordinal));

        if (page is null)
        {
            return;
        }

        var targetSectionId = ResolveTargetSectionId(page);

        if (targetSectionId is null)
        {
            _editContext.AddSection(page.Id);
            page = _editContext.Draft.Definition.Pages
                .First(candidate => string.Equals(candidate.Id, _activePageId, StringComparison.Ordinal));
            targetSectionId = page.Sections[^1].Id;
        }

        _editContext.AddNode(nodeType, targetSectionId);
    }

    /// <summary>
    /// Picks which of <paramref name="page"/>'s sections a palette add lands in -- see
    /// <see cref="OnPaletteAddRequested"/> for the policy. <see langword="null"/> means the page
    /// has no section at all yet.
    /// </summary>
    private string? ResolveTargetSectionId(FormPage page)
    {
        var selectedSectionId = _editContext!.Selection.SectionId;

        if (selectedSectionId is not null
            && page.Sections.Any(section => string.Equals(section.Id, selectedSectionId, StringComparison.Ordinal)))
        {
            return selectedSectionId;
        }

        return page.Sections.Count > 0 ? page.Sections[^1].Id : null;
    }

    /// <summary>
    /// Switches <see cref="_activePageId"/> when the author clicks a different tab in
    /// <see cref="Canvas.PageTabStrip"/> -- pure Designer view state, not a
    /// <see cref="DesignerEditContext"/> mutation (PRD §4.1). Adding a page or a section, by
    /// contrast, follows <see cref="DesignerEditContext.Selection"/> instead, via
    /// <see cref="SyncActivePageFromSelection"/>.
    /// </summary>
    /// <param name="pageId">
    /// The page the author just switched to.
    /// </param>
    private void OnActivePageIdChanged(string pageId) => _activePageId = pageId;

    /// <summary>
    /// Receives every lint pass's own results from the hosted <see cref="LinterDock"/>
    /// (<see cref="LinterDock.ResultsChanged"/>) and hands them straight down to
    /// <see cref="Canvas.DesignerCanvas.LintResults"/>, so a node's own inline findings come from
    /// the dock's exact lint pass instead of a second one this shell would otherwise have to run
    /// itself.
    /// </summary>
    /// <param name="results">
    /// The lint pass's results.
    /// </param>
    private void OnLintResultsChanged(IReadOnlyList<LintResult> results)
    {
        _lintResults = results;
        StateHasChanged();
    }

    /// <summary>
    /// Opens the keyboard-help dialog (PRD §4.1's "discoverable via an in-app dialog") -- the
    /// toolbar's own Help button.
    /// </summary>
    private void OpenKeyboardHelp() => _showKeyboardHelp = true;

    /// <summary>
    /// Closes the keyboard-help dialog -- its own Close button or <c>Esc</c> -- and arms
    /// <see cref="_restoreHelpFocusOnNextRender"/> so the very next render, once
    /// <c>KeyboardHelpDialog</c> has actually left the DOM, moves real DOM focus back to the
    /// Help button that opened it (PRD §11) rather than letting it fall to <c>&lt;body&gt;</c> --
    /// the same "re-arm a pending-focus flag, consume it in the following render" shape
    /// <c>DesignerCanvas.CloseMoveDialog</c>/<c>CloseDeleteDialog</c> use for their own dialogs.
    /// </summary>
    private void CloseKeyboardHelp()
    {
        _showKeyboardHelp = false;
        _restoreHelpFocusOnNextRender = true;
    }

    /// <summary>
    /// Opens the publish dialog (PRD §7) -- the toolbar's own Publish button. A no-op before the
    /// draft has loaded, since the button that calls this is only ever rendered once
    /// <see cref="_editContext"/> exists.
    /// </summary>
    private void OpenPublishDialog() => _showPublishDialog = true;

    /// <summary>
    /// Closes the publish dialog -- its own Cancel button or <c>Esc</c>, and the tail of a
    /// successful publish too (<see cref="OnDraftPublishedAsync"/> calls this after its own
    /// teardown-and-reload) -- and arms <see cref="_restorePublishFocusOnNextRender"/> so the very
    /// next render, once <see cref="PublishDialog"/> has actually left the DOM, moves real DOM
    /// focus back to the Publish button (PRD §11).
    /// </summary>
    private void ClosePublishDialog()
    {
        _showPublishDialog = false;
        _restorePublishFocusOnNextRender = true;
    }

    /// <summary>
    /// Reacts to <see cref="PublishDialog.OnPublished"/>: the draft <see cref="_editContext"/> was
    /// editing no longer exists as a draft at all (<see cref="IFormDefinitionStore.PublishAsync"/>
    /// consumes it), so this tears that context down exactly as <see cref="DisposeAsync"/> would
    /// its own, then re-runs <see cref="LoadDraftAsync"/> -- the very same store-miss path that
    /// already knows how to revise a form's latest published version into a fresh in-memory draft,
    /// which is now the version <paramref name="published"/> names. Bubbles the same version out
    /// through <see cref="OnPublished"/> for the host, after this shell's own state is consistent
    /// again.
    /// </summary>
    /// <param name="published">
    /// The version <see cref="PublishDialog"/> just published.
    /// </param>
    [SuppressMessage(
        "Reliability",
        "CA2007:Consider calling ConfigureAwait on the awaited task",
        Justification = "An event-callback handler resumes on the renderer's synchronization context, and must stay on it through to the LoadDraftAsync call, which itself schedules a render.")]
    private async Task OnDraftPublishedAsync(FormVersion published)
    {
        if (_editContext is not null)
        {
            _editContext.StateChanged -= OnEditContextStateChanged;
            _editContext.AutosaveFailed -= OnEditContextAutosaveFailed;
            await _editContext.DisposeAsync();
            _editContext = null;
        }

        await LoadDraftAsync();
        ClosePublishDialog();
        _editContext?.Announce(Localizer["PublishDialogVersionAnnouncement", published.Version].Value);
        await OnPublished.InvokeAsync(published);
    }

    /// <summary>
    /// Opens the version-history panel (PRD §7) -- the toolbar's own Version-history button.
    /// </summary>
    private void OpenVersionHistory() => _showVersionHistory = true;

    /// <summary>
    /// Closes the version-history panel -- its own Close button, <c>Esc</c>, or the tail of a
    /// completed "revise as draft" (<see cref="OnDraftRevisedAsync"/> calls this after its own
    /// context swap) -- and arms <see cref="_restoreVersionHistoryFocusOnNextRender"/> so the very
    /// next render moves real DOM focus back to the Version-history button (PRD §11).
    /// </summary>
    private void CloseVersionHistory()
    {
        _showVersionHistory = false;
        _restoreVersionHistoryFocusOnNextRender = true;
    }

    /// <summary>
    /// Reacts to <see cref="VersionHistory.OnRevised"/>: swaps this designer's whole editing
    /// session onto the new draft <see cref="VersionHistory"/> just saved, the same
    /// construct-and-subscribe sequence <see cref="LoadDraftAsync"/> uses for its own draft, minus
    /// the store round-trip <see cref="VersionHistory"/> already made. The version that draft's
    /// content was revised from is never touched (AGENTS.md invariant #3) -- this only ever
    /// replaces which draft <em>this designer instance</em> is looking at.
    /// </summary>
    /// <param name="revisedDraft">
    /// The new, unpublished draft <see cref="VersionHistory"/> just saved.
    /// </param>
    [SuppressMessage(
        "Reliability",
        "CA2007:Consider calling ConfigureAwait on the awaited task",
        Justification = "An event-callback handler resumes on the renderer's synchronization context, and must stay on it through to the CloseVersionHistory call at the end.")]
    private async Task OnDraftRevisedAsync(FormVersion revisedDraft)
    {
        if (_editContext is not null)
        {
            _editContext.StateChanged -= OnEditContextStateChanged;
            _editContext.AutosaveFailed -= OnEditContextAutosaveFailed;
            await _editContext.DisposeAsync();
        }

        _editContext = new DesignerEditContext(revisedDraft, _store, _disposalCts.Token);
        _editContext.StateChanged += OnEditContextStateChanged;
        _editContext.AutosaveFailed += OnEditContextAutosaveFailed;
        _activePageId = revisedDraft.Definition.Pages.Count > 0 ? revisedDraft.Definition.Pages[0].Id : null;
        CloseVersionHistory();
    }

    /// <summary>
    /// The toolbar's own Preview toggle (PRD §4.1, §4.2): a single <c>aria-pressed</c> button that
    /// both enters and leaves preview, rather than the separate open-button/dialog-close-button
    /// pair the keyboard-help, publish, and version-history affordances each use. Entering needs no
    /// flag of its own here -- <see cref="Preview.PreviewPane"/> moves focus to its own heading the
    /// moment it mounts; only leaving needs <see cref="ExitPreview"/>'s one-shot restore, the same
    /// asymmetry every one-shot-focus pair in this file has.
    /// </summary>
    private void TogglePreview()
    {
        if (_isPreviewing)
        {
            ExitPreview();
        }
        else
        {
            _isPreviewing = true;
        }
    }

    /// <summary>
    /// Leaves preview -- the toolbar toggle's own second click, or <see cref="Preview.PreviewPane"/>'s
    /// own Exit button (<see cref="Preview.PreviewPane.OnExit"/>) -- and arms
    /// <see cref="_restorePreviewFocusOnNextRender"/> so the very next render, once
    /// <see cref="Preview.PreviewPane"/> has actually left the DOM, moves real DOM focus back to
    /// the toggle button (PRD §11). Touches nothing on <see cref="_editContext"/>'s own draft --
    /// preview never mutates it, so there is nothing to roll back on exit, only test data inside
    /// the torn-down <see cref="Preview.PreviewPane"/> itself to discard.
    /// </summary>
    private void ExitPreview()
    {
        _isPreviewing = false;
        _restorePreviewFocusOnNextRender = true;
    }

    /// <summary>
    /// Keeps <see cref="_activePageId"/> pointed at whichever page
    /// <see cref="DesignerEditContext.Selection"/> currently names, so the tab strip and the
    /// canvas both follow a mutation's own selection (an added page, an added section) the same
    /// way <see cref="Canvas.DesignerCanvas"/> follows it down to the node level. Falls back to
    /// the draft's first page when the currently active one no longer exists and the selection
    /// does not name a page either -- both fresh loads and (in a later phase) a deleted active
    /// page land here.
    /// </summary>
    private void SyncActivePageFromSelection()
    {
        var pages = _editContext!.Draft.Definition.Pages;

        if (_editContext.Selection.PageId is { } selectedPageId
            && pages.Any(page => string.Equals(page.Id, selectedPageId, StringComparison.Ordinal)))
        {
            _activePageId = selectedPageId;
        }
        else if (_activePageId is null || !pages.Any(page => string.Equals(page.Id, _activePageId, StringComparison.Ordinal)))
        {
            _activePageId = pages.Count > 0 ? pages[0].Id : null;
        }
    }

    /// <summary>
    /// Loads <see cref="FormId"/>'s working draft, once, after interactivity — see the call site
    /// in <see cref="OnAfterRenderAsync"/> for why. A hit renders the store's draft as-is; a miss
    /// builds one purely in memory — revised from the form's latest published version when one
    /// exists, or from scratch (with the localized "Untitled form" fallback name) otherwise — and
    /// deliberately never saves it here. Persisting the created or revised draft happens on the
    /// first edit, in a later phase's autosave (PRD §7) — a mere open must never flip a published
    /// form's library status to "draft in progress" just from viewing it.
    /// </summary>
    [SuppressMessage(
        "Reliability",
        "CA2007:Consider calling ConfigureAwait on the awaited task",
        Justification = "Called only from OnAfterRenderAsync, which must itself resume on the renderer's synchronization context so the StateHasChanged() this method calls is safe to schedule.")]
    private async Task LoadDraftAsync()
    {
        var draft = await _store.GetDraftAsync(FormId, _disposalCts.Token);

        draft ??= await LoadOrCreateInMemoryDraftAsync();

        // The component may have been disposed while a real (async, over-the-wire) store was
        // mid-load. Bail before touching state or calling StateHasChanged on a torn-down
        // designer; the in-memory store completes synchronously so this only ever bites a real
        // host store.
        if (_disposed || _disposalCts.IsCancellationRequested)
        {
            return;
        }

        _editContext = new DesignerEditContext(draft, _store, _disposalCts.Token);
        _editContext.StateChanged += OnEditContextStateChanged;
        _editContext.AutosaveFailed += OnEditContextAutosaveFailed;
        _activePageId = draft.Definition.Pages.Count > 0 ? draft.Definition.Pages[0].Id : null;
        StateHasChanged();
    }

    /// <summary>
    /// Re-renders the shell whenever <see cref="_editContext"/> reports a mutation, after first
    /// following its selection to whichever page it now names (<see cref="SyncActivePageFromSelection"/>)
    /// so the tab strip and the canvas stay on the page a mutation just landed on. Dispatched
    /// through <see cref="ComponentBase.InvokeAsync(Action)"/> because
    /// <see cref="DesignerEditContext.StateChanged"/> is a plain <see cref="Action"/>, not a
    /// Blazor <see cref="EventCallback"/>, so nothing already guarantees this runs on the
    /// renderer's synchronization context.
    /// </summary>
    private void OnEditContextStateChanged() => InvokeAsync(() =>
    {
        SyncActivePageFromSelection();
        StateHasChanged();
    });

    /// <summary>
    /// Turns a raised <see cref="DesignerEditContext.AutosaveFailed"/> into the one thing an
    /// author actually needs right now: a polite aria-live announcement that their edit is still
    /// safe to keep making, just not yet written to the store (PRD §7, §11). Deliberately raises
    /// through <see cref="DesignerEditContext.Announce"/> rather than anything that blocks
    /// editing or surfaces the exception itself — there is nothing an author can do about e.g. a
    /// transient host outage, and a richer retry affordance is a later phase's concern. Dispatched
    /// through <see cref="ComponentBase.InvokeAsync(Action)"/> for the same reason
    /// <see cref="OnEditContextStateChanged"/> is — <see cref="DesignerEditContext.AutosaveFailed"/>
    /// is a plain <see cref="Action{T}"/>, not a Blazor <see cref="EventCallback"/>.
    /// </summary>
    /// <param name="exception">
    /// The store failure that was just observed. Unused beyond having fired at all — this phase
    /// shows the same plain-language message regardless of cause, and logging it is a host
    /// concern, not this component's.
    /// </param>
    private void OnEditContextAutosaveFailed(Exception exception) =>
        InvokeAsync(() => _editContext!.Announce(Localizer["AutosaveFailed"].Value));

    /// <summary>
    /// Builds the in-memory draft <see cref="LoadDraftAsync"/> falls back to on a store miss —
    /// split out purely so that method's single <c>await</c>-then-guard shape stays easy to read.
    /// </summary>
    [SuppressMessage(
        "Reliability",
        "CA2007:Consider calling ConfigureAwait on the awaited task",
        Justification = "Called only from LoadDraftAsync, which must itself resume on the renderer's synchronization context.")]
    private async Task<FormVersion> LoadOrCreateInMemoryDraftAsync()
    {
        var published = await _store.GetLatestPublishedVersionAsync(FormId, _disposalCts.Token);

        return published is not null
            ? FormLifecycle.ReviseAsDraft(published)
            : FormLifecycle.CreateDraft(new FormDefinition { Id = FormId, Name = Localizer["UntitledFormName"].Value });
    }

    /// <summary>
    /// Cancels any in-flight draft load via <see cref="_disposalCts"/> and disposes it, then
    /// unsubscribes from and disposes <see cref="_editContext"/> — the one
    /// <see cref="DesignerEditContext"/> this designer instance owns for its whole lifetime, so
    /// its pending autosave (if any) gets the same deterministic teardown
    /// <see cref="FormRenderer.DisposeAsync"/> gives its own draft I/O. Safe to call more than
    /// once, and safe even when the draft load never completed (<see cref="_editContext"/> is
    /// simply still <see langword="null"/> in that case).
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
        await _disposalCts.CancelAsync();
        _disposalCts.Dispose();

        if (_editContext is not null)
        {
            _editContext.StateChanged -= OnEditContextStateChanged;
            _editContext.AutosaveFailed -= OnEditContextAutosaveFailed;
            await _editContext.DisposeAsync();
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// The mutation engine this designer instance owns, once its draft has finished loading.
    /// <see langword="internal"/>, not <see langword="private"/>, purely so
    /// <c>FormDesignerTests</c> can assert the ownership and disposal contract directly, the same
    /// reason <c>FormRenderer.SubmitAsync</c> is <see langword="internal"/> rather than
    /// <see langword="private"/>.
    /// </summary>
    internal DesignerEditContext? EditContext => _editContext;
}
