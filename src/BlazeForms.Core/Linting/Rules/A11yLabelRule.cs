using BlazeForms.Definitions;

namespace BlazeForms.Linting.Rules;

/// <summary>
/// A11Y-01 (Blocking): every input node needs a label. A placeholder disappears on entry and is
/// never announced as the field's name, so it does not satisfy this rule; static content nodes,
/// which capture no answer, are never flagged (PRD §8, §5.1).
/// </summary>
internal sealed class A11yLabelRule : ILintRule
{
    /// <inheritdoc />
    public string Id => LintRuleIds.A11y01;

    /// <inheritdoc />
    public LintSeverity Severity => LintSeverity.Blocking;

    /// <inheritdoc />
    public string Rationale =>
        "An input with no label cannot be announced to assistive technology, and a placeholder is not a label because it vanishes once the field has a value.";

    /// <inheritdoc />
    public IEnumerable<LintResult> Analyze(LintContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var results = new List<LintResult>();

        foreach (var node in context.Definition.EnumerateNodes())
        {
            if (!FormSchema.IsInputNode(node.Type) || !string.IsNullOrWhiteSpace(node.Label))
            {
                continue;
            }

            results.Add(new LintResult
            {
                RuleId = Id,
                Severity = Severity,
                Message = "This field has no label.",
                Detail = "Give the field a label; a placeholder is not a label.",
                NodeId = node.Id,
            });
        }

        return results;
    }
}
