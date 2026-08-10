using BlazeForms.Definitions;
using BlazeForms.Linting;

namespace BlazeForms.Core.Tests.Linting;

/// <summary>
/// A11Y-09 (PRD §8): a Markdown link's text should describe its destination — "click here" and a
/// bare URL both fail. The rule inspects any node's help and a paragraph's or callout's content.
/// </summary>
public sealed class LinkTextRuleTests
{
    private static readonly ILintRule Rule = RuleTestHelpers.RuleFor(LintRuleIds.A11y09);

    [Fact]
    public void RuleIsAdvisory()
    {
        Assert.Equal(LintSeverity.Advisory, Rule.Severity);
    }

    [Fact]
    public void NonDescriptiveLinkTextInAParagraphIsFlagged()
    {
        var definition = RuleTestHelpers.Definition(
        [
            new FormNode
            {
                Id = "node-1",
                Type = NodeType.Paragraph,
                Content = "For the form, [click here](https://example.gov/form).",
            },
        ]);

        var result = Assert.Single(RuleTestHelpers.Analyze(Rule, definition));

        Assert.Equal(LintRuleIds.A11y09, result.RuleId);
        Assert.Equal("node-1", result.NodeId);
    }

    [Fact]
    public void ABareUrlAsLinkTextInHelpIsFlagged()
    {
        var definition = RuleTestHelpers.Definition(
        [
            new FormNode
            {
                Id = "node-1",
                Type = NodeType.Text,
                Label = "Name",
                Help = "See [https://example.gov](https://example.gov).",
            },
        ]);

        Assert.Single(RuleTestHelpers.Analyze(Rule, definition));
    }

    [Fact]
    public void DescriptiveLinkTextIsClean()
    {
        var definition = RuleTestHelpers.Definition(
        [
            new FormNode
            {
                Id = "node-1",
                Type = NodeType.Callout,
                Content = "Read the [enrollment policy](https://example.gov/policy).",
            },
            new FormNode
            {
                Id = "node-2",
                Type = NodeType.Text,
                Label = "Name",
                Help = "See the [official records page](https://example.gov/records).",
            },
        ]);

        Assert.Empty(RuleTestHelpers.Analyze(Rule, definition));
    }
}
