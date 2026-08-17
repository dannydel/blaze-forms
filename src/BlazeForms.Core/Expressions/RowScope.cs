using BlazeForms.Serialization;

namespace BlazeForms.Expressions;

/// <summary>
/// Builds the merged view a row-scoped rule or calculation reads (the "Reference semantics"
/// design): a repeating group row's own answers, overlaid on the outer flat answers, so a bare
/// node ID resolves the same way whether it names a sibling inside the row or a field outside the
/// group entirely. Internal — row-awareness lives in this orchestration helper, never inside the
/// flat, pure evaluators (<see cref="ConditionEvaluator"/>, <see cref="CalcEvaluator"/>) that
/// consume its result.
/// </summary>
internal static class RowScope
{
    /// <summary>
    /// Overlays a row's own answers on top of the outer flat answers.
    /// </summary>
    /// <param name="outerValues">
    /// The respondent's answers outside the repeating group, keyed by node ID — the base layer a
    /// row-scoped rule falls back to for any ID its own row does not carry.
    /// </param>
    /// <param name="row">
    /// The row whose own values take precedence over <paramref name="outerValues"/> for every ID
    /// they share.
    /// </param>
    /// <returns>
    /// A new, detached map. Neither <paramref name="outerValues"/> nor <paramref name="row"/> is
    /// mutated.
    /// </returns>
    internal static Dictionary<string, object?> Merge(
        IReadOnlyDictionary<string, object?> outerValues,
        RepeatingRow row)
    {
        ArgumentNullException.ThrowIfNull(outerValues);
        ArgumentNullException.ThrowIfNull(row);

        var merged = new Dictionary<string, object?>(outerValues, StringComparer.Ordinal);

        foreach (var pair in row.Values)
        {
            merged[pair.Key] = pair.Value;
        }

        return merged;
    }
}
