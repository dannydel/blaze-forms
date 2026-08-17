using BlazeForms.Definitions;
using BlazeForms.Expressions;
using BlazeForms.Serialization;

namespace BlazeForms.Internal;

/// <summary>
/// Evaluates <see cref="FormDefinition.ValidationRules"/> against the respondent's answers
/// (PRD §6). Only used at submit — page-advance validates per-field checks alone.
/// </summary>
/// <remarks>
/// A rule whose <see cref="ValidationRule.Target"/> and every
/// <see cref="Condition.Field"/> in its <see cref="ValidationRule.Expression"/> live inside the
/// same repeating group evaluates once per row, row-scoped (repeating-groups-plan.md's "Reference
/// semantics" — the outer flat answers merged under the row's own). A rule that instead crosses a
/// group's boundary — reading from outside into a row, or naming fields in two different groups —
/// is a design-time lint (FR-04) this evaluator never guesses at: it is silently skipped here
/// rather than evaluated against an arbitrary or ambiguous row.
/// </remarks>
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
    /// The respondent's raw, unsettled answers, keyed by node ID — read here for a flat rule's own
    /// evaluation and for fetching a repeating group's full <see cref="RepeatingRows"/> value
    /// (every row's own answers, un-trimmed by visibility) so a row-scoped rule's merged view
    /// still sees a sibling that is itself hidden within the row, exactly like the row-scoped
    /// evaluators elsewhere.
    /// </param>
    /// <param name="settledOuterValues">
    /// The settled outer answers (<c>FormRenderer</c>'s own <c>GetVisibleNodeIds</c> out-overload)
    /// for this same pass — the outer-values argument every row-scoped rule's
    /// <see cref="VisibilityEvaluator.GetVisibleChildIds"/> call and merged view use instead of
    /// <paramref name="values"/>. Using the raw dictionary there would let a row-scoped rule
    /// disagree with what a submit actually captures whenever an outer field the rule reads is
    /// itself conditionally hidden — the same fixed-point settling the top-level, flat validation
    /// path already gets from <paramref name="visibleNodeIds"/> having been computed off of it.
    /// </param>
    /// <param name="visibleNodeIds">
    /// The node IDs currently visible to the respondent. A rule whose
    /// <see cref="ValidationRule.Target"/> is not in this set — including a repeating group whose
    /// own value is currently hidden — is skipped entirely.
    /// </param>
    /// <param name="errors">
    /// The accumulated field-error map, keyed by node ID (or, for a row-scoped rule, by
    /// <see cref="RepeatingFieldKeys.ChildKey"/>), that per-field validation has already
    /// populated. A rule is skipped when its target already carries an error, so a structural
    /// failure (an empty required field) is never masked by a cross-field message about the same
    /// field; otherwise the rule's <see cref="ValidationRule.Message"/> is added against its
    /// target.
    /// </param>
    public static void Evaluate(
        FormDefinition definition,
        IReadOnlyDictionary<string, object?> values,
        IReadOnlyDictionary<string, object?> settledOuterValues,
        IReadOnlySet<string> visibleNodeIds,
        Dictionary<string, string> errors)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(settledOuterValues);
        ArgumentNullException.ThrowIfNull(visibleNodeIds);
        ArgumentNullException.ThrowIfNull(errors);

        foreach (var rule in definition.ValidationRules)
        {
            var targetGroupId = ExpressionDependencyAnalysis.GetRepeatingGroupOf(definition, rule.Target);

            if (targetGroupId is null)
            {
                if (ReferencesAnyGroup(definition, rule))
                {
                    // Outside -> inside a row: ambiguous (which row?), FR-04-blocked at design
                    // time -- never guessed here.
                    continue;
                }

                EvaluateFlatRule(rule, values, visibleNodeIds, errors);
                continue;
            }

            if (CrossesGroupBoundary(definition, rule, targetGroupId))
            {
                continue;
            }

            EvaluateRowScopedRule(definition, rule, targetGroupId, values, settledOuterValues, visibleNodeIds, errors);
        }
    }

    private static void EvaluateFlatRule(
        ValidationRule rule,
        IReadOnlyDictionary<string, object?> values,
        IReadOnlySet<string> visibleNodeIds,
        Dictionary<string, string> errors)
    {
        if (!visibleNodeIds.Contains(rule.Target) || errors.ContainsKey(rule.Target))
        {
            return;
        }

        if (ConditionEvaluator.Evaluate(rule.Expression, values))
        {
            errors[rule.Target] = rule.Message;
        }
    }

    private static void EvaluateRowScopedRule(
        FormDefinition definition,
        ValidationRule rule,
        string groupId,
        IReadOnlyDictionary<string, object?> values,
        IReadOnlyDictionary<string, object?> settledOuterValues,
        IReadOnlySet<string> visibleNodeIds,
        Dictionary<string, string> errors)
    {
        if (!visibleNodeIds.Contains(groupId))
        {
            return;
        }

        var group = definition.FindNode(groupId);

        // The group's own RepeatingRows value is read from the raw store, not
        // settledOuterValues -- each row's own answers must stay full and un-trimmed here so the
        // merged view below can still see a sibling that is itself hidden within the row, exactly
        // like every other row-scoped evaluator.
        if (group is null || !values.TryGetValue(groupId, out var raw) || raw is not RepeatingRows rows)
        {
            return;
        }

        foreach (var row in rows.Rows)
        {
            var visibleChildIds = VisibilityEvaluator.GetVisibleChildIds(group, row, settledOuterValues);

            if (!visibleChildIds.Contains(rule.Target))
            {
                continue;
            }

            var key = RepeatingFieldKeys.ChildKey(rule.Target, row.RowId);

            if (errors.ContainsKey(key))
            {
                continue;
            }

            // The outer BASE layer must be settled (agreeing with what a submit will actually
            // capture); the row's own values overlay it unsettled, per RowScope.Merge's contract.
            var merged = MergeRowValues(settledOuterValues, row);

            if (ConditionEvaluator.Evaluate(rule.Expression, merged))
            {
                errors[key] = rule.Message;
            }
        }
    }

    private static bool ReferencesAnyGroup(FormDefinition definition, ValidationRule rule) =>
        rule.Expression.Conditions.Any(condition =>
            ExpressionDependencyAnalysis.GetRepeatingGroupOf(definition, condition.Field) is not null);

    /// <summary>
    /// Whether any of <paramref name="rule"/>'s condition fields belongs to a different repeating
    /// group than <paramref name="targetGroupId"/>. A condition field that belongs to no group at
    /// all (a top-level field outside every group) is never a violation — a row-scoped rule's
    /// merged view already resolves it through the outer layer.
    /// </summary>
    private static bool CrossesGroupBoundary(FormDefinition definition, ValidationRule rule, string targetGroupId) =>
        rule.Expression.Conditions.Any(condition =>
        {
            var conditionGroupId = ExpressionDependencyAnalysis.GetRepeatingGroupOf(definition, condition.Field);
            return conditionGroupId is not null && !string.Equals(conditionGroupId, targetGroupId, StringComparison.Ordinal);
        });

    /// <summary>
    /// The row-scoped counterpart of Core's own (internal, cross-assembly-invisible)
    /// <c>RowScope.Merge</c>: overlays a row's own answers on top of the outer flat answers so a
    /// bare node id resolves the same way whether it names a sibling inside the row or a field
    /// outside the group.
    /// </summary>
    private static Dictionary<string, object?> MergeRowValues(IReadOnlyDictionary<string, object?> outerValues, RepeatingRow row)
    {
        var merged = new Dictionary<string, object?>(outerValues, StringComparer.Ordinal);

        foreach (var pair in row.Values)
        {
            merged[pair.Key] = pair.Value;
        }

        return merged;
    }
}
