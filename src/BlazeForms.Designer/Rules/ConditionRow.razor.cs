using System.Diagnostics.CodeAnalysis;
using BlazeForms.Definitions;
using BlazeForms.Expressions;
using BlazeForms.Internal;
using BlazeForms.Resources;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;

namespace BlazeForms.Rules;

/// <summary>
/// One condition row of the expression tree's rule editor: the design's Model A -- separately
/// labelled Field, Operator, and Value controls, each with its own accessible label naming the
/// row's own one-based ordinal (PRD §6, D8). Both <see cref="VisibilityRuleEditor"/> and
/// <see cref="ValidationRuleEditor"/> host a list of these, one per <see cref="Condition"/> in the
/// <see cref="ConditionGroup"/> they are building.
/// </summary>
/// <remarks>
/// <para>
/// <b>The Value control's three shapes (PRD §6).</b> Hidden entirely for the four operators that
/// take no operand (<see cref="ConditionOperator.IsTrue"/>, <see cref="ConditionOperator.IsFalse"/>,
/// <see cref="ConditionOperator.IsBlank"/>, <see cref="ConditionOperator.IsNotBlank"/>) -- the
/// evaluator ignores whatever a hidden control would otherwise hold, so showing it would only
/// invite an author to fill in a value that is never read. Rendered as an option-<em>value</em>
/// select (bound to <see cref="FormOption.Value"/>, showing <see cref="FormOption.Label"/>) once
/// the chosen field is a choice node -- never a label, per AGENTS.md invariant #5. A plain text
/// input otherwise.
/// </para>
/// <para>
/// <b>Controlled, not owned.</b> This row holds no data of its own: every control commits through
/// <see cref="ConditionChanged"/> the moment it changes (never a keystroke, since every control
/// here is a <c>&lt;select&gt;</c> or an <c>&lt;input&gt;</c> bound on <c>onchange</c>), handing
/// back a whole replacement <see cref="Condition"/> record for the host editor to fold into its own
/// <see cref="ConditionGroup"/> and commit however it sees fit -- immediately
/// (<see cref="ValidationRuleEditor"/>) or as part of a larger candidate an Apply step still has to
/// approve (<see cref="VisibilityRuleEditor"/>'s cycle check). Changing <see cref="Condition.Field"/>
/// always clears <see cref="Condition.Value"/>: a value authored against the old field's own
/// value-space (an option's stored value, a number, a date) is rarely meaningful against a
/// different field, so carrying it over silently would invite a rule that looks configured but
/// never actually matches.
/// </para>
/// <para>
/// <b>Focus (PRD §11, WCAG 2.4.3).</b> The only state this row does keep is the one-shot
/// <see cref="RequestFocus"/> follow-through: when the host editor asks, this row moves real DOM
/// focus to its own Field select in <see cref="OnAfterRenderAsync"/>, the same
/// <c>_focusOnNextRender</c> pattern <see cref="Canvas.CanvasNodeRow"/> uses for its own
/// post-mutation focus moves. <see cref="VisibilityRuleEditor"/> and <see cref="ValidationRuleEditor"/>
/// both set this for a freshly added row, and for whichever row a removal elsewhere in the list
/// should land focus on -- this row never decides that for itself.
/// </para>
/// </remarks>
public partial class ConditionRow : ComponentBase
{
    private static readonly ConditionOperator[] NoOperandOperators =
    [
        ConditionOperator.IsTrue,
        ConditionOperator.IsFalse,
        ConditionOperator.IsBlank,
        ConditionOperator.IsNotBlank,
    ];

    private static readonly ConditionOperator[] AllOperators = Enum.GetValues<ConditionOperator>();

    private readonly string _instanceId = "bf-condition-row-" + Guid.NewGuid().ToString("n");
    private ElementReference _fieldSelectElement;
    private bool _focusOnNextRender;

    /// <summary>
    /// The clause this row edits.
    /// </summary>
    [Parameter, EditorRequired]
    public Condition Condition { get; set; } = default!;

    /// <summary>
    /// The candidate fields the Field select offers -- every input node in the form (PRD §5's
    /// <see cref="FormSchema.IsInputNode"/>), by identifier, showing each one's own label.
    /// </summary>
    [Parameter, EditorRequired]
    public IReadOnlyList<FormNode> Fields { get; set; } = default!;

