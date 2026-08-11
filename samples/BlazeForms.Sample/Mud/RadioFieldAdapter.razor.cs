using System.Diagnostics.CodeAnalysis;

using BlazeForms.Definitions;
using BlazeForms.Fields;
using BlazeForms.Markdown;
using Microsoft.AspNetCore.Components;

namespace BlazeForms.Sample.Mud;

/// <summary>
/// The MudBlazor honesty-test adapter (PRD §10, §14 success criterion #4) for
/// <see cref="NodeType.Radio"/>: renders a <see cref="MudBlazor.MudRadioGroup{T}"/> of
/// <see cref="string"/> in place of the shipped <see cref="RadioGroupField"/>, wired through
/// <see cref="MudFieldComponentRegistry"/> with no change to <c>BlazeForms.Core</c> or
/// <c>BlazeForms.Renderer</c>. Renders each <see cref="FormOption.Label"/> but stores each
/// option's <see cref="FormOption.Value"/> — labels are display-only and never key data
/// (AGENTS.md invariant #5). Stores its answer as <see cref="string"/>, or
/// <see langword="null"/> when unanswered.
/// </summary>
/// <remarks>
/// <see cref="MudBlazor.MudRadioGroup{T}"/> has no <c>Label</c> or <c>HelperText</c> slot of its
/// own (unlike the single-input Mud adapters), so this adapter keeps the shipped component's
/// <c>fieldset</c>/<c>legend</c> wrapper and its own help/error elements, and points the group at
/// them with <c>aria-labelledby</c>/<c>aria-describedby</c> — the same accessible-name and
/// description wiring <see cref="RadioGroupField"/> uses, just around a Mud control instead of
/// bare radio inputs.
/// </remarks>
[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "BlazeForms.Renderer resolves this type by reflection across the assembly boundary (DynamicComponent, via MudFieldComponentRegistry), and the Razor SDK generates this component's other partial as `public partial class` unconditionally -- an `internal` declaration here would conflict (CS0262).")]
public partial class RadioFieldAdapter : FormFieldBase
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

    // MudRadioGroup raises no OnBlur; the group's rendered root captures unmatched attributes
    // (including event handlers) into MudBlazor's own UserAttributes splat, so an ordinary
    // @onfocusout on the tag reaches the DOM the same way it would on a plain element.
    private Task OnBlurAsync() => OnBlur.InvokeAsync();
}
