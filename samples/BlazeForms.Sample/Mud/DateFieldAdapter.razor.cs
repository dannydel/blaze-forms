using System.Diagnostics.CodeAnalysis;

using BlazeForms.Definitions;
using BlazeForms.Fields;

namespace BlazeForms.Sample.Mud;

/// <summary>
/// The MudBlazor honesty-test adapter (PRD §10, §14 success criterion #4) for
/// <see cref="NodeType.Date"/>: renders a <see cref="MudBlazor.MudDatePicker"/> in place of the
/// shipped <see cref="DateField"/>, wired through <see cref="MudFieldComponentRegistry"/> with no
/// change to <c>BlazeForms.Core</c> or <c>BlazeForms.Renderer</c>. Stores its answer as
/// <see cref="DateOnly"/>?, or <see langword="null"/> when empty — <see cref="MudDatePicker"/>
/// works in <see cref="DateTime"/>?, so this adapter converts at the boundary rather than
/// widening the CLR shape <c>FieldValueConventions</c> pins for every
/// <see cref="NodeType.Date"/> answer, Mud or shipped.
/// </summary>
[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "BlazeForms.Renderer resolves this type by reflection across the assembly boundary (DynamicComponent, via MudFieldComponentRegistry), and the Razor SDK generates this component's other partial as `public partial class` unconditionally -- an `internal` declaration here would conflict (CS0262).")]
public partial class DateFieldAdapter : FormFieldBase
{
    private DateOnly? _renderedDateValue;

    /// <inheritdoc/>
    protected override void CaptureValueSnapshot() => _renderedDateValue = DateValue;

    /// <inheritdoc/>
    protected override bool ShouldRender()
    {
        var changed = HaveSharedParametersChanged() || _renderedDateValue != DateValue;
        _renderedDateValue = DateValue;
        return changed;
    }

    private DateOnly? DateValue => Value as DateOnly?;

    private DateTime? MudDateValue => DateValue is { } dateOnly ? dateOnly.ToDateTime(TimeOnly.MinValue) : null;

    private bool HasError => !string.IsNullOrWhiteSpace(Error);

    private Task SetMudDateValueAsync(DateTime? value) =>
        ValueChanged.InvokeAsync(value is { } dateTime ? DateOnly.FromDateTime(dateTime) : null);

    // MudDatePicker raises no OnBlur -- PickerClosed is the closest equivalent commit point,
    // firing once the respondent picks a date or dismisses the popover.
    private Task OnBlurAsync() => OnBlur.InvokeAsync();
}
