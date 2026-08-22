using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
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
/// resolver. Conditional visibility is evaluated live against the
/// respondent's in-progress answers on every render — a hidden node is simply never emitted, so
/// it is excluded from the accessibility tree, from validation, and from the submission payload.
/// Validation runs on blur, on page-advance, and on submit; a failed page-advance or submit
/// renders a focusable error summary and blocks the corresponding action. A
/// successful submit builds the submission envelope and hands it to
/// <see cref="OnSubmitted"/> and, when the host registered one, its <c>IFormSubmissionSink</c>.
/// </summary>
public sealed partial class FormRenderer : ComponentBase, IAsyncDisposable
{
    private readonly string _instanceId = "bf-renderer-" + Guid.NewGuid().ToString("n");
    private readonly Dictionary<string, object?> _values = new(StringComparer.Ordinal);
    private readonly Dictionary<string, EventCallback<object?>> _valueChangedCallbacks = new(StringComparer.Ordinal);
    private readonly Dictionary<string, EventCallback> _onBlurCallbacks = new(StringComparer.Ordinal);
    private readonly Dictionary<string, EventCallback<object?>> _repeatingChildValueChangedCallbacks = new(StringComparer.Ordinal);
    private readonly Dictionary<string, EventCallback> _repeatingChildBlurCallbacks = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _errors = new(StringComparer.Ordinal);
    private readonly HashSet<string> _validatedNodeIds = new(StringComparer.Ordinal);
    private int _currentPageIndex;
    private bool _focusPageHeadingOnNextRender;
    private bool _focusSummaryOnNextRender;
    private bool _focusConfirmationOnNextRender;
    private bool _showSummary;
    private ElementReference _pageHeadingElement;
    private ElementReference _confirmationElement;
    private string _calcAnnouncement = "";
    private string _repeatingRowAnnouncement = "";
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
    /// on either integration point without the other silently going unfired. Never
    /// resolved at all — stays <see langword="null"/> for this renderer's whole lifetime — when
    /// <see cref="Ephemeral"/> is <see langword="true"/>.
    /// </summary>
    private IFormSubmissionSink? _sink;

    /// <summary>
    /// The host's optional fill-draft store, resolved once in <see cref="OnInitialized"/> the
    /// same way as <see cref="_sink"/> — through <see cref="ServiceProvider"/> directly, never
    /// <c>[Inject]</c>, because it is genuinely optional. <see langword="null"/>
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
    /// swaps the definition mid-fill — a newer version publishing while a respondent
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
    /// resolver the shipped field components resolve through. <see langword="null"/>
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
    [Parameter]
    public bool Ephemeral { get; set; }

    /// <summary>
    /// Raised once, with the completed submission envelope, when the respondent submits a form
    /// that passes validation. This is the primary submission contract — a host
    /// wires this up even when it has no <see cref="IFormSubmissionSink"/> registered in DI.
    /// </summary>
    [Parameter]
    public EventCallback<FormSubmissionEnvelope> OnSubmitted { get; set; }

    /// <summary>
    /// A host-templatable confirmation shown in place of the form once <see cref="OnSubmitted"/>
    /// has fired. Receives the submission envelope. <see langword="null"/> renders the
    /// shipped default confirmation text.
    /// </summary>
    [Parameter]
    public RenderFragment<FormSubmissionEnvelope>? ConfirmationTemplate { get; set; }

    /// <summary>
    /// The renderer chrome's localizer — the internal, host-immune
    /// <see cref="RendererLocalization.Shared"/> instance, not a DI-injected one (see
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
    /// Always built from <see cref="Version"/>, which the renderer never swaps.
    /// mid-fill, so <see cref="FormDraftKey.DefinitionVersion"/> stays pinned to the version this
    /// fill started on even if a newer one publishes meanwhile.
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
    /// <see cref="FormSubmissionView"/> uses for the same case. Without this, an
    /// untitled page would announce an empty step name and its progress-list entry and page
    /// heading would both render blank.
    /// </summary>
    private static string PageTitleOrFallback(FormPage page, int pageIndex) =>
        page.Title ?? Localizer["PageFallbackTitle", pageIndex + 1].Value;

