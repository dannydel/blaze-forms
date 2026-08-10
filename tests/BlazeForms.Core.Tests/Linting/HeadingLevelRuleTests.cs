using BlazeForms.Definitions;
using BlazeForms.Linting;

namespace BlazeForms.Core.Tests.Linting;

/// <summary>
/// A11Y-08 (PRD §8): heading levels should step by one; a jump of more than one rung is flagged,
/// and the first heading is never flagged.
/// </summary>
public sealed class HeadingLevelRuleTests
{
    private static readonly ILintRule Rule = RuleTestHelpers.RuleFor(LintRuleIds.A11y08);

    private static FormNode Heading(string id, int level) =>
        new() { Id = id, Type = NodeType.Heading, Label = id, Level = level };

    [Fact]
    public void RuleIsAdvisory()
    {
        Assert.Equal(LintSeverity.Advisory, Rule.Severity);
    }

    [Fact]
    public void AJumpOfMoreThanOneRungIsFlagged()
    {
        var definition = RuleTestHelpers.Definition([Heading("h-top", 2), Heading("h-skip", 4)]);

        var result = Assert.Single(RuleTestHelpers.Analyze(Rule, definition));

        Assert.Equal(LintRuleIds.A11y08, result.RuleId);
        Assert.Equal("h-skip", result.NodeId);
    }

    [Fact]
    public void TheFirstHeadingsStartingLevelIsNotFlagged()
    {
        var definition = RuleTestHelpers.Definition([Heading("h-only", 4)]);

        Assert.Empty(RuleTestHelpers.Analyze(Rule, definition));
    }

    [Fact]
    public void SteppingByOneAndSteppingBackUpAreClean()
    {
        var definition = RuleTestHelpers.Definition(
        [
            Heading("h-1", 2),
            Heading("h-2", 3),
            Heading("h-3", 4),
            Heading("h-4", 2),
        ]);

        Assert.Empty(RuleTestHelpers.Analyze(Rule, definition));
    }
}
