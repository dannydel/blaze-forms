using System.Diagnostics.CodeAnalysis;
using BlazeForms.Components;
using BlazeForms.Definitions;
using BlazeForms.Expressions;
using BlazeForms.Fields;
using BlazeForms.Fields.Internal;
using BlazeForms.Hosting;
using BlazeForms.Internal;
using BlazeForms.Resources;
using BlazeForms.Serialization;
using BlazeForms.Versioning;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;

namespace BlazeForms;

/// <summary>
/// Fills one published <see cref="FormVersion"/>: pages as steps behind a progress header,
/// sections as <c>fieldset</c>/<c>legend</c>, and nodes through the registry-first component
/// resolver (PRD §4.2, §5, §10). Conditional visibility (PRD §6) is evaluated live against the
/// respondent's in-progress answers on every render — a hidden node is simply never emitted, so
/// it is excluded from the accessibility tree, from validation, and from the submission payload.
/// Validation runs on blur, on page-advance, and on submit; a failed page-advance or submit
/// renders a focusable error summary and blocks the corresponding action (PRD §4.2, §11). A
/// successful submit builds the submission envelope and hands it to
/// <see cref="OnSubmitted"/> and, when the host registered one, its <c>IFormSubmissionSink</c>.
/// </summary>
/// <remarks>
/// Fill drafts (<c>IFormDraftStore</c>, PRD §4.2, §9, D13) autosave on field blur and on every
/// page change, and resume once, after interactivity, when the host both registers a store and
/// supplies a non-<see langword="null"/> <see cref="RespondentKey"/> — an anonymous fill never
/// touches the store at all. Setting <see cref="Ephemeral"/> goes further still: it turns off
/// both optional host integrations (the draft store and the submission sink) unconditionally, so
/// a design-time preview of an unpublished draft — <c>BlazeForms.Designer</c>'s own preview pane
/// (PRD §4.1) — can render this exact component, with live logic and validation, over test data
/// that never touches the host at all.
/// </remarks>
public partial class FormRenderer : ComponentBase, IAsyncDisposable
{
    private readonly string _instanceId = "bf-renderer-" + Guid.NewGuid().ToString("n");
    private readonly Dictionary<string, object?> _values = new(StringComparer.Ordinal);
    private readonly Dictionary<string, EventCallback<object?>> _valueChangedCallbacks = new(StringComparer.Ordinal);
    private readonly Dictionary<string, EventCallback> _onBlurCallbacks = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _errors = new(StringComparer.Ordinal);
    private readonly HashSet<string> _validatedNodeIds = new(StringComparer.Ordinal);
    private int _currentPageIndex;
    private bool _focusPageHeadingOnNextRender;
    private bool _focusSummaryOnNextRender;
    private bool _focusConfirmationOnNextRender;
    private bool _showSummary;
    private ElementReference _pageHeadingElement;
    private ElementReference _confirmationElement;
    private ErrorSummary? _errorSummary;
    private FieldValidator _fieldValidator = default!;
    private DateTimeOffset _startedAt;
    private bool _isSubmitted;
    private FormSubmissionEnvelope? _submittedEnvelope;
    private bool _draftLoadAttempted;
    private bool _disposed;
    private readonly CancellationTokenSource _disposalCts = new();

    /// <summary>
    /// The host's optional submission sink, resolved once in <see cref="OnInitialized"/> from
    /// <see cref="ServiceProvider"/> rather than through <c>[Inject]</c> directly — see the
    /// remarks on <see cref="ServiceProvider"/> for why. Invoked alongside
    /// <see cref="OnSubmitted"/> on a successful submit, never instead of it, so a host can rely
    /// on either integration point without the other silently going unfired (PRD §9). Never
    /// resolved at all — stays <see langword="null"/> for this renderer's whole lifetime — when
    /// <see cref="Ephemeral"/> is <see langword="true"/>.
    /// </summary>
    private IFormSubmissionSink? _sink;

    /// <summary>
    /// The host's optional fill-draft store, resolved once in <see cref="OnInitialized"/> the
    /// same way as <see cref="_sink"/> — through <see cref="ServiceProvider"/> directly, never
    /// <c>[Inject]</c>, because it is genuinely optional (PRD §4.2, §9). <see langword="null"/>
    /// turns drafts off entirely: no load, no autosave, no delete — the same outcome
    /// <see cref="Ephemeral"/> forces unconditionally, by simply never resolving this at all.
    /// </summary>
    private IFormDraftStore? _draftStore;

