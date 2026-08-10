using BlazeForms.Markdown;
using Microsoft.AspNetCore.Components;

namespace BlazeForms.Fields;

/// <summary>
/// Zero or more choices presented as a checkbox group (PRD §5,
/// <see cref="Definitions.NodeType.CheckboxGroup"/>). Renders each
/// <see cref="Definitions.FormOption.Label"/> but stores each option's
/// <see cref="Definitions.FormOption.Value"/> — labels are display-only and never key data
/// (AGENTS.md invariant #5). Stores its answer as <see cref="List{T}"/> of
/// <see cref="string"/>; an unanswered group is an empty list, not <see langword="null"/> — a
/// respondent who has selected nothing is different from a field the form never rendered.
/// </summary>
/// <remarks>
/// <b>Accessibility.</b> The options share one <c>fieldset</c>/<c>legend</c> group labelled with
/// <see cref="Definitions.FormNode.Label"/>; each checkbox has its own <c>&lt;label for&gt;</c>
/// whose id folds in the option index, so uniqueness never depends on distinct option values
/// sanitizing to distinct id fragments. There is no group-level <c>aria-required</c> — a
/// fieldset's implicit <c>group</c> role does not support it and a checkbox set has no single
/// required control — so a required group's constraint is conveyed through validation messaging
/// (a later slice). <c>aria-invalid</c> on each checkbox and <c>aria-describedby</c> on the
/// fieldset activate when <see cref="FormFieldBase.Error"/> is set. Each choice meets the 44px
/// touch target via <c>--bf-touch-target</c>.
/// </remarks>
public partial class CheckboxGroupField : FormFieldBase
{
    private IReadOnlyList<string> _renderedSelections = [];

    /// <inheritdoc />
    protected override void CaptureValueSnapshot() => _renderedSelections = Selections;

    /// <inheritdoc />
    protected override bool ShouldRender()
    {
        var selections = Selections;
        var changed = HaveSharedParametersChanged() || !_renderedSelections.SequenceEqual(selections);
        _renderedSelections = selections;
        return changed;
    }

    private IReadOnlyList<string> Selections => Value as IReadOnlyList<string> ?? [];

    private bool IsChecked(string optionValue) => Selections.Contains(optionValue, StringComparer.Ordinal);

    private bool HasHelp => !string.IsNullOrWhiteSpace(Node.Help);

    private bool HasError => !string.IsNullOrWhiteSpace(Error);

    private string? InvalidAttributeValue => HasError ? "true" : null;

    private MarkupString HelpMarkup => new(SafeMarkdown.ToHtml(Node.Help).Value);

    /// <summary>
    /// Derives a DOM-safe, collision-free id for one option's checkbox input from
    /// <see cref="FormFieldBase.FieldId"/> and the option's position. Keying on the index rather
    /// than the (author-controlled, importable) option value means two distinct values that would
    /// sanitize to the same fragment — <c>"a b"</c> and <c>"a/b"</c> — still get distinct ids, so
    /// no <c>&lt;label for&gt;</c> can bind to the wrong input.
    /// </summary>
    private string OptionElementId(int index) => $"{FieldId}-opt-{index}";

    private Task ToggleAsync(string optionValue, bool isChecked)
    {
        var current = Selections;

        List<string> next = isChecked
            ? current.Contains(optionValue, StringComparer.Ordinal) ? [.. current] : [.. current, optionValue]
            : [.. current.Where(selected => !string.Equals(selected, optionValue, StringComparison.Ordinal))];

        return ValueChanged.InvokeAsync(next);
    }

    private Task OnBlurAsync() => OnBlur.InvokeAsync();
}
