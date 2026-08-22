using System.ComponentModel;

namespace BlazeForms.Fields;

/// <summary>
/// A static visual separator (PRD §5, <see cref="Definitions.NodeType.Divider"/>). Carries no
/// answer and no author content, so it ignores every parameter on <see cref="FormFieldBase"/>
/// except <see cref="FormFieldBase.FieldId"/>, which it still renders as an id so a lint
/// jump-to-node action has something to anchor to.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed partial class DividerBlock : FormFieldBase
{
    /// <inheritdoc />
    protected override bool ShouldRender() => HaveSharedParametersChanged();
}
