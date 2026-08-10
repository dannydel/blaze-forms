using BlazeForms.Definitions;

namespace BlazeForms.Linting.Rules;

/// <summary>
/// A11Y-08 (Advisory): heading levels should step by one, not skip a rung (PRD §8). A jump like
/// h2 → h4 breaks the outline a screen-reader user navigates by. Only a downward jump of more
/// than one rung is flagged; the first heading sets the starting level and is never flagged, and
/// stepping back up to a shallower heading is fine.
/// </summary>
internal sealed class HeadingLevelRule : ILintRule
{
    private const int DefaultHeadingLevel = 2;

    /// <inheritdoc />
    public string Id => LintRuleIds.A11y08;

    /// <inheritdoc />
    public LintSeverity Severity => LintSeverity.Advisory;

    /// <inheritdoc />
    public string Rationale =>
        "Skipping a heading level leaves a gap in the document outline that assistive technology exposes as a broken structure.";

    /// <inheritdoc />
    public IEnumerable<LintResult> Analyze(LintContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var results = new List<LintResult>();
        int? previousLevel = null;

        foreach (var node in context.Definition.EnumerateNodes())
        {
            if (node.Type != NodeType.Heading)
            {
                continue;
            }

            var level = node.Level ?? DefaultHeadingLevel;

            if (previousLevel is int previous && level - previous > 1)
            {
                results.Add(new LintResult
                {
                    RuleId = Id,
                    Severity = Severity,
                    Message = "This heading skips a level.",
                    Detail = $"Heading jumps from level {previous} to level {level}.",
                    NodeId = node.Id,
                });
            }

            previousLevel = level;
        }

        return results;
    }
}
