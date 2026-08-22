using System.ComponentModel;

namespace BlazeForms.Fields;

/// <summary>
/// A static section heading (PRD §5, <see cref="Definitions.NodeType.Heading"/>). Emits an
/// <c>&lt;h2&gt;</c>, <c>&lt;h3&gt;</c>, or <c>&lt;h4&gt;</c> from
/// <see cref="Definitions.FormNode.Level"/> — a heading node carries no answer, so it ignores
/// every value/validation parameter on <see cref="FormFieldBase"/>. The heading text is
/// <see cref="Definitions.FormNode.Label"/>, rendered as plain text always (PRD §5.1) — never as
/// Markdown.
/// </summary>
/// <remarks>
/// <b>Accessibility.</b> Using the correct semantic heading level (rather than styled text) is
/// the whole of this component's accessibility contract: screen-reader users navigate by
/// heading level, and the linter's A11Y-08 rule flags a level that skips a rung.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed partial class HeadingBlock : FormFieldBase
{
    /// <inheritdoc />
    protected override bool ShouldRender() => HaveSharedParametersChanged();

    private int Level => Node.Level is >= 2 and <= 4 ? Node.Level.Value : 2;
}
