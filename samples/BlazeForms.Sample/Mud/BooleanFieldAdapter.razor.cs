using System.Diagnostics.CodeAnalysis;

using BlazeForms.Definitions;
using BlazeForms.Fields;
using BlazeForms.Markdown;
using Microsoft.AspNetCore.Components;

namespace BlazeForms.Sample.Mud;

/// <summary>
/// The MudBlazor honesty-test adapter (PRD §10, §14 success criterion #4) for
/// <see cref="NodeType.Boolean"/>: renders a <see cref="MudBlazor.MudCheckBox{T}"/> of
/// <see cref="bool"/> in place of the shipped <see cref="BooleanField"/>, wired through
/// <see cref="MudFieldComponentRegistry"/> with no change to <c>BlazeForms.Core</c> or
/// <c>BlazeForms.Renderer</c>. Stores its answer as <see cref="bool"/> — unlike every other
/// choice type, an unanswered box always has a value: unchecked is <see langword="false"/>,
/// never <see langword="null"/>.
/// </summary>
/// <remarks>
/// <see cref="MudBlazor.MudCheckBox{T}"/> renders its own label but has no <c>HelperText</c> or
/// <c>OnBlur</c> slot, so this adapter renders the help and error text itself (same convention as
/// <see cref="RadioFieldAdapter"/>) and reaches blur through an <c>@onfocusout</c> unmatched
/// attribute, which MudBlazor's own <c>UserAttributes</c> splat forwards to the checkbox's root
/// element.
/// </remarks>
[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "BlazeForms.Renderer resolves this type by reflection across the assembly boundary (DynamicComponent, via MudFieldComponentRegistry), and the Razor SDK generates this component's other partial as `public partial class` unconditionally -- an `internal` declaration here would conflict (CS0262).")]
public partial class BooleanFieldAdapter : FormFieldBase
{
    private bool _renderedBoolValue;

    /// <inheritdoc/>
    protected override void CaptureValueSnapshot() => _renderedBoolValue = BoolValue;

    /// <inheritdoc/>
    protected override bool ShouldRender()
    {
        var changed = HaveSharedParametersChanged() || _renderedBoolValue != BoolValue;
        _renderedBoolValue = BoolValue;
        return changed;
    }

    private bool BoolValue => Value is true;

    private bool HasHelp => !string.IsNullOrWhiteSpace(Node.Help);

    private bool HasError => !string.IsNullOrWhiteSpace(Error);

    private MarkupString HelpMarkup => new(SafeMarkdown.ToHtml(Node.Help).Value);

    private Task SetBoolValueAsync(bool value) => ValueChanged.InvokeAsync(value);

    private Task OnBlurAsync() => OnBlur.InvokeAsync();
}
