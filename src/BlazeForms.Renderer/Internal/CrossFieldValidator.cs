using BlazeForms.Definitions;
using BlazeForms.Expressions;

namespace BlazeForms.Internal;

/// <summary>
/// Evaluates <see cref="FormDefinition.ValidationRules"/> against the respondent's answers
/// (PRD §6). Only used at submit — page-advance validates per-field checks alone.
/// </summary>
internal static class CrossFieldValidator
{
    /// <summary>
    /// Adds one entry to <paramref name="errors"/> for every rule whose expression currently
    /// describes the invalid state.
    /// </summary>
    /// <param name="definition">
    /// The definition whose <see cref="FormDefinition.ValidationRules"/> to evaluate.
    /// </param>
    /// <param name="values">
    /// The respondent's answers, keyed by node ID.
    /// </param>
    /// <param name="visibleNodeIds">
    /// The node IDs currently visible to the respondent. A rule whose
    /// <see cref="ValidationRule.Target"/> is not in this set is skipped — a hidden field carries
    /// no validation state (PRD §6).
    /// </param>
    /// <param name="errors">
    /// The accumulated field-error map, keyed by node ID, that per-field validation has already
    /// populated. A rule is skipped when its target already carries a per-field error, so a
    /// structural failure (an empty required field) is never masked by a cross-field message
    /// about the same field; otherwise the rule's <see cref="ValidationRule.Message"/> is added
    /// against its <see cref="ValidationRule.Target"/>.
    /// </param>
    public static void Evaluate(
        FormDefinition definition,
        IReadOnlyDictionary<string, object?> values,
        IReadOnlySet<string> visibleNodeIds,
        Dictionary<string, string> errors)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(visibleNodeIds);
        ArgumentNullException.ThrowIfNull(errors);

        foreach (var rule in definition.ValidationRules)
        {
            if (!visibleNodeIds.Contains(rule.Target) || errors.ContainsKey(rule.Target))
            {
                continue;
            }

            if (ConditionEvaluator.Evaluate(rule.Expression, values))
            {
                errors[rule.Target] = rule.Message;
            }
        }
    }
}
