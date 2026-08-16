using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using BlazeForms.Definitions;
using BlazeForms.Expressions;
using BlazeForms.Internal;
using BlazeForms.Resources;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Localization;

namespace BlazeForms.Rules;

/// <summary>
/// One operand row of <see cref="CalculationEditor"/>'s expression editor: a Kind toggle deciding
/// which of <see cref="CalcOperand"/>'s three shapes this row edits (a field reference, a numeric
/// literal, or <see cref="CalcFunction.Today"/>), plus whichever single control that shape needs
/// (PRD §5, §13). <see cref="ConditionRow"/>'s own labelling and focus patterns are reused here —
/// it is not itself reused, since it edits a boolean <see cref="Condition"/>, not a value-producing
/// <see cref="CalcOperand"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Controlled, not owned.</b> Exactly like <see cref="ConditionRow"/>, this row holds no data of
/// its own: every control commits through <see cref="OperandChanged"/> the moment it changes,
/// handing back a whole replacement <see cref="CalcOperand"/> for <see cref="CalculationEditor"/> to
/// fold into its own working operand list and commit however it sees fit — never per keystroke,
/// since <see cref="CalculationEditor"/> only ever reaches <see cref="Designer.DesignerEditContext"/>
/// on Apply.
/// </para>
/// <para>
/// <b>Kind is sticky, not purely derived.</b> <see cref="CalcOperand"/>'s own "exactly one of
/// Field, Number, Function" contract (PRD §13, its own remarks) decides <see cref="Kind"/> whenever
/// <see cref="Operand"/> unambiguously carries one of the three shapes, so switching the Kind
/// select immediately hands back a fresh <see cref="CalcOperand"/> with only the new shape's member
/// set — never a stale leftover from whichever shape the row previously showed. But a fully blank
/// operand (every member null — a Number-kind row whose author cleared the input to retype it, or
/// a freshly added row with no candidate field to default to) is genuinely ambiguous: it could be
/// any of the three shapes. <see cref="_kind"/> is this row's own memory of which one it actually
/// was, so clearing a number input mid-edit keeps showing the number control rather than silently
/// flipping to the Field select the moment the value becomes blank.
/// </para>
/// <para>
/// <b>Focus (PRD §11, WCAG 2.4.3).</b> The only state this row keeps is the one-shot
/// <see cref="RequestFocus"/> follow-through onto its own Kind select — the first of this row's
/// controls, the same "first control" convention <see cref="ConditionRow"/> follows for its own
/// Field select.
/// </para>
/// </remarks>
public partial class CalcOperandRow : ComponentBase
{
    /// <summary>
    /// The UI-only shape this row currently edits, derived from whichever single member
    /// <see cref="CalcOperand"/> actually carries (see this type's own remarks) rather than stored
    /// as separate state that could ever drift out of sync with <see cref="Operand"/> itself.
    /// </summary>
    private enum OperandKind
    {
        Field,
        Number,
        Today,
    }

    private static readonly OperandKind[] AllKinds = Enum.GetValues<OperandKind>();

    private readonly string _instanceId = "bf-calc-operand-row-" + Guid.NewGuid().ToString("n");
    private ElementReference _kindSelectElement;
    private bool _focusOnNextRender;

    /// <summary>
    /// This row's own sticky memory of which shape it is showing (see this type's own remarks on
    /// why <see cref="Operand"/>'s shape alone cannot always decide). Seeded from whatever
    /// unambiguous shape <see cref="Operand"/> first carries, and from then on only ever moved by
    /// an unambiguous <see cref="Operand"/> update or by the Kind select itself
    /// (<see cref="OnKindChangedAsync"/>) — never by a fully blank <see cref="Operand"/>.
    /// </summary>
    private OperandKind _kind;

    /// <summary>
    /// The operand this row edits.
    /// </summary>
    [Parameter, EditorRequired]
    public CalcOperand Operand { get; set; } = default!;

