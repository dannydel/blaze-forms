using BlazeForms.Definitions;
using BlazeForms.Expressions;

namespace BlazeForms.Linting.Rules;

/// <summary>
/// FR-04 (Blocking): a visibility rule, a calculation, or a cross-field validation rule may not
/// reference a field inside a repeating group from outside that exact group (PRD §5's "Reference
/// semantics" — an outside-to-inside or group-to-group reference is ambiguous, since there is no
/// single row to resolve it against). A reference from inside a group to a field outside it, or
/// to a sibling in the same group, is unambiguous and stays clean.
/// </summary>
internal sealed class RepeatingGroupBoundaryRule : ILintRule
{
    /// <inheritdoc />
    public string Id => LintRuleIds.Fr04;

    /// <inheritdoc />
    public LintSeverity Severity => LintSeverity.Blocking;

    /// <inheritdoc />
    public string Rationale =>
        "A rule that reaches into a repeating group's rows from outside that exact group cannot say which row it means, so it must be rewritten before publishing.";

    /// <inheritdoc />
    public IEnumerable<LintResult> Analyze(LintContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var definition = context.Definition;
        var knownIds = definition.EnumerateNodes()
            .Select(node => node.Id)
            .ToHashSet(StringComparer.Ordinal);

        var results = new List<LintResult>();

        void Check(string? ownerGroupId, string referencedFieldId, string? anchorNodeId)
        {
            // A dangling reference (one that names no node at all) is FR-03's concern, not this
            // rule's: there is no group to compare against.
            if (!knownIds.Contains(referencedFieldId))
            {
                return;
            }

            var targetGroupId = ExpressionDependencyAnalysis.GetRepeatingGroupOf(definition, referencedFieldId);

            if (targetGroupId is null || string.Equals(targetGroupId, ownerGroupId, StringComparison.Ordinal))
            {
                return;
            }

            results.Add(new LintResult
            {
                RuleId = Id,
                Severity = Severity,
                Message = "A rule references a field inside a repeating group from outside that group.",
                Detail = $"Referenced field '{referencedFieldId}' belongs to repeating group '{targetGroupId}'.",
                NodeId = anchorNodeId,
            });
        }

        foreach (var node in definition.EnumerateNodes())
        {
            var ownerGroupId = ExpressionDependencyAnalysis.GetRepeatingGroupOf(definition, node.Id);

            if (node.VisibleWhen is not null)
            {
                foreach (var condition in node.VisibleWhen.Conditions)
                {
                    Check(ownerGroupId, condition.Field, node.Id);
                }
            }

            if (node.Calculation is not null)
            {
                foreach (var operand in node.Calculation.Operands)
                {
                    if (operand.Field is not null)
                    {
                        Check(ownerGroupId, operand.Field, node.Id);
                    }
                }
            }
        }

        foreach (var rule in definition.ValidationRules)
        {
            var ownerGroupId = ExpressionDependencyAnalysis.GetRepeatingGroupOf(definition, rule.Target);
            var anchor = knownIds.Contains(rule.Target) ? rule.Target : null;

            foreach (var condition in rule.Expression.Conditions)
            {
                Check(ownerGroupId, condition.Field, anchor);
            }
        }

        return results;
    }
}
