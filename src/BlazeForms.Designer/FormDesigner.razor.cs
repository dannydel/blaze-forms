using System.Diagnostics.CodeAnalysis;
using BlazeForms.Definitions;
using BlazeForms.Hosting;
using BlazeForms.Internal;
using BlazeForms.Resources;
using BlazeForms.Versioning;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;

namespace BlazeForms;

/// <summary>
/// The designer shell: a three-pane docked layout — field palette, canvas, and properties panel
/// (PRD §4.1, D9 — docked is the only P1 layout). This phase renders the pane structure only;
/// canvas editing, the properties panel's field-specific controls, undo/redo, the linter dock,
/// and the publish dialog all land in later phases.
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
/// "draft in progress" as a side effect of viewing it. Persisting the in-memory draft on first
/// mutation lands with autosave in a later phase.
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
    private FormVersion? _draft;
    private readonly string _liveMessage = string.Empty;
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
    }

    /// <summary>
    /// Placeholder wiring for the palette's add-request event. Phase 1 has no canvas mutation
    /// pipeline yet (that lands in Phase 3) — this exists only so the parameter contract between
    /// <see cref="Palette.FieldPalette"/> and the shell carries the requested node type end to
    /// end before mutation logic exists to act on it.
    /// </summary>
    /// <param name="nodeType">
    /// The node type the author asked to add.
    /// </param>
    private static Task OnPaletteAddRequested(NodeType nodeType) => Task.CompletedTask;

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

        _draft = draft;
        StateHasChanged();
    }

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
    /// Cancels any in-flight draft load via <see cref="_disposalCts"/> and disposes it, mirroring
    /// <see cref="FormRenderer.DisposeAsync"/>. <see cref="FormDesigner"/> owns no JS module and
    /// no timer this phase — the cancellation source is the only thing here that needs a
    /// deterministic teardown.
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
        GC.SuppressFinalize(this);
    }
}
