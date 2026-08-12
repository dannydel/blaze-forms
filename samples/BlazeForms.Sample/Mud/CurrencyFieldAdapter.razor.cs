using System.Diagnostics.CodeAnalysis;

using BlazeForms.Definitions;
using BlazeForms.Fields;

namespace BlazeForms.Sample.Mud;

/// <summary>
/// The MudBlazor honesty-test adapter (PRD §10, §14 success criterion #4) for
/// <see cref="NodeType.Currency"/>: renders a <see cref="MudBlazor.MudNumericField{T}"/> of
/// <see cref="decimal"/>? with a leading currency adornment in place of the shipped
/// <see cref="CurrencyField"/>, wired through <see cref="MudFieldComponentRegistry"/> with no
/// change to <c>BlazeForms.Core</c> or <c>BlazeForms.Renderer</c>. Stores its answer as
/// <see cref="decimal"/>?, or <see langword="null"/> when empty.
/// </summary>
[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "BlazeForms.Renderer resolves this type by reflection across the assembly boundary (DynamicComponent, via MudFieldComponentRegistry), and the Razor SDK generates this component's other partial as `public partial class` unconditionally -- an `internal` declaration here would conflict (CS0262).")]
public partial class CurrencyFieldAdapter : FormFieldBase
{
    private decimal? _renderedDecimalValue;

    /// <inheritdoc/>
    protected override void CaptureValueSnapshot() => _renderedDecimalValue = DecimalValue;

    /// <inheritdoc/>
    protected override bool ShouldRender()
    {
        var changed = HaveSharedParametersChanged() || _renderedDecimalValue != DecimalValue;
        _renderedDecimalValue = DecimalValue;
        return changed;
    }

    private decimal? DecimalValue => Value as decimal?;

    private bool HasError => !string.IsNullOrWhiteSpace(Error);

    private Task SetDecimalValueAsync(decimal? value) => ValueChanged.InvokeAsync(value);

    private Task OnBlurAsync() => OnBlur.InvokeAsync();
}
