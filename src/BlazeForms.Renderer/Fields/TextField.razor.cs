using System.ComponentModel;
using BlazeForms.Markdown;
using Microsoft.AspNetCore.Components;

namespace BlazeForms.Fields;

/// <summary>
/// A single-line free-text input (PRD §5, <see cref="Definitions.NodeType.Text"/>). Stores its
/// answer as <see cref="string"/>, or <see langword="null"/> when empty.
/// </summary>
/// <remarks>
/// <b>Accessibility.</b> The <c>&lt;label for&gt;</c> is programmatically associated with the
/// input via <see cref="FormFieldBase.FieldId"/> — never the placeholder, which carries only
/// example text. <c>aria-required</c> always reflects <see cref="Definitions.FormNode.Required"/>.
/// When <see cref="FormFieldBase.Error"/> is set, <c>aria-invalid="true"</c> and
/// <c>aria-describedby</c> includes the error element; it always includes the help element when
/// help text is present. The input meets the 44px touch target via <c>--bf-touch-target</c> and
/// shows a visible focus ring, both from the shared theme (<c>blazeforms.css</c>).
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed partial class TextField : FormFieldBase
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
