using BlazeForms.Definitions;
using BlazeForms.Linting;

namespace BlazeForms.Core.Tests.Linting;

/// <summary>
/// REP-01 (PRD §5): a repeating group's <c>MinRows</c> may not exceed its <c>MaxRows</c>.
/// </summary>
public sealed class RepeatingRowBoundsRuleTests
{
    private static readonly ILintRule Rule = RuleTestHelpers.RuleFor(LintRuleIds.Rep01);

    private static FormNode RepeatingGroup(int? minRows, int? maxRows) => new()
    {
        Id = "node-group",
        Type = NodeType.Repeating,
        Label = "Group",
        MinRows = minRows,
        MaxRows = maxRows,
        Children = [new FormNode { Id = "child-a", Type = NodeType.Text, Label = "A" }],
    };

    [Fact]
    public void AGroupWhoseMinimumExceedsItsMaximumIsFlagged()
    {
        var definition = RuleTestHelpers.Definition([RepeatingGroup(minRows: 5, maxRows: 2)]);

        var result = Assert.Single(RuleTestHelpers.Analyze(Rule, definition));

        Assert.Equal(LintRuleIds.Rep01, result.RuleId);
        Assert.Equal(LintSeverity.Advisory, result.Severity);
        Assert.Equal("node-group", result.NodeId);
        Assert.Contains("5", result.Detail, StringComparison.Ordinal);
        Assert.Contains("2", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void AGroupWhoseMinimumEqualsItsMaximumIsClean()
    {
        var definition = RuleTestHelpers.Definition([RepeatingGroup(minRows: 2, maxRows: 2)]);

        Assert.Empty(RuleTestHelpers.Analyze(Rule, definition));
    }

    [Fact]
    public void AGroupWhoseMinimumIsBelowItsMaximumIsClean()
    {
        var definition = RuleTestHelpers.Definition([RepeatingGroup(minRows: 0, maxRows: 4)]);

        Assert.Empty(RuleTestHelpers.Analyze(Rule, definition));
    }

    [Fact]
    public void AGroupWithNoBoundsIsClean()
    {
        var definition = RuleTestHelpers.Definition([RepeatingGroup(minRows: null, maxRows: null)]);

        Assert.Empty(RuleTestHelpers.Analyze(Rule, definition));
    }

    [Fact]
    public void AGroupWithOnlyAMinimumIsClean()
    {
        var definition = RuleTestHelpers.Definition([RepeatingGroup(minRows: 1, maxRows: null)]);

        Assert.Empty(RuleTestHelpers.Analyze(Rule, definition));
    }

    [Fact]
    public void AGroupWithOnlyAMaximumIsClean()
    {
        var definition = RuleTestHelpers.Definition([RepeatingGroup(minRows: null, maxRows: 4)]);

        Assert.Empty(RuleTestHelpers.Analyze(Rule, definition));
    }

    [Fact]
    public void ANonRepeatingNodeIsNeverFlagged()
    {
        var definition = RuleTestHelpers.Definition([new FormNode { Id = "node-a", Type = NodeType.Text, Label = "A" }]);

        Assert.Empty(RuleTestHelpers.Analyze(Rule, definition));
    }
}
