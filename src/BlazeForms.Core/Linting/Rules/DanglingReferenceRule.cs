using BlazeForms.Definitions;

namespace BlazeForms.Linting.Rules;

/// <summary>
/// FR-03 (Blocking): a rule may not reference a field that no longer exists (PRD §8). Deleting a
/// referenced field is allowed — with a warning — so the dangling reference it leaves behind is
/// what the linter catches, both in a node's visibility rule and in a cross-field validation
/// rule.
/// </summary>
internal sealed class DanglingReferenceRule : ILintRule
{
    /// <inheritdoc />
    public string Id => LintRuleIds.Fr03;

    /// <inheritdoc />
    public LintSeverity Severity => LintSeverity.Blocking;

    /// <inheritdoc />
    public string Rationale =>
        "A rule that points at a deleted field can neither evaluate nor be repaired by the respondent, so it must be resolved before publishing.";

    /// <inheritdoc />
    public IEnumerable<LintResult> Analyze(LintContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var definition = context.Definition;
        var knownIds = definition.EnumerateNodes()
            .Select(node => node.Id)
            .ToHashSet(StringComparer.Ordinal);

        var results = new List<LintResult>();

        void Check(string referencedId, string? anchorNodeId)
        {
            if (knownIds.Contains(referencedId))
            {
                return;
            }

            results.Add(new LintResult
            {
                RuleId = Id,
                Severity = Severity,
                Message = "A rule references a field that no longer exists.",
                Detail = $"Referenced field '{referencedId}' does not exist.",
                NodeId = anchorNodeId,
            });
        }

        foreach (var node in definition.EnumerateNodes())
        {
            if (node.VisibleWhen is null)
            {
                continue;
            }

            foreach (var condition in node.VisibleWhen.Conditions)
            {
                Check(condition.Field, node.Id);
            }
        }

        foreach (var rule in definition.ValidationRules)
        {
            var anchor = knownIds.Contains(rule.Target) ? rule.Target : null;

            Check(rule.Target, anchor);

            foreach (var condition in rule.Expression.Conditions)
            {
                Check(condition.Field, anchor);
            }
        }

        return results;
    }
}
