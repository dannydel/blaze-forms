using BlazeForms.Definitions;
using BlazeForms.Hosting;

namespace BlazeForms.Fields;

/// <summary>
/// Resolves the component type the renderer instantiates (via <c>&lt;DynamicComponent&gt;</c> in
/// a later slice) for a given <see cref="NodeType"/>: the host's own registration from
/// <see cref="IFieldComponentRegistry"/> when one exists, falling back to the default component
/// shipped in this namespace. Internal — hosts influence resolution entirely through
/// <see cref="IFieldComponentRegistry"/>; they never call this type directly.
/// </summary>
/// <remarks>
/// <see cref="NodeType.Repeating"/> carries no entry in <see cref="DefaultsByNodeType"/> and
/// never will: a repeating group is resolved structurally, by <c>FormRenderer</c>'s own section
/// loop, which renders the internal <c>Components.RepeatingGroup</c> directly — bypassing this
/// resolver entirely — unless the host's registry (checked by that same loop before ever reaching
/// this type) carries an override for <see cref="NodeType.Repeating"/>, in which case this
/// method's ordinary registry-first path resolves it like any other node type. A call here for
/// <see cref="NodeType.Repeating"/> with no registry override — reachable only from a caller that
/// bypasses <c>FormRenderer</c>'s structural branch, such as this type's own direct unit tests —
/// throws exactly like the two node types P2 still fully reserves, <see cref="NodeType.File"/>
/// and <see cref="NodeType.Lookup"/>.
/// </remarks>
internal static class DefaultFieldComponents
{
    private static readonly Dictionary<NodeType, Type> DefaultsByNodeType = new()
    {
        [NodeType.Text] = typeof(TextField),
        [NodeType.TextArea] = typeof(TextAreaField),
        [NodeType.Email] = typeof(EmailField),
        [NodeType.Phone] = typeof(PhoneField),
        [NodeType.Number] = typeof(NumberField),
        [NodeType.Currency] = typeof(CurrencyField),
        [NodeType.Date] = typeof(DateField),
        [NodeType.DateRange] = typeof(DateRangeField),
        [NodeType.Select] = typeof(SelectField),
        [NodeType.Radio] = typeof(RadioGroupField),
        [NodeType.CheckboxGroup] = typeof(CheckboxGroupField),
        [NodeType.YesNo] = typeof(YesNoField),
        [NodeType.Boolean] = typeof(BooleanField),
        [NodeType.Calc] = typeof(CalcField),
        [NodeType.Heading] = typeof(HeadingBlock),
        [NodeType.Paragraph] = typeof(ParagraphBlock),
        [NodeType.Callout] = typeof(CalloutBlock),
        [NodeType.Divider] = typeof(DividerBlock),
    };

    /// <summary>
    /// Resolves the component type to render for a node type.
    /// </summary>
    /// <param name="nodeType">
    /// The node type to resolve a component for.
    /// </param>
    /// <param name="registry">
    /// The host's optional component registry. Consulted first; its answer wins whenever it has
    /// one registered for <paramref name="nodeType"/>.
    /// </param>
    /// <returns>
    /// The host's registered component type when <paramref name="registry"/> has one for
    /// <paramref name="nodeType"/> and it is assignable to <see cref="FormFieldBase"/>; otherwise
    /// the shipped default for a P1 node type.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// The host registered a component type for <paramref name="nodeType"/> that is not
    /// assignable to <see cref="FormFieldBase"/>, or no default exists for
    /// <paramref name="nodeType"/> (a P2-reserved type — <see cref="NodeType.Repeating"/>,
    /// <see cref="NodeType.File"/>, <see cref="NodeType.Lookup"/> — ships schema only and has no
    /// renderer in P1).
    /// </exception>
    public static Type Resolve(NodeType nodeType, IFieldComponentRegistry? registry)
    {
        if (registry is not null && registry.TryGetComponentType(nodeType, out var registered) && registered is not null)
        {
            if (!typeof(FormFieldBase).IsAssignableFrom(registered))
            {
                throw new InvalidOperationException(
                    $"The component registered for node type '{nodeType}' ({registered.FullName}) does not derive from {nameof(FormFieldBase)}.");
            }

            return registered;
        }

        if (!DefaultsByNodeType.TryGetValue(nodeType, out var defaultType))
        {
            throw new InvalidOperationException(
                $"No default field component is registered for node type '{nodeType}'. It is reserved for a later phase (PRD §5) and has no P1 renderer.");
        }

        return defaultType;
    }
}
