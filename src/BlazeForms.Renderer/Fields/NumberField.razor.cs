using BlazeForms.Markdown;
using Microsoft.AspNetCore.Components;

namespace BlazeForms.Fields;

/// <summary>
/// A numeric input, optionally bounded by <see cref="Definitions.FormNode.Min"/> and
/// <see cref="Definitions.FormNode.Max"/> (PRD §5, <see cref="Definitions.NodeType.Number"/>).
/// Stores its answer as <see cref="decimal"/>?, or <see langword="null"/> when empty. Renders
/// with <c>type="number"</c> and <c>inputmode="decimal"</c> so mobile keyboards offer digits and
/// a decimal separator (PRD §4.2).
/// </summary>
/// <remarks>
/// <b>Accessibility.</b> Same wiring as <see cref="TextField"/>: labelled by
/// <c>&lt;label for&gt;</c>, <c>aria-required</c> always reflects
/// <see cref="Definitions.FormNode.Required"/>, and <c>aria-invalid</c>/<c>aria-describedby</c>
/// activate when <see cref="FormFieldBase.Error"/> is set.
/// </remarks>
public partial class NumberField : FormFieldBase
{
    private decimal? _renderedDecimalValue;

    /// <inheritdoc />
    protected override void CaptureValueSnapshot() => _renderedDecimalValue = DecimalValue;

    /// <inheritdoc />
    protected override bool ShouldRender()
    {
        var changed = HaveSharedParametersChanged() || _renderedDecimalValue != DecimalValue;
        _renderedDecimalValue = DecimalValue;
        return changed;
    }

    private decimal? DecimalValue => Value as decimal?;

    private bool HasHelp => !string.IsNullOrWhiteSpace(Node.Help);

    private bool HasError => !string.IsNullOrWhiteSpace(Error);

    private string RequiredAttributeValue => Node.Required ? "true" : "false";

    private string? InvalidAttributeValue => HasError ? "true" : null;

    private MarkupString HelpMarkup => new(SafeMarkdown.ToHtml(Node.Help).Value);

    private Task SetDecimalValueAsync(decimal? value) => ValueChanged.InvokeAsync(value);

    private Task OnBlurAsync() => OnBlur.InvokeAsync();
}
