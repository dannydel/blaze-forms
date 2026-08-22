using System.ComponentModel;
using BlazeForms.Markdown;
using Microsoft.AspNetCore.Components;

namespace BlazeForms.Fields;

/// <summary>
/// A single-line input constrained to a telephone number (PRD §5,
/// <see cref="Definitions.NodeType.Phone"/>). Stores its answer as <see cref="string"/>, or
/// <see langword="null"/> when empty. Renders with <c>type="tel"</c>, <c>inputmode="tel"</c>,
/// and <c>autocomplete="tel"</c> so mobile keyboards and browser autofill behave correctly
/// (PRD §4.2).
/// </summary>
/// <remarks>
/// <b>Accessibility.</b> Same wiring as <see cref="TextField"/>: labelled by
/// <c>&lt;label for&gt;</c>, <c>aria-required</c> always reflects
/// <see cref="Definitions.FormNode.Required"/>, and <c>aria-invalid</c>/<c>aria-describedby</c>
/// activate when <see cref="FormFieldBase.Error"/> is set.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed partial class PhoneField : FormFieldBase
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