    /// <summary>
    /// The clock <see cref="RecomputeCalculations"/> reads <see cref="CalcFunction.Today"/> from,
    /// resolved once in <see cref="OnInitialized"/> from <see cref="ServiceProvider"/> the same
    /// optional-service way as <see cref="_sink"/> and <see cref="_draftStore"/>, falling back to
    /// <see cref="TimeProvider.System"/> when a host registers none. Unlike those two, this is
    /// never gated on <see cref="Ephemeral"/> — a design-time preview still wants
    /// <c>today()</c>-only calculations to show a real date, and reading a clock has no host side
    /// effect for <see cref="Ephemeral"/> to guard against.
    /// </summary>
    private TimeProvider _timeProvider = TimeProvider.System;

    /// <summary>
    /// The published version to fill. The renderer holds this for the whole fill and never
    /// swaps the definition mid-fill (PRD D13) — a newer version publishing while a respondent
    /// is partway through never changes what they see or how it validates.
    /// </summary>
    [Parameter, EditorRequired]
    public FormVersion Version { get; set; } = default!;

    /// <summary>
    /// An opaque host-supplied key identifying the respondent. Carried through unchanged onto
    /// the submission envelope's <see cref="FormSubmissionEnvelope.RespondentKey"/>;
    /// <see langword="null"/> for a form the host does not track individual respondents on.
    /// </summary>
    [Parameter]
    public string? RespondentKey { get; set; }

    /// <summary>
    /// The host's optional field component overrides, forwarded to the same registry-first
    /// resolver the shipped field components resolve through (PRD §10). <see langword="null"/>
    /// renders every node with the shipped default component for its type.
    /// </summary>
    [Parameter]
    public IFieldComponentRegistry? FieldComponents { get; set; }

    /// <summary>
    /// When <see langword="true"/>, this renderer runs in a throwaway, design-time preview mode:
    /// it never resolves the host's <see cref="IFormSubmissionSink"/> or
    /// <see cref="IFormDraftStore"/> from <see cref="ServiceProvider"/> at all, so filling and
    /// submitting the current draft inside a designer's preview pane has zero host side
    /// effects — no submission ever reaches a registered sink, and no draft is ever loaded,
    /// autosaved, or deleted, however long the preview fill runs or however many times it
    /// submits.
    /// </summary>
    /// <remarks>
    /// <see cref="OnSubmitted"/> and <see cref="ConfirmationTemplate"/> still fire and render
    /// exactly as they would on a real fill — a preview still needs its own confirmation once its
    /// test data "submits" (PRD §4.1); only the two optional host integrations are skipped.
    /// Defaults to <see langword="false"/>, so every existing host that never sets this parameter
    /// keeps behaving exactly as it always has.
    /// </remarks>
    [Parameter]
    public bool Ephemeral { get; set; }

    /// <summary>
    /// Raised once, with the completed submission envelope, when the respondent submits a form
    /// that passes validation (PRD §4.2, §9). This is the primary submission contract — a host
    /// wires this up even when it has no <see cref="IFormSubmissionSink"/> registered in DI.
    /// </summary>
    [Parameter]
    public EventCallback<FormSubmissionEnvelope> OnSubmitted { get; set; }

    /// <summary>
    /// A host-templatable confirmation shown in place of the form once <see cref="OnSubmitted"/>
    /// has fired (PRD §4.2). Receives the submission envelope. <see langword="null"/> renders the
    /// shipped default confirmation text.
    /// </summary>
    [Parameter]
    public RenderFragment<FormSubmissionEnvelope>? ConfirmationTemplate { get; set; }

    /// <summary>
    /// The renderer chrome's localizer — the internal, host-immune
    /// <see cref="RendererLocalization.Shared"/> instance (PRD §12), not a DI-injected one (see
    /// its remarks for why a DI-injected <c>IStringLocalizer&lt;RendererStrings&gt;</c> is unsafe
    /// against a host's own <c>LocalizationOptions.ResourcesPath</c>). Kept as a property, rather
    /// than referencing <see cref="RendererLocalization.Shared"/> at each call site, purely so
    /// this file's and the markup's existing <c>Localizer[...]</c> expressions read unchanged.
    /// </summary>
    private static IStringLocalizer<RendererStrings> Localizer => RendererLocalization.Shared;

    /// <summary>
    /// Used once, in <see cref="OnInitialized"/>, to resolve <see cref="_sink"/>. <c>[Inject]</c>
    /// itself has no notion of an optional service — Blazor's property injector throws when
    /// nothing is registered for the property's type, nullable annotation or not — so an
    /// actually-optional dependency like <see cref="IFormSubmissionSink"/> has to be resolved
    /// through the raw service provider via <see cref="IServiceProvider.GetService(Type)"/>
    /// instead, which returns <see langword="null"/> rather than throwing.
    /// </summary>
    [Inject]
    private IServiceProvider ServiceProvider { get; set; } = default!;

