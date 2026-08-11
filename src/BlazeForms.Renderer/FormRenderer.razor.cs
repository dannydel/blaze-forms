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
/// Fill drafts (<c>IFormDraftStore</c>) are a later slice — nothing here persists an in-progress
/// fill across a page reload.
/// </remarks>
public partial class FormRenderer : ComponentBase
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

    /// <summary>
    /// The host's optional submission sink, resolved once in <see cref="OnInitialized"/> from
    /// <see cref="ServiceProvider"/> rather than through <c>[Inject]</c> directly — see the
    /// remarks on <see cref="ServiceProvider"/> for why. Invoked alongside
    /// <see cref="OnSubmitted"/> on a successful submit, never instead of it, so a host can rely
    /// on either integration point without the other silently going unfired (PRD §9).
    /// </summary>
    private IFormSubmissionSink? _sink;

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
        : $"Step {_currentPageIndex + 1} of {Pages.Count}: {CurrentPage.Title}";

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
        _sink = ServiceProvider.GetService(typeof(IFormSubmissionSink)) as IFormSubmissionSink;
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
    /// assistive technology at the same moment the visible content changes.
    /// </summary>
    private void GoToPreviousPage() => GoToPage(_currentPageIndex - 1, focusHeading: true);

    /// <summary>
    /// Validates the current page's visible input nodes and, only if every one passes, moves to
    /// the next step (PRD §4.2). A failure renders the error summary and moves focus to it
    /// instead of advancing.
    /// </summary>
    private void GoToNextPage()
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

        GoToPage(_currentPageIndex + 1, focusHeading: true);
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
            NavigateToFirstOffendingPage();
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
    }

    private void GoToPage(int pageIndex, bool focusHeading, bool resetSummary = true)
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

    private void NavigateToFirstOffendingPage()
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
            GoToPage(pageIndex, focusHeading: false, resetSummary: false);
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
    /// respondent has not yet advanced the page or submitted (PRD §4.2).
    /// </summary>
    private void HandleBlur(string nodeId)
    {
        var node = Definition.FindNode(nodeId);

        if (node is null)
        {
            return;
        }

        _validatedNodeIds.Add(nodeId);
        ApplyFieldValidation(node);
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
    /// <see cref="FormFieldBase.Error"/> are wired only for node types that actually capture an
    /// answer — static content and <see cref="NodeType.Calc"/> (which renders but never writes a
    /// value, PRD §5) never seed a payload key in <see cref="_values"/> and are never validated.
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

    private void SetValue(string nodeId, object? value) => _values[nodeId] = value;
}
