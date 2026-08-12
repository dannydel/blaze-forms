using System.Diagnostics.CodeAnalysis;

using BlazeForms.Definitions;
using BlazeForms.Fields;
using BlazeForms.Markdown;
using Microsoft.AspNetCore.Components;

namespace BlazeForms.Sample.Mud;

/// <summary>
/// The MudBlazor honesty-test adapter (PRD §10, §14 success criterion #4) for
/// <see cref="NodeType.CheckboxGroup"/>: renders a set of <see cref="MudBlazor.MudCheckBox{T}"/>
/// of <see cref="bool"/> — one per <see cref="FormOption"/> — maintaining a
/// <see cref="List{T}"/> of <see cref="string"/> in place of the shipped
/// <see cref="CheckboxGroupField"/>, wired through <see cref="MudFieldComponentRegistry"/> with
/// no change to <c>BlazeForms.Core</c> or <c>BlazeForms.Renderer</c>. Renders each
/// <see cref="FormOption.Label"/> but stores each option's <see cref="FormOption.Value"/> —
/// labels are display-only and never key data (AGENTS.md invariant #5). An unanswered group is
/// an empty list, not <see langword="null"/>, the same convention the shipped component follows.
/// </summary>
[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "BlazeForms.Renderer resolves this type by reflection across the assembly boundary (DynamicComponent, via MudFieldComponentRegistry), and the Razor SDK generates this component's other partial as `public partial class` unconditionally -- an `internal` declaration here would conflict (CS0262).")]
public partial class CheckboxGroupFieldAdapter : FormFieldBase
{
    private IReadOnlyList<string> _renderedSelections = [];

    /// <inheritdoc/>
    protected override void CaptureValueSnapshot() => _renderedSelections = Selections;

    /// <inheritdoc/>
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

    private MarkupString HelpMarkup => new(SafeMarkdown.ToHtml(Node.Help).Value);

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