    /// <summary>
    /// This fill's draft key, or <see langword="null"/> when <see cref="RespondentKey"/> is
    /// unset — an anonymous fill has nothing to key a draft by, so it is never persisted
    /// (PRD §4.2, §9). Always built from <see cref="Version"/>, which the renderer never swaps
    /// mid-fill, so <see cref="FormDraftKey.DefinitionVersion"/> stays pinned to the version this
    /// fill started on even if a newer one publishes meanwhile (PRD D13).
    /// </summary>
    private FormDraftKey? DraftKey => RespondentKey is null
        ? null
        : new FormDraftKey(Version.FormId, Version.Version, RespondentKey);

    private FormDefinition Definition => Version.Definition;

    private IReadOnlyList<FormPage> Pages => Definition.Pages;

    private FormPage? CurrentPage => _currentPageIndex >= 0 && _currentPageIndex < Pages.Count
        ? Pages[_currentPageIndex]
        : null;

    private bool IsFirstPage => _currentPageIndex <= 0;

    private bool IsLastPage => _currentPageIndex >= Pages.Count - 1;

    private string PageHeadingElementId => $"{_instanceId}-page-heading";

    /// <summary>
    /// The text an <c>aria-live="polite"</c> region announces whenever the current step
    /// changes, e.g. "Step 2 of 4: Contact information".
    /// </summary>
    private string StepAnnouncement => CurrentPage is null
        ? ""
        : $"Step {_currentPageIndex + 1} of {Pages.Count}: {PageTitleOrFallback(CurrentPage, _currentPageIndex)}";

    /// <summary>
    /// A page's title, falling back to a localized positional placeholder ("Page 2") when the
    /// author left <see cref="FormPage.Title"/> null — the same fallback
    /// <see cref="FormSubmissionView"/> uses for the same case (PRD §12). Without this, an
    /// untitled page would announce an empty step name and its progress-list entry and page
    /// heading would both render blank.
    /// </summary>
    private static string PageTitleOrFallback(FormPage page, int pageIndex) =>
        page.Title ?? Localizer["PageFallbackTitle", pageIndex + 1].Value;

    /// <summary>
    /// The error summary's entries, in document order, built fresh from the current answers on
    /// every render — a node currently hidden never appears here even if it carries a stale error
    /// from before it hid (PRD §6), and the anchors point at the same namespaced DOM id the
    /// field itself rendered with (§10.6). Empty whenever <see cref="_showSummary"/> is
    /// <see langword="false"/>: a lone blur validates and reveals only its own field's inline
    /// error (PRD §4.2) — the page-wide summary itself only ever appears as the direct result of
    /// a failed page-advance or submit, never as a side effect of blurring one field.
    /// </summary>
    private IReadOnlyList<ErrorSummaryEntry> SummaryEntries
    {
        get
        {
            if (!_showSummary)
            {
                return [];
            }

            var visibleNodeIds = GetVisibleNodeIds();
            var entries = new List<ErrorSummaryEntry>();

            foreach (var node in Definition.EnumerateNodes())
            {
                if (visibleNodeIds.Contains(node.Id) && _errors.TryGetValue(node.Id, out var message))
                {
                    entries.Add(new ErrorSummaryEntry(BuildFieldDomId(node.Id), message));
                }
            }

            return entries;
        }
    }

    /// <inheritdoc />
    protected override void OnInitialized()
    {
        _startedAt = DateTimeOffset.UtcNow;
        _fieldValidator = new FieldValidator(Localizer);

        // An ephemeral (preview) fill never resolves either optional host integration, no matter
        // what the host actually registered -- that is the entire point of Ephemeral. Leaving
        // both null here is what makes every downstream sink/draft-store call site's existing
        // null-guard (SubmitAsync, LoadDraftAsync, PersistDraftAsync, DeleteDraftAsync) a no-op
        // for free, with no separate Ephemeral check duplicated at each of them.
        if (!Ephemeral)
        {
            _sink = ServiceProvider.GetService(typeof(IFormSubmissionSink)) as IFormSubmissionSink;
            _draftStore = ServiceProvider.GetService(typeof(IFormDraftStore)) as IFormDraftStore;
        }

        _timeProvider = ServiceProvider.GetService(typeof(TimeProvider)) as TimeProvider ?? TimeProvider.System;

        // So a calc node whose expression depends on nothing but today() already shows a value
        // on the very first render, before the respondent has touched anything.
        RecomputeCalculations();
    }

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        var lastPageIndex = Math.Max(0, Pages.Count - 1);

