using BlazeForms.Definitions;
using BlazeForms.Linting;

namespace BlazeForms.Core.Tests.Linting;

/// <summary>
/// A11Y-01 (PRD §8): an input needs a label; a placeholder does not count, and static nodes are
/// never flagged.
/// </summary>
public sealed class A11yLabelRuleTests
{
    private static readonly ILintRule Rule = RuleTestHelpers.RuleFor(LintRuleIds.A11y01);

    [Fact]
    public void RuleIsBlocking()
    {
        Assert.Equal(LintSeverity.Blocking, Rule.Severity);
    }

    [Fact]
    public void AnInputWithoutALabelIsFlaggedEvenWithAPlaceholder()
    {
        var definition = RuleTestHelpers.Definition(
        [
            new FormNode { Id = "node-1", Type = NodeType.Text, Placeholder = "Your name" },
        ]);

        var result = Assert.Single(RuleTestHelpers.Analyze(Rule, definition));

        Assert.Equal(LintRuleIds.A11y01, result.RuleId);
        Assert.Equal(LintSeverity.Blocking, result.Severity);
        Assert.Equal("node-1", result.NodeId);
    }

    [Fact]
    public void AWhitespaceLabelIsTreatedAsNoLabel()
    {
        var definition = RuleTestHelpers.Definition(
        [
            new FormNode { Id = "node-1", Type = NodeType.Text, Label = "   " },
        ]);

        Assert.Single(RuleTestHelpers.Analyze(Rule, definition));
    }

    [Fact]
    public void LabelledInputsAndStaticNodesAreClean()
    {
        var definition = RuleTestHelpers.Definition(
        [
            new FormNode { Id = "node-1", Type = NodeType.Text, Label = "First name" },
            new FormNode { Id = "node-2", Type = NodeType.Divider },
            new FormNode { Id = "node-3", Type = NodeType.Paragraph, Content = "Some prose." },
        ]);

        Assert.Empty(RuleTestHelpers.Analyze(Rule, definition));
    }
}