    /// <summary>
    /// The error summary's entries, in document order, built fresh from the current answers on
    /// every render — a node currently hidden never appears here even if it carries a stale error
    /// from before it hid, and the anchors point at the same namespaced DOM id the
    /// field itself rendered with. Empty whenever <see cref="_showSummary"/> is
    /// <see langword="false"/>: a lone blur validates and reveals only its own field's inline
    /// error — the page-wide summary itself only ever appears as the direct result of
    /// a failed page-advance or submit, never as a side effect of blurring one field.
    /// </summary>
    private IReadOnlyList<ErrorSummaryEntry> SummaryEntries
    {
        get
        {
            if (!_showSummary)
            {
                return Array.Empty<ErrorSummaryEntry>();
            }

            var visibleNodeIds = GetVisibleNodeIds(out var settledValues);
            var entries = new List<ErrorSummaryEntry>();

            foreach (var node in Definition.EnumerateNodes())
            {
                if (node.Type == NodeType.Repeating)
                {
                    if (!visibleNodeIds.Contains(node.Id))
                    {
                        continue;
                    }

                    if (_errors.TryGetValue(node.Id, out var groupMessage))
                    {
                        entries.Add(new ErrorSummaryEntry(BuildFieldDomId(node.Id), groupMessage));
                    }

                    // A host-registered Repeating override owns its own per-child validation
                    // (IsRepeatingComponentRegisteredByHost's remarks) — ValidateRepeatingGroup
                    // never writes a composite-key error for one, so this walk would find nothing
                    // to add anyway; skipping it outright avoids the wasted per-row work.
                    if (IsRepeatingComponentRegisteredByHost)
                    {
                        continue;
                    }

                    foreach (var row in GetRepeatingRows(node).Rows)
                    {
                        foreach (var childId in VisibilityEvaluator.GetVisibleChildIds(node, row, settledValues))
                        {
                            var key = RepeatingFieldKeys.ChildKey(childId, row.RowId);

                            if (_errors.TryGetValue(key, out var childMessage))
                            {
                                entries.Add(new ErrorSummaryEntry(BuildRepeatingChildDomId(childId, row.RowId), childMessage));
                            }
                        }
                    }

                    continue;
                }

                if (visibleNodeIds.Contains(node.Id) && _errors.TryGetValue(node.Id, out var message))
                {
                    entries.Add(new ErrorSummaryEntry(BuildFieldDomId(node.Id), message));
                }
            }

            return entries;
        }
    }

    /// <summary>
    /// Hydrates the raw values loaded from a draft store into CLR types suitable
    /// for validation and eventual submission. A draft store always persists
    /// raw JSON values, so a date field's value is a string in the store, but
    /// it must be hydrated to a <see cref="DateOnly"/> for validation and submission.
    /// </summary>
    private Dictionary<string, object?> HydrateDraftValues(
        IReadOnlyDictionary<string, JsonElement> values)
    {
        // Enumerate nodes on dictionary
        var nodes = Definition.EnumerateNodes().ToDictionary(node => node.Id, StringComparer.Ordinal);

        var rawValues = FormValues.FromJsonValues(values);
        var hydratedValues = new Dictionary<string, object?>(StringComparer.Ordinal);

        // Loop through and hydrate any that are present in the definition.
        foreach(var pair in rawValues){
            hydratedValues[pair.Key] = nodes.TryGetValue(pair.Key, out var node)
                ? HydrateValue(node, pair.Value)
                : pair.Value;
        }
        return hydratedValues;
    }

    /// <summary>
    /// Hydrates a single value from the draft store, if it is a type that needs hydration.
    /// This is used when hydrating the entire draft values dictionary. A
    /// <see cref="NodeType.Repeating"/> group's stored value recurses into each row and
    /// re-hydrates its own children by their type, reusing the same <see cref="NodeType.Date"/>
    /// case below per child — a resumed draft's date answers inside a row need exactly the same
    /// <see cref="DateOnly"/> hydration a top-level date answer does.
    /// </summary>
    private static object? HydrateValue(FormNode node, object? value)
    {
        if (node.Type == NodeType.Repeating && value is RepeatingRows rows)
        {
            return HydrateRepeatingRows(node, rows);
        }

        return HydrateScalarValue(node.Type, value);
    }

    private static RepeatingRows HydrateRepeatingRows(FormNode group, RepeatingRows rows)
    {
        var childrenById = group.Children.ToDictionary(child => child.Id, StringComparer.Ordinal);
        var hydratedRows = new List<RepeatingRow>(rows.Rows.Count);

        foreach (var row in rows.Rows)
        {
            var hydratedValues = new Dictionary<string, object?>(StringComparer.Ordinal);

            foreach (var pair in row.Values)
            {
                hydratedValues[pair.Key] = childrenById.TryGetValue(pair.Key, out var child)
                    ? HydrateValue(child, pair.Value)
                    : pair.Value;
            }

            hydratedRows.Add(row with { Values = hydratedValues });
        }

        return rows with { Rows = hydratedRows };
    }

    private static object? HydrateScalarValue(NodeType type, object? value) =>
    type == NodeType.Date
    && value is string text
    && DateOnly.TryParseExact(
        text,
        "yyyy-MM-dd",
        CultureInfo.InvariantCulture,
        DateTimeStyles.None,
        out var date)
        ? date
        : value;


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

        SeedRepeatingGroups();

