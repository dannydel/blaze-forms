using System.Diagnostics.CodeAnalysis;
using BlazeForms.Definitions;
using BlazeForms.Expressions;
using BlazeForms.Fields;
using BlazeForms.Fields.Internal;
using BlazeForms.Hosting;
using BlazeForms.Versioning;
using Microsoft.AspNetCore.Components;

namespace BlazeForms;

/// <summary>
/// Fills one published <see cref="FormVersion"/>: pages as steps behind a progress header,
/// sections as <c>fieldset</c>/<c>legend</c>, and nodes through the registry-first component
/// resolver (PRD §4.2, §5, §10). Conditional visibility (PRD §6) is evaluated live against the
/// respondent's in-progress answers on every render — a hidden node is simply never emitted, so
/// it is excluded from the accessibility tree as well as (in a later slice) validation and the
/// submission payload.
/// </summary>
/// <remarks>
/// This slice covers structure and live visibility only: page-advance validation, drafts, and
/// submission are later slices, so <see cref="GoToPreviousPage"/>/<see cref="GoToNextPage"/> move
/// between steps unconditionally.
/// </remarks>
public partial class FormRenderer : ComponentBase
{
    private readonly string _instanceId = "bf-renderer-" + Guid.NewGuid().ToString("n");
    private readonly Dictionary<string, object?> _values = new(StringComparer.Ordinal);
    private readonly Dictionary<string, EventCallback<object?>> _valueChangedCallbacks = new(StringComparer.Ordinal);
    private int _currentPageIndex;
    private bool _focusPageHeadingOnNextRender;
    private ElementReference _pageHeadingElement;

    /// <summary>
    /// The published version to fill. The renderer holds this for the whole fill and never
    /// swaps the definition mid-fill (PRD D13) — a newer version publishing while a respondent
    /// is partway through never changes what they see or how it validates.
    /// </summary>
    [Parameter, EditorRequired]
    public FormVersion Version { get; set; } = default!;

    /// <summary>
    /// An opaque host-supplied key identifying the respondent. Unused until fill drafts land in
    /// a later slice (PRD §4.2); <see langword="null"/> for a form the host does not track
    /// individual respondents on.
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
        if (!_focusPageHeadingOnNextRender)
        {
            return;
        }

        _focusPageHeadingOnNextRender = false;
        await _pageHeadingElement.FocusAsync();
    }

    /// <summary>
    /// Moves to the previous step, unguarded — page-advance validation arrives in a later
    /// slice. Moves focus to the new step's heading once it has rendered, so the step change is
    /// announced to assistive technology at the same moment the visible content changes.
    /// </summary>
    private void GoToPreviousPage() => GoToPage(_currentPageIndex - 1);

    /// <summary>
    /// Moves to the next step, unguarded — page-advance validation arrives in a later slice.
    /// Moves focus to the new step's heading once it has rendered, so the step change is
    /// announced to assistive technology at the same moment the visible content changes.
    /// </summary>
    private void GoToNextPage() => GoToPage(_currentPageIndex + 1);

    private void GoToPage(int pageIndex)
    {
        if (pageIndex < 0 || pageIndex >= Pages.Count || pageIndex == _currentPageIndex)
        {
            return;
        }

        _currentPageIndex = pageIndex;
        _focusPageHeadingOnNextRender = true;
    }

    /// <summary>
    /// Resolves the component type to render for a node, honoring <see cref="FieldComponents"/>
    /// via the same registry-first resolver every shipped field component resolves through.
    /// </summary>
    private Type ResolveComponentType(FormNode node) => DefaultFieldComponents.Resolve(node.Type, FieldComponents);

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
    /// component resolved for <paramref name="node"/>. <see cref="FormFieldBase.Value"/> and
    /// <see cref="FormFieldBase.ValueChanged"/> are wired only for node types that actually
    /// capture an answer — static content and <see cref="NodeType.Calc"/> (which renders but
    /// never writes a value, PRD §5) never seed a payload key in <see cref="_values"/>.
    /// </summary>
    private Dictionary<string, object> BuildFieldParameters(FormNode node)
    {
        var parameters = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            [nameof(FormFieldBase.Node)] = node,
            [nameof(FormFieldBase.FieldId)] = node.Id,
        };

        if (FieldValueConventions.GetStoredClrType(node.Type) is not null)
        {
            _values.TryGetValue(node.Id, out var value);
            parameters[nameof(FormFieldBase.Value)] = value!;
            parameters[nameof(FormFieldBase.ValueChanged)] = GetValueChangedCallback(node.Id);
        }

        return parameters;
    }

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

    private void SetValue(string nodeId, object? value) => _values[nodeId] = value;
}