        if (_currentPageIndex > lastPageIndex)
        {
            _currentPageIndex = lastPageIndex;
        }
    }

    /// <inheritdoc />
    [SuppressMessage(
        "Reliability",
        "CA2007:Consider calling ConfigureAwait on the awaited task",
        Justification = "A Blazor lifecycle method must resume on the renderer's synchronization context, not a captured-context-free one, so it can safely schedule the next render.")]
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender && !_draftLoadAttempted && !Ephemeral)
        {
            // Loading here rather than in OnInitializedAsync is what keeps this prerender-safe:
            // OnInitializedAsync runs twice under a prerender-then-resume host (once on the
            // server-rendered pass, again once the circuit reconnects), which would load the
            // draft twice and, worse, would run before there is any interactive circuit to
            // eventually persist back to. The explicit flag is a second, belt-and-suspenders
            // guard against ever awaiting LoadAsync more than once, on top of firstRender itself
            // only ever being true for the very first call (PRD §4.2). The Ephemeral check is a
            // second, belt-and-suspenders guard of its own on top of _draftStore already being
            // null under Ephemeral (see OnInitialized) -- a preview fill never even attempts the
            // call, rather than relying solely on LoadDraftAsync's own null-store guard.
            _draftLoadAttempted = true;
            await LoadDraftAsync();
        }

        if (_focusPageHeadingOnNextRender)
        {
            _focusPageHeadingOnNextRender = false;
            await _pageHeadingElement.FocusAsync();
        }

        if (_focusSummaryOnNextRender)
        {
            _focusSummaryOnNextRender = false;

            if (_errorSummary is not null)
            {
                await _errorSummary.FocusAsync();
            }
        }

        if (_focusConfirmationOnNextRender)
        {
            _focusConfirmationOnNextRender = false;

            // Only the shipped default confirmation renders `_confirmationElement` at all -- a
            // host-supplied ConfirmationTemplate owns its own markup and its own focus
            // management, exactly like a host-supplied field component owns its own a11y model.
            if (ConfirmationTemplate is null)
            {
                await _confirmationElement.FocusAsync();
            }
        }
    }

    /// <summary>
    /// Moves to the previous step, unguarded — nothing blocks moving backward. Moves focus to
    /// the new step's heading once it has rendered, so the step change is announced to
    /// assistive technology at the same moment the visible content changes. Autosaves the draft
    /// afterward (PRD §4.2, §9), same as <see cref="GoToNextPage"/>.
    /// </summary>
    [SuppressMessage(
        "Reliability",
        "CA2007:Consider calling ConfigureAwait on the awaited task",
        Justification = "A Blazor event handler must resume on the renderer's synchronization context so it can safely schedule the next render.")]
    private async Task GoToPreviousPage() => await GoToPage(_currentPageIndex - 1, focusHeading: true);

    /// <summary>
    /// Validates the current page's visible input nodes and, only if every one passes, moves to
    /// the next step and autosaves the draft (PRD §4.2, §9). A failure renders the error summary
    /// and moves focus to it instead of advancing, and never touches the draft.
    /// </summary>
    [SuppressMessage(
        "Reliability",
        "CA2007:Consider calling ConfigureAwait on the awaited task",
        Justification = "A Blazor event handler must resume on the renderer's synchronization context so it can safely schedule the next render.")]
    private async Task GoToNextPage()
    {
        if (_currentPageIndex + 1 >= Pages.Count)
        {
            return;
        }

        if (!ValidateCurrentPage())
        {
            _showSummary = true;
            _focusSummaryOnNextRender = true;
            return;
        }

        await GoToPage(_currentPageIndex + 1, focusHeading: true);
    }

    /// <summary>
    /// Validates every visible input node across every page plus the cross-field rules and,
    /// only if all of it passes, builds the submission envelope and dispatches it to
    /// <see cref="OnSubmitted"/> and <see cref="_sink"/> before showing the confirmation
    /// (PRD §4.2, §9). A failure navigates to the page holding the first offending field (if it
    /// is not already the current one) and renders the error summary there. Re-entrant while a
    /// prior call is still in flight — a fast double-click delivers two <c>onclick</c> dispatches
    /// before the first has re-rendered the button away — is a no-op: the guard on
    /// <see cref="_isSubmitted"/> is the first statement, before validation or envelope-building
    /// run again, so a second call can never build a second envelope or fire
    /// <see cref="OnSubmitted"/>/<see cref="_sink"/> a second time.
    /// </summary>
    /// <remarks>
    /// <c>internal</c>, not <c>private</c>, solely so <c>FormRendererSubmissionTests</c> can call
    /// it directly and deterministically prove the re-entry guard: the guard flag is set
    /// synchronously before the first genuine <c>await</c>, so calling this method a second time
    /// while the first call is still executing (even if the first has not itself completed) is
    /// guaranteed to observe it already set, regardless of the host's <see cref="OnSubmitted"/>
    /// handler timing — reproducing that race through the DOM alone would depend on exactly when
    /// the button element is removed relative to a second dispatched click, which bUnit cannot
    /// control deterministically.
    /// </remarks>
    [SuppressMessage(
        "Reliability",
        "CA2007:Consider calling ConfigureAwait on the awaited task",
        Justification = "A Blazor event handler must resume on the renderer's synchronization context so it can safely schedule the next render.")]
    internal async Task SubmitAsync()
    {
        if (_isSubmitted)
        {
            return;
        }

        if (!ValidateWholeForm())
        {
            _showSummary = true;
            await NavigateToFirstOffendingPage();
            _focusSummaryOnNextRender = true;
            return;
        }

        var envelope = BuildSubmissionEnvelope();
        _submittedEnvelope = envelope;
        _isSubmitted = true;
        _focusConfirmationOnNextRender = true;

        await OnSubmitted.InvokeAsync(envelope);

        if (_sink is not null)
        {
            await _sink.SubmitAsync(envelope);
        }

        // A completed fill leaves no resumable draft behind (PRD §4.2, §9) -- only when a store
        // and a respondent key both exist; DeleteDraftAsync is itself a no-op otherwise, same as
        // every other draft operation here.
        await DeleteDraftAsync();
    }

    /// <summary>
    /// Moves to <paramref name="pageIndex"/>, same bounds/no-op rules as before, then autosaves
    /// the draft (PRD §4.2, §9) so a page change — forward, backward, or the error-navigation
    /// path a failed submit takes — never leaves the store holding a stale page index.
    /// </summary>
    [SuppressMessage(
        "Reliability",
        "CA2007:Consider calling ConfigureAwait on the awaited task",
        Justification = "A Blazor event handler must resume on the renderer's synchronization context so it can safely schedule the next render.")]
    private async Task GoToPage(int pageIndex, bool focusHeading, bool resetSummary = true)
    {
        if (pageIndex < 0 || pageIndex >= Pages.Count || pageIndex == _currentPageIndex)
        {
            return;
        }

        _currentPageIndex = pageIndex;

        if (resetSummary)
        {
            _showSummary = false;
        }

        if (focusHeading)
        {
            _focusPageHeadingOnNextRender = true;
        }

        await PersistDraftAsync();
    }

    /// <summary>
    /// Validates every visible input node in <see cref="CurrentPage"/>, marking each one
    /// validated so its error (if any) becomes visible even though the respondent never blurred
    /// it directly (PRD §4.2).
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when none of the current page's visible input nodes carry an
    /// error.
    /// </returns>
    private bool ValidateCurrentPage()
    {
        if (CurrentPage is null)
        {
            return true;
        }

        var visibleNodeIds = GetVisibleNodeIds();
        var pageNodeIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var node in CurrentPage.Sections.SelectMany(section => section.EnumerateNodes()))
        {
            if (!visibleNodeIds.Contains(node.Id) || FieldValueConventions.GetStoredClrType(node.Type) is null)
            {
                continue;
            }

            pageNodeIds.Add(node.Id);
            _validatedNodeIds.Add(node.Id);
            ApplyFieldValidation(node);
        }

        PruneHiddenErrors(visibleNodeIds);

        return !pageNodeIds.Overlaps(_errors.Keys);
    }

    /// <summary>
    /// Validates every visible input node across every page, then the cross-field rules whose
    /// target is visible (PRD §4.2, §6). Cross-field rules run only here — a page-advance checks
    /// per-field validity alone.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when nothing currently visible carries an error.
    /// </returns>
    private bool ValidateWholeForm()
    {
        var visibleNodeIds = GetVisibleNodeIds();

        foreach (var node in Definition.EnumerateNodes())
        {
            if (!visibleNodeIds.Contains(node.Id) || FieldValueConventions.GetStoredClrType(node.Type) is null)
            {
                continue;
            }

            _validatedNodeIds.Add(node.Id);
            ApplyFieldValidation(node);
        }

        CrossFieldValidator.Evaluate(Definition, _values, visibleNodeIds, _errors);
        PruneHiddenErrors(visibleNodeIds);

        return _errors.Count == 0;
    }

    private void ApplyFieldValidation(FormNode node)
    {
        _values.TryGetValue(node.Id, out var value);
        var message = _fieldValidator.Validate(node, value, isVisible: true);

        if (message is null)
        {
            _errors.Remove(node.Id);
        }
        else
        {
            _errors[node.Id] = message;
        }
    }

    /// <summary>
    /// Drops every error whose node is not currently visible — a hidden node carries no
    /// validation state at all (PRD §6), so a field that hid after it last failed must not go on
    /// counting toward "the form has an error" or lingering in the summary.
    /// </summary>
    private void PruneHiddenErrors(HashSet<string> visibleNodeIds)
    {
        foreach (var nodeId in _errors.Keys.Where(id => !visibleNodeIds.Contains(id)).ToList())
        {
            _errors.Remove(nodeId);
        }
    }

    [SuppressMessage(
        "Reliability",
        "CA2007:Consider calling ConfigureAwait on the awaited task",
        Justification = "A Blazor event handler must resume on the renderer's synchronization context so it can safely schedule the next render.")]
    private async Task NavigateToFirstOffendingPage()
    {
        var firstOffendingNodeId = Definition.EnumerateNodes()
            .Select(node => node.Id)
            .FirstOrDefault(id => _errors.ContainsKey(id));

        if (firstOffendingNodeId is null)
        {
            return;
        }

        if (Definition.LocateNode(firstOffendingNodeId) is { PageIndex: var pageIndex }
            && pageIndex != _currentPageIndex)
        {
            // The summary itself takes focus once the page it now lives on has rendered, so the
            // page heading must not also compete for focus (focusHeading: false) — and the
            // summary the caller just asked to show must survive this navigation
            // (resetSummary: false), unlike the ordinary Previous/Next path.
            await GoToPage(pageIndex, focusHeading: false, resetSummary: false);
        }
    }

    private FormSubmissionEnvelope BuildSubmissionEnvelope()
    {
        var visiblePayload = VisibilityEvaluator.FilterToVisible(Definition, _values);

        return new FormSubmissionEnvelope
        {
            SubmissionId = FormIds.NewSubmissionId(),
            FormId = Version.FormId,
            DefinitionVersion = Version.Version,
            StartedAt = _startedAt,
            SubmittedAt = DateTimeOffset.UtcNow,
            Values = FormValues.ToJsonValues(visiblePayload),
            RespondentKey = RespondentKey,
        };
    }

    /// <summary>
    /// Handles the blur of one field's control: validates that field alone and marks it
    /// validated, so its error (if any) becomes visible from this point on even though the
    /// respondent has not yet advanced the page or submitted (PRD §4.2). Autosaves the draft
    /// afterward (PRD §9).
    /// </summary>
    [SuppressMessage(
        "Reliability",
        "CA2007:Consider calling ConfigureAwait on the awaited task",
        Justification = "A Blazor event handler must resume on the renderer's synchronization context so it can safely schedule the next render.")]
    private async Task HandleBlur(string nodeId)
    {
        var node = Definition.FindNode(nodeId);

        if (node is null)
        {
            return;
        }

        _validatedNodeIds.Add(nodeId);
        ApplyFieldValidation(node);

        await PersistDraftAsync();
    }

    /// <summary>
    /// Resumes a returning respondent's in-progress fill, once, after interactivity — see the
    /// call site in <see cref="OnAfterRenderAsync"/> for why. A miss (nothing to resume, no
    /// store registered, or an anonymous fill) leaves every field exactly as it started.
    /// </summary>
    [SuppressMessage(
        "Reliability",
        "CA2007:Consider calling ConfigureAwait on the awaited task",
        Justification = "Called only from OnAfterRenderAsync, which must itself resume on the renderer's synchronization context so the StateHasChanged() this method calls on a hit is safe to schedule.")]
    private async Task LoadDraftAsync()
    {
        if (_draftStore is null || DraftKey is not { } key)
        {
            return;
        }

        var draft = await _draftStore.LoadAsync(key, _disposalCts.Token);

        // The component may have been disposed while a real (async, over-the-wire) store was
        // mid-load. Bail before touching state or calling StateHasChanged on a torn-down renderer;
        // the in-memory store completes synchronously so this only ever bites a real host store.
        if (draft is null || _disposed || _disposalCts.IsCancellationRequested)
        {
            return;
        }

        foreach (var pair in FormValues.FromJsonValues(draft.Values))
        {
            _values[pair.Key] = pair.Value;
        }

        // Pinned to the version the fill started on (PRD D13) means the page shape cannot
        // actually have changed since the draft was saved, but the clamp is defensive rather
        // than load-bearing on that guarantee.
        var lastPageIndex = Math.Max(0, Pages.Count - 1);
        _currentPageIndex = Math.Clamp(draft.CurrentPageIndex, 0, lastPageIndex);

        // The eventual submission envelope must report when the respondent originally started
        // the fill, not when they resumed it.
        _startedAt = draft.StartedAt;

        // The resumed answers may feed a calc node the respondent never has to touch again --
        // recompute before the first post-resume render rather than waiting for a SetValue that
        // may never come.
        RecomputeCalculations();

        StateHasChanged();
    }

    /// <summary>
    /// Autosaves the in-progress fill, keyed by <see cref="DraftKey"/>, whenever a store is
    /// registered and the fill is not anonymous (PRD §4.2, §9). Persists the raw, unfiltered
    /// answers — including an answer to a field currently hidden by logic — so resuming restores
    /// exactly what the respondent typed; only the eventual submission envelope filters a hidden
    /// answer out (<see cref="BuildSubmissionEnvelope"/>).
    /// </summary>
    [SuppressMessage(
        "Reliability",
        "CA2007:Consider calling ConfigureAwait on the awaited task",
        Justification = "Called only from Blazor event handlers that must resume on the renderer's synchronization context to safely schedule the next render.")]
    private async Task PersistDraftAsync()
    {
        if (_draftStore is null || DraftKey is not { } key)
        {
            return;
        }

        var draft = new FormDraft
        {
            Key = key,
            StartedAt = _startedAt,
            UpdatedAt = DateTimeOffset.UtcNow,
            Values = FormValues.ToJsonValues(_values),
            CurrentPageIndex = _currentPageIndex,
        };

        await _draftStore.SaveAsync(draft, _disposalCts.Token);
    }

    /// <summary>
    /// Discards the draft once a fill completes (PRD §4.2, §9), so a submitted fill leaves no
    /// resumable draft behind. A no-op under the same conditions <see cref="PersistDraftAsync"/>
    /// and <see cref="LoadDraftAsync"/> are.
    /// </summary>
    [SuppressMessage(
        "Reliability",
        "CA2007:Consider calling ConfigureAwait on the awaited task",
        Justification = "Called only from SubmitAsync, which must resume on the renderer's synchronization context to safely schedule the next render.")]
    private async Task DeleteDraftAsync()
    {
        if (_draftStore is null || DraftKey is not { } key)
        {
            return;
        }

        await _draftStore.DeleteAsync(key, _disposalCts.Token);
    }

    /// <summary>
    /// Resolves the component type to render for a node, honoring <see cref="FieldComponents"/>
    /// via the same registry-first resolver every shipped field component resolves through.
    /// </summary>
    private Type ResolveComponentType(FormNode node) => DefaultFieldComponents.Resolve(node.Type, FieldComponents);

    /// <summary>
    /// The stable DOM id a field renders its primary control with, namespaced to this renderer
    /// instance (§10.6) so two <see cref="FormRenderer"/> instances of the same definition on one
    /// page never collide on <c>id</c>. <see cref="_values"/> and the submission envelope stay
    /// keyed by the raw <paramref name="nodeId"/> — only the DOM id is namespaced.
    /// </summary>
    private string BuildFieldDomId(string nodeId) => $"{_instanceId}-{nodeId}";

    /// <summary>
    /// Every node currently visible to the respondent, across the whole definition — not just
    /// the current page — as decided purely by <see cref="VisibilityEvaluator"/>. The renderer
    /// never reimplements condition logic; it only decides which of the current page's nodes to
    /// emit against this set.
    /// </summary>
    /// <remarks>
    /// Visibility is settled to a fixed point via
    /// <see cref="VisibilityEvaluator.FilterToVisible"/> before the visible-node set is taken,
    /// because <see cref="_values"/> is only ever written, never pruned when a field hides. A raw
    /// pass would let a stale answer to a now-hidden field keep a field whose rule points at it
    /// visible — a chain a → b → c would leak c into the DOM and the accessibility tree after a
    /// hides b, exactly the leak PRD §6 forbids. Settling first also keeps what the respondent
    /// sees identical to what the submission payload will contain.
    /// </remarks>
    private HashSet<string> GetVisibleNodeIds()
    {
        var settledValues = VisibilityEvaluator.FilterToVisible(Definition, _values);
        return VisibilityEvaluator.GetVisibleNodes(Definition, settledValues)
            .Select(node => node.Id)
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// Builds the parameter set a <c>DynamicComponent</c> hands to the field or static-content
    /// component resolved for <paramref name="node"/>. <see cref="FormFieldBase.Value"/>,
    /// <see cref="FormFieldBase.ValueChanged"/>, <see cref="FormFieldBase.OnBlur"/>, and
    /// <see cref="FormFieldBase.Error"/> are wired only for node types that actually capture a
    /// respondent-typed answer — static content and <see cref="NodeType.Calc"/> never seed a
    /// payload key in <see cref="_values"/> through the field component and are never validated.
    /// A <see cref="NodeType.Calc"/> node is its own case: it still gets a
    /// <see cref="FormFieldBase.Value"/> — its own formatted display text, computed by
    /// <see cref="RecomputeCalculations"/> and formatted by
    /// <see cref="Fields.Internal.CalcDisplayFormatter"/> — but never
    /// <see cref="FormFieldBase.ValueChanged"/>, <see cref="FormFieldBase.OnBlur"/>, or
    /// <see cref="FormFieldBase.Error"/>, since nothing the respondent does to a read-only
    /// calculated field is ever an answer to validate (PRD §5, decision log D-E, #5).
    /// </summary>
    private Dictionary<string, object> BuildFieldParameters(FormNode node)
    {
        var parameters = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            [nameof(FormFieldBase.Node)] = node,
            [nameof(FormFieldBase.FieldId)] = BuildFieldDomId(node.Id),
        };

        if (FieldValueConventions.GetStoredClrType(node.Type) is not null)
        {
            _values.TryGetValue(node.Id, out var value);
            parameters[nameof(FormFieldBase.Value)] = value!;
            parameters[nameof(FormFieldBase.ValueChanged)] = GetValueChangedCallback(node.Id);
            parameters[nameof(FormFieldBase.OnBlur)] = GetOnBlurCallback(node.Id);
            parameters[nameof(FormFieldBase.Error)] = GetFieldError(node.Id)!;
        }
        else if (node.Type == NodeType.Calc)
        {
            _values.TryGetValue(node.Id, out var computed);
            var formatted = node.Calculation is null ? null : CalcDisplayFormatter.Format(computed, node.Calculation.Format);
            parameters[nameof(FormFieldBase.Value)] = formatted!;
        }

        return parameters;
    }

    /// <summary>
    /// The error currently attached to a node, or <see langword="null"/> when it has none or the
    /// respondent has not yet had it validated — a field the respondent has not blurred, and that
    /// no page-advance or submit has checked yet, never shows an error even if answering it would
    /// fail (PRD §4.2).
    /// </summary>
    private string? GetFieldError(string nodeId) =>
        _validatedNodeIds.Contains(nodeId) ? _errors.GetValueOrDefault(nodeId) : null;

    /// <summary>
    /// Returns the same <see cref="EventCallback{T}"/> instance for a given node on every call,
    /// caching it the first time the node is encountered. A fresh delegate every render would be
    /// functionally harmless — <see cref="FormFieldBase.HaveSharedParametersChanged"/> does not
    /// compare it — but a stable instance means <c>DynamicComponent</c> hands the underlying
    /// field component the exact same callback whenever nothing about the node changed, rather
    /// than manufacturing one only to be excluded from a comparison downstream.
    /// </summary>
    private EventCallback<object?> GetValueChangedCallback(string nodeId)
    {
        if (!_valueChangedCallbacks.TryGetValue(nodeId, out var callback))
        {
            callback = EventCallback.Factory.Create<object?>(this, (object? value) => SetValue(nodeId, value));
            _valueChangedCallbacks[nodeId] = callback;
        }

        return callback;
    }

    /// <summary>
    /// Returns the same <see cref="EventCallback"/> instance for a given node on every call, for
    /// the same reason <see cref="GetValueChangedCallback"/> does.
    /// </summary>
    private EventCallback GetOnBlurCallback(string nodeId)
    {
        if (!_onBlurCallbacks.TryGetValue(nodeId, out var callback))
        {
            callback = EventCallback.Factory.Create(this, () => HandleBlur(nodeId));
            _onBlurCallbacks[nodeId] = callback;
        }

        return callback;
    }

    private void SetValue(string nodeId, object? value)
    {
        _values[nodeId] = value;
        RecomputeCalculations();
    }

    /// <summary>
    /// Recomputes every <see cref="NodeType.Calc"/> node's value via
    /// <see cref="CalcEvaluator.EvaluateAll"/> and writes each result straight back into
    /// <see cref="_values"/>, keyed by the calc node's own ID (PRD §5, decision log D-D). This is
    /// capture-at-submit, not recompute-at-view: a computed value lands in exactly the same
    /// dictionary a typed answer would, so it flows through <see cref="VisibilityEvaluator"/> and
    /// into <see cref="BuildSubmissionEnvelope"/> like any other answer — a visible calc node's
    /// value is captured into the envelope, a hidden one is filtered out, and a calc value is
    /// available to a <see cref="FormNode.VisibleWhen"/> or cross-field rule with no extra wiring
    /// here at all. Called after every answer change (<see cref="SetValue"/>), after a draft
    /// resumes (<see cref="LoadDraftAsync"/>), and once in <see cref="OnInitialized"/> so a
    /// <c>today()</c>-only calculation already has a value before the respondent types anything.
    /// </summary>
    private void RecomputeCalculations()
    {
        var today = DateOnly.FromDateTime(_timeProvider.GetLocalNow().DateTime);

        foreach (var (nodeId, value) in CalcEvaluator.EvaluateAll(Definition, _values, today))
        {
            _values[nodeId] = value;
        }
    }

    /// <summary>
    /// Cancels any in-flight draft load/save/delete via <see cref="_disposalCts"/> and disposes
    /// it. <see cref="FormRenderer"/> owns no JS module and no timer this phase — the
    /// cancellation source is the only thing here that needs a deterministic teardown.
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
