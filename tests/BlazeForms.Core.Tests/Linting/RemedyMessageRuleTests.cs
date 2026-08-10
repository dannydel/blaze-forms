using BlazeForms.Definitions;
using BlazeForms.Expressions;
using BlazeForms.Linting;

namespace BlazeForms.Core.Tests.Linting;

/// <summary>
/// A11Y-06 (PRD §8): a validation message should state a remedy, not just name the failure.
/// </summary>
public sealed class RemedyMessageRuleTests
{
    private static readonly ILintRule Rule = RuleTestHelpers.RuleFor(LintRuleIds.A11y06);

    private static FormDefinition WithMessage(string message) => RuleTestHelpers.Definition(
        [new FormNode { Id = "node-a", Type = NodeType.Text, Label = "A" }],
        [
            new ValidationRule
            {
                Target = "node-a",
                Message = message,
                Expression = new ConditionGroup
                {
                    Conditions = [new Condition { Field = "node-a", Operator = ConditionOperator.IsBlank }],
                },
            },
        ]);

    [Fact]
    public void RuleIsAdvisory()
    {
        Assert.Equal(LintSeverity.Advisory, Rule.Severity);
    }

    [Theory]
    [InlineData("Invalid")]
    [InlineData("Error")]
    [InlineData("Invalid input")]
    [InlineData("This field is wrong")]
    public void NonActionableMessagesAreFlagged(string message)
    {
        var result = Assert.Single(RuleTestHelpers.Analyze(Rule, WithMessage(message)));

        Assert.Equal(LintRuleIds.A11y06, result.RuleId);
        Assert.Equal(LintSeverity.Advisory, result.Severity);
        Assert.Equal("node-a", result.NodeId);
    }

    [Theory]
    [InlineData("Enter a date for 'Date of birth'.")]
    [InlineData("Choose a bus route, or answer No to the transportation question.")]
    public void MessagesThatStateARemedyAreClean(string message)
    {
        Assert.Empty(RuleTestHelpers.Analyze(Rule, WithMessage(message)));
    }
}
