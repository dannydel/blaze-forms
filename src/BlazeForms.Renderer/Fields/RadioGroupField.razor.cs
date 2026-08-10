using BlazeForms.Markdown;
using Microsoft.AspNetCore.Components;

namespace BlazeForms.Fields;

/// <summary>
/// A single choice presented as a radio group (PRD §5, <see cref="Definitions.NodeType.Radio"/>).
/// Renders each <see cref="Definitions.FormOption.Label"/> but stores each option's
/// <see cref="Definitions.FormOption.Value"/> — labels are display-only and never key data
/// (AGENTS.md invariant #5). Stores its answer as <see cref="string"/>, or
/// <see langword="null"/> when unanswered.
/// </summary>
/// <remarks>
/// <b>Accessibility.</b> The options sit in a <c>fieldset</c>/<c>legend</c> for visual grouping and
/// carry a <c>role="radiogroup"</c> container named from the legend via <c>aria-labelledby</c>
/// (<c>aria-required</c> is not valid on a fieldset's implicit <c>group</c> role, so it rides the
/// radiogroup, which supports it). Each radio has its own <c>&lt;label for&gt;</c> whose id folds
/// in the option index, so uniqueness never depends on distinct option values sanitizing to
/// distinct id fragments. <c>aria-required</c> always reflects
/// <see cref="Definitions.FormNode.Required"/>; <c>aria-invalid</c> on each radio and
/// <c>aria-describedby</c> on the radiogroup activate when <see cref="FormFieldBase.Error"/> is
/// set. Each choice meets the 44px touch target via <c>--bf-touch-target</c>.
/// </remarks>
public partial class RadioGroupField : FormFieldBase
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

    /// <summary>
    /// Derives a DOM-safe, collision-free id for one option's radio input from
    /// <see cref="FormFieldBase.FieldId"/> and the option's position. Keying on the index rather
    /// than the (author-controlled, importable) option value means two distinct values that would
    /// sanitize to the same fragment — <c>"a b"</c> and <c>"a/b"</c> — still get distinct ids, so
    /// no <c>&lt;label for&gt;</c> can bind to the wrong input.
    /// </summary>
    private string OptionElementId(int index) => $"{FieldId}-opt-{index}";

    private Task SetStringValueAsync(string? value) =>
        ValueChanged.InvokeAsync(string.IsNullOrEmpty(value) ? null : value);

    private Task OnBlurAsync() => OnBlur.InvokeAsync();
}
