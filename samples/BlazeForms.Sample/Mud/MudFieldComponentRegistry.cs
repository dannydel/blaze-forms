using BlazeForms.Definitions;
using BlazeForms.Hosting;

namespace BlazeForms.Sample.Mud;

/// <summary>
/// The P1 honesty test for <see cref="IFieldComponentRegistry"/> (PRD §10, §14 success criterion
/// #4): registers a MudBlazor adapter for every P1 field <see cref="NodeType"/> a Mud control can
/// reasonably represent, so <see cref="FillMud"/> renders through <c>BlazeForms.Renderer</c>'s
/// <c>DynamicComponent</c> resolution with no change to <c>BlazeForms.Core</c> or
/// <c>BlazeForms.Renderer</c> — only this registry and the adapters it points to.
/// </summary>
/// <remarks>
/// Static content (<see cref="NodeType.Heading"/>, <see cref="NodeType.Paragraph"/>,
/// <see cref="NodeType.Callout"/>, <see cref="NodeType.Divider"/>), <see cref="NodeType.Calc"/>,
/// and <see cref="NodeType.DateRange"/> have no entry here and so fall back to the shipped
/// default component — exactly the behavior <c>DefaultFieldComponents.Resolve</c> documents for
/// a node type the registry doesn't override. A <see cref="NodeType.DateRange"/> adapter is a
/// reasonable follow-up (MudBlazor has no built-in date-range picker to wrap without composing
/// two <see cref="MudBlazor.MudDatePicker"/> instances by hand), left out here to keep this
/// honesty test to the field types a single Mud control covers directly.
/// </remarks>
internal sealed class MudFieldComponentRegistry : IFieldComponentRegistry
{
    private static readonly Dictionary<NodeType, Type> ComponentsByNodeType = new()
    {
        [NodeType.Text] = typeof(TextFieldAdapter),
        [NodeType.TextArea] = typeof(TextAreaFieldAdapter),
        [NodeType.Email] = typeof(EmailFieldAdapter),
        [NodeType.Phone] = typeof(PhoneFieldAdapter),
        [NodeType.Number] = typeof(NumberFieldAdapter),
        [NodeType.Currency] = typeof(CurrencyFieldAdapter),
        [NodeType.Date] = typeof(DateFieldAdapter),
        [NodeType.Select] = typeof(SelectFieldAdapter),
        [NodeType.Radio] = typeof(RadioFieldAdapter),
        [NodeType.CheckboxGroup] = typeof(CheckboxGroupFieldAdapter),
        [NodeType.YesNo] = typeof(YesNoFieldAdapter),
        [NodeType.Boolean] = typeof(BooleanFieldAdapter),
    };

    /// <inheritdoc/>
    public bool TryGetComponentType(NodeType nodeType, out Type? componentType) =>
        ComponentsByNodeType.TryGetValue(nodeType, out componentType);
}