    /// <summary>
    /// This row's own one-based ordinal among its siblings, named in every one of its controls'
    /// own accessible labels (PRD §6, D8).
    /// </summary>
    [Parameter]
    public int RowNumber { get; set; } = 1;

    /// <summary>
    /// Raised with the complete replacement clause the moment any control here commits.
    /// </summary>
    [Parameter]
    public EventCallback<Condition> ConditionChanged { get; set; }

    /// <summary>
    /// Raised when this row's own remove button is activated. The host editor decides what
    /// removing a row actually does; this row does not remove itself.
    /// </summary>
    [Parameter]
    public EventCallback OnRemove { get; set; }

    /// <summary>
    /// A one-shot signal from the host editor that this render is the reason real DOM focus
    /// should land on this row's own Field select -- a freshly added row, or the stable neighbour
    /// a removal elsewhere in the list lands focus on (PRD §11, WCAG 2.4.3). Mirrors
    /// <see cref="Canvas.CanvasNodeRow.RequestFocus"/>'s own one-shot contract: the host editor
    /// only ever sets this <see langword="true"/> for the one render that should move focus, and
    /// this row claims it exactly once via <see cref="OnAfterRenderAsync"/>.
    /// </summary>
    [Parameter]
    public bool RequestFocus { get; set; }

    private static IStringLocalizer<DesignerStrings> Localizer => DesignerLocalization.Shared;

    private static IReadOnlyList<ConditionOperator> Operators => AllOperators;

    private string FieldSelectId => _instanceId + "-field";

    private string OperatorSelectId => _instanceId + "-operator";

    private string ValueControlId => _instanceId + "-value";

    private bool IsNoOperandOperator => NoOperandOperators.Contains(Condition.Operator);

    /// <summary>
    /// The chosen field's own options, when it is a choice node -- the signal that switches the
    /// Value control from a text input to an option-value select. <see langword="null"/> for a
    /// non-choice field, a dangling field reference, or a choice field that carries no option yet.
    /// </summary>
    private IReadOnlyList<FormOption>? SelectedFieldOptions =>
        Fields.FirstOrDefault(candidate => string.Equals(candidate.Id, Condition.Field, StringComparison.Ordinal)) is { } selected
            && IsChoiceType(selected.Type)
            ? selected.Options
            : null;

    /// <inheritdoc/>
    protected override void OnParametersSet()
    {
        if (RequestFocus)
        {
            _focusOnNextRender = true;
        }
    }

    /// <inheritdoc/>
    [SuppressMessage(
        "Reliability",
        "CA2007:Consider calling ConfigureAwait on the awaited task",
        Justification = "A Blazor lifecycle method must resume on the renderer's synchronization context, not a captured-context-free one, so it can safely schedule the next render.")]
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_focusOnNextRender)
        {
            _focusOnNextRender = false;
            await _fieldSelectElement.FocusAsync();
        }
    }

    private static bool IsChoiceType(NodeType type) =>
        type is NodeType.Select or NodeType.Radio or NodeType.CheckboxGroup or NodeType.YesNo;

    private static string FieldLabel(FormNode field) =>
        field.Label ?? Localizer["UntitledNodeLabel", Localizer[$"NodeType{field.Type}"].Value].Value;

    private Task OnFieldChangedAsync(string field) =>
        ConditionChanged.InvokeAsync(Condition with { Field = field, Value = null });

    private Task OnOperatorChangedAsync(ConditionOperator op) =>
        ConditionChanged.InvokeAsync(Condition with { Operator = op });

    private Task OnValueSelectedAsync(string? value) =>
        ConditionChanged.InvokeAsync(Condition with { Value = NormalizeToNull(value) });

    private Task OnValueTypedAsync(ChangeEventArgs e) =>
        ConditionChanged.InvokeAsync(Condition with { Value = NormalizeToNull(e.Value) });

    private Task OnRemoveAsync() => OnRemove.InvokeAsync();

    private static string? NormalizeToNull(object? value)
    {
        var text = value?.ToString();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }
}
