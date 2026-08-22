using System.ComponentModel;
using BlazeForms.Markdown;
using Microsoft.AspNetCore.Components;

namespace BlazeForms.Fields;

/// <summary>
/// A single calendar date (PRD §5, <see cref="Definitions.NodeType.Date"/>). Stores its answer
/// as <see cref="DateOnly"/>?, or <see langword="null"/> when empty. Renders with
/// <c>type="date"</c>, which gives every supported browser its native date picker and a
/// locale-aware presentation of an ISO-8601 value.
/// </summary>
/// <remarks>
/// <b>Accessibility.</b> Same wiring as <see cref="TextField"/>: labelled by
/// <c>&lt;label for&gt;</c>, <c>aria-required</c> always reflects
/// <see cref="Definitions.FormNode.Required"/>, and <c>aria-invalid</c>/<c>aria-describedby</c>
/// activate when <see cref="FormFieldBase.Error"/> is set.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed partial class DateField : FormFieldBase
{
    private DateOnly? _renderedDateValue;

    /// <inheritdoc />
    protected override void CaptureValueSnapshot() => _renderedDateValue = DateValue;

    /// <inheritdoc />
    protected override bool ShouldRender()
    {
        var changed = HaveSharedParametersChanged() || _renderedDateValue != DateValue;
        _renderedDateValue = DateValue;
        return changed;
    }

    private DateOnly? DateValue => Value as DateOnly?;

    private bool HasHelp => !string.IsNullOrWhiteSpace(Node.Help);

    private bool HasError => !string.IsNullOrWhiteSpace(Error);

    private string RequiredAttributeValue => Node.Required ? "true" : "false";

    private string? InvalidAttributeValue => HasError ? "true" : null;

    private MarkupString HelpMarkup => new(SafeMarkdown.ToHtml(Node.Help).Value);

    private Task SetDateValueAsync(DateOnly? value) => ValueChanged.InvokeAsync(value);

    private Task OnBlurAsync() => OnBlur.InvokeAsync();
}
