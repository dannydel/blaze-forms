using System.Globalization;
using BlazeForms.Internal;
using BlazeForms.Markdown;
using BlazeForms.Resources;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;

namespace BlazeForms.Fields;

/// <summary>
/// A start and end calendar date captured as one answer (PRD §5,
/// <see cref="Definitions.NodeType.DateRange"/>). Stores its answer as a two-element array of
/// ISO-8601 date strings, <c>[start, end]</c>, using an empty string for a side the respondent
/// has not filled in; the whole answer is <see langword="null"/> when both sides are empty.
/// </summary>
/// <remarks>
/// <b>Accessibility.</b> The two dates share one <c>fieldset</c>/<c>legend</c> group labelled
/// with <see cref="Definitions.FormNode.Label"/>, and each date additionally has its own
/// <c>&lt;label for&gt;</c> (localized "Start date"/"End date", PRD §12) so a screen-reader user
/// hears which side they are on. <c>aria-required</c> on each input always reflects
/// <see cref="Definitions.FormNode.Required"/>; <c>aria-invalid</c> activates on both inputs, and
/// <c>aria-describedby</c> on the fieldset, when <see cref="FormFieldBase.Error"/> is set.
/// </remarks>
public partial class DateRangeField : FormFieldBase
{
    private DateOnly? _renderedStart;
    private DateOnly? _renderedEnd;

    /// <summary>
    /// This field's sub-labels' localizer — the internal, host-immune
    /// <see cref="RendererLocalization.Shared"/> instance (PRD §12), not a DI-injected one (see
    /// its remarks for why a DI-injected <c>IStringLocalizer&lt;RendererStrings&gt;</c> is unsafe
    /// against a host's own <c>LocalizationOptions.ResourcesPath</c>).
    /// </summary>
    private static IStringLocalizer<RendererStrings> Localizer => RendererLocalization.Shared;

    /// <inheritdoc />
    protected override void CaptureValueSnapshot()
    {
        _renderedStart = StartDate;
        _renderedEnd = EndDate;
    }

    /// <inheritdoc />
    protected override bool ShouldRender()
    {
        var changed = HaveSharedParametersChanged()
            || _renderedStart != StartDate
            || _renderedEnd != EndDate;

        _renderedStart = StartDate;
        _renderedEnd = EndDate;

        return changed;
    }

    private string StartFieldId => $"{FieldId}-start";

    private string EndFieldId => $"{FieldId}-end";

    private static string StartLabel => Localizer["DateRangeStartLabel"].Value;

    private static string EndLabel => Localizer["DateRangeEndLabel"].Value;

    private DateOnly? StartDate => ParsePart(0);

    private DateOnly? EndDate => ParsePart(1);

    private bool HasHelp => !string.IsNullOrWhiteSpace(Node.Help);

    private bool HasError => !string.IsNullOrWhiteSpace(Error);

    private string RequiredAttributeValue => Node.Required ? "true" : "false";

    private string? InvalidAttributeValue => HasError ? "true" : null;

    private MarkupString HelpMarkup => new(SafeMarkdown.ToHtml(Node.Help).Value);

    private DateOnly? ParsePart(int index)
    {
        if (Value is not IReadOnlyList<string> parts || parts.Count <= index)
        {
            return null;
        }

        var part = parts[index];

        return DateOnly.TryParseExact(part, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;
    }

    private Task SetStartDateAsync(DateOnly? value) => UpdateRangeAsync(value, EndDate);

    private Task SetEndDateAsync(DateOnly? value) => UpdateRangeAsync(StartDate, value);

    private Task UpdateRangeAsync(DateOnly? start, DateOnly? end)
    {
        if (start is null && end is null)
        {
            return ValueChanged.InvokeAsync(null);
        }

        var range = new[]
        {
            start?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
            end?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
        };

        return ValueChanged.InvokeAsync(range);
    }

    private Task OnBlurAsync() => OnBlur.InvokeAsync();
}
