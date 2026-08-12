using System.Diagnostics.CodeAnalysis;

using BlazeForms.Definitions;
using BlazeForms.Fields;

namespace BlazeForms.Sample.Mud;

/// <summary>
/// The MudBlazor honesty-test adapter (PRD §10, §14 success criterion #4) for
/// <see cref="NodeType.Select"/>: renders a <see cref="MudBlazor.MudSelect{T}"/> of
/// <see cref="string"/> in place of the shipped <see cref="SelectField"/>, wired through
/// <see cref="MudFieldComponentRegistry"/> with no change to <c>BlazeForms.Core</c> or
/// <c>BlazeForms.Renderer</c>. Renders each <see cref="FormOption.Label"/> but stores each
/// option's <see cref="FormOption.Value"/> — labels are display-only and never key data
/// (AGENTS.md invariant #5). Stores its answer as <see cref="string"/>, or
/// <see langword="null"/> when unanswered.
/// </summary>
[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "BlazeForms.Renderer resolves this type by reflection across the assembly boundary (DynamicComponent, via MudFieldComponentRegistry), and the Razor SDK generates this component's other partial as `public partial class` unconditionally -- an `internal` declaration here would conflict (CS0262).")]
public partial class SelectFieldAdapter : FormFieldBase
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

    private bool HasError => !string.IsNullOrWhiteSpace(Error);

    private Task SetStringValueAsync(string? value) =>
        ValueChanged.InvokeAsync(string.IsNullOrEmpty(value) ? null : value);

    private Task OnBlurAsync() => OnBlur.InvokeAsync();
}
