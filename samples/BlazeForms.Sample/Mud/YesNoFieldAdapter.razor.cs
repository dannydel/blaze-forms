using System.Diagnostics.CodeAnalysis;

using BlazeForms.Definitions;
using BlazeForms.Fields;
using BlazeForms.Markdown;
using Microsoft.AspNetCore.Components;

namespace BlazeForms.Sample.Mud;

/// <summary>
/// The MudBlazor honesty-test adapter (PRD §10, §14 success criterion #4) for
/// <see cref="NodeType.YesNo"/>: renders a <see cref="MudBlazor.MudRadioGroup{T}"/> of
/// <see cref="string"/> in place of the shipped <see cref="YesNoField"/>, wired through
/// <see cref="MudFieldComponentRegistry"/> with no change to <c>BlazeForms.Core</c> or
/// <c>BlazeForms.Renderer</c>. Kept as its own component — a near-duplicate of
/// <see cref="RadioFieldAdapter"/> — for the same reason the shipped <see cref="YesNoField"/> is
/// its own component rather than a mode of <see cref="RadioGroupField"/>: so a host's
/// <see cref="MudFieldComponentRegistry"/> registration can swap the two node types
/// independently. Stores its answer as <see cref="string"/>, or <see langword="null"/> when
/// unanswered.
/// </summary>
[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "BlazeForms.Renderer resolves this type by reflection across the assembly boundary (DynamicComponent, via MudFieldComponentRegistry), and the Razor SDK generates this component's other partial as `public partial class` unconditionally -- an `internal` declaration here would conflict (CS0262).")]
public partial class YesNoFieldAdapter : FormFieldBase
{
    private string? _renderedStringValue;

    /// <inheritdoc/>
    protected override void CaptureValueSnapshot() => _renderedStringValue = StringValue;

    /// <inheritdoc/>
    protected override bool ShouldRender()
    {
        var changed = HaveSharedParametersChanged() || _renderedStringValue != StringValue;
        _renderedStringValue = StringValue;
        return changed;
    }

    private string? StringValue => Value as string;

    private bool HasHelp => !string.IsNullOrWhiteSpace(Node.Help);

    private bool HasError => !string.IsNullOrWhiteSpace(Error);

    private MarkupString HelpMarkup => new(SafeMarkdown.ToHtml(Node.Help).Value);

    private Task SetStringValueAsync(string? value) =>
        ValueChanged.InvokeAsync(string.IsNullOrEmpty(value) ? null : value);

    private Task OnBlurAsync() => OnBlur.InvokeAsync();
}