        // So a calc node whose expression depends on nothing but today() already shows a value
        // on the very first render, before the respondent has touched anything.
        RecomputeCalculations();
    }

    /// <summary>
    /// Seeds every <see cref="NodeType.Repeating"/> group's initial answer to
    /// <see cref="FormNode.MinRows"/> (or zero, when unset) fresh rows via
    /// <see cref="RepeatingRows.Empty"/>. Runs once, here, rather than as part of
    /// <see cref="VisibilityEvaluator.FilterToVisible"/> — seeding is this renderer's own
    /// fill-time concern, not a pure visibility computation. A resumed draft's own repeating
    /// value (<see cref="LoadDraftAsync"/>) overwrites this seed entirely, since that runs later
    /// and unconditionally assigns every key the draft carries.
    /// </summary>
    private void SeedRepeatingGroups()
    {
        foreach (var node in Definition.EnumerateNodes())
        {
            if (node.Type != NodeType.Repeating || _values.ContainsKey(node.Id))
            {
                continue;
            }

            var seeded = RepeatingRows.Empty;

            for (var i = 0; i < (node.MinRows ?? 0); i++)
            {
                seeded = seeded.AddRow();
            }

            _values[node.Id] = seeded;
        }
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
            // Loading here rather than in OnInitializedAsync is what keeps this prerender-safe.
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
    /// afterward, same as <see cref="GoToNextPage"/>.
    /// </summary>
    [SuppressMessage(
        "Reliability",
        "CA2007:Consider calling ConfigureAwait on the awaited task",
        Justification = "A Blazor event handler must resume on the renderer's synchronization context so it can safely schedule the next render.")]
    private async Task GoToPreviousPage() => await GoToPage(_currentPageIndex - 1, focusHeading: true);

    /// <summary>
    /// Validates the current page's visible input nodes and, only if every one passes, moves to
    /// the next step and autosaves the draft. A failure renders the error summary
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
    /// <see cref="OnSubmitted"/> and <see cref="_sink"/> before showing the confirmation.
    /// A failure navigates to the page holding the first offending field (if it
    /// is not already the current one) and renders the error summary there. Re-entrant while a
    /// prior call is still in flight — a fast double-click delivers two <c>onclick</c> dispatches
    /// before the first has re-rendered the button away — is a no-op: the guard on
    /// <see cref="_isSubmitted"/> is the first statement, before validation or envelope-building
    /// run again, so a second call can never build a second envelope or fire
    /// <see cref="OnSubmitted"/>/<see cref="_sink"/> a second time.
    /// </summary>
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

        await DeleteDraftAsync();
    }

    /// <summary>
    /// Moves to <paramref name="pageIndex"/>, same bounds/no-op rules as before, then autosaves
    /// the draft so a page change — forward, backward, or the error-navigation
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
    /// it directly.
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

        var visibleNodeIds = GetVisibleNodeIds(out var settledValues);
        var pageNodeIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var node in CurrentPage.Sections.SelectMany(section => section.Nodes))
        {
            if (!visibleNodeIds.Contains(node.Id))
            {
                continue;
            }

            if (node.Type == NodeType.Repeating)
            {
                pageNodeIds.UnionWith(ValidateRepeatingGroup(node, settledValues));
                continue;
            }

            if (FieldValueConventions.GetStoredClrType(node.Type) is null)
            {
                continue;
            }

            pageNodeIds.Add(node.Id);
            _validatedNodeIds.Add(node.Id);
            ApplyFieldValidation(node);
        }

        PruneHiddenErrors(visibleNodeIds, settledValues);

        return !pageNodeIds.Overlaps(_errors.Keys);
    }

    /// <summary>
    /// Validates every visible input node across every page, then the cross-field rules whose
    /// target is visible. Cross-field rules run only here — a page-advance checks
    /// per-field validity alone.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when nothing currently visible carries an error.
    /// </returns>
    private bool ValidateWholeForm()
    {
        var visibleNodeIds = GetVisibleNodeIds(out var settledValues);

        foreach (var node in Definition.Pages.SelectMany(page => page.Sections).SelectMany(section => section.Nodes))
        {
            if (!visibleNodeIds.Contains(node.Id))
            {
                continue;
            }

            if (node.Type == NodeType.Repeating)
            {
                ValidateRepeatingGroup(node, settledValues);
                continue;
            }

            if (FieldValueConventions.GetStoredClrType(node.Type) is null)
            {
                continue;
            }

            _validatedNodeIds.Add(node.Id);
            ApplyFieldValidation(node);
        }

        CrossFieldValidator.Evaluate(Definition, _values, settledValues, visibleNodeIds, _errors);
        PruneHiddenErrors(visibleNodeIds, settledValues);

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
    /// Validates one visible <see cref="NodeType.Repeating"/> group: the group-level row-count
    /// rule (<see cref="ValidateRepeatingRowCount"/>), then — unless
    /// <see cref="IsRepeatingComponentRegisteredByHost"/> — every visible child of every row,
    /// row-scoped (<see cref="VisibilityEvaluator.GetVisibleChildIds"/>), keyed by
    /// <see cref="RepeatingFieldKeys.ChildKey"/>. A host-registered override never gets composite
    /// per-child errors here: it renders through its own <c>DynamicComponent</c>, at its own DOM
    /// ids, and gets only the single group-level <see cref="FormFieldBase.Error"/> — a composite
    /// key this method never writes could otherwise block submit invisibly (nothing in the host's
    /// own markup carries that error) and would resolve to a dead error-summary anchor pointing
    /// at a DOM id (<see cref="BuildRepeatingChildDomId"/>) the host's component never renders
    /// (repeating-groups-plan.md's "Risks": a host-registered Repeating component owns its own
    /// per-child validation entirely).
    /// </summary>
    /// <param name="group">
    /// The group to validate.
    /// </param>
    /// <param name="outerValues">
    /// The settled outer answers — the same settled dictionary the caller's own
    /// <c>GetVisibleNodeIds</c> out-overload already computed for this pass — to resolve each
    /// row's own child visibility against. Never the raw answer store, so a row child's own
    /// <c>VisibleWhen</c> naming a conditionally hidden outer field agrees exactly with what
    /// <see cref="BuildSubmissionEnvelope"/> will actually capture.
    /// </param>
    /// <returns>
    /// Every key this call touched — the group's own id plus, when not host-overridden, one
    /// composite key per validated child — for a caller (<see cref="ValidateCurrentPage"/>) that
    /// needs to know whether any of them now carries an error.
    /// </returns>
    private HashSet<string> ValidateRepeatingGroup(FormNode group, IReadOnlyDictionary<string, object?> outerValues)
    {
        var touchedKeys = new HashSet<string>(StringComparer.Ordinal) { group.Id };
        ValidateRepeatingRowCount(group);

        if (IsRepeatingComponentRegisteredByHost)
        {
            return touchedKeys;
        }

        foreach (var row in GetRepeatingRows(group).Rows)
        {
            foreach (var childId in VisibilityEvaluator.GetVisibleChildIds(group, row, outerValues))
            {
                var child = FindChild(group, childId);

                if (child is null || FieldValueConventions.GetStoredClrType(child.Type) is null)
                {
                    continue;
                }

                var key = RepeatingFieldKeys.ChildKey(childId, row.RowId);
                touchedKeys.Add(key);
                _validatedNodeIds.Add(key);

                row.Values.TryGetValue(childId, out var value);
                var message = _fieldValidator.Validate(child, value, isVisible: true);

                if (message is null)
                {
                    _errors.Remove(key);
                }
                else
                {
                    _errors[key] = message;
                }
            }
        }

        return touchedKeys;
    }

    /// <summary>
    /// Validates a repeating group's row count against <see cref="FormNode.MinRows"/> and
    /// <see cref="FormNode.MaxRows"/> — the one mechanism a group has for "at least N rows" (PRD
    /// §5, since <see cref="FormNode.Required"/> is hidden for a repeating group in the
    /// designer). Marks the group itself validated, so its error (if any) becomes visible from
    /// this point on — including immediately after an add/remove, not only at page-advance or
    /// submit.
    /// </summary>
    private void ValidateRepeatingRowCount(FormNode group)
    {
        _validatedNodeIds.Add(group.Id);
        var rowCount = GetRepeatingRows(group).Rows.Count;

        if (group.MinRows is int min && rowCount < min)
        {
            _errors[group.Id] = Localizer["RepeatingMinRowsRemedy", min, ItemNoun(group)].Value;
        }
        else if (group.MaxRows is int max && rowCount > max)
        {
            _errors[group.Id] = Localizer["RepeatingMaxRowsRemedy", max, ItemNoun(group)].Value;
        }
        else
        {
            _errors.Remove(group.Id);
        }
    }

    private static string ItemNoun(FormNode group) => group.ItemLabel ?? group.Label ?? "";

    private static FormNode? FindChild(FormNode group, string childId) =>
        group.Children.FirstOrDefault(child => string.Equals(child.Id, childId, StringComparison.Ordinal));

    /// <summary>
    /// Drops every error whose node — or, for a repeating group's child, whose (child, row) pair
    /// — is not currently visible. A hidden node carries no validation state at all, so a field
    /// that hid after it last failed (including a row that was removed entirely) must not go on
    /// counting toward "the form has an error" or lingering in the summary.
    /// </summary>
    /// <param name="visibleNodeIds">
    /// The flat, top-level visible node ids for this pass.
    /// </param>
    /// <param name="settledValues">
    /// The same settled outer answers <paramref name="visibleNodeIds"/> was computed from —
    /// threaded through to <see cref="CollectVisibleErrorKeys"/> so a repeating child's visibility
    /// is resolved exactly like <see cref="BuildSubmissionEnvelope"/> resolves it, never against
    /// the raw, unsettled answer store.
    /// </param>
    private void PruneHiddenErrors(HashSet<string> visibleNodeIds, IReadOnlyDictionary<string, object?> settledValues)
    {
        var keep = CollectVisibleErrorKeys(visibleNodeIds, settledValues);

        foreach (var key in _errors.Keys.Where(id => !keep.Contains(id)).ToList())
        {
            _errors.Remove(key);
        }
    }

    /// <summary>
    /// The full set of keys <see cref="_errors"/> and <see cref="_validatedNodeIds"/> may
    /// currently carry without being stale: every flat, top-level visible node id, plus — for
    /// every currently visible repeating group not overridden by a host component
    /// (<see cref="IsRepeatingComponentRegisteredByHost"/>; that branch never writes a composite
    /// key in the first place, per <see cref="ValidateRepeatingGroup"/>'s own remarks) — one
    /// composite <see cref="RepeatingFieldKeys.ChildKey"/> per currently visible child of every
    /// row.
    /// </summary>
    private HashSet<string> CollectVisibleErrorKeys(HashSet<string> visibleNodeIds, IReadOnlyDictionary<string, object?> settledValues)
    {
        var keep = new HashSet<string>(visibleNodeIds, StringComparer.Ordinal);

        if (IsRepeatingComponentRegisteredByHost)
        {
            return keep;
        }

        foreach (var node in Definition.EnumerateNodes())
        {
            if (node.Type != NodeType.Repeating || !visibleNodeIds.Contains(node.Id))
            {
                continue;
            }

            foreach (var row in GetRepeatingRows(node).Rows)
            {
                foreach (var childId in VisibilityEvaluator.GetVisibleChildIds(node, row, settledValues))
                {
                    keep.Add(RepeatingFieldKeys.ChildKey(childId, row.RowId));
                }
            }
        }

        return keep;
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
    /// respondent has not yet advanced the page or submitted, and refreshes the calc
    /// announcer (<see cref="RefreshCalcAnnouncement"/>) — the point every field's control commits
    /// its answer, whether that control recomputes live on <c>oninput</c> (number, currency, text)
    /// or only on <c>onchange</c> (select, date, checkbox). Autosaves the draft afterward.
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
        RefreshCalcAnnouncement();

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

        // Changing from just the FormValues.FormJsonValues dictionary to a hydrated one so that
        // this preserves the generic JSON behavior for submission viewing while restoring DateOnly
        // exactly where DateField and FieldValidator require it.
        // DateRange already restores as string arrays and needs no change.
        foreach (var pair in HydrateDraftValues(draft.Values))
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
    /// registered and the fill is not anonymous. Persists the raw, unfiltered
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
    /// Discards the draft once a fill completes, so a submitted fill leaves no
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
    /// Whether the host registered its own component for <see cref="NodeType.Repeating"/>. When
    /// <see langword="true"/>, the section loop renders that override as an ordinary
    /// <see cref="FormFieldBase"/> through the same <c>DynamicComponent</c> path every other node
    /// uses — the whole group's <see cref="Serialization.RepeatingRows"/> as
    /// <see cref="FormFieldBase.Value"/>, one group-level <see cref="FormFieldBase.Error"/> — a
    /// documented limitation of that seam (repeating-groups-plan.md's "Risks"): per-child inline
    /// errors are the internal <see cref="Components.RepeatingGroup"/>'s own affordance, not
    /// something a host override gets for free. When <see langword="false"/> (the common case),
    /// the section loop renders <see cref="Components.RepeatingGroup"/> directly instead, never
    /// reaching <see cref="DefaultFieldComponents"/> for <see cref="NodeType.Repeating"/> at all.
    /// </summary>
    private bool IsRepeatingComponentRegisteredByHost =>
        FieldComponents is not null
        && FieldComponents.TryGetComponentType(NodeType.Repeating, out var registered)
        && registered is not null;

    /// <summary>
    /// The current answer to a repeating group, or <see cref="RepeatingRows.Empty"/> when the
    /// group has not yet been seeded or its stored value is not a <see cref="RepeatingRows"/> —
    /// defensive against a host-registered override handing back something else through
    /// <see cref="FormFieldBase.ValueChanged"/>.
    /// </summary>
    private RepeatingRows GetRepeatingRows(FormNode group) =>
        _values.TryGetValue(group.Id, out var raw) && raw is RepeatingRows rows ? rows : RepeatingRows.Empty;

    /// <summary>
    /// The per-(child, row) DOM id a repeating group's child renders its primary control with —
    /// the same <c>{_instanceId}-…</c> namespacing <see cref="BuildFieldDomId"/> uses for a
    /// top-level field, extended with the row id so the same child node id repeating once per row
    /// still gets a unique id per row.
    /// </summary>
    private string BuildRepeatingChildDomId(string childId, string rowId) => $"{_instanceId}-{childId}-{rowId}";

    /// <summary>
    /// The child ids currently visible within one row, resolved against the row-scoped merged
    /// view (<see cref="VisibilityEvaluator.GetVisibleChildIds"/>). Curried by
    /// <see cref="FormNode"/> and the current render's settled outer values in
    /// <c>FormRenderer.razor</c> so <see cref="Components.RepeatingGroup"/> gets a plain
    /// <c>Func&lt;RepeatingRow, IReadOnlyList&lt;string&gt;&gt;</c> for one specific group.
    /// </summary>
    /// <param name="group">
    /// The repeating group whose child to resolve.
    /// </param>
    /// <param name="row">
    /// The row to resolve against — passed through unsettled; only <paramref name="outerValues"/>
    /// needs settling, since <see cref="VisibilityEvaluator.GetVisibleChildIds"/> overlays the
    /// row's own (full, raw) values on top of it.
    /// </param>
    /// <param name="outerValues">
    /// The settled outer answers for this render — never the raw answer store, so what the
    /// respondent sees agrees exactly with what a submit would capture for the same state.
    /// </param>
    private static IReadOnlyList<string> GetVisibleRepeatingChildIds(
        FormNode group,
        RepeatingRow row,
        IReadOnlyDictionary<string, object?> outerValues) =>
        VisibilityEvaluator.GetVisibleChildIds(group, row, outerValues);

    /// <summary>
    /// Builds the <c>DynamicComponent</c> parameter set for one repeating group's child within
    /// one row — the row-scoped counterpart of <see cref="BuildFieldParameters"/>. Value,
    /// change/blur callbacks, and error are wired only for a child whose type actually captures
    /// an answer, exactly like the top-level case; a <see cref="NodeType.Calc"/> child gets its
    /// own formatted display value the same way <see cref="RecomputeCalculations"/>'s per-row
    /// results feed it.
    /// </summary>
    private Dictionary<string, object> BuildRepeatingChildParametersFor(FormNode group, FormNode child, RepeatingRow row)
    {
        var parameters = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            [nameof(FormFieldBase.Node)] = child,
            [nameof(FormFieldBase.FieldId)] = BuildRepeatingChildDomId(child.Id, row.RowId),
        };

        if (FieldValueConventions.GetStoredClrType(child.Type) is not null)
        {
            row.Values.TryGetValue(child.Id, out var value);
            parameters[nameof(FormFieldBase.Value)] = value!;
            parameters[nameof(FormFieldBase.ValueChanged)] = GetRepeatingChildValueChangedCallback(group, row.RowId, child.Id);
            parameters[nameof(FormFieldBase.OnBlur)] = GetRepeatingChildBlurCallback(group, row.RowId, child.Id);
            parameters[nameof(FormFieldBase.Error)] = GetRepeatingChildError(child.Id, row.RowId)!;
        }
        else if (child.Type == NodeType.Calc)
        {
            row.Values.TryGetValue(child.Id, out var computed);
            var formatted = child.Calculation is null ? null : CalcDisplayFormatter.Format(computed, child.Calculation.Format);
            parameters[nameof(FormFieldBase.Value)] = formatted!;
        }

        return parameters;
    }

    /// <summary>
    /// The error currently attached to one repeating child within one row, or
    /// <see langword="null"/> when it has none or has not yet been validated — the row-scoped
    /// counterpart of <see cref="GetFieldError"/>.
    /// </summary>
    private string? GetRepeatingChildError(string childId, string rowId)
    {
        var key = RepeatingFieldKeys.ChildKey(childId, rowId);
        return _validatedNodeIds.Contains(key) ? _errors.GetValueOrDefault(key) : null;
    }

    /// <summary>
    /// Returns the same <see cref="EventCallback{T}"/> instance for a given (child, row) on every
    /// call, caching it the first time that pair is encountered — the row-scoped counterpart of
    /// <see cref="GetValueChangedCallback"/>.
    /// </summary>
    private EventCallback<object?> GetRepeatingChildValueChangedCallback(FormNode group, string rowId, string childId)
    {
        var key = RepeatingFieldKeys.ChildKey(childId, rowId);

        if (!_repeatingChildValueChangedCallbacks.TryGetValue(key, out var callback))
        {
            callback = EventCallback.Factory.Create<object?>(this, (object? value) => SetRepeatingChildValue(group, rowId, childId, value));
            _repeatingChildValueChangedCallbacks[key] = callback;
        }

        return callback;
    }

    /// <summary>
    /// Returns the same <see cref="EventCallback"/> instance for a given (child, row) on every
    /// call, for the same reason <see cref="GetRepeatingChildValueChangedCallback"/> does.
    /// </summary>
    private EventCallback GetRepeatingChildBlurCallback(FormNode group, string rowId, string childId)
    {
        var key = RepeatingFieldKeys.ChildKey(childId, rowId);

        if (!_repeatingChildBlurCallbacks.TryGetValue(key, out var callback))
        {
            callback = EventCallback.Factory.Create(this, () => HandleRepeatingChildBlur(group, rowId, childId));
            _repeatingChildBlurCallbacks[key] = callback;
        }

        return callback;
    }

    /// <summary>
    /// Sets one child's answer within one row: mutates the group's <see cref="RepeatingRows"/>
    /// value through <see cref="RepeatingRows.SetValue"/>, then hands the updated value to
    /// <see cref="SetValue(string, object?)"/> exactly as any top-level answer change would —
    /// the existing pipeline recomputes every calc (including this group's own per-row
    /// calculations) with no repeating-specific recompute code needed here.
    /// </summary>
    private void SetRepeatingChildValue(FormNode group, string rowId, string childId, object? value) =>
        SetValue(group.Id, GetRepeatingRows(group).SetValue(rowId, childId, value));

    /// <summary>
    /// Handles the blur of one repeating child's control — the row-scoped counterpart of
    /// <see cref="HandleBlur"/>: validates that one child alone, marks it validated, refreshes
    /// the calc announcer, and autosaves the draft.
    /// </summary>
    [SuppressMessage(
        "Reliability",
        "CA2007:Consider calling ConfigureAwait on the awaited task",
        Justification = "A Blazor event handler must resume on the renderer's synchronization context so it can safely schedule the next render.")]
    private async Task HandleRepeatingChildBlur(FormNode group, string rowId, string childId)
    {
        var child = FindChild(group, childId);
        var row = GetRepeatingRows(group).Rows.FirstOrDefault(r => string.Equals(r.RowId, rowId, StringComparison.Ordinal));

        if (child is null || row is null)
        {
            return;
        }

        var key = RepeatingFieldKeys.ChildKey(childId, rowId);
        _validatedNodeIds.Add(key);

        row.Values.TryGetValue(childId, out var value);
        var message = _fieldValidator.Validate(child, value, isVisible: true);

        if (message is null)
        {
            _errors.Remove(key);
        }
        else
        {
            _errors[key] = message;
        }

        RefreshCalcAnnouncement();
        await PersistDraftAsync();
    }

    /// <summary>
    /// Handles the Add control: appends a fresh row via <see cref="RepeatingRows.AddRow"/> and
    /// revalidates the group's row count. Defensive against exceeding
    /// <see cref="FormNode.MaxRows"/> even though <see cref="Components.RepeatingGroup"/> already
    /// gates its own Add control before ever raising this — a host's own replacement for that
    /// component might not.
    /// </summary>
    [SuppressMessage(
        "Reliability",
        "CA2007:Consider calling ConfigureAwait on the awaited task",
        Justification = "A Blazor event handler must resume on the renderer's synchronization context so it can safely schedule the next render.")]
    private async Task AddRepeatingRow(FormNode group)
    {
        var rows = GetRepeatingRows(group);

        if (group.MaxRows is int max && rows.Rows.Count >= max)
        {
            return;
        }

        SetValue(group.Id, rows.AddRow());
        ValidateRepeatingRowCount(group);
        await PersistDraftAsync();
    }

    /// <summary>
    /// Handles one row's Remove control: removes it via <see cref="RepeatingRows.RemoveRow"/>,
    /// clears every bit of per-row state the removed row's children carried (errors, validated
    /// flags, cached callbacks — <see cref="ClearRemovedRowState"/>) so nothing about a row that
    /// no longer exists can ever surface again, and revalidates the group's row count. Defensive
    /// against going below <see cref="FormNode.MinRows"/> for the same reason
    /// <see cref="AddRepeatingRow"/> is defensive against <see cref="FormNode.MaxRows"/>.
    /// </summary>
    [SuppressMessage(
        "Reliability",
        "CA2007:Consider calling ConfigureAwait on the awaited task",
        Justification = "A Blazor event handler must resume on the renderer's synchronization context so it can safely schedule the next render.")]
    private async Task RemoveRepeatingRow(FormNode group, string rowId)
    {
        var rows = GetRepeatingRows(group);

        if (group.MinRows is int min && rows.Rows.Count <= min)
        {
            return;
        }

        SetValue(group.Id, rows.RemoveRow(rowId));
        ClearRemovedRowState(group, rowId);
        ValidateRepeatingRowCount(group);
        await PersistDraftAsync();
    }

    /// <summary>
    /// Handles one row's Move up/down control via <see cref="RepeatingRows.MoveRow"/>. A no-op
    /// move (the row was not found, or the move would land it outside the list) never persists
    /// the draft — nothing changed. Reordering never touches error/validated state: a row's
    /// composite keys are keyed by its own row id, which a move never changes.
    /// </summary>
    [SuppressMessage(
        "Reliability",
        "CA2007:Consider calling ConfigureAwait on the awaited task",
        Justification = "A Blazor event handler must resume on the renderer's synchronization context so it can safely schedule the next render.")]
    private async Task MoveRepeatingRow(FormNode group, string rowId, int delta)
    {
        var rows = GetRepeatingRows(group);
        var moved = rows.MoveRow(rowId, delta);

        if (ReferenceEquals(moved, rows))
        {
            return;
        }

        SetValue(group.Id, moved);
        await PersistDraftAsync();
    }

    /// <summary>
    /// Purges every trace of a removed row's children from the renderer's per-row state — the
    /// composite error/validated entries and the cached value-changed/blur callback instances —
    /// so a long fill session with many add/remove cycles never accumulates dead entries keyed by
    /// a row id that no longer exists.
    /// </summary>
    private void ClearRemovedRowState(FormNode group, string rowId)
    {
        foreach (var child in group.Children)
        {
            var key = RepeatingFieldKeys.ChildKey(child.Id, rowId);
            _errors.Remove(key);
            _validatedNodeIds.Remove(key);
            _repeatingChildValueChangedCallbacks.Remove(key);
            _repeatingChildBlurCallbacks.Remove(key);
        }
    }

    /// <summary>
    /// Receives the localized text a <see cref="Components.RepeatingGroup"/> built for one row
    /// mutation (including a blocked one at min/max) and renders it into <c>FormRenderer.razor</c>'s
    /// own shared, visually-hidden <c>aria-live="polite"</c> region — separate from
    /// <see cref="_calcAnnouncement"/>'s own region, per the keyboard/SR model
    /// (repeating-groups-plan.md, D-3).
    /// </summary>
    private void AnnounceRepeatingRowChange(string text) => _repeatingRowAnnouncement = text;

    /// <summary>
    /// The stable DOM id a field renders its primary control with, namespaced to this renderer
    /// instance so two <see cref="FormRenderer"/> instances of the same definition on one
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
    private HashSet<string> GetVisibleNodeIds() => GetVisibleNodeIds(out _);

    /// <summary>
    /// The same visible-node-id set <see cref="GetVisibleNodeIds()"/> returns, plus the settled
    /// outer values (<see cref="VisibilityEvaluator.FilterToVisible"/>) that set was computed
    /// from. A caller that also needs to resolve a repeating group's per-row child visibility
    /// (<see cref="VisibilityEvaluator.GetVisibleChildIds"/>) for the <em>same pass</em> — display,
    /// validation, the error summary, pruning — must reuse this exact settled dictionary as that
    /// call's outer-values argument rather than the raw <see cref="_values"/>. Using raw values
    /// there would silently diverge from what <see cref="BuildSubmissionEnvelope"/> actually
    /// captures: when a row child's own <c>VisibleWhen</c> names an <em>outer</em> field that is
    /// itself conditionally hidden, the settled dictionary already reflects that field's answer
    /// having been dropped — exactly the shrink-only fixed point <see cref="VisibilityEvaluator"/>
    /// computes for the flat, top-level case — while the raw dictionary still carries the stale
    /// answer. Computed once per call so a single validation or render pass never re-settles the
    /// whole form more than once.
    /// </summary>
    private HashSet<string> GetVisibleNodeIds(out IReadOnlyDictionary<string, object?> settledValues)
    {
        settledValues = VisibilityEvaluator.FilterToVisible(Definition, _values);
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
    /// calculated field is ever an answer to validate.
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
    /// fail.
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
    /// Refreshes the visually-hidden, <c>aria-live="polite"</c> calc-announcer region
    /// <c>FormRenderer.razor</c> renders, from every currently visible calc node that both lives on
    /// <see cref="CurrentPage"/> and carries a calculation (PRD §5, decision log D-E's "announce on
    /// commit" refinement). Scoped to <see cref="CurrentPage"/> — not every calc node in the whole
    /// definition — because a calc that lives on some other page is not on screen at all right now;
    /// announcing it anyway would tell a screen-reader user about a value they have no way to see or
    /// relate to whatever field they just blurred (code review fix #4). Deliberately called only
    /// from <see cref="HandleBlur"/> — the one point every field's control, regardless of whether it
    /// recomputes live on <c>oninput</c> or only on <c>onchange</c>, reports that its answer is
    /// settled — never from <see cref="SetValue"/> itself, so a calc's visible
    /// <c>&lt;output&gt;</c> still updates on every keystroke (its own <c>aria-live="off"</c>
    /// keeps that silent for assistive technology) while a screen reader hears the calc's own
    /// label and settled value exactly once per commit, not once per keystroke.
    /// </summary>
    private void RefreshCalcAnnouncement()
    {
        var visibleNodeIds = GetVisibleNodeIds();
        var announcements = new List<string>();
        var currentPageNodes = CurrentPage?.Sections.SelectMany(section => section.EnumerateNodes()) ?? [];

        foreach (var node in currentPageNodes)
        {
            // EnumerateNodes flattens into a repeating group's own children too, but
            // visibleNodeIds never contains a repeating child's plain id (only the group's own),
            // so every one of them -- calc or not, in every row -- is skipped here by construction.
            // Deliberate, not a gap: announcing a per-row calc's settled value on every row's own
            // commit, for however many rows a group holds, would be noisy rather than helpful; a
            // per-row calc's live <output> already shows the value visually, same as any other
            // calc.
            if (node.Type != NodeType.Calc || node.Calculation is null || !visibleNodeIds.Contains(node.Id))
            {
                continue;
            }

            _values.TryGetValue(node.Id, out var computed);
            var formatted = CalcDisplayFormatter.Format(computed, node.Calculation.Format);

            if (formatted is not null)
            {
                announcements.Add(Localizer["CalcAnnouncementEntry", node.Label ?? node.Id, formatted].Value);
            }
        }

        _calcAnnouncement = string.Join(" ", announcements);
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
