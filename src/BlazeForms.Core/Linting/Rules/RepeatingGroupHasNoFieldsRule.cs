using BlazeForms.Definitions;

namespace BlazeForms.Linting.Rules;

/// <summary>
/// REP-02 (Advisory): a <see cref="NodeType.Repeating"/> group with no child fields captures
/// nothing, so a respondent gains nothing from adding a row (PRD §5).
/// </summary>
internal sealed class RepeatingGroupHasNoFieldsRule : ILintRule
{
    /// <inheritdoc />
    public string Id => LintRuleIds.Rep02;

    /// <inheritdoc />
    public LintSeverity Severity => LintSeverity.Advisory;

    /// <inheritdoc />
    public string Rationale =>
        "A repeating group with no fields captures nothing, so a respondent gains nothing from adding a row.";

    /// <inheritdoc />
    public IEnumerable<LintResult> Analyze(LintContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var results = new List<LintResult>();

        foreach (var node in context.Definition.EnumerateNodes())
        {
            if (node.Type != NodeType.Repeating || node.Children.Count > 0)
            {
                continue;
            }

            results.Add(new LintResult
            {
                RuleId = Id,
                Severity = Severity,
                Message = "This repeating group has no fields.",
                Detail = "Add at least one field, or remove the group.",
                NodeId = node.Id,
            });
        }

        return results;
    }
}
