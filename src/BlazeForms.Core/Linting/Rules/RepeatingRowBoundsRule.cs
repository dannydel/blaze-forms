using BlazeForms.Definitions;

namespace BlazeForms.Linting.Rules;

/// <summary>
/// REP-01 (Advisory): a <see cref="NodeType.Repeating"/> group whose <see cref="FormNode.MinRows"/>
/// is greater than its <see cref="FormNode.MaxRows"/> can never be satisfied — no respondent could
/// ever reach the minimum without exceeding the maximum (PRD §5).
/// </summary>
internal sealed class RepeatingRowBoundsRule : ILintRule
{
    /// <inheritdoc />
    public string Id => LintRuleIds.Rep01;

    /// <inheritdoc />
    public LintSeverity Severity => LintSeverity.Advisory;

    /// <inheritdoc />
    public string Rationale =>
        "A group whose minimum row count exceeds its maximum can never be satisfied, so no respondent could ever finish it.";

    /// <inheritdoc />
    public IEnumerable<LintResult> Analyze(LintContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var results = new List<LintResult>();

        foreach (var node in context.Definition.EnumerateNodes())
        {
            if (node.Type != NodeType.Repeating || node.MinRows is null || node.MaxRows is null)
            {
                continue;
            }

            if (node.MinRows <= node.MaxRows)
            {
                continue;
            }

            results.Add(new LintResult
            {
                RuleId = Id,
                Severity = Severity,
                Message = "This repeating group's minimum row count exceeds its maximum.",
                Detail = $"MinRows ({node.MinRows}) is greater than MaxRows ({node.MaxRows}).",
                NodeId = node.Id,
            });
        }

        return results;
    }
}
