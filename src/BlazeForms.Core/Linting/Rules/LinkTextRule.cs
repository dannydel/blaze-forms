using BlazeForms.Definitions;
using BlazeForms.Markdown;

namespace BlazeForms.Linting.Rules;

/// <summary>
/// A11Y-09 (Advisory): a Markdown link's text should describe where it goes (PRD §8). "Click
/// here" and a bare URL both fail a screen-reader user scanning a list of links out of context.
/// The rule inspects every Markdown-enabled string — any node's <c>help</c>, and a paragraph's or
/// callout's <c>content</c> (PRD §5.1) — through the shared safe-Markdown pipeline, so it judges
/// exactly the links the renderer will show.
/// </summary>
internal sealed class LinkTextRule : ILintRule
{
    private static readonly string[] NonDescriptiveText =
        ["click here", "here", "link", "read more", "more", "this"];

    /// <inheritdoc />
    public string Id => LintRuleIds.A11y09;

    /// <inheritdoc />
    public LintSeverity Severity => LintSeverity.Advisory;

    /// <inheritdoc />
    public string Rationale =>
        "Assistive technology can list a page's links out of context, so link text like \"click here\" or a bare URL tells the user nothing about the destination.";

    /// <inheritdoc />
    public IEnumerable<LintResult> Analyze(LintContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var results = new List<LintResult>();

        foreach (var node in context.Definition.EnumerateNodes())
        {
            Inspect(node.Help, node, results);

            if (node.Type is NodeType.Paragraph or NodeType.Callout)
            {
                Inspect(node.Content, node, results);
            }
        }

        return results;
    }

    private void Inspect(string? markdown, FormNode node, List<LintResult> results)
    {
        foreach (var link in SafeMarkdown.ExtractLinks(markdown))
        {
            var text = link.Text.Trim();

            var nonDescriptive = text.Length == 0
                || NonDescriptiveText.Contains(text, StringComparer.OrdinalIgnoreCase)
                || string.Equals(text, link.Url, StringComparison.OrdinalIgnoreCase);

            if (!nonDescriptive)
            {
                continue;
            }

            results.Add(new LintResult
            {
                RuleId = Id,
                Severity = Severity,
                Message = "This link's text does not describe its destination.",
                Detail = $"Link text \"{link.Text}\" points at {link.Url}; describe the destination instead.",
                NodeId = node.Id,
            });
        }
    }
}
