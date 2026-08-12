using BlazeForms.Markdown;
using Microsoft.AspNetCore.Components;

namespace BlazeForms.Fields;

/// <summary>
/// A read-only computed value (PRD §5, <see cref="Definitions.NodeType.Calc"/>). The evaluation
/// engine is P2, so in P1 this renders a read-only, disabled placeholder and never writes an
/// answer: it never invokes <see cref="FormFieldBase.ValueChanged"/>, is never required, and is
/// excluded from the submission payload. A host that already has a computed display string may
/// pass it through <see cref="FormFieldBase.Value"/> as a <see cref="string"/> purely for
/// display; this component treats that string as read-only text, never as an editable answer.
/// </summary>
/// <remarks>
/// <b>Accessibility.</b> The control is both <c>readonly</c> and <c>disabled</c> — it can never
/// hold an answer a respondent typed — so it is out of the tab order and not exposed as an
/// editable form control to assistive technology. The <c>&lt;label for&gt;</c> is still
/// programmatically associated with it via <see cref="FormFieldBase.FieldId"/> so its purpose is
/// still announced.
/// </remarks>
public partial class CalcField : FormFieldBase
{
    private string? _renderedDisplayText;

    /// <inheritdoc />
    protected override void CaptureValueSnapshot() => _renderedDisplayText = DisplayText;

    /// <inheritdoc />
    protected override bool ShouldRender()
    {
        var changed = HaveSharedParametersChanged() || _renderedDisplayText != DisplayText;
        _renderedDisplayText = DisplayText;
        return changed;
    }

    private string DisplayText => (Value as string) ?? Node.Placeholder ?? "";

    private bool HasHelp => !string.IsNullOrWhiteSpace(Node.Help);

    private MarkupString HelpMarkup => new(SafeMarkdown.ToHtml(Node.Help).Value);
}
