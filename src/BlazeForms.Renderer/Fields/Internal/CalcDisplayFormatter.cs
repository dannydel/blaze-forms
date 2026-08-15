using System.Globalization;
using BlazeForms.Expressions;

namespace BlazeForms.Fields.Internal;

/// <summary>
/// Formats a <see cref="CalcEvaluator"/> result for display, per its <see cref="CalcFormat"/>
/// hint (PRD §5). Shared by <c>FormRenderer</c>'s live <c>CalcField</c> display and
/// <c>FormSubmissionView</c>'s captured-value row, so one calculation reads identically whether
/// the respondent is still filling the form or reviewing what they already submitted.
/// </summary>
/// <remarks>
/// Display-only, exactly like <see cref="CalcFormat"/> itself: nothing here ever rounds the value
/// it is handed, only the text it hands back — the stored answer keeps full precision regardless
/// of how it is shown (decision log #2). Given a value that does not match the CLR shape the
/// evaluator would actually produce for the requested format — a mismatch only an untrusted or
/// hand-edited definition could cause, since the designer keeps a calculation's operation and
/// format in agreement — this falls back to the plain number/date reading rather than throwing.
/// </remarks>
internal static class CalcDisplayFormatter
{
    /// <summary>
    /// Formats one computed calc value.
    /// </summary>
    /// <param name="value">
    /// The value <see cref="CalcEvaluator.Evaluate"/> or <see cref="CalcEvaluator.EvaluateAll"/>
    /// produced: a <see cref="decimal"/>, a <see cref="DateOnly"/>, or <see langword="null"/>.
    /// </param>
    /// <param name="format">
    /// How to present it.
    /// </param>
    /// <returns>
    /// The formatted display text, or <see langword="null"/> when <paramref name="value"/> is
    /// <see langword="null"/> — a caller falls back to the calc node's own
    /// <see cref="Definitions.FormNode.Placeholder"/> in that case, exactly as it always has.
    /// </returns>
    public static string? Format(object? value, CalcFormat format) => (value, format) switch
    {
        (decimal number, CalcFormat.Integer) =>
            Math.Round(number, 0, MidpointRounding.AwayFromZero).ToString(CultureInfo.CurrentCulture),
        (decimal number, CalcFormat.Currency) => number.ToString("F2", CultureInfo.CurrentCulture),
        (decimal number, _) => number.ToString(CultureInfo.CurrentCulture),
        (DateOnly date, _) => date.ToString("d", CultureInfo.CurrentCulture),
        _ => null,
    };
}
