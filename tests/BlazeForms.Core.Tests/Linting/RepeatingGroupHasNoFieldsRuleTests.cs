using BlazeForms.Definitions;
using BlazeForms.Linting;

namespace BlazeForms.Core.Tests.Linting;

/// <summary>
/// REP-02 (PRD §5): a repeating group with no child fields captures nothing.
/// </summary>
public sealed class RepeatingGroupHasNoFieldsRuleTests
{
    private static readonly ILintRule Rule = RuleTestHelpers.RuleFor(LintRuleIds.Rep02);

    [Fact]
    public void AGroupWithNoChildrenIsFlagged()
    {
        var definition = RuleTestHelpers.Definition(
            [new FormNode { Id = "node-group", Type = NodeType.Repeating, Label = "Group" }]);

        var result = Assert.Single(RuleTestHelpers.Analyze(Rule, definition));

        Assert.Equal(LintRuleIds.Rep02, result.RuleId);
        Assert.Equal(LintSeverity.Advisory, result.Severity);
        Assert.Equal("node-group", result.NodeId);
    }

    [Fact]
    public void AGroupWithAtLeastOneChildIsClean()
    {
        var definition = RuleTestHelpers.Definition(
        [
            new FormNode
            {
                Id = "node-group",
                Type = NodeType.Repeating,
                Label = "Group",
                Children = [new FormNode { Id = "child-a", Type = NodeType.Text, Label = "A" }],
            },
        ]);

        Assert.Empty(RuleTestHelpers.Analyze(Rule, definition));
    }

    [Fact]
    public void ANonRepeatingNodeIsNeverFlagged()
    {
        var definition = RuleTestHelpers.Definition([new FormNode { Id = "node-a", Type = NodeType.Text, Label = "A" }]);

        Assert.Empty(RuleTestHelpers.Analyze(Rule, definition));
    }
}