    /// <summary>
    /// The candidate fields the Field select offers, already filtered by
    /// <see cref="CalculationEditor"/> to whichever operand typing its own current
    /// <see cref="CalcOperation"/> calls for (numeric or date), and, when this row's own current
    /// field is not itself one of them, unioned in so the select never silently drops the row's
    /// existing selection out from under it.
    /// </summary>
    [Parameter, EditorRequired]
    public IReadOnlyList<FormNode> Fields { get; set; } = default!;

    /// <summary>
    /// This row's own one-based ordinal among its siblings, named in every one of its controls'
    /// own accessible labels (PRD §5, §13, D8).
    /// </summary>
    [Parameter]
    public int RowNumber { get; set; } = 1;

    /// <summary>
    /// Raised with the complete replacement operand the moment any control here commits.
    /// </summary>
    [Parameter]
    public EventCallback<CalcOperand> OperandChanged { get; set; }

    /// <summary>
    /// Raised when this row's own remove button is activated. The host editor decides what
    /// removing a row actually does; this row does not remove itself.
    /// </summary>
    [Parameter]
    public EventCallback OnRemove { get; set; }

    /// <summary>
    /// A one-shot signal from the host editor that this render is the reason real DOM focus
    /// should land on this row's own Kind select — a freshly added row, or the stable neighbour a
    /// removal elsewhere in the list lands focus on (PRD §11, WCAG 2.4.3).
    /// </summary>
    [Parameter]
    public bool RequestFocus { get; set; }

    private static IStringLocalizer<DesignerStrings> Localizer => DesignerLocalization.Shared;

    private string KindSelectId => _instanceId + "-kind";

    private string FieldSelectId => _instanceId + "-field";

    private string NumberInputId => _instanceId + "-number";

    private OperandKind Kind => _kind;

    /// <inheritdoc/>
    protected override void OnParametersSet()
    {
        // Only an unambiguous shape ever moves this row's own sticky Kind forward -- a fully
        // blank Operand (every member null) leaves _kind exactly where it already was, whether
        // that is its OnInitialized-time seed or wherever OnKindChangedAsync last explicitly put
        // it (see this type's own remarks; code review fix #1).
        _kind = Operand switch
        {
            { Number: not null } => OperandKind.Number,
            { Function: not null } => OperandKind.Today,
            { Field: not null } => OperandKind.Field,
            _ => _kind,
        };

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
            await _kindSelectElement.FocusAsync();
        }
    }

    private static string FieldLabel(FormNode field) =>
        field.Label ?? Localizer["UntitledNodeLabel", Localizer[$"NodeType{field.Type}"].Value].Value;

    private Task OnKindChangedAsync(OperandKind kind)
    {
        // Set eagerly, before the replacement operand round-trips back through OperandChanged --
        // this is explicit author intent, never the ambiguous "fully blank" case OnParametersSet
        // itself has to guess at, so it is always safe to move _kind here directly. Doing it here
        // (rather than waiting for the round trip) also keeps this row showing the newly chosen
        // shape even when there is no candidate field to default to (Field kind) or the round trip
        // otherwise reflects a blank operand.
        _kind = kind;

        var replacement = kind switch
        {
            OperandKind.Number => new CalcOperand { Number = 0m },
            OperandKind.Today => new CalcOperand { Function = CalcFunction.Today },
            _ => new CalcOperand { Field = Fields.Count > 0 ? Fields[0].Id : null },
        };

        return OperandChanged.InvokeAsync(replacement);
    }

    private Task OnFieldChangedAsync(string field) =>
        OperandChanged.InvokeAsync(new CalcOperand { Field = string.IsNullOrEmpty(field) ? null : field });

    private Task OnNumberTypedAsync(ChangeEventArgs e) => OperandChanged.InvokeAsync(new CalcOperand { Number = ParseDecimal(e.Value) });

    private Task OnRemoveAsync() => OnRemove.InvokeAsync();

    private static decimal? ParseDecimal(object? value) =>
        decimal.TryParse(value?.ToString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
}
