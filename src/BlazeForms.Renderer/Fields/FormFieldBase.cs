using BlazeForms.Definitions;
using Microsoft.AspNetCore.Components;

namespace BlazeForms.Fields;

/// <summary>
/// The parameter contract shared by every field and static-content component the renderer places
/// on a page. Every shipped component under <c>Fields/</c> subclasses this, and so does a host's
/// own replacement registered through <c>IFieldComponentRegistry</c> (PRD §10) — the MudBlazor
/// adapter in <c>samples/</c> is the honesty test for that seam. Only BCL and
/// <c>Microsoft.AspNetCore.Components</c> types appear here, so a subclass in a third-party
/// design-system package never drags that package into <c>BlazeForms.Renderer</c>'s public
/// contract (AGENTS.md invariant #1).
/// </summary>
public abstract class FormFieldBase : ComponentBase
{
    /// <summary>
    /// The definition node this component renders. Never null in practice — the renderer always
    /// supplies one — but nullability is left to the host's own null-safety posture, since
    /// <see cref="EditorRequiredAttribute"/> already makes an omission a compile-time diagnostic.
    /// </summary>
    [Parameter, EditorRequired]
    public FormNode Node { get; set; } = default!;

    /// <summary>
    /// The current answer, in the CLR shape <c>Fields/Internal/FieldValueConventions.cs</c>
    /// documents for <see cref="Node"/>'s <see cref="NodeType"/>. <see langword="null"/> for
    /// static-content nodes, which never read this parameter.
    /// </summary>
    [Parameter]
    public object? Value { get; set; }

    /// <summary>
    /// Raised when the respondent changes the answer. The argument is in the same CLR shape as
    /// <see cref="Value"/>. Never raised by a static-content component, and never raised by
    /// <c>CalcField</c> — a calculated field writes no value in P1 (PRD §5).
    /// </summary>
    [Parameter]
    public EventCallback<object?> ValueChanged { get; set; }

    /// <summary>
    /// Raised when the field's control loses focus, so a host can run per-field validation on
    /// blur (PRD §4.2). Never raised by a static-content component.
    /// </summary>
    [Parameter]
    public EventCallback OnBlur { get; set; }

    /// <summary>
    /// The validation message currently attached to this field, or <see langword="null"/> when
    /// it has none. Drives <c>aria-invalid</c> and <c>aria-describedby</c> — this phase wires
    /// those attributes only; the error summary and the validation logic that produces this
    /// message arrive in a later slice.
    /// </summary>
    [Parameter]
    public string? Error { get; set; }

    /// <summary>
    /// The stable DOM id this instance renders its primary control with. The component derives
    /// every other id it needs (a group's individual options, a help or error element) from this
    /// one, so a host that anchors to it — a lint jump-to-node link, an error-summary link — has
    /// one id to know about per field.
    /// </summary>
    [Parameter]
    public string FieldId { get; set; } = "";

    /// <summary>
    /// Whether the field's control is disabled. Static-content components ignore this parameter.
    /// </summary>
    [Parameter]
    public bool Disabled { get; set; }

    private bool _hasCapturedInitialSnapshot;
    private FormNode? _renderedNode;
    private string? _renderedError;
    private bool _renderedDisabled;
    private string? _renderedFieldId;

    /// <summary>
    /// Seeds every render-discipline snapshot — the shared parameters tracked here, and a
    /// subclass's own <see cref="Value"/> snapshot via <see cref="CaptureValueSnapshot"/> — from
    /// the parameters this instance mounted with. Blazor never consults
    /// <see cref="ComponentBase.ShouldRender"/> for the very first render, so without this
    /// one-time seeding a subclass's first genuine <c>ShouldRender</c> call would compare live
    /// parameters against each snapshot field's type default and re-render even when nothing
    /// actually changed.
    /// </summary>
    protected override void OnParametersSet()
    {
        if (_hasCapturedInitialSnapshot)
        {
            return;
        }

        _hasCapturedInitialSnapshot = true;
        _renderedNode = Node;
        _renderedError = Error;
        _renderedDisabled = Disabled;
        _renderedFieldId = FieldId;
        CaptureValueSnapshot();
    }

    /// <summary>
    /// Seeds a subclass's own <see cref="Value"/>-comparison snapshot — whose CLR type varies by
    /// field, so no single field here could hold it — to match the value this instance mounted
    /// with. The default implementation does nothing, which is correct for the static-content
    /// components that carry no value; every component that tracks its own snapshot field for
    /// <see cref="ComponentBase.ShouldRender"/> overrides this to initialize that field.
    /// </summary>
    protected virtual void CaptureValueSnapshot()
    {
    }

    /// <summary>
    /// Reports whether any parameter <em>other than</em> <see cref="Value"/> has changed since
    /// the last render, and records a fresh snapshot of them. A subclass combines this with its
    /// own comparison of <see cref="Value"/> — whose CLR type varies by field, so no single
    /// comparison serves every one — to implement <see cref="ComponentBase.ShouldRender"/>
    /// without re-rendering every sibling field on every keystroke (AGENTS.md render discipline).
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when <see cref="Node"/>, <see cref="Error"/>,
    /// <see cref="Disabled"/>, or <see cref="FieldId"/> differs from the last recorded snapshot.
    /// </returns>
    protected bool HaveSharedParametersChanged()
    {
        var changed = _renderedNode != Node
            || _renderedError != Error
            || _renderedDisabled != Disabled
            || _renderedFieldId != FieldId;

        _renderedNode = Node;
        _renderedError = Error;
        _renderedDisabled = Disabled;
        _renderedFieldId = FieldId;

        return changed;
    }

    /// <summary>
    /// The id of the <c>legend</c> a grouped field (radio, yes/no, checkbox group, date range)
    /// renders, so a <c>role="radiogroup"</c> container can borrow it as its accessible name
    /// through <c>aria-labelledby</c> — a <c>legend</c> names the <c>fieldset</c> it sits in, not
    /// a nested element with an overridden role.
    /// </summary>
    protected string LegendElementId => $"{FieldId}-legend";

    /// <summary>
    /// The id of this field's help element, present whenever <see cref="FormNode.Help"/> is set.
    /// </summary>
    protected string HelpElementId => $"{FieldId}-help";

    /// <summary>
    /// The id of this field's error element, present whenever <see cref="Error"/> is set.
    /// </summary>
    protected string ErrorElementId => $"{FieldId}-error";

    /// <summary>
    /// Builds the space-separated <c>aria-describedby</c> value for this field: the help element
    /// id when <see cref="FormNode.Help"/> is set, the error element id when <see cref="Error"/>
    /// is set, or <see langword="null"/> when the field describes itself with neither.
    /// </summary>
    /// <returns>
    /// The <c>aria-describedby</c> attribute value, or <see langword="null"/> to omit it.
    /// </returns>
    protected string? BuildDescribedBy()
    {
        var hasHelp = !string.IsNullOrWhiteSpace(Node.Help);
        var hasError = !string.IsNullOrWhiteSpace(Error);

        return (hasHelp, hasError) switch
        {
            (true, true) => $"{HelpElementId} {ErrorElementId}",
            (true, false) => HelpElementId,
            (false, true) => ErrorElementId,
            (false, false) => null,
        };
    }
}
