using BlazeForms.Markdown;
using Microsoft.AspNetCore.Components;

namespace BlazeForms.Fields;

/// <summary>
/// A single choice presented as a drop-down list (PRD §5, <see cref="Definitions.NodeType.Select"/>).
/// Renders each <see cref="Definitions.FormOption.Label"/> but stores each option's
/// <see cref="Definitions.FormOption.Value"/> — labels are display-only and never key data
/// (AGENTS.md invariant #5). Stores its answer as <see cref="string"/>, or
/// <see langword="null"/> when unanswered.
/// </summary>
/// <remarks>
/// <b>Accessibility.</b> The <c>&lt;label for&gt;</c> is programmatically associated with the
/// <c>&lt;select&gt;</c> via <see cref="FormFieldBase.FieldId"/>. <c>aria-required</c> always
/// reflects <see cref="Definitions.FormNode.Required"/>, and <c>aria-invalid</c>/
/// <c>aria-describedby</c> activate when <see cref="FormFieldBase.Error"/> is set. The leading
/// blank option carries <see cref="Definitions.FormNode.Placeholder"/> as its text — a genuine
/// unanswered state, not a substitute for the label.
/// </remarks>
public partial class SelectField : FormFieldBase
{
    private string? _renderedStringValue;

    /// <inheritdoc />
    protected override void CaptureValueSnapshot() => _renderedStringValue = StringValue;

    /// <inheritdoc />
    protected override bool ShouldRender()
    {
        var changed = HaveSharedParametersChanged() || _renderedStringValue != StringValue;
        _renderedStringValue = StringValue;
        return changed;
    }

    private string? StringValue => Value as string;

    private bool HasHelp => !string.IsNullOrWhiteSpace(Node.Help);

    private bool HasError => !string.IsNullOrWhiteSpace(Error);

    private string RequiredAttributeValue => Node.Required ? "true" : "false";

    private string? InvalidAttributeValue => HasError ? "true" : null;

    private MarkupString HelpMarkup => new(SafeMarkdown.ToHtml(Node.Help).Value);

    private Task SetStringValueAsync(string? value) =>
        ValueChanged.InvokeAsync(string.IsNullOrEmpty(value) ? null : value);

    private Task OnBlurAsync() => OnBlur.InvokeAsync();
}
