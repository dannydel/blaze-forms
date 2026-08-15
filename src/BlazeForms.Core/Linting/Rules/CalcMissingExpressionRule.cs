using BlazeForms.Definitions;

namespace BlazeForms.Linting.Rules;

/// <summary>
/// CALC-01 (Advisory): a <see cref="NodeType.Calc"/> node with no
/// <see cref="FormNode.Calculation"/> renders as an empty read-only field, which is almost never
/// what an author intends (PRD §5, §8). Advisory rather than blocking, because an in-progress form
/// legitimately carries a calc node whose formula has not been written yet.
/// </summary>
internal sealed class CalcMissingExpressionRule : ILintRule
{
    /// <inheritdoc />
    public string Id => LintRuleIds.Calc01;

    /// <inheritdoc />
    public LintSeverity Severity => LintSeverity.Advisory;

    /// <inheritdoc />
    public string Rationale =>
        "A calc node with no calculation shows the respondent an empty read-only field, so it either needs a formula or should be removed.";

    /// <inheritdoc />
    public IEnumerable<LintResult> Analyze(LintContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var results = new List<LintResult>();

        foreach (var node in context.Definition.EnumerateNodes())
        {
            if (node.Type != NodeType.Calc || node.Calculation is not null)
            {
                continue;
            }

            results.Add(new LintResult
            {
                RuleId = Id,
                Severity = Severity,
                Message = "This calculated field has no calculation.",
                Detail = "Add a calculation, or remove the field so it does not render as an empty read-only input.",
                NodeId = node.Id,
            });
        }

        return results;
    }
}
