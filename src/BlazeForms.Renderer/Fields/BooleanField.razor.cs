using BlazeForms.Markdown;
using Microsoft.AspNetCore.Components;

namespace BlazeForms.Fields;

/// <summary>
/// A single opt-in checkbox (PRD §5, <see cref="Definitions.NodeType.Boolean"/>). Stores its
/// answer as <see cref="bool"/> — unlike every other choice type, an unanswered box always has a
/// value: unchecked is <see langword="false"/>, never <see langword="null"/>.
/// </summary>
/// <remarks>
/// <b>Accessibility.</b> The <c>&lt;label for&gt;</c> is programmatically associated with the
/// checkbox via <see cref="FormFieldBase.FieldId"/>. <c>aria-required</c> always reflects
/// <see cref="Definitions.FormNode.Required"/>, and <c>aria-invalid</c>/<c>aria-describedby</c>
/// activate when <see cref="FormFieldBase.Error"/> is set. The checkbox meets the 44px touch
/// target via <c>--bf-touch-target</c>.
/// </remarks>
public partial class BooleanField : FormFieldBase
{
    private bool _renderedBoolValue;

    /// <inheritdoc />
    protected override void CaptureValueSnapshot() => _renderedBoolValue = BoolValue;

    /// <inheritdoc />
    protected override bool ShouldRender()
    {
        var changed = HaveSharedParametersChanged() || _renderedBoolValue != BoolValue;
        _renderedBoolValue = BoolValue;
        return changed;
    }

    private bool BoolValue => Value is true;

    private bool HasHelp => !string.IsNullOrWhiteSpace(Node.Help);

    private bool HasError => !string.IsNullOrWhiteSpace(Error);

    private string RequiredAttributeValue => Node.Required ? "true" : "false";

    private string? InvalidAttributeValue => HasError ? "true" : null;

    private MarkupString HelpMarkup => new(SafeMarkdown.ToHtml(Node.Help).Value);

    private Task SetBoolValueAsync(bool value) => ValueChanged.InvokeAsync(value);

    private Task OnBlurAsync() => OnBlur.InvokeAsync();
}
